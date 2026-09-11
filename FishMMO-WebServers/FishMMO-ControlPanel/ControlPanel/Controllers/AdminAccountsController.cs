using FishMMO.Auth.Core;
using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Administrator actions on an account.
	/// </summary>
	/// <remarks>
	/// One route. Changing an access level is the only account action that needs more than a
	/// game master, because it is the only one that can create an operator rather than
	/// discipline a player.
	/// </remarks>
	[ApiController]
	[Route("api/admin/accounts")]
	public sealed class AdminAccountsController : ControllerBase
	{
		private readonly IAccountService accounts;
		private readonly IWebSessionService webSessions;
		private readonly AuditScope audit;
		private readonly ILogger<AdminAccountsController> log;

		public AdminAccountsController(
			IAccountService accounts,
			IWebSessionService webSessions,
			AuditScope audit,
			ILogger<AdminAccountsController> log)
		{
			this.accounts = accounts;
			this.webSessions = webSessions;
			this.audit = audit;
			this.log = log;
		}

		/// <summary>
		/// Sets an account's access level.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Three guards, each closing a path from administering to escalating: not your own
		/// account, not an account at or above your own, and not granting a level at or above
		/// your own. Without the last an administrator can mint administrators, and every
		/// ceiling above becomes decorative.
		/// </para>
		/// <para>
		/// The same rules the in-game <c>/admin access</c> command applies, deliberately: two
		/// surfaces onto one capability must not disagree about who may use it, or the weaker
		/// one becomes the way round the stronger.
		/// </para>
		/// </remarks>
		[HttpPost("{username}/access-level")]
		[Authorize(Policy = PanelPolicies.OperatorStepUp)]
		[Audited(AuditActions.AccountAccessLevel, TargetType = "account", TargetRouteValue = "username")]
		public async Task<IActionResult> SetAccessLevel(string username, [FromBody] AccessLevelRequest request)
		{
			if (string.IsNullOrWhiteSpace(request?.Reason))
			{
				return BadRequest(new { error = "A reason is required." });
			}
			if (!Authentication.IsAllowedUsername(username))
			{
				return BadRequest(new { error = Authentication.InvalidUsernameError });
			}
			if (request.Level == null || !Enum.IsDefined(typeof(AccessLevel), request.Level.Value))
			{
				audit.Outcome = "Refused: not a valid access level.";
				return BadRequest(new { error = "That is not a valid access level." });
			}

			var level = (AccessLevel)request.Level.Value;
			audit.TargetName = username;

			var existing = await accounts.FetchAdminAsync(username, HttpContext.RequestAborted);
			if (!existing.IsSuccess)
			{
				audit.Outcome = "Refused: no such account.";
				return NotFound(new { error = "No such account." });
			}

			var currentLevel = (AccessLevel)existing.Data.AccessLevel;
			audit.Details = new { from = currentLevel.ToString(), to = level.ToString() };

			IActionResult refusal = ModerationGuards.Check(this, username, currentLevel)
				?? ModerationGuards.CheckGrant(this, level);
			if (refusal != null)
			{
				audit.Outcome = "Refused: the operator may not act on that account, or may not grant that level.";
				return refusal;
			}

			if (currentLevel == level)
			{
				audit.Outcome = "Refused: already at that level.";
				return BadRequest(new { error = $"That account is already {level}." });
			}

			var result = await accounts.PersistAccessLevelAsync(username, (byte)level, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				audit.Outcome = result.ErrorMessage;
				return BadRequest(new { error = result.ErrorMessage ?? "That access level could not be set." });
			}

			/* The panel's own sessions for that account end here rather than at their next
			 * request. The authentication handler already revokes a session whose level has
			 * changed, so this only makes the moment deterministic — but a demotion that waits
			 * for the demoted operator to click something is a demotion with a window in it. */
			await webSessions.RevokeAllForAccountAsync(username, HttpContext.RequestAborted);

			log.LogWarning("Account '{Account}' set from {From} to {To} by '{Actor}'. Reason: {Reason}",
				username, currentLevel, level, User.Identity?.Name, request.Reason);

			return Ok(new
			{
				message = $"'{username}' is now {level}. Their panel sessions have ended; in the game they keep their current level until they reconnect.",
			});
		}

		/// <summary>An access-level change.</summary>
		public sealed class AccessLevelRequest
		{
			/// <summary>The new level, as the numeric enum value.</summary>
			public byte? Level { get; set; }

			/// <summary>Why the action was taken. Recorded.</summary>
			public string Reason { get; set; } = "";
		}
	}
}
