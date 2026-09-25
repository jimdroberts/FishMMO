using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Logging;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.DiscordBot.Services;

namespace FishMMO.DiscordBot.Modules
{
	/// <summary>
	/// Moderation commands for game server administration via Discord.
	/// Requires ManageGuild permission (moderator-level).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Kick, ban and unban act on game accounts, so they obey the game's rules, not Discord's.</b>
	/// They used to write the account row directly: any holder of Discord's ManageGuild could ban or
	/// unban any account — an Admin's included — with no audit row, and the ban set the level alone,
	/// leaving the account's tokens and panel sessions live. Now:
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// The Discord user must be linked (<c>/link</c>) to a game account at Game Master or above, and
	/// that account is the actor. The same guards the Control Panel and the in-game commands apply:
	/// not your own account, and not one at or above your own level. Two surfaces onto one capability
	/// must not disagree about who may use it, or the weaker one becomes the way round the stronger.
	/// </description></item>
	/// <item><description>
	/// The writes are the same service calls the panel makes — <c>BanAsync</c> is the level, the game
	/// tokens, the panel sessions and a kick request in one transaction.
	/// </description></item>
	/// <item><description>
	/// Every attempt by a resolved actor, refused or done, is written to the admin audit log with
	/// source <c>discord</c>. The typed command line is the reason, as it is for a chat command.
	/// </description></item>
	/// </list>
	/// </remarks>
	[Group("mod")]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public class ModerationModule : ModuleBase<SocketCommandContext>
	{
		/* The action identifiers the Control Panel writes (AuditActions), so one query finds a ban
		 * whichever surface placed it. Literals because the panel's constants live in its assembly. */
		private const string AuditActionKick = "account.kick";
		private const string AuditActionBan = "account.ban";
		private const string AuditActionUnban = "account.unban";
		private const string AuditSource = "discord";

		private readonly BridgeBanService bridgeBanService;
		private readonly BotConfigurationService botConfigService;
		private readonly AccountLinkingService accountLinkingService;
		private readonly IAccountService accounts;
		private readonly IKickRequestService kickRequests;
		private readonly IAdminAuditService auditLog;
		private readonly ILogger<ModerationModule> logger;

		private const int MaxNameLength = 64;

		public ModerationModule(
			BridgeBanService bridgeBanService,
			BotConfigurationService botConfigService,
			AccountLinkingService accountLinkingService,
			IAccountService accounts,
			IKickRequestService kickRequests,
			IAdminAuditService auditLog,
			ILogger<ModerationModule> logger)
		{
			this.bridgeBanService = bridgeBanService;
			this.botConfigService = botConfigService;
			this.accountLinkingService = accountLinkingService;
			this.accounts = accounts;
			this.kickRequests = kickRequests;
			this.auditLog = auditLog;
			this.logger = logger;
		}

		/// <summary>
		/// Kicks a player by writing a kick request, which the game servers act on at their next poll.
		/// </summary>
		[Command("kick")]
		[Summary("Kicks a player from the game by account name. Needs your Discord linked to a Game Master account.")]
		public async Task KickAsync(string accountName, [Remainder] string? reason = null)
		{
			var (actor, target) = await ResolveAsync(accountName, AuditActionKick);
			if (actor == null || target == null)
			{
				return;
			}

			var result = await kickRequests.PersistAsync(target.Name);
			if (!result.IsSuccess)
			{
				logger.LogError("Kick request for '{AccountName}' by '{Actor}' could not be written: [{Code}] {Message}",
					target.Name, actor.Name, result.ErrorCode, result.ErrorMessage);
				await RecordAsync(actor, AuditActionKick, target.Name, false, $"Failed: the kick request could not be written [{result.ErrorCode}].");
				await ReplyAsync("The kick request could not be written. Try again shortly.");
				return;
			}

			logger.LogInformation("Kick request created for account '{AccountName}' by '{Actor}' from Discord user {User}.",
				target.Name, actor.Name, Context.User.Username);
			await RecordAsync(actor, AuditActionKick, target.Name, true, null);
			await ReplyAsync($"Kick request submitted for account '{target.Name}'. The game server will process it shortly.");
		}

		/// <summary>
		/// Bans a game account: level, game tokens, panel sessions and a kick request, in one transaction.
		/// </summary>
		[Command("ban")]
		[Summary("Bans a game account (level, tokens, sessions and a kick, together). Needs your Discord linked to a Game Master account.")]
		public async Task BanAsync(string accountName, [Remainder] string? reason = null)
		{
			var (actor, target) = await ResolveAsync(accountName, AuditActionBan);
			if (actor == null || target == null)
			{
				return;
			}

			if (target.AccessLevel == (byte)AccessLevel.Banned)
			{
				await RecordAsync(actor, AuditActionBan, target.Name, false, "Refused: already banned.");
				await ReplyAsync($"Account '{target.Name}' is already banned.");
				return;
			}

			/* The same service method the panel's ban calls, which is the point: the level alone stops
			 * the next sign-in and nothing else, and this used to be all the bot changed. Who and why
			 * go onto the account row, so the account page shows them. */
			var result = await accounts.BanAsync(target.Name, null, actor.Name, BanReason(reason));
			if (!result.IsSuccess)
			{
				logger.LogError("Ban of '{AccountName}' by '{Actor}' failed: [{Code}] {Message}",
					target.Name, actor.Name, result.ErrorCode, result.ErrorMessage);
				await RecordAsync(actor, AuditActionBan, target.Name, false, $"Failed: the ban could not be written [{result.ErrorCode}].");
				await ReplyAsync("The ban could not be applied. Nothing was changed; try again shortly.");
				return;
			}

			logger.LogWarning("Account '{AccountName}' banned by '{Actor}' from Discord user {User}.",
				target.Name, actor.Name, Context.User.Username);
			await RecordAsync(actor, AuditActionBan, target.Name, true, null);
			await ReplyAsync($"Account '{target.Name}' has been **banned**: its tokens and sessions are revoked and a kick request has been written.");
		}

		/// <summary>
		/// Lifts a ban, restoring Player. Revoked tokens and sessions stay revoked.
		/// </summary>
		[Command("unban")]
		[Summary("Unbans a game account (restores Player). Needs your Discord linked to a Game Master account.")]
		public async Task UnbanAsync(string accountName, [Remainder] string? reason = null)
		{
			/* The peer-or-superior guard compares against the target's CURRENT level, which for a
			 * banned account is 0 and passes trivially — the Control Panel's unban notes the same.
			 * What is enforced is that the actor is staff, and the lift is recorded. */
			var (actor, target) = await ResolveAsync(accountName, AuditActionUnban);
			if (actor == null || target == null)
			{
				return;
			}

			if (target.AccessLevel != (byte)AccessLevel.Banned)
			{
				await RecordAsync(actor, AuditActionUnban, target.Name, false, "Refused: not banned.");
				await ReplyAsync($"Account '{target.Name}' is not currently banned.");
				return;
			}

			var result = await accounts.UnbanAsync(target.Name);
			if (!result.IsSuccess)
			{
				logger.LogError("Unban of '{AccountName}' by '{Actor}' failed: [{Code}] {Message}",
					target.Name, actor.Name, result.ErrorCode, result.ErrorMessage);
				await RecordAsync(actor, AuditActionUnban, target.Name, false, $"Failed: the ban could not be lifted [{result.ErrorCode}].");
				await ReplyAsync("The ban could not be lifted. Try again shortly.");
				return;
			}

			logger.LogWarning("Account '{AccountName}' unbanned by '{Actor}' from Discord user {User}.",
				target.Name, actor.Name, Context.User.Username);
			await RecordAsync(actor, AuditActionUnban, target.Name, true, null);
			await ReplyAsync($"Account '{target.Name}' has been **unbanned** and must sign in afresh.");
		}

		/// <summary>
		/// Resolves the acting game account from the Discord user's link, then the target, and applies
		/// the shared guards. Replies and returns nulls when the command must not proceed.
		/// </summary>
		/// <remarks>
		/// Every database failure here refuses the command. A check that could not be made is not a
		/// check that passed.
		/// </remarks>
		private async Task<(AccountAdminData? Actor, AccountAdminData? Target)> ResolveAsync(string accountName, string action)
		{
			if (string.IsNullOrWhiteSpace(accountName) || accountName.Length > MaxNameLength)
			{
				await ReplyAsync($"Account name must be between 1 and {MaxNameLength} characters.");
				return (null, null);
			}

			var link = await accountLinkingService.GetLinkedAccountAsync(Context.User.Id);
			if (!link.IsSuccess)
			{
				logger.LogError("Could not read the account link for Discord user {UserId} to authorise '{Action}' ({ErrorCode}: {ErrorMessage}).",
					Context.User.Id, action, link.ErrorCode, link.ErrorMessage);
				await ReplyAsync("Your linked game account could not be checked, so nothing was done. Try again shortly.");
				return (null, null);
			}
			if (!link.Data.HasValue)
			{
				await ReplyAsync("Moderating game accounts from Discord needs your Discord account linked to a Game Master or Admin game account. Use `/link` first.");
				return (null, null);
			}

			var actorResult = await accounts.FetchAdminAsync(link.Data.Value.AccountName);
			if (!actorResult.IsSuccess)
			{
				logger.LogError("Could not read linked account '{Account}' of Discord user {UserId}: [{Code}] {Message}",
					link.Data.Value.AccountName, Context.User.Id, actorResult.ErrorCode, actorResult.ErrorMessage);
				await ReplyAsync("Your linked game account could not be checked, so nothing was done. Try again shortly.");
				return (null, null);
			}
			AccountAdminData actor = actorResult.Data;
			if (actor.AccessLevel < (byte)AccessLevel.GameMaster)
			{
				logger.LogWarning("Discord user {User} ({UserId}), linked to '{Account}' ({Level}), was refused '{Action}': below Game Master.",
					Context.User.Username, Context.User.Id, actor.Name, (AccessLevel)actor.AccessLevel, action);
				await ReplyAsync("Your linked game account is not a Game Master or Admin, so it cannot moderate game accounts.");
				return (null, null);
			}

			var targetResult = await accounts.FetchAdminAsync(accountName.Trim());
			if (!targetResult.IsSuccess)
			{
				if (targetResult.ErrorCode is DatabaseErrorCodes.NotFound or DatabaseErrorCodes.ValidationError)
				{
					await RecordAsync(actor, action, accountName.Trim(), false, "Refused: no such account.");
					await ReplyAsync($"Account '{accountName}' not found.");
					return (null, null);
				}
				logger.LogError("Could not read account '{Account}' for '{Action}': [{Code}] {Message}",
					accountName, action, targetResult.ErrorCode, targetResult.ErrorMessage);
				await ReplyAsync("That account could not be read, so nothing was done. Try again shortly.");
				return (null, null);
			}
			AccountAdminData target = targetResult.Data;

			if (string.Equals(actor.Name, target.Name, StringComparison.OrdinalIgnoreCase))
			{
				await RecordAsync(actor, action, target.Name, false, "Refused: the target is the operator's own account.");
				await ReplyAsync("You cannot take this action against your own account.");
				return (null, null);
			}
			if (target.AccessLevel >= actor.AccessLevel)
			{
				await RecordAsync(actor, action, target.Name, false, "Refused: the target is at or above the operator's level.");
				await ReplyAsync($"That account is {(AccessLevel)target.AccessLevel}; you cannot act on an account at or above your own level.");
				return (null, null);
			}

			return (actor, target);
		}

		/// <summary>
		/// Writes the admin audit row for a moderation attempt. A failed write is logged loudly and does
		/// not undo the action, which has already happened.
		/// </summary>
		private async Task RecordAsync(AccountAdminData actor, string action, string target, bool succeeded, string? outcome)
		{
			try
			{
				var result = await auditLog.AppendAsync(new AdminAuditData
				{
					OccurredUtc = DateTime.UtcNow,
					ActorName = actor.Name,
					ActorAccessLevel = actor.AccessLevel,
					ActorSessionID = null,
					Action = action,
					TargetType = "account",
					TargetID = target,
					TargetName = target,
					// The typed command line, as for an in-game command: the most honest record of intent.
					Reason = Context.Message.Content,
					Succeeded = succeeded,
					// Null for a success: the column is optional, as the panel's writer treats it.
					Outcome = outcome!,
					Details = JsonSerializer.Serialize(new
					{
						discordUserId = Context.User.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
						discordUsername = Context.User.Username,
					}),
					// No IpAddress: a Discord command has none that is ours to record.
					Source = AuditSource,
				});
				if (!result.IsSuccess)
				{
					logger.LogError("AUDIT WRITE FAILED for '{Action}' on '{Target}' by '{Actor}': [{Code}] {Message}",
						action, target, actor.Name, result.ErrorCode, result.ErrorMessage);
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "AUDIT WRITE FAILED for '{Action}' on '{Target}' by '{Actor}'.", action, target, actor.Name);
			}
		}

		/// <summary>The ban reason stored on the account row: what the moderator typed after the name, if anything.</summary>
		private static string? BanReason(string? reason) =>
			string.IsNullOrWhiteSpace(reason) ? "Banned from Discord." : reason.Trim();

		/// <summary>
		/// Bans a character or account from the Discord chat bridge.
		/// Messages from bridge-banned names are not forwarded in either direction.
		/// </summary>
		[Command("ban-bridge")]
		[Summary("Bans a character/account from the Discord-game chat bridge.")]
		public async Task BanBridgeAsync(string name, [Remainder] string reason = "No reason provided")
		{
			if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
			{
				await ReplyAsync($"Name must be between 1 and {MaxNameLength} characters.");
				return;
			}

			bool added = bridgeBanService.AddBridgeBan(name, isAccountBan: false, Context.User.Username, reason);
			if (!added)
			{
				await ReplyAsync($"'{name}' is already bridge-banned.");
				return;
			}

			await botConfigService.SavePersistentDataAsync();

			logger.LogInformation(
				"Bridge ban added for '{Name}' by {User}. Reason: {Reason}",
				name, Context.User.Username, reason);

			await ReplyAsync($"'{name}' has been **bridge-banned**. Their messages will no longer be forwarded.");
		}

		/// <summary>
		/// Removes a bridge ban from a character or account.
		/// </summary>
		[Command("unban-bridge")]
		[Summary("Removes a bridge ban from a character/account.")]
		public async Task UnbanBridgeAsync([Remainder] string name)
		{
			if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
			{
				await ReplyAsync($"Name must be between 1 and {MaxNameLength} characters.");
				return;
			}

			bool removed = bridgeBanService.RemoveBridgeBan(name);
			if (!removed)
			{
				await ReplyAsync($"'{name}' is not currently bridge-banned.");
				return;
			}

			await botConfigService.SavePersistentDataAsync();

			logger.LogInformation(
				"Bridge ban removed for '{Name}' by {User}.",
				name, Context.User.Username);

			await ReplyAsync($"Bridge ban removed for '{name}'.");
		}

		/// <summary>
		/// Lists all current bridge bans.
		/// </summary>
		[Command("bridge-bans")]
		[Summary("Lists all current bridge bans.")]
		public async Task ListBridgeBansAsync()
		{
			var bans = bridgeBanService.GetAllBans();

			if (bans.Count == 0)
			{
				await ReplyAsync("No active bridge bans.");
				return;
			}

			var sb = new StringBuilder();
			sb.AppendLine("**Active Bridge Bans:**");
			foreach (var ban in bans)
			{
				string type = ban.IsAccountBan ? "Account" : "Character";
				sb.AppendLine($"• **{ban.Name}** ({type}) — by {ban.BannedBy} on {ban.CreatedAtUtc:yyyy-MM-dd} — {ban.Reason}");
			}

			string response = sb.ToString();
			if (response.Length > 1900)
			{
				response = response.Substring(0, 1900) + "\n... (truncated)";
			}

			await ReplyAsync(response, allowedMentions: AllowedMentions.None);
		}
	}
}