using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Extensions.Logging;
using FishMMO.DiscordBot.Data;

namespace FishMMO.DiscordBot.Services
{
	/// <summary>
	/// Manages Discord-to-game account linking via verification codes.
	/// Users request a link with a character name, receive a code, type it in-game,
	/// and the polling service confirms the link when the code appears in chat.
	/// </summary>
	public sealed class AccountLinkingService : IDisposable
	{
		private readonly BotConfigurationService botConfigService;
		private readonly ILogger<AccountLinkingService> logger;
		private readonly Timer cleanupTimer;
		private int disposed;

		/// <summary>
		/// Pending verifications keyed by lowercased character name.
		/// </summary>
		private readonly ConcurrentDictionary<string, PendingLinkVerification> pendingVerifications = new();

		/// <summary>How long a verification code stays valid.</summary>
		private static readonly TimeSpan VerificationTimeout = TimeSpan.FromMinutes(5);

		/// <summary>
		/// Initializes a new instance of the <see cref="AccountLinkingService"/> class.
		/// </summary>
		public AccountLinkingService(
			BotConfigurationService botConfigService,
			ILogger<AccountLinkingService> logger)
		{
			this.botConfigService = botConfigService;
			this.logger = logger;

			cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
			logger.LogInformation("AccountLinkingService initialized.");
		}

		/// <summary>
		/// Starts a link request for the given Discord user and character name.
		/// Returns the verification code the user must type in-game.
		/// </summary>
		/// <param name="discordUserId">The Discord user ID.</param>
		/// <param name="characterName">The character name claimed by the user.</param>
		/// <returns>The verification code, or null if the user already has a linked account.</returns>
		public string? StartLinkRequest(ulong discordUserId, string characterName)
		{
			var persistentData = botConfigService.GetPersistentData();
			if (persistentData.LinkedAccounts.ContainsKey(discordUserId))
			{
				return null; // already linked
			}

			string code = GenerateCode();
			string key = characterName.ToLowerInvariant();

			var pending = new PendingLinkVerification
			{
				DiscordUserId = discordUserId,
				CharacterName = characterName,
				VerificationCode = code,
				ExpiresAtUtc = DateTime.UtcNow.Add(VerificationTimeout)
			};

			pendingVerifications[key] = pending;

			logger.LogInformation(
				"Link request started for Discord user {UserId} with character '{CharacterName}'. Code: {Code}",
				discordUserId, characterName, code);

			return code;
		}

		/// <summary>
		/// Checks whether a chat message from the game contains a pending verification code.
		/// If it matches, confirms the link and returns the associated Discord user ID.
		/// </summary>
		/// <param name="characterName">The character name that sent the message.</param>
		/// <param name="accountName">The game account name.</param>
		/// <param name="message">The chat message content.</param>
		/// <returns>The Discord user ID if verification succeeded; null otherwise.</returns>
		public ulong? TryVerifyFromChat(string characterName, string accountName, string message)
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

			// Confirmed! Remove pending and create linked account
			pendingVerifications.TryRemove(key, out _);

			var linkedAccount = new LinkedAccount
			{
				DiscordUserId = pending.DiscordUserId,
				GameAccountName = accountName,
				CharacterName = characterName,
				LinkedAtUtc = DateTime.UtcNow
			};

			var persistentData = botConfigService.GetPersistentData();
			persistentData.LinkedAccounts[pending.DiscordUserId] = linkedAccount;

			logger.LogInformation(
				"Account link verified! Discord user {UserId} linked to account '{AccountName}' via character '{CharacterName}'.",
				pending.DiscordUserId, accountName, characterName);

			return pending.DiscordUserId;
		}

		/// <summary>
		/// Removes a linked account for the specified Discord user.
		/// </summary>
		/// <returns>True if the account was unlinked.</returns>
		public bool Unlink(ulong discordUserId)
		{
			var persistentData = botConfigService.GetPersistentData();
			return persistentData.LinkedAccounts.Remove(discordUserId);
		}

		/// <summary>
		/// Gets the linked account for a Discord user, if any.
		/// </summary>
		public LinkedAccount? GetLinkedAccount(ulong discordUserId)
		{
			var persistentData = botConfigService.GetPersistentData();
			persistentData.LinkedAccounts.TryGetValue(discordUserId, out var linked);
			return linked;
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
		/// Removes expired pending verifications.
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
		}

		/// <inheritdoc />
		public void Dispose()
		{
			if (Interlocked.Exchange(ref disposed, 1) == 0)
			{
				cleanupTimer.Dispose();
			}
		}
	}
}