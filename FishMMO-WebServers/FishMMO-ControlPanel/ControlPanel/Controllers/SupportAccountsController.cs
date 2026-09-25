using FishMMO.Auth.Core;
using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Account search, inspection and moderation for support staff.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Moderation sits at <see cref="PanelPolicies.SupportStepUp"/> rather than at Operator:
	/// banning is a game master's daily work, and requiring an administrator for it means the
	/// person actually handling the report cannot act. The step-up is what makes that safe — a
	/// borrowed unlocked session cannot ban anybody without the authenticator.
	/// </para>
	/// <para>
	/// Every action here is recorded by <see cref="AuditActionFilter"/>. Nothing in this file
	/// calls an audit method; it assigns to <see cref="AuditScope"/> only where it knows
	/// something the filter cannot.
	/// </para>
	/// <para>
	/// <b>What is deliberately absent.</b> No password reset: an operator who can set a password
	/// can sign in as the player, and SRP exists so that nobody holds a credential they can
	/// replay. A player who has lost their password uses the recovery path. No account deletion
	/// either — it would take the audit record of that account's history with it.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/support/accounts")]
	public sealed class SupportAccountsController : ControllerBase
	{
		private readonly IAccountService accounts;
		private readonly ICharacterService characters;
		private readonly IAuthTokenService authTokens;
		private readonly IWebSessionService webSessions;
		private readonly ITwoFactorRecoveryCodeService recoveryCodes;
		private readonly IKickRequestService kickRequests;
		private readonly IAdminAuditService auditLog;
		private readonly ISupportTicketService tickets;
		private readonly ITwoFactorResetRequestService resetRequests;
		private readonly IBetaCodeService betaCodes;
		private readonly SecurityNoticeService notices;
		private readonly AuditScope audit;
		private readonly ILogger<SupportAccountsController> log;

		/// <summary>Most tickets each history list carries; the total is always reported.</summary>
		private const int HistoryTicketCount = 10;

		/// <summary>Most moderation rows the history carries, newest first.</summary>
		private const int HistoryAuditCount = 25;

		/// <summary>Most characters whose own moderation rows are read into the history.</summary>
		private const int HistoryCharacterLimit = 12;

		public SupportAccountsController(
			IAccountService accounts,
			ICharacterService characters,
			IAuthTokenService authTokens,
			IWebSessionService webSessions,
			ITwoFactorRecoveryCodeService recoveryCodes,
			IKickRequestService kickRequests,
			IAdminAuditService auditLog,
			ISupportTicketService tickets,
			ITwoFactorResetRequestService resetRequests,
			IBetaCodeService betaCodes,
			SecurityNoticeService notices,
			AuditScope audit,
			ILogger<SupportAccountsController> log)
		{
			this.resetRequests = resetRequests;
			this.betaCodes = betaCodes;
			this.notices = notices;
			this.auditLog = auditLog;
			this.tickets = tickets;
			this.accounts = accounts;
			this.characters = characters;
			this.authTokens = authTokens;
			this.webSessions = webSessions;
			this.recoveryCodes = recoveryCodes;
			this.kickRequests = kickRequests;
			this.audit = audit;
			this.log = log;
		}

		/// <summary>Searches accounts by name or email prefix.</summary>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> Search(
			[FromQuery] string query,
			[FromQuery] byte? accessLevel,
			[FromQuery] bool includeBanned = true,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 25)
		{
			var result = await accounts.SearchAdminAsync(new AccountAdminQuery
			{
				Query = query,
				AccessLevel = accessLevel,
				IncludeBanned = includeBanned,
				Page = page,
				PageSize = pageSize,
			}, HttpContext.RequestAborted);

			if (!result.IsSuccess)
			{
				return DatabaseReplies.Failure(this, result, log, "That search could not be run.");
			}

			var data = result.Data;
			return Ok(new
			{
				items = data.Items.Select(Summarise),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>One account, with its characters.</summary>
		[HttpGet("{username}")]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> Get(string username)
		{
			var result = await accounts.FetchAdminAsync(username, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return DatabaseReplies.Failure(this, result, log, "That account could not be read.", notFound: "No such account.");
			}

			/* An exact per-account fetch, not the character search filtered afterwards. The
			 * search matches a PREFIX, so an account whose name merely starts the same way
			 * shares the result set and, on a page boundary, pushes this account's own
			 * characters out of it. A detail page that quietly omits characters is worse than
			 * one that fails. */
			var characterResult = await characters.FetchAdminByAccountAsync(username, true, HttpContext.RequestAborted);

			/* The detail page, and only the detail page, carries the personal data and the security
			 * state: a lookup of one named account is a deliberate read, recorded by the audit filter.
			 * Search results are skimmed by the page, and the list projection stays free of it. */
			var reset = await resetRequests.FetchPendingAsync(result.Data.Name, HttpContext.RequestAborted);
			var beta = await betaCodes.FetchForAccountAsync(result.Data.Name, HttpContext.RequestAborted);

			/* A section that could not be read is NAMED, never shown empty. Each of these reads has an
			 * empty answer that is ordinary — no characters, no pending reset, no beta codes — so an
			 * empty list standing in for a failed read is indistinguishable from the truth, and it is
			 * the page staff decide a ban or a two-factor cancellation on. The account the operator
			 * asked for is still shown; the page says which parts of it are missing. */
			var incomplete = new List<string>();
			if (!characterResult.IsSuccess) NoteUnread(incomplete, "characters", username, characterResult.ErrorCode, characterResult.ErrorMessage);
			if (!reset.IsSuccess) NoteUnread(incomplete, "pendingTwoFactorReset", username, reset.ErrorCode, reset.ErrorMessage);
			if (!beta.IsSuccess) NoteUnread(incomplete, "betaCodes", username, beta.ErrorCode, beta.ErrorMessage);

			return Ok(Flatten(
				new[]
				{
					Summarise(result.Data),
					Detail(result.Data, reset.IsSuccess ? reset.Data : null, beta.IsSuccess ? beta.Data : null),
					new { incomplete },
				},
				characterResult.IsSuccess
					? characterResult.Data.Select(SupportCharactersController.Summarise)
					: Enumerable.Empty<object>()));
		}

		/// <summary>Records a section of an account page that could not be read, and logs why.</summary>
		private void NoteUnread(List<string> incomplete, string section, string username, string? errorCode, string? errorMessage)
		{
			incomplete.Add(section);
			log.LogWarning("Account page for '{Account}': {Section} could not be read: [{Code}] {Message}",
				username, section, errorCode, errorMessage);
		}

		/// <summary>
		/// Pending self-service two-factor resets across every account, soonest to take effect first.
		/// </summary>
		/// <remarks>
		/// A reset about to take effect is the moment somebody should look: if the account holder did not
		/// ask for it, staff cancelling it is the last line. Game master readable; recorded like every read.
		/// </remarks>
		[HttpGet("2fa-resets")]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> PendingTwoFactorResets([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
		{
			var result = await resetRequests.SearchPendingAsync(Math.Max(1, page), pageSize, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The pending two-factor resets could not be read." });
			}
			return Ok(new
			{
				items = result.Data.Items.Select(r => ProjectReset(r)),
				page = result.Data.Page,
				pageSize = result.Data.PageSize,
				totalCount = result.Data.TotalCount,
			});
		}

		/// <summary>Lifts both sign-in lockouts on an account.</summary>
		/// <remarks>
		/// For a player locked out by somebody else guessing at their account. Step-up, like every
		/// moderation write, because lifting a lock also lifts the brake on whoever was guessing.
		/// </remarks>
		[HttpPost("{username}/clear-lockout")]
		[Authorize(Policy = PanelPolicies.SupportStepUp)]
		[Audited(AuditActions.AccountClearLockout, TargetType = "account", TargetRouteValue = "username")]
		public async Task<IActionResult> ClearLockout(string username, [FromBody] ReasonRequest request)
		{
			var (target, failure) = await ResolveAsync(username, request);
			if (failure != null)
			{
				return failure;
			}

			audit.Details = new
			{
				loginLockedUntilUtc = target.LoginLockedUntilUtc,
				twoFactorLockedUntilUtc = target.TwoFactorLockedUntilUtc,
			};

			var cleared = await accounts.ClearAuthLockoutAsync(username, HttpContext.RequestAborted);
			if (!cleared.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(cleared);
				return DatabaseReplies.Failure(this, cleared, log, "The lockout could not be cleared.");
			}

			log.LogWarning("Sign-in lockout cleared for '{Account}' by '{Actor}' (was locked: {Locked}). Reason: {Reason}",
				username, User.Identity?.Name, cleared.Data, request.Reason);
			return Ok(new
			{
				message = cleared.Data
					? "Sign-in lockout cleared. The player can try again now."
					: "Nothing was locked on this account.",
				wasLocked = cleared.Data,
			});
		}

		/// <summary>Brings a pending self-service two-factor reset forward, to now or a chosen earlier time.</summary>
		/// <remarks>
		/// Removes the protection the waiting period gives, so it is a step-up write with a mandatory
		/// reason, and the holder is emailed. Only ever earlier: a longer wait is a cancel and a new request.
		/// </remarks>
		[HttpPost("{username}/2fa-reset/shorten")]
		[Authorize(Policy = PanelPolicies.SupportStepUp)]
		[Audited(AuditActions.AccountTwoFactorResetShorten, TargetType = "account", TargetRouteValue = "username")]
		public async Task<IActionResult> ShortenTwoFactorReset(string username, [FromBody] ShortenResetRequest request)
		{
			var (target, failure) = await ResolveAsync(username, request == null ? null : new ReasonRequest { Reason = request.Reason });
			if (failure != null)
			{
				return failure;
			}
			if (request.Reason.Trim().Length > 256)
			{
				return BadRequest(new { error = "The reason must be 256 characters or fewer." });
			}

			var pending = await resetRequests.FetchPendingAsync(target.Name, HttpContext.RequestAborted);
			if (!pending.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(pending);
				return DatabaseReplies.Failure(this, pending, log, "The pending reset could not be read.");
			}
			if (pending.Data == null)
			{
				audit.Outcome = "Refused: no pending two-factor reset.";
				return NotFound(new { error = "There is no pending two-factor reset on this account." });
			}

			DateTime now = DateTime.UtcNow;
			DateTime effective = request.EffectiveUtc.HasValue ? AsUtc(request.EffectiveUtc.Value) : now;
			if (effective < now)
			{
				effective = now;
			}
			if (effective >= pending.Data.EffectiveUtc)
			{
				audit.Outcome = "Refused: not earlier than the current effective time.";
				return BadRequest(new { error = "A reset can only be brought forward. Choose a time before it currently takes effect." });
			}

			audit.Details = new { requestId = pending.Data.ID, previousEffectiveUtc = pending.Data.EffectiveUtc, newEffectiveUtc = effective };

			var shortened = await resetRequests.ShortenAsync(pending.Data.ID, effective, User.Identity?.Name, request.Reason.Trim(), HttpContext.RequestAborted);
			if (!shortened.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(shortened);
				return DatabaseReplies.Failure(this, shortened, log, "The reset could not be brought forward.");
			}

			log.LogWarning("Two-factor reset for '{Account}' brought forward to {Effective:o} by '{Actor}'. Reason: {Reason}",
				username, effective, User.Identity?.Name, request.Reason);
			await notices.ResetShortenedAsync(target.Name, effective, HttpContext.RequestAborted);
			return Ok(new { message = "Reset brought forward. The account holder has been emailed.", effectiveUtc = effective });
		}

		/// <summary>Cancels a pending self-service two-factor reset.</summary>
		[HttpPost("{username}/2fa-reset/cancel")]
		[Authorize(Policy = PanelPolicies.SupportStepUp)]
		[Audited(AuditActions.AccountTwoFactorResetCancel, TargetType = "account", TargetRouteValue = "username")]
		public async Task<IActionResult> CancelTwoFactorReset(string username, [FromBody] ReasonRequest request)
		{
			var (target, failure) = await ResolveAsync(username, request);
			if (failure != null)
			{
				return failure;
			}
			if (request.Reason.Trim().Length > 256)
			{
				return BadRequest(new { error = "The reason must be 256 characters or fewer." });
			}

			var pending = await resetRequests.FetchPendingAsync(target.Name, HttpContext.RequestAborted);
			if (pending.IsSuccess && pending.Data != null)
			{
				audit.Details = new { requestId = pending.Data.ID, effectiveUtc = pending.Data.EffectiveUtc };
			}

			var cancelled = await resetRequests.CancelAsync(target.Name, User.Identity?.Name, request.Reason.Trim(), HttpContext.RequestAborted);
			if (!cancelled.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(cancelled);
				return DatabaseReplies.Failure(this, cancelled, log, "The reset could not be cancelled.");
			}
			if (!cancelled.Data)
			{
				audit.Outcome = "Refused: no pending two-factor reset.";
				return NotFound(new { error = "There is no pending two-factor reset on this account." });
			}

			log.LogWarning("Two-factor reset for '{Account}' cancelled by '{Actor}'. Reason: {Reason}",
				username, User.Identity?.Name, request.Reason);
			await notices.ResetCancelledAsync(target.Name, byStaff: true, HttpContext.RequestAborted);
			return Ok(new { message = "Reset cancelled. The account's two-factor is unchanged, and the account holder has been emailed." });
		}

		/// <summary>
		/// What staff need to weigh a report against an account: its standing, what has been reported
		/// about it, what it has reported, and what staff have already done to it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A report read in isolation is how the fifth complaint about the same player gets the same
		/// warning as the first. This puts the pattern beside the report.
		/// </para>
		/// <para>
		/// <b>The moderation rows are the audit log, narrowed to this account and its characters.</b>
		/// The full log stays Operator-only; a game master reading who banned this player before is
		/// doing the job, and reading everyone's actions is not. The actor's address, session and the
		/// free-form details are left out for the same reason. Tickets above the reader's tier are
		/// filtered in the query, as they are in the queue. Recorded in the audit log, like every staff read.
		/// </para>
		/// </remarks>
		[HttpGet("{username}/history")]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> History(string username, [FromQuery] long? excludeTicketId = null)
		{
			if (!Authentication.IsAllowedUsername(username))
			{
				return BadRequest(new { error = Authentication.InvalidUsernameError });
			}

			var cancellation = HttpContext.RequestAborted;
			var account = await accounts.FetchAdminAsync(username, cancellation);
			if (!account.IsSuccess)
			{
				return DatabaseReplies.Failure(this, account, log, "That account could not be read.", notFound: "No such account.");
			}

			/* Every section below has an ordinary empty answer — no reports, no staff actions, no
			 * characters, no pending reset — so a read that failed must be NAMED rather than shown as
			 * that empty answer. "No prior reports and no moderation history" is exactly what would
			 * earn a repeat offender a first warning. */
			var incomplete = new List<string>();

			// The stored name: tickets and audit rows copy it in, and match it exactly.
			string name = account.Data.Name;
			byte tier = (byte)ModerationGuards.ActorLevel(this);

			var against = await tickets.SearchAsync(new SupportTicketQuery
			{
				TargetAccount = name,
				MaxRequiredAccessLevel = tier,
				Page = 1,
				PageSize = HistoryTicketCount,
			}, cancellation);
			if (!against.IsSuccess) NoteUnread(incomplete, "reportsAgainst", name, against.ErrorCode, against.ErrorMessage);

			/* The ticket being read is not "another report". Excluded here, where the total is known:
			 * the page is ten rows sorted by priority, so a client could only subtract a ticket that
			 * happened to land on it. Only a ticket that really is about this account, at a tier the
			 * reader may see, is subtracted — the parameter cannot be used to shave a count. */
			int excluded = 0;
			if (excludeTicketId is long exclude && exclude > 0)
			{
				var current = await tickets.FetchAsync(exclude, includeInternal: false, cancellation);
				if (current.IsSuccess &&
					current.Data.RequiredAccessLevel <= tier &&
					string.Equals(current.Data.TargetAccount, name, StringComparison.Ordinal))
				{
					excluded = 1;
				}
			}

			var filed = await tickets.SearchAsync(new SupportTicketQuery
			{
				ReporterAccount = name,
				MaxRequiredAccessLevel = tier,
				Page = 1,
				PageSize = HistoryTicketCount,
			}, cancellation);
			if (!filed.IsSuccess) NoteUnread(incomplete, "filed", name, filed.ErrorCode, filed.ErrorMessage);

			var characterResult = await characters.FetchAdminByAccountAsync(name, true, cancellation);
			IReadOnlyList<CharacterAdminData> owned = characterResult.IsSuccess
				? characterResult.Data
				: Array.Empty<CharacterAdminData>();
			if (!characterResult.IsSuccess)
			{
				/* Moderation rows are read per character too, so an unread character list leaves the
				 * staff actions incomplete as well, and the page must not present them as the whole. */
				NoteUnread(incomplete, "characters", name, characterResult.ErrorCode, characterResult.ErrorMessage);
				NoteUnread(incomplete, "moderation", name, characterResult.ErrorCode, characterResult.ErrorMessage);
			}

			/* The account's own rows, then each character's. Characters are audited under their id,
			 * so "everything staff did about this player" is one query per target; bounded, because
			 * an account holds a handful of characters. */
			var moderation = new List<AdminAuditData>();
			var accountRows = await auditLog.SearchAsync(new AdminAuditQuery
			{
				TargetType = "account",
				TargetID = name,
				Page = 1,
				PageSize = HistoryAuditCount,
			}, cancellation);
			if (accountRows.IsSuccess)
			{
				moderation.AddRange(accountRows.Data.Items);
			}
			else if (!incomplete.Contains("moderation"))
			{
				NoteUnread(incomplete, "moderation", name, accountRows.ErrorCode, accountRows.ErrorMessage);
			}
			foreach (CharacterAdminData character in owned.Take(HistoryCharacterLimit))
			{
				var rows = await auditLog.SearchAsync(new AdminAuditQuery
				{
					TargetType = "character",
					TargetID = character.ID.ToString(CultureInfo.InvariantCulture),
					Page = 1,
					PageSize = HistoryAuditCount,
				}, cancellation);
				if (rows.IsSuccess)
				{
					moderation.AddRange(rows.Data.Items);
				}
				else if (!incomplete.Contains("moderation"))
				{
					NoteUnread(incomplete, "moderation", name, rows.ErrorCode, rows.ErrorMessage);
				}
			}

			// Cheap, and the one piece of account security a report is most often really about.
			var pendingReset = await resetRequests.FetchPendingAsync(name, cancellation);
			if (!pendingReset.IsSuccess) NoteUnread(incomplete, "pendingTwoFactorReset", name, pendingReset.ErrorCode, pendingReset.ErrorMessage);

			return Ok(new
			{
				incomplete,
				account = Summarise(account.Data),
				pendingTwoFactorReset = pendingReset.IsSuccess ? ProjectReset(pendingReset.Data) : null,
				reportsAgainst = TicketList(against, excludeTicketId, excluded),
				filed = TicketList(filed, excludeTicketId, 0),
				moderation = moderation
					.OrderByDescending(e => e.OccurredUtc)
					.Take(HistoryAuditCount)
					.Select(e => new
					{
						occurredUtc = e.OccurredUtc,
						actor = e.ActorName,
						action = e.Action,
						targetType = e.TargetType,
						targetId = e.TargetID,
						targetName = e.TargetName,
						reason = e.Reason,
						succeeded = e.Succeeded,
						outcome = e.Outcome,
						source = e.Source,
					}),
				characters = owned.Select(SupportCharactersController.Summarise),
			});
		}

		/// <summary>A ticket search as the history shows it: the rows, and how many there are in all.</summary>
		/// <remarks>A search that failed has no count, not a count of zero; the section is named in <c>incomplete</c>.</remarks>
		private static object TicketList(Database.DatabaseResult<SupportTicketPage> result, long? excludeId, int excludedFromTotal) => result.IsSuccess
			? new
			{
				totalCount = (int?)Math.Max(0, result.Data.TotalCount - excludedFromTotal),
				items = result.Data.Items
					.Where(t => excludeId == null || t.ID != excludeId.Value)
					.Select(t => SupportTicketsController.Project(t, includeMessages: false)),
			}
			: new
			{
				totalCount = (int?)null,
				items = Enumerable.Empty<object>(),
			};

		/// <summary>Asks the game servers to disconnect an account.</summary>
		/// <remarks>
		/// Writes a request; it does not sever a connection. The servers act on it on their next
		/// poll, which is also when a character's session lease starts running down.
		/// </remarks>
		[HttpPost("{username}/kick")]
		[Authorize(Policy = PanelPolicies.Support)]
		[Audited(AuditActions.AccountKick, TargetType = "account", TargetRouteValue = "username")]
		public async Task<IActionResult> Kick(string username, [FromBody] ReasonRequest request)
		{
			var (target, failure) = await ResolveAsync(username, request);
			if (failure != null)
			{
				return failure;
			}

			var result = await kickRequests.PersistAsync(username, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(result);
				return DatabaseReplies.Failure(this, result, log, "That kick request could not be written.");
			}

			log.LogInformation("Kick request written for '{Account}' by '{Actor}'. Reason: {Reason}",
				username, User.Identity?.Name, request.Reason);
			return Ok(new { message = "Kick request written." });
		}

		/// <summary>
		/// Bans an account: level, game tokens, panel sessions and a kick request, atomically.
		/// </summary>
		/// <remarks>
		/// All four in one transaction, in the database layer. A half-applied ban leaves the
		/// account playing, which is the failure the atomicity exists to prevent — and it is
		/// what the Discord bot's ban once did by composing the writes separately; it now calls
		/// the same service method.
		/// </remarks>
		[HttpPost("{username}/ban")]
		[Authorize(Policy = PanelPolicies.SupportStepUp)]
		[Audited(AuditActions.AccountBan, TargetType = "account", TargetRouteValue = "username")]
		public async Task<IActionResult> Ban(string username, [FromBody] ReasonRequest request)
		{
			var (target, failure) = await ResolveAsync(username, request);
			if (failure != null)
			{
				return failure;
			}

			if (target.AccessLevel == (byte)AccessLevel.Banned)
			{
				audit.Outcome = "Refused: already banned.";
				return BadRequest(new { error = "That account is already banned." });
			}

			audit.Details = new { previousLevel = (AccessLevel)target.AccessLevel };

			/* Who and why go onto the account row as well as into the audit log. The account page and
			 * the history panel read banned_by and ban_reason from the row, and the overload without
			 * them left both empty for every ban placed from here. */
			var result = await accounts.BanAsync(username, null, User.Identity?.Name, request.Reason.Trim(), HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(result);
				return DatabaseReplies.Failure(this, result, log, "That account could not be banned.");
			}

			log.LogWarning("Account '{Account}' banned by '{Actor}'. Reason: {Reason}",
				username, User.Identity?.Name, request.Reason);
			return Ok(new { message = "Account banned. Tokens and sessions revoked, and a kick request written." });
		}

		/// <summary>Lifts a ban, restoring Player.</summary>
		/// <remarks>
		/// The level only. The revoked tokens and sessions stay revoked and are not reissued: a
		/// credential that was withdrawn should not come back because a moderation decision was
		/// reversed. The player signs in again.
		/// </remarks>
		[HttpPost("{username}/unban")]
		[Authorize(Policy = PanelPolicies.SupportStepUp)]
		[Audited(AuditActions.AccountUnban, TargetType = "account", TargetRouteValue = "username")]
		public async Task<IActionResult> Unban(string username, [FromBody] ReasonRequest request)
		{
			/* Not ResolveAsync: its peer-or-superior check compares against the target's CURRENT
			 * level, and a banned account is level 0, so the check would pass trivially. The
			 * meaningful guard here is that the operator is not unbanning themselves, which they
			 * could not be doing anyway — a banned session cannot authenticate. */
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			if (!Authentication.IsAllowedUsername(username))
			{
				return BadRequest(new { error = Authentication.InvalidUsernameError });
			}

			var existing = await accounts.FetchAdminAsync(username, HttpContext.RequestAborted);
			if (!existing.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.IsNotFound(existing.ErrorCode) ? "Refused: no such account." : DatabaseReplies.Outcome(existing);
				return DatabaseReplies.Failure(this, existing, log, "That account could not be read.", notFound: "No such account.");
			}
			audit.TargetName = username;

			if (existing.Data.AccessLevel != (byte)AccessLevel.Banned)
			{
				audit.Outcome = "Refused: not banned.";
				return BadRequest(new { error = "That account is not banned." });
			}

			var result = await accounts.UnbanAsync(username, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(result);
				return DatabaseReplies.Failure(this, result, log, "That ban could not be lifted.");
			}

			log.LogWarning("Account '{Account}' unbanned by '{Actor}'. Reason: {Reason}",
				username, User.Identity?.Name, request.Reason);
			return Ok(new { message = "Ban lifted. The account is a Player again and must sign in afresh." });
		}

		/// <summary>Revokes every game token, forcing re-authentication.</summary>
		[HttpPost("{username}/revoke-tokens")]
		[Authorize(Policy = PanelPolicies.SupportStepUp)]
		[Audited(AuditActions.AccountRevokeTokens, TargetType = "account", TargetRouteValue = "username")]
		public async Task<IActionResult> RevokeTokens(string username, [FromBody] ReasonRequest request)
		{
			var (target, failure) = await ResolveAsync(username, request);
			if (failure != null)
			{
				return failure;
			}

			var tokens = await authTokens.RevokeAllForAccountAsync(username, HttpContext.RequestAborted);
			if (!tokens.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(tokens);
				return DatabaseReplies.Failure(this, tokens, log, "Those tokens could not be revoked.");
			}

			/* Panel sessions too. An operator revoking "every token" and leaving a live browser
			 * session behind has not done what they were told they did, and the gap is exactly
			 * where a compromised account keeps its foothold.
			 *
			 * So a failure here fails the request. It used to be folded into the audit detail as a
			 * count of zero — indistinguishable from an account that had no sessions — under a 200
			 * the filter recorded as a success. Both revocations are idempotent, so the operator's
			 * retry simply does the whole thing again. */
			var sessions = await webSessions.RevokeAllForAccountAsync(username, HttpContext.RequestAborted);
			if (!sessions.IsSuccess)
			{
				log.LogError("Game tokens for '{Account}' were revoked by '{Actor}' but panel sessions were NOT: [{Code}] {Message}",
					username, User.Identity?.Name, sessions.ErrorCode, sessions.ErrorMessage);
				audit.Outcome = $"Partial: game tokens revoked, panel sessions NOT revoked [{sessions.ErrorCode}].";
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new
				{
					error = "Game tokens were revoked, but panel sessions could not be. Nothing on this account is safe to assume signed out yet; try again.",
				});
			}
			audit.Details = new { gameTokensRevoked = tokens.Data, panelSessionsRevoked = sessions.Data };

			log.LogWarning("Tokens revoked for '{Account}' by '{Actor}' ({Tokens} game tokens, {Sessions} panel sessions). Reason: {Reason}",
				username, User.Identity?.Name, tokens.Data, sessions.Data, request.Reason);
			return Ok(new { message = "Game tokens and panel sessions revoked." });
		}

		/// <summary>
		/// Clears an account's authenticator and its recovery codes.
		/// </summary>
		/// <remarks>
		/// The most dangerous action here, because it removes a security control rather than
		/// applying one: afterwards the account is reachable with the password alone until the
		/// player enrols again. It exists because a player who has lost both their authenticator
		/// and their recovery codes otherwise has no way back in at all.
		/// </remarks>
		[HttpPost("{username}/reset-2fa")]
		[Authorize(Policy = PanelPolicies.SupportStepUp)]
		[Audited(AuditActions.AccountResetTwoFactor, TargetType = "account", TargetRouteValue = "username")]
		public async Task<IActionResult> ResetTwoFactor(string username, [FromBody] ReasonRequest request)
		{
			var (target, failure) = await ResolveAsync(username, request);
			if (failure != null)
			{
				return failure;
			}

			var cleared = await accounts.ClearTotpAsync(username, HttpContext.RequestAborted);
			if (!cleared.IsSuccess)
			{
				audit.Outcome = DatabaseReplies.Outcome(cleared);
				return DatabaseReplies.Failure(this, cleared, log, "Two-factor could not be reset.");
			}

			/* The codes go with the secret. Leaving them would leave a set of working
			 * single-use passwords for an account whose second factor is supposedly cleared.
			 *
			 * Either follow-on failing fails the request rather than answering "cleared" over it:
			 * the operator must know the reset is not finished, and every step here is idempotent,
			 * so the retry repeats the lot. */
			var codes = await recoveryCodes.DeleteAllForAccountAsync(username, HttpContext.RequestAborted);
			if (!codes.IsSuccess)
			{
				log.LogError("Recovery codes for '{Account}' survived a two-factor reset: [{Code}] {Message}",
					username, codes.ErrorCode, codes.ErrorMessage);
				audit.Outcome = "Partial: the secret was cleared but the recovery codes were not.";
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new
				{
					error = "The authenticator was cleared, but the recovery codes could not be. They still work; try again.",
				});
			}

			// Sessions that already satisfied two-factor must not keep that standing.
			var sessions = await webSessions.RevokeAllForAccountAsync(username, HttpContext.RequestAborted);
			if (!sessions.IsSuccess)
			{
				log.LogError("Two-factor reset for '{Account}': panel sessions were NOT revoked: [{Code}] {Message}",
					username, sessions.ErrorCode, sessions.ErrorMessage);
				audit.Outcome = $"Partial: the secret and recovery codes were cleared, panel sessions NOT revoked [{sessions.ErrorCode}].";
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new
				{
					error = "Two-factor was cleared, but the account's panel sessions could not be signed out. Try again.",
				});
			}

			log.LogWarning("Two-factor reset for '{Account}' by '{Actor}'. Reason: {Reason}",
				username, User.Identity?.Name, request.Reason);
			return Ok(new { message = "Two-factor cleared. The account must enrol again before it can reach anything privileged." });
		}

		/// <summary>
		/// Resolves the target and applies the shared guards.
		/// </summary>
		/// <remarks>
		/// One helper rather than the same six lines in five endpoints: the fifth copy is where
		/// a guard goes missing.
		/// </remarks>
		private async Task<(AccountAdminData Target, IActionResult Failure)> ResolveAsync(string username, ReasonRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return (null, BadRequest(new { error = "A reason is required." }));
			}
			if (!Authentication.IsAllowedUsername(username))
			{
				return (null, BadRequest(new { error = Authentication.InvalidUsernameError }));
			}

			var result = await accounts.FetchAdminAsync(username, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				/* Only NOT_FOUND is "no such account". A database that did not answer is a 503 and
				 * says so in the audit row too; recording it as a refusal would log a lookup of a
				 * real account as an operator probing for one that does not exist. */
				audit.Outcome = DatabaseReplies.IsNotFound(result.ErrorCode) ? "Refused: no such account." : DatabaseReplies.Outcome(result);
				return (null, DatabaseReplies.Failure(this, result, log, "That account could not be read.", notFound: "No such account."));
			}

			audit.TargetName = username;

			IActionResult refusal = ModerationGuards.Check(this, username, (AccessLevel)result.Data.AccessLevel);
			if (refusal != null)
			{
				audit.Outcome = "Refused: the target is at or above the operator's level, or is the operator.";
				return (null, refusal);
			}
			return (result.Data, null);
		}

		/// <summary>
		/// The account projection with its characters attached, as one flat object.
		/// </summary>
		/// <remarks>
		/// Flat rather than <c>{ account, characters }</c> so the detail response is the list
		/// response plus one field. A client that has rendered an account from the list can
		/// render it from the detail without a second shape to learn.
		/// </remarks>
		private static object Flatten(IEnumerable<object> parts, IEnumerable<object> characters)
		{
			var fields = new Dictionary<string, object>();
			foreach (object part in parts)
			{
				foreach (var property in part.GetType().GetProperties())
				{
					fields[property.Name] = property.GetValue(part);
				}
			}
			fields["characters"] = characters;
			return fields;
		}

		/// <summary>
		/// The detail-only projection: personal data and security state.
		/// </summary>
		/// <remarks>
		/// Separate from <see cref="Summarise"/> on purpose. That projection feeds search results and
		/// the history panel, which list accounts; a phone number or an address has no business in a
		/// list. These fields are read when one account is opened, and that read is recorded.
		/// </remarks>
		private static object Detail(AccountAdminData a, TwoFactorResetRequestData reset, IReadOnlyList<AccountBetaCodeData> beta)
		{
			DateTime now = DateTime.UtcNow;
			return new
			{
				phone = a.Phone,
				phoneVerified = a.PhoneVerified,
				emailVerified = a.EmailVerified,
				verificationChannels = AccountRegistrationService.ChannelNames((FishMMO.Database.Data.Enums.AccountVerificationChannels)a.VerificationChannels),
				verificationEmailSentAt = a.VerificationEmailSentAt,
				verifyFailedCount = a.VerifyFailedCount,
				/* Where the one Discord DM stands, so staff answering a verification ticket can see
				 * whether the bot reached the player without asking the bot's operator. The linked
				 * Discord user's id is reduced to a yes/no: nothing on this page shows raw platform ids. */
				discordUsername = a.DiscordUsername,
				discordVerified = a.DiscordVerified,
				discordLinked = a.DiscordUserId.HasValue,
				discordDmSentAt = a.DiscordDmSentAt,
				discordDmClaimedAt = a.DiscordDmClaimedAt,
				discordDmLastError = a.DiscordDmLastError,
				realName = a.RealName,
				country = a.Country,
				address = a.Address,
				referralAccount = a.ReferralAccount,
				loginLockedUntilUtc = a.LoginLockedUntilUtc,
				loginLocked = a.LoginLockedUntilUtc.HasValue && a.LoginLockedUntilUtc.Value > now,
				twoFactorLockedUntilUtc = a.TwoFactorLockedUntilUtc,
				twoFactorLocked = a.TwoFactorLockedUntilUtc.HasValue && a.TwoFactorLockedUntilUtc.Value > now,
				pendingTwoFactorReset = ProjectReset(reset),
				betaCodes = (beta ?? Array.Empty<AccountBetaCodeData>()).Select(c => new
				{
					code = c.Code,
					program = c.Program,
					redeemedUtc = c.RedeemedUtc,
					revoked = c.CodeRevoked,
				}),
			};
		}

		/// <summary>A reset request as staff see it, or null.</summary>
		private static object ProjectReset(TwoFactorResetRequestData r) => r == null ? null : new
		{
			id = r.ID,
			accountName = r.AccountName,
			status = r.Status.ToString(),
			requestedUtc = r.RequestedUtc,
			effectiveUtc = r.EffectiveUtc,
			isEffective = r.EffectiveUtc <= DateTime.UtcNow,
			requestedIp = r.RequestedIp,
			shortenedUtc = r.ShortenedUtc,
			shortenedBy = r.ShortenedBy,
			staffReason = r.StaffReason,
		};

		private static DateTime AsUtc(DateTime value) => value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

		/// <summary>A shorten request: the reason, and the new effective time (null for now).</summary>
		public sealed class ShortenResetRequest
		{
			/// <summary>Why. Recorded, and stored on the request.</summary>
			public string Reason { get; set; } = "";

			/// <summary>The new, earlier, effective time in UTC. Null or past means now.</summary>
			public DateTime? EffectiveUtc { get; set; }
		}

		/// <summary>
		/// The account projection.
		/// </summary>
		/// <remarks>
		/// Built by hand rather than serialized from <c>AccountData</c>, which carries the salt,
		/// the verifier and the encrypted TOTP secret. Sending those to a browser because a
		/// projection was forgotten would put every account's password material behind nothing
		/// but an access-level check.
		/// </remarks>
		internal static object Summarise(AccountAdminData a) => new
		{
			name = a.Name,
			email = a.Email,
			accessLevel = a.AccessLevel,
			levelName = ((AccessLevel)a.AccessLevel).ToString(),
			age = a.Age,
			verified = a.Verified,
			totpEnabled = a.TotpEnabled,
			totpVerifiedAt = a.TotpVerifiedAt,
			created = a.Created,
			lastLogin = a.LastLogin,
			characterCount = a.CharacterCount,
			bannedUntil = a.BannedUntil,
			bannedBy = a.BannedBy,
			banReason = a.BanReason,
			muted = a.Muted,
			mutedUntil = a.MutedUntil,
			mutedBy = a.MutedBy,
			muteReason = a.MuteReason,
		};

		/// <summary>A request carrying only the audit reason.</summary>
		public sealed class ReasonRequest
		{
			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}
	}
}
