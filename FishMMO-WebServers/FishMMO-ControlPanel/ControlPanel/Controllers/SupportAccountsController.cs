using FishMMO.Auth.Core;
using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;
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
		private readonly AuditScope audit;
		private readonly ILogger<SupportAccountsController> log;

		public SupportAccountsController(
			IAccountService accounts,
			ICharacterService characters,
			IAuthTokenService authTokens,
			IWebSessionService webSessions,
			ITwoFactorRecoveryCodeService recoveryCodes,
			IKickRequestService kickRequests,
			AuditScope audit,
			ILogger<SupportAccountsController> log)
		{
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
				return BadRequest(new { error = result.ErrorMessage ?? "That search could not be run." });
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
				return NotFound(new { error = "No such account." });
			}

			/* An exact per-account fetch, not the character search filtered afterwards. The
			 * search matches a PREFIX, so an account whose name merely starts the same way
			 * shares the result set and, on a page boundary, pushes this account's own
			 * characters out of it. A detail page that quietly omits characters is worse than
			 * one that fails. */
			var characterResult = await characters.FetchAdminByAccountAsync(username, true, HttpContext.RequestAborted);

			var account = Summarise(result.Data);
			return Ok(Flatten(account, characterResult.IsSuccess
				// An empty list rather than an error: an account with no characters is ordinary,
				// and failing the page over it would hide the account the operator asked for.
				? characterResult.Data.Select(SupportCharactersController.Summarise)
				: Enumerable.Empty<object>()));
		}

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
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That kick request could not be written." });
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
		/// what the Discord bot does today by composing the writes separately.
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

			var result = await accounts.BanAsync(username, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That account could not be banned." });
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
				audit.Outcome = "Refused: no such account.";
				return NotFound(new { error = "No such account." });
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
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That ban could not be lifted." });
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
				audit.Outcome = tokens.ErrorMessage;
				return BadRequest(new { error = tokens.ErrorMessage ?? "Those tokens could not be revoked." });
			}

			/* Panel sessions too. An operator revoking "every token" and leaving a live browser
			 * session behind has not done what they were told they did, and the gap is exactly
			 * where a compromised account keeps its foothold. */
			var sessions = await webSessions.RevokeAllForAccountAsync(username, HttpContext.RequestAborted);
			audit.Details = new { panelSessionsRevoked = sessions.IsSuccess ? sessions.Data : 0 };

			log.LogWarning("Tokens revoked for '{Account}' by '{Actor}'. Reason: {Reason}",
				username, User.Identity?.Name, request.Reason);
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
				audit.Outcome = cleared.ErrorMessage;
				return BadRequest(new { error = cleared.ErrorMessage ?? "Two-factor could not be reset." });
			}

			/* The codes go with the secret. Leaving them would leave a set of working
			 * single-use passwords for an account whose second factor is supposedly cleared. */
			var codes = await recoveryCodes.DeleteAllForAccountAsync(username, HttpContext.RequestAborted);
			if (!codes.IsSuccess)
			{
				log.LogError("Recovery codes for '{Account}' survived a two-factor reset: [{Code}] {Message}",
					username, codes.ErrorCode, codes.ErrorMessage);
				audit.Outcome = "Partial: the secret was cleared but the recovery codes were not.";
			}

			// Sessions that already satisfied two-factor must not keep that standing.
			await webSessions.RevokeAllForAccountAsync(username, HttpContext.RequestAborted);

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
				audit.Outcome = "Refused: no such account.";
				return (null, NotFound(new { error = "No such account." }));
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
		private static object Flatten(object account, IEnumerable<object> characters)
		{
			var fields = account.GetType().GetProperties()
				.ToDictionary(p => p.Name, p => p.GetValue(account));
			fields["characters"] = characters;
			return fields;
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
		};

		/// <summary>A request carrying only the audit reason.</summary>
		public sealed class ReasonRequest
		{
			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}
	}
}
