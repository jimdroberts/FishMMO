using System.Security.Claims;
using System.Text.Json;
using FishMMO.ControlPanel.Auth;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Writes an audit row from the current request's identity.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Controllers do not call this. <see cref="AuditActionFilter"/> does, once per privileged
	/// write, which is what makes coverage structural instead of a matter of each author
	/// remembering. A controller that wants to say more about what it did assigns to
	/// <see cref="AuditScope"/> instead.
	/// </para>
	/// <para>
	/// Everything about the actor comes from the authenticated principal, never from the request
	/// body. An operator must not be able to write a row attributing their action to somebody
	/// else, which is exactly what a client-supplied actor field would allow.
	/// </para>
	/// <para>
	/// A failed audit write never fails the action. The action has already happened by the time
	/// this is called; refusing to answer would not un-happen it, and would leave the operator
	/// believing it had not worked. The failure is logged loudly instead, because a panel that
	/// cannot record what it does needs attention even though it can still act.
	/// </para>
	/// </remarks>
	public sealed class AuditWriter
	{
		private readonly IAdminAuditService audit;
		private readonly IHttpContextAccessor accessor;
		private readonly ILogger<AuditWriter> log;

		public AuditWriter(IAdminAuditService audit, IHttpContextAccessor accessor, ILogger<AuditWriter> log)
		{
			this.audit = audit;
			this.accessor = accessor;
			this.log = log;
		}

		/// <summary>Records a successful action.</summary>
		public Task SucceededAsync(string action, string targetType, string targetId, string targetName, string reason, object details = null) =>
			WriteAsync(action, targetType, targetId, targetName, reason, true, null, details);

		/// <summary>
		/// Records a refused or failed action.
		/// </summary>
		/// <remarks>
		/// Called as often as the success path, deliberately. An operator repeatedly failing to
		/// reach something is the more interesting record, and without these rows the log cannot
		/// tell "never tried" from "tried and was stopped".
		/// </remarks>
		public Task FailedAsync(string action, string targetType, string targetId, string targetName, string reason, string outcome, object details = null) =>
			WriteAsync(action, targetType, targetId, targetName, reason, false, outcome, details);

		private async Task WriteAsync(
			string action, string targetType, string targetId, string targetName,
			string reason, bool succeeded, string outcome, object details)
		{
			HttpContext context = accessor.HttpContext;
			ClaimsPrincipal user = context?.User;

			string actor = user?.Identity?.Name;
			if (string.IsNullOrWhiteSpace(actor))
			{
				// Nothing here should be reachable unauthenticated, so this is a bug rather than
				// a case. Record it as such rather than dropping the row.
				actor = "(unauthenticated)";
				log.LogWarning("Audit write for '{Action}' had no authenticated actor.", action);
			}

			byte.TryParse(user?.FindFirstValue(PanelClaims.AccessLevel), out byte level);
			long? sessionId = long.TryParse(user?.FindFirstValue(PanelClaims.SessionId), out long parsed) ? parsed : null;

			var entry = new AdminAuditData
			{
				OccurredUtc = DateTime.UtcNow,
				ActorName = actor,
				ActorAccessLevel = level,
				ActorSessionID = sessionId,
				Action = action,
				TargetType = targetType,
				TargetID = targetId,
				TargetName = targetName,
				Reason = reason,
				Succeeded = succeeded,
				Outcome = outcome,
				Details = details == null ? null : JsonSerializer.Serialize(details),
				IpAddress = context?.Connection?.RemoteIpAddress?.ToString(),
				Source = "panel",
			};

			/* Deliberately NOT HttpContext.RequestAborted. That token is cancelled when the
			 * client goes away, which would mean a caller who disconnects at the right moment
			 * suppresses their own audit row — the action has already happened by the time this
			 * runs, so the record must not depend on the actor still listening. A standalone
			 * timeout bounds it instead, so a stalled database cannot pin the request open. */
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			var result = await audit.AppendAsync(entry, timeout.Token);
			if (!result.IsSuccess)
			{
				log.LogError("AUDIT WRITE FAILED for '{Action}' by '{Actor}': [{Code}] {Message}",
					action, actor, result.ErrorCode, result.ErrorMessage);
			}
		}
	}

	/// <summary>
	/// The action identifiers written to the log.
	/// </summary>
	/// <remarks>
	/// Constants rather than literals at each call site, because these strings are queried
	/// against and shown in a filter: a typo would silently create a second, near-identical
	/// action that no existing query matches.
	/// </remarks>
	public static class AuditActions
	{
		/// <summary>A character's stored fields were changed.</summary>
		public const string CharacterEdit = "character.edit";

		/// <summary>A soft-deleted character was brought back.</summary>
		public const string CharacterRestore = "character.restore";

		/// <summary>A character was renamed.</summary>
		public const string CharacterRename = "character.rename";

		/// <summary>A kick request was written for an account.</summary>
		public const string AccountKick = "account.kick";

		/// <summary>An account was banned: level, tokens, sessions and a kick, in one transaction.</summary>
		public const string AccountBan = "account.ban";

		/// <summary>A ban was lifted.</summary>
		public const string AccountUnban = "account.unban";

		/// <summary>Every game token for an account was revoked.</summary>
		public const string AccountRevokeTokens = "account.revoke-tokens";

		/// <summary>An account's authenticator and recovery codes were cleared.</summary>
		public const string AccountResetTwoFactor = "account.reset-2fa";

		/// <summary>An account's access level was changed.</summary>
		public const string AccountAccessLevel = "account.access-level";

		/// <summary>A support ticket was taken or handed back.</summary>
		public const string TicketAssign = "ticket.assign";

		/// <summary>Staff replied to a ticket, or wrote an internal note on it.</summary>
		public const string TicketReply = "ticket.reply";

		/// <summary>A ticket's status changed.</summary>
		public const string TicketStatus = "ticket.status";

		/// <summary>A ticket's priority changed.</summary>
		public const string TicketPriority = "ticket.priority";

		/// <summary>A server was locked against new logins.</summary>
		public const string ServerLock = "server.lock";

		/// <summary>A server was unlocked.</summary>
		public const string ServerUnlock = "server.unlock";

		/// <summary>A server shutdown was scheduled.</summary>
		public const string ServerShutdown = "server.shutdown";

		/// <summary>A scheduled server shutdown was cleared.</summary>
		public const string ServerShutdownCancel = "server.shutdown-cancel";

		/// <summary>
		/// A start, stop or restart was queued for a supervised process.
		/// </summary>
		/// <remarks>
		/// The only action in the panel that can cause a process to run on another machine, so
		/// it is the one whose audit rows matter most.
		/// </remarks>
		public const string DaemonCommand = "daemon.command";

		/// <summary>A failed verification email was queued for another attempt.</summary>
		public const string EmailRetry = "platform.email-retry";

		/// <summary>A sequenced maintenance was started across a set of servers.</summary>
		public const string MaintenanceStart = "maintenance.start";

		/// <summary>A sequenced maintenance was cancelled.</summary>
		/// <remarks>
		/// Worth its own action rather than folding into the start record: cancelling clears the
		/// shutdown deadlines but leaves every target locked, so "who cancelled it and when" is
		/// the question asked when nobody can log in afterwards.
		/// </remarks>
		public const string MaintenanceCancel = "maintenance.cancel";
	}
}
