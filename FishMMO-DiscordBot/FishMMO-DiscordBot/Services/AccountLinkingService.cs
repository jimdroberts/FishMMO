using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.DiscordBot.Data;
using FishMMO.Shared;

namespace FishMMO.DiscordBot.Services
{
	/// <summary>
	/// Manages Discord-to-game account linking. The link itself lives in the database: the account's
	/// linked Discord user is a unique column, so one Discord account links one game account, and a
	/// verified Discord code and a <c>/link</c> are the same link.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The <c>/link</c> flow is unchanged for the player: they name a character, the bot DMs them a
	/// short-lived code, and they type it into game chat, where <see cref="ChatPollingService"/> sees it.
	/// Only the pending code is kept in memory; the confirmed link is written to the database.
	/// </para>
	/// <para>
	/// Lookups are cached briefly because <see cref="GameChatBridgeService"/> checks the link on every
	/// bridged message. Links made or removed by this bot invalidate their entry at once; a link made
	/// elsewhere (a Discord code redeemed in the game or Control Panel) is seen within
	/// <see cref="LinkCacheLifetime"/>.
	/// </para>
	/// <para>
	/// On start it imports the links older bots kept in botdata.json; see <see cref="ImportLegacyLinksAsync"/>.
	/// </para>
	/// </remarks>
	public sealed class AccountLinkingService : IHostedService, IDisposable
	{
		private readonly BotConfigurationService botConfigService;
		private readonly IDiscordAccountService discordAccountService;
		private readonly NpgsqlDbContextFactory dbContextFactory;
		private readonly ILogger<AccountLinkingService> logger;
		private readonly Timer cleanupTimer;
		private int disposed;

		/// <summary>
		/// Pending verifications keyed by lowercased character name.
		/// </summary>
		private readonly ConcurrentDictionary<string, PendingLinkVerification> pendingVerifications = new();

		/// <summary>Cached link lookups keyed by Discord user id. A null link is cached too: most bridged authors are unlinked.</summary>
		private readonly ConcurrentDictionary<ulong, CachedLink> linkCache = new();

		/// <summary>How long a verification code stays valid.</summary>
		private static readonly TimeSpan VerificationTimeout = TimeSpan.FromMinutes(5);

		/// <summary>How long a link lookup is trusted before the database is asked again.</summary>
		internal static readonly TimeSpan LinkCacheLifetime = TimeSpan.FromSeconds(60);

		/// <summary>
		/// Initializes a new instance of the <see cref="AccountLinkingService"/> class.
		/// </summary>
		public AccountLinkingService(
			BotConfigurationService botConfigService,
			IDiscordAccountService discordAccountService,
			NpgsqlDbContextFactory dbContextFactory,
			ILogger<AccountLinkingService> logger)
		{
			this.botConfigService = botConfigService;
			this.discordAccountService = discordAccountService;
			this.dbContextFactory = dbContextFactory;
			this.logger = logger;

			cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
			logger.LogInformation("AccountLinkingService initialized.");
		}

		/// <summary>
		/// Starts the one-time botdata.json import in the background.
		/// </summary>
		/// <remarks>
		/// Registered after <see cref="DynamicChannelManagerService"/>, whose start loads botdata.json, so the
		/// file is in memory by now. Not awaited: a slow or unreachable database must not hold the bot's
		/// start, and an entry that fails transiently simply stays in the file for the next start.
		/// </remarks>
		public Task StartAsync(CancellationToken cancellationToken)
		{
			_ = Task.Run(async () =>
			{
				try
				{
					await ImportLegacyLinksAsync();
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Importing botdata.json account links into the database failed; they stay in the file for the next start.");
				}
			});
			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		/// <summary>
		/// Starts a link request for the given Discord user and character name.
		/// </summary>
		/// <param name="discordUserId">The Discord user ID.</param>
		/// <param name="discordUsername">The user's Discord username as Discord reports it, recorded on the account when the link is made.</param>
		/// <param name="characterName">The character name claimed by the user.</param>
		/// <returns>The status, and the verification code the user must type in-game when it started.</returns>
		public async Task<(LinkRequestStatus Status, string? Code)> StartLinkRequestAsync(ulong discordUserId, string? discordUsername, string characterName)
		{
			var existing = await GetLinkedAccountAsync(discordUserId);
			if (!existing.IsSuccess)
			{
				return (LinkRequestStatus.Error, null);
			}
			if (existing.Data.HasValue)
			{
				return (LinkRequestStatus.AlreadyLinked, null);
			}

			string code = GenerateCode();
			string key = characterName.ToLowerInvariant();

			var pending = new PendingLinkVerification
			{
				DiscordUserId = discordUserId,
				CharacterName = characterName,
				DiscordUsername = discordUsername,
				VerificationCode = code,
				ExpiresAtUtc = DateTime.UtcNow.Add(VerificationTimeout)
			};

			pendingVerifications[key] = pending;

			// The code is a credential for the link; it is not logged.
			logger.LogInformation(
				"Link request started for Discord user {UserId} with character '{CharacterName}'.",
				discordUserId, characterName);

			return (LinkRequestStatus.Started, code);
		}

		/// <summary>
		/// Checks whether a chat message from the game contains a pending verification code.
		/// If it matches, writes the link to the database.
		/// </summary>
		/// <param name="characterName">The character name that sent the message.</param>
		/// <param name="accountName">The game account name.</param>
		/// <param name="message">The chat message content.</param>
		/// <returns>How the link ended, or null when the message carried no pending code.</returns>
		public async Task<ChatLinkVerification?> TryVerifyFromChatAsync(string characterName, string accountName, string message)
		{
			string key = characterName.ToLowerInvariant();
			if (!pendingVerifications.TryGetValue(key, out var pending))
			{
				return null;
			}

			if (DateTime.UtcNow > pending.ExpiresAtUtc)
			{
				pendingVerifications.TryRemove(key, out _);
				return null;
			}

			string trimmed = message.Trim();
			// Check if message contains the verification code (case-insensitive)
			if (trimmed.IndexOf(pending.VerificationCode, StringComparison.OrdinalIgnoreCase) < 0)
			{
				return null;
			}

			// Only the request that matched is consumed; a newer one for the same character is left alone.
			if (!pendingVerifications.TryRemove(new System.Collections.Generic.KeyValuePair<string, PendingLinkVerification>(key, pending)))
			{
				return null;
			}

			var result = await discordAccountService.LinkAsync(accountName, unchecked((long)pending.DiscordUserId), pending.DiscordUsername);
			InvalidateLink(pending.DiscordUserId);

			if (result.IsSuccess)
			{
				logger.LogInformation(
					"Account link verified! Discord user {UserId} linked to account '{AccountName}' via character '{CharacterName}'.",
					pending.DiscordUserId, accountName, characterName);
				return new ChatLinkVerification(pending.DiscordUserId, characterName, ChatLinkOutcome.Linked);
			}

			if (result.ErrorCode == DatabaseErrorCodes.UniqueViolation)
			{
				logger.LogWarning(
					"Account link refused: Discord user {UserId} is already linked to another game account (code typed by '{CharacterName}' on account '{AccountName}').",
					pending.DiscordUserId, characterName, accountName);
				return new ChatLinkVerification(pending.DiscordUserId, characterName, ChatLinkOutcome.LinkedToAnotherAccount);
			}

			logger.LogError(
				"Account link for Discord user {UserId} to account '{AccountName}' could not be saved ({ErrorCode}: {ErrorMessage}).",
				pending.DiscordUserId, accountName, result.ErrorCode, result.ErrorMessage);
			return new ChatLinkVerification(pending.DiscordUserId, characterName, ChatLinkOutcome.Failed);
		}

		/// <summary>
		/// Removes the link for the specified Discord user.
		/// </summary>
		/// <returns>True in <c>Data</c> when there was a link to remove.</returns>
		public async Task<DatabaseResult<bool>> UnlinkAsync(ulong discordUserId)
		{
			var result = await discordAccountService.UnlinkAsync(unchecked((long)discordUserId));
			InvalidateLink(discordUserId);
			return result;
		}

		/// <summary>
		/// Gets the account a Discord user is linked to, or null in <c>Data</c> when there is none.
		/// Successful lookups are cached for <see cref="LinkCacheLifetime"/>; failures are not.
		/// </summary>
		public async Task<DatabaseResult<DiscordAccountLink?>> GetLinkedAccountAsync(ulong discordUserId)
		{
			DateTime now = DateTime.UtcNow;
			if (linkCache.TryGetValue(discordUserId, out var cached) && cached.ExpiresAtUtc > now)
			{
				return DatabaseResult<DiscordAccountLink?>.Success(cached.Link);
			}

			var result = await discordAccountService.FetchLinkByDiscordUserAsync(unchecked((long)discordUserId));
			if (result.IsSuccess)
			{
				linkCache[discordUserId] = new CachedLink(result.Data, now.Add(LinkCacheLifetime));
			}
			return result;
		}

		/// <summary>
		/// Checks if a Discord user has a pending verification.
		/// </summary>
		public bool HasPendingVerification(ulong discordUserId)
		{
			foreach (var kvp in pendingVerifications)
			{
				if (kvp.Value.DiscordUserId == discordUserId && DateTime.UtcNow <= kvp.Value.ExpiresAtUtc)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Moves the links older bots kept in botdata.json into the database, once.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Each entry is written with the account's Discord user. An entry is removed from the file when it
		/// was imported, or when the database refused it for good — that Discord user is already linked to
		/// another account, the account no longer exists, or the name is not a valid account name — and each
		/// refusal is logged. An entry that failed transiently stays for the next start.
		/// </para>
		/// <para>
		/// An account that already has a DIFFERENT Discord user in the database is not overwritten, because
		/// the database link is newer than anything in the file: the entry is refused and removed.
		/// </para>
		/// </remarks>
		public async Task ImportLegacyLinksAsync()
		{
			var legacy = botConfigService.GetPersistentData().LinkedAccounts;
			if (legacy.Count == 0)
			{
				return;
			}

			logger.LogInformation("Importing {Count} account links from botdata.json into the database.", legacy.Count);

			int imported = 0, refused = 0, kept = 0;
			foreach (var entry in legacy.ToList())
			{
				ulong discordUserId = entry.Key;
				string accountName = entry.Value.GameAccountName;
				long signedId = unchecked((long)discordUserId);

				var outcome = await ImportOneAsync(accountName, signedId);
				switch (outcome.Kind)
				{
					case ImportKind.Imported:
						imported++;
						legacy.Remove(discordUserId);
						InvalidateLink(discordUserId);
						break;

					case ImportKind.Refused:
						refused++;
						legacy.Remove(discordUserId);
						logger.LogWarning(
							"Did not import the botdata.json link of Discord user {UserId} to account '{AccountName}': {Reason}. The entry was removed.",
							discordUserId, accountName, outcome.Reason);
						break;

					default:
						kept++;
						logger.LogWarning(
							"Could not import the botdata.json link of Discord user {UserId} to account '{AccountName}' ({Reason}); it stays in the file for the next start.",
							discordUserId, accountName, outcome.Reason);
						break;
				}
			}

			await botConfigService.SavePersistentDataAsync();

			logger.LogInformation(
				"botdata.json link import finished: {Imported} imported, {Refused} refused, {Kept} kept for retry.",
				imported, refused, kept);
		}

		/// <summary>Imports one legacy link.</summary>
		private async Task<(ImportKind Kind, string Reason)> ImportOneAsync(string accountName, long signedDiscordUserId)
		{
			if (string.IsNullOrWhiteSpace(accountName) || !Authentication.IsAllowedUsername(accountName))
			{
				return (ImportKind.Refused, "the account name is not a valid account name");
			}

			// Never replace a link the database already holds for this account with an older one from the file.
			try
			{
				string lookup = Authentication.NormalizeAccountLookup(accountName);
				using var dbContext = dbContextFactory.CreateDbContext();
				var current = await dbContext.Accounts
					.AsNoTracking()
					.Where(a => a.NameLowercase == lookup)
					.Select(a => new { a.DiscordUserId })
					.FirstOrDefaultAsync();

				if (current == null)
				{
					return (ImportKind.Refused, "the account does not exist");
				}
				if (current.DiscordUserId.HasValue)
				{
					return current.DiscordUserId.Value == signedDiscordUserId
						? (ImportKind.Imported, string.Empty)
						: (ImportKind.Refused, "the account is already linked to a different Discord user in the database");
				}
			}
			catch (Exception ex)
			{
				return (ImportKind.Transient, ex.Message);
			}

			var result = await discordAccountService.LinkAsync(accountName, signedDiscordUserId, null);
			if (result.IsSuccess)
			{
				return (ImportKind.Imported, string.Empty);
			}

			return result.ErrorCode switch
			{
				DatabaseErrorCodes.UniqueViolation => (ImportKind.Refused, "that Discord user is already linked to another game account"),
				DatabaseErrorCodes.NotFound => (ImportKind.Refused, "the account does not exist"),
				DatabaseErrorCodes.ValidationError => (ImportKind.Refused, "the account name is not a valid account name"),
				_ => (ImportKind.Transient, $"{result.ErrorCode}: {result.ErrorMessage}"),
			};
		}

		/// <summary>Drops a cached lookup after this bot changed the link.</summary>
		private void InvalidateLink(ulong discordUserId)
		{
			linkCache.TryRemove(discordUserId, out _);
		}

		/// <summary>
		/// Generates a 6-character alphanumeric verification code.
		/// </summary>
		private static string GenerateCode()
		{
			const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no ambiguous chars
			Span<byte> bytes = stackalloc byte[6];
			RandomNumberGenerator.Fill(bytes);
			var result = new char[6];
			for (int i = 0; i < 6; i++)
			{
				result[i] = chars[bytes[i] % chars.Length];
			}
			return new string(result);
		}

		/// <summary>
		/// Removes expired pending verifications and expired cache entries.
		/// </summary>
		private void CleanupExpired(object? state)
		{
			DateTime now = DateTime.UtcNow;
			foreach (var kvp in pendingVerifications)
			{
				if (now > kvp.Value.ExpiresAtUtc)
				{
					pendingVerifications.TryRemove(kvp.Key, out _);
				}
			}
			foreach (var kvp in linkCache)
			{
				if (now > kvp.Value.ExpiresAtUtc)
				{
					linkCache.TryRemove(kvp.Key, out _);
				}
			}
		}

		/// <inheritdoc />
		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				cleanupTimer.Dispose();
			}
		}

		/// <summary>A cached link lookup.</summary>
		private readonly struct CachedLink
		{
			public readonly DiscordAccountLink? Link;
			public readonly DateTime ExpiresAtUtc;

			public CachedLink(DiscordAccountLink? link, DateTime expiresAtUtc)
			{
				Link = link;
				ExpiresAtUtc = expiresAtUtc;
			}
		}

		/// <summary>How one legacy import ended.</summary>
		private enum ImportKind
		{
			Imported,
			Refused,
			Transient,
		}
	}

	/// <summary>The result of starting a <c>/link</c> request.</summary>
	public enum LinkRequestStatus
	{
		/// <summary>A code was issued.</summary>
		Started,
		/// <summary>The Discord user is already linked to a game account.</summary>
		AlreadyLinked,
		/// <summary>The database could not be asked whether the user is linked.</summary>
		Error,
	}

	/// <summary>How a chat-code link ended once its code was seen in game chat.</summary>
	public enum ChatLinkOutcome
	{
		/// <summary>The link was written.</summary>
		Linked,
		/// <summary>The Discord user is already linked to a different game account; nothing changed.</summary>
		LinkedToAnotherAccount,
		/// <summary>The link could not be saved.</summary>
		Failed,
	}

	/// <summary>A chat-code link whose code was typed in game chat.</summary>
	/// <param name="DiscordUserId">The Discord user who asked for the link.</param>
	/// <param name="CharacterName">The character that typed the code.</param>
	/// <param name="Outcome">How it ended.</param>
	public sealed record ChatLinkVerification(ulong DiscordUserId, string CharacterName, ChatLinkOutcome Outcome);
}
