using System.Security.Claims;
using FishMMO.Auth.Core;
using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Database.Data;
using FishMMO.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Player self-service: registration, verification, and the signed-in account's own profile.
	/// </summary>
	/// <remarks>
	/// Registration performs the same steps in the same order as the LoginServer's account
	/// creation pipeline, so an account made here and an account made in the game client are
	/// indistinguishable afterwards. See <see cref="AccountRegistrationService"/>.
	/// </remarks>
	[ApiController]
	[Route("api/account")]
	public sealed class AccountController : ControllerBase
	{
		private readonly AccountRegistrationService registration;
		private readonly SelfServiceService selfService;
		private readonly PasswordResetService passwordReset;
		private readonly SrpLoginService srp;
		private readonly TwoFactorService twoFactor;
		private readonly PanelRegistrationOptions options;
		private readonly IAccountService accounts;
		private readonly IWebSessionService webSessions;
		private readonly PanelSessionManager sessions;
		private readonly IWebHostEnvironment environment;
		private readonly ILogger<AccountController> log;

		public AccountController(
			AccountRegistrationService registration,
			SelfServiceService selfService,
			PasswordResetService passwordReset,
			SrpLoginService srp,
			TwoFactorService twoFactor,
			PanelRegistrationOptions options,
			IAccountService accounts,
			IWebSessionService webSessions,
			PanelSessionManager sessions,
			IWebHostEnvironment environment,
			ILogger<AccountController> log)
		{
			this.registration = registration;
			this.selfService = selfService;
			this.passwordReset = passwordReset;
			this.srp = srp;
			this.twoFactor = twoFactor;
			this.options = options;
			this.accounts = accounts;
			this.webSessions = webSessions;
			this.sessions = sessions;
			this.environment = environment;
			this.log = log;
		}

		/// <summary>
		/// Creates an account from a browser-computed SRP salt and verifier.
		/// </summary>
		/// <remarks>
		/// There is no password parameter. The browser derives the salt and verifier locally with
		/// the same SRP rules the game client uses, so the shard never holds anything a password
		/// could be recovered from — the same property the in-game path has.
		/// </remarks>
		[HttpPost("register")]
		[AllowAnonymous]
		[EnableRateLimiting("Register")]
		public async Task<IActionResult> Register([FromBody] RegisterRequest request)
		{
			if (!options.RegistrationEnabled)
			{
				return StatusCode(StatusCodes.Status403Forbidden, new { error = "Registration is closed." });
			}

			if (request == null)
			{
				return BadRequest(new { error = "A registration body is required." });
			}

			var result = await registration.RegisterAsync(
				request.Username,
				request.Salt,
				request.Verifier,
				request.Email,
				request.Age,
				HttpContext.RequestAborted);

			if (!result.Ok)
			{
				return BadRequest(new { error = result.Error });
			}

			// The otpauth URI and the recovery codes are returned exactly once, here, and are
			// never retrievable again — the same contract the in-game TwoFactorSetup broadcast
			// has. If the player loses them they re-enrol.
			return Ok(new
			{
				message = result.AutoVerified
					? "Account created and verified."
					: "Account created. Check your email for the verification code.",
				autoVerified = result.AutoVerified,
				requiresVerification = !result.AutoVerified,
				otpauthUri = result.OtpauthUri,
				recoveryCodes = result.RecoveryCodes,
			});
		}

		/// <summary>Redeems the code emailed at registration.</summary>
		[HttpPost("verify")]
		[AllowAnonymous]
		[EnableRateLimiting("Register")]
		public async Task<IActionResult> Verify([FromBody] VerifyRequest request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.Username) || request.Code <= 0)
			{
				return BadRequest(new { error = "A username and verification code are required." });
			}

			var (ok, error) = await registration.VerifyAsync(request.Username.Trim(), request.Code, HttpContext.RequestAborted);
			if (!ok)
			{
				return BadRequest(new { error });
			}
			return Ok(new { message = "Account verified." });
		}

		// ── Password recovery ───────────────────────────────────────────────────
		/* Three anonymous endpoints for an account holder who cannot sign in at all, and so
		 * cannot be authenticated before being helped. They are [AllowAnonymous], which
		 * AuditCoverage already excludes from the audit requirement, and they deliberately
		 * write no admin_audit_log row: this is the account holder acting on their own
		 * account, not an operator acting on somebody else's.
		 *
		 * None of them touches two-factor. A reset replaces the SRP credentials and nothing
		 * else, so a mailbox alone is never enough to get into an account. */

		/// <summary>
		/// Starts password recovery for the account at an email address.
		/// </summary>
		/// <remarks>
		/// Always 200, always the same body. It says nothing about whether the address is
		/// registered, whether the account is banned, or whether the per-account resend cooldown
		/// suppressed the mail — a reset form that answered differently for a registered address
		/// would be an account enumeration oracle, and that is exactly what they are probed for.
		/// </remarks>
		[HttpPost("password-reset/request")]
		[AllowAnonymous]
		[EnableRateLimiting("Register")]
		public async Task<IActionResult> RequestPasswordReset([FromBody] PasswordResetRequest request)
		{
			var outcome = await passwordReset.RequestAsync(
				request?.Email,
				HttpContext.Connection.RemoteIpAddress?.ToString(),
				HttpContext.RequestAborted);

			/* Outside Production only, and only when the affordance is configured on, the code
			 * comes back in the body so the flow can be exercised with no mail sender running.
			 * In Production the field is absent rather than empty — the two branches below are
			 * two different response shapes on purpose. */
			if (outcome.DevelopmentCode != null)
			{
				// "devCode", not "code": the name says what it is, and the browser's own
				// recovery page already reads it under that name.
				return Ok(new { message = ResetRequestedMessage, devCode = outcome.DevelopmentCode });
			}
			return Ok(new { message = ResetRequestedMessage });
		}

		/// <summary>
		/// Returns the account name a reset code belongs to.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The browser cannot derive an SRP verifier without the username, because the username is
		/// an input to the key derivation. Holding the code already proves control of the mailbox
		/// the code was sent to, and the mail names the account, so answering this adds nothing an
		/// attacker did not already have.
		/// </para>
		/// <para>
		/// The code travels in the BODY, never in the path or the query string: URLs end up in
		/// access logs, proxy logs, Referer headers and browser history, and a reset code in any
		/// of those is a reset code somebody else can use.
		/// </para>
		/// </remarks>
		[HttpPost("password-reset/lookup")]
		[AllowAnonymous]
		[EnableRateLimiting("Register")]
		public async Task<IActionResult> LookupPasswordReset([FromBody] PasswordResetCodeRequest request)
		{
			string username = await passwordReset.LookupUsernameAsync(request?.Code, HttpContext.RequestAborted);
			if (username == null)
			{
				// Unknown, malformed, expired and already-used share one message. Telling them
				// apart would tell an attacker which guess was structurally right.
				return BadRequest(new { error = PasswordResetService.InvalidCodeError });
			}
			return Ok(new { username });
		}

		/// <summary>
		/// Completes a reset with a browser-derived salt and verifier.
		/// </summary>
		/// <remarks>
		/// There is no password parameter here either. The browser generates a fresh salt and
		/// derives the verifier locally from the username this code resolved to, so the shard
		/// never holds anything a password could be recovered from.
		/// </remarks>
		[HttpPost("password-reset/complete")]
		[AllowAnonymous]
		[EnableRateLimiting("Register")]
		public async Task<IActionResult> CompletePasswordReset([FromBody] PasswordResetCompleteRequest request)
		{
			if (request == null)
			{
				return BadRequest(new { error = "A reset code and new credentials are required." });
			}

			var result = await passwordReset.CompleteAsync(
				request.Code, request.Salt, request.Verifier, HttpContext.RequestAborted);
			if (!result.Ok)
			{
				return BadRequest(new { error = result.Error });
			}

			return Ok(new
			{
				message = "Password reset. Every browser and game client signed in to this account has been signed out.",
				twoFactorUnchanged = true,
			});
		}

		/// <summary>
		/// The one answer a reset request ever gives, registered address or not.
		/// </summary>
		private const string ResetRequestedMessage =
			"If that address has an account, a reset code is on its way to it. The code expires in 60 minutes.";

		/// <summary>
		/// Returns the account rules, so the browser can apply exactly the server's validation.
		/// </summary>
		/// <remarks>
		/// It deliberately takes no input. An endpoint that answered "is this password allowed?"
		/// would require the password to be transmitted — which is the one thing this whole
		/// design exists to prevent. The rules come from <see cref="Authentication"/>, which the
		/// LoginServer uses too, so the browser, the game client and the server cannot drift.
		/// </remarks>
		[HttpGet("policy")]
		[AllowAnonymous]
		public IActionResult Policy()
		{
			return Ok(new
			{
				registrationEnabled = options.RegistrationEnabled,
				username = new
				{
					minLength = Authentication.AccountNameMinLength,
					maxLength = Authentication.AccountNameMaxLength,
					pattern = Authentication.UsernamePattern,
					error = Authentication.InvalidUsernameError,
				},
				password = new
				{
					minLength = Authentication.AccountPasswordMinLength,
					maxLength = Authentication.AccountPasswordMaxLength,
					pattern = Authentication.PasswordPattern,
					error = Authentication.InvalidPasswordError,
				},
				email = new
				{
					pattern = Authentication.EmailPattern,
					error = "A valid email address is required to register.",
				},
				characterName = new
				{
					minLength = Authentication.CharacterNameMinLength,
					maxLength = Authentication.CharacterNameMaxLength,
					pattern = Authentication.CharacterNamePattern,
					error = Authentication.InvalidCharacterNameError,
				},
				minimumAge = 13,
			});
		}

		/// <summary>The signed-in account's own profile.</summary>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> Me()
		{
			string username = User.Identity?.Name;
			var result = await accounts.FetchForLoginAsync(username, false, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return NotFound(new { error = "That account no longer exists." });
			}

			var data = result.Data;
			// Never the salt or the verifier: this projection is the reason the panel does not
			// hand AccountData straight to a controller.
			return Ok(new
			{
				name = username,
				email = data.Email,
				age = data.Age,
				accessLevel = data.AccessLevel,
				levelName = ((AccessLevel)data.AccessLevel).ToString(),
				verified = data.Verified,
				totpEnabled = data.TotpEnabled,
				totpVerifiedAtUtc = data.TotpVerifiedAt,
				lastLoginUtc = data.LastLogin,
			});
		}

		/// <summary>The signed-in account's live panel sessions.</summary>
		[HttpGet("sessions")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> Sessions()
		{
			string username = User.Identity?.Name;

			var result = await webSessions.FetchActiveForAccountAsync(username, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Could not load sessions." });
			}

			// Which row is the caller's own, answered from the id claim rather than by
			// returning a session hash to the browser.
			_ = long.TryParse(User.FindFirstValue(PanelClaims.SessionId), out long currentId);

			return Ok(result.Data.Select(s => new
			{
				id = s.ID,
				current = s.ID == currentId,
				ipAddress = s.IpAddress,
				userAgent = s.UserAgent,
				createdUtc = s.CreatedUtc,
				lastSeenUtc = s.LastSeenUtc,
				expiresUtc = s.ExpiresUtc,
				twoFactorSatisfied = s.TwoFactorSatisfied,
			}));
		}

		/// <summary>
		/// Changes the account password.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The current password is proved by a full SRP exchange, not by sending it: the browser
		/// asks for a challenge, derives a proof from the password the user typed, and submits
		/// that alongside the new salt and verifier it derived locally. Neither password is ever
		/// transmitted, so there is nothing here that could leak one.
		/// </para>
		/// <para>
		/// Every game token and every OTHER panel session is revoked on success. The session
		/// making the change survives, because signing the user out of the page they are looking
		/// at to tell them it worked is not an improvement.
		/// </para>
		/// </remarks>
		[HttpPost("password")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
		{
			string username = User.Identity?.Name;
			string currentHash = User.FindFirstValue(PanelClaims.SessionHash);

			if (request == null || string.IsNullOrWhiteSpace(request.Handle) ||
				string.IsNullOrWhiteSpace(request.ClientPublicEphemeral) ||
				string.IsNullOrWhiteSpace(request.ClientProof) ||
				string.IsNullOrWhiteSpace(request.NewSalt) ||
				string.IsNullOrWhiteSpace(request.NewVerifier))
			{
				return BadRequest(new { error = "A current-password proof and new credentials are required." });
			}

			// The exchange must belong to THIS account, or a proof for some other account the
			// caller happens to know would be accepted here.
			var proof = srp.Proof(request.Handle, request.ClientPublicEphemeral, request.ClientProof);
			if (!proof.Ok || !string.Equals(proof.Username, username, StringComparison.OrdinalIgnoreCase))
			{
				return Unauthorized(new { error = "That current password is not correct." });
			}

			var result = await selfService.ChangePasswordAsync(
				username, request.NewSalt, request.NewVerifier, HttpContext.RequestAborted);
			if (!result.Ok)
			{
				return BadRequest(new { error = result.Error });
			}

			/* Revoke EVERY session for the account, this one included, then mint a fresh one
			 * for the browser in front of us. Revoking all and re-issuing is simpler and safer
			 * than trying to spare one row: there is no path where a stale session survives
			 * because an exclusion was computed wrongly. */
			var revoked = await sessions.RevokeAllAsync(username, HttpContext.RequestAborted);
			int revokedSessions = revoked.IsSuccess ? revoked.Data : 0;

			var reissued = await sessions.IssueAsync(
				username, GetAccessLevel(), twoFactorSatisfied: true,
				HttpContext.Connection.RemoteIpAddress?.ToString(),
				Request.Headers.UserAgent.ToString(), HttpContext.RequestAborted);

			if (reissued.Ok)
			{
				Response.Cookies.Append(PanelSessionManager.CookieName, reissued.SessionId,
					PanelSessionManager.BuildCookieOptions(reissued.Session.ExpiresUtc, environment.IsDevelopment()));
			}
			else
			{
				// The password did change, so this is not a failure — the browser simply has to
				// sign in again with the new one.
				log.LogWarning("Password changed for '{User}' but a replacement session could not be issued.", username);
				Response.Cookies.Delete(PanelSessionManager.CookieName);
			}

			return Ok(new
			{
				message = "Password changed. Every other browser and game client has been signed out.",
				revokedSessions = Math.Max(0, revokedSessions - 1),
				signedOut = !reissued.Ok,
			});
		}

		/// <summary>Changes the contact email and restarts verification.</summary>
		[HttpPost("email")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request)
		{
			string username = User.Identity?.Name;
			if (request == null || string.IsNullOrWhiteSpace(request.Email))
			{
				return BadRequest(new { error = "An email address is required." });
			}

			var result = await selfService.ChangeEmailAsync(username, request.Email.Trim(), HttpContext.RequestAborted);
			if (!result.Ok)
			{
				return BadRequest(new { error = result.Error });
			}
			return Ok(new { message = "Email changed. Check the new address for a verification code.", verificationSent = true });
		}

		/// <summary>Starts two-factor enrolment. Does not enable it.</summary>
		[HttpPost("2fa/setup")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> BeginTwoFactorSetup()
		{
			string username = User.Identity?.Name;
			var setup = await selfService.BeginTwoFactorSetupAsync(username, HttpContext.RequestAborted);
			if (!setup.Ok)
			{
				return BadRequest(new { error = setup.Error });
			}
			// Shown once. Two-factor is not on until the code is confirmed below.
			return Ok(new { otpauthUri = setup.OtpauthUri, recoveryCodes = setup.RecoveryCodes });
		}

		/// <summary>Enables two-factor once a code from the new authenticator verifies.</summary>
		[HttpPost("2fa/confirm")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> ConfirmTwoFactorSetup([FromBody] AuthController.CodeRequest request)
		{
			string username = User.Identity?.Name;
			string hash = User.FindFirstValue(PanelClaims.SessionHash);
			if (request == null || string.IsNullOrWhiteSpace(request.Code))
			{
				return BadRequest(new { error = "A code is required." });
			}

			bool verified = await twoFactor.VerifyAsync(username, request.Code, HttpContext.RequestAborted);
			var result = await selfService.ConfirmTwoFactorAsync(username, verified, HttpContext.RequestAborted);
			if (!result.Ok)
			{
				return BadRequest(new { error = result.Error });
			}

			// Enrolling counts as proving the authenticator, so the step-up window starts now.
			await sessions.StepUpAsync(hash, HttpContext.RequestAborted);
			return Ok(new { message = "Two-factor is now enabled." });
		}

		/// <summary>Turns two-factor off, invalidating every recovery code with it.</summary>
		[HttpDelete("2fa")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> DisableTwoFactor()
		{
			string username = User.Identity?.Name;

			/* Two-factor is mandatory above Player, so an operator cannot turn off the thing
			 * their own privileges depend on and keep them. */
			if (GetAccessLevel() >= (byte)AccessLevel.GameMaster)
			{
				return StatusCode(StatusCodes.Status403Forbidden, new
				{
					error = "Two-factor is required at your access level and cannot be disabled.",
				});
			}

			if (!HasFreshStepUp())
			{
				return StatusCode(StatusCodes.Status428PreconditionRequired, new
				{
					error = "This action needs a fresh two-factor code.",
				});
			}

			var result = await selfService.DisableTwoFactorAsync(username, HttpContext.RequestAborted);
			if (!result.Ok)
			{
				return BadRequest(new { error = result.Error });
			}
			return Ok(new { message = "Two-factor is now disabled." });
		}

		/// <summary>Issues a fresh set of recovery codes, discarding the previous ones.</summary>
		[HttpPost("2fa/recovery-codes")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> RegenerateRecoveryCodes()
		{
			string username = User.Identity?.Name;
			if (!HasFreshStepUp())
			{
				return StatusCode(StatusCodes.Status428PreconditionRequired, new
				{
					error = "This action needs a fresh two-factor code.",
				});
			}

			var result = await selfService.RegenerateRecoveryCodesAsync(username, HttpContext.RequestAborted);
			if (!result.Ok)
			{
				return BadRequest(new { error = result.Error });
			}
			return Ok(new { recoveryCodes = result.RecoveryCodes });
		}

		/// <summary>
		/// The signed-in account's characters.
		/// </summary>
		/// <remarks>
		/// Read-only, and deliberately so. Creating and deleting a character are in-game only —
		/// deletion spans fourteen sub-entity tables inside one unit of work in the login server,
		/// and a second implementation of that is how a shard loses data.
		/// </remarks>
		[HttpGet("/api/characters")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> Characters([FromServices] ICharacterService characters)
		{
			string username = User.Identity?.Name;
			var result = await characters.FetchManyAsync(username, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Could not load characters." });
			}
			return Ok(result.Data.Select(ToSummary));
		}

		/// <summary>One of the signed-in account's characters.</summary>
		[HttpGet("/api/characters/{id:long}")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> Character(long id, [FromServices] ICharacterService characters)
		{
			string username = User.Identity?.Name;
			var result = await characters.FetchManyAsync(username, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Could not load characters." });
			}

			// Scoped to the account: an id alone must not read somebody else's character.
			foreach (var c in result.Data)
			{
				if (c.ID == id) return Ok(ToSummary(c));
			}
			return NotFound(new { error = "No such character on this account." });
		}

		private static object ToSummary(CharacterData c) => new
		{
			id = c.ID,
			name = c.Name,
			raceId = c.RaceID,
			selected = c.Selected,
			online = c.Online,
			sceneName = c.SceneName,
			bindScene = c.BindScene,
			x = c.X,
			y = c.Y,
			z = c.Z,
			accessLevel = c.AccessLevel,
			createdUtc = c.TimeCreated,
			lastSavedUtc = c.LastSaved,
		};

		private byte GetAccessLevel() =>
			byte.TryParse(User.FindFirstValue(PanelClaims.AccessLevel), out byte level) ? level : (byte)0;

		/// <summary>Whether a two-factor code was proved inside the step-up window.</summary>
		private bool HasFreshStepUp()
		{
			string raw = User.FindFirstValue(PanelClaims.LastStepUp);
			return DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime last)
				&& DateTime.UtcNow - last <= PanelPolicies.StepUpWindow;
		}

		/// <summary>Password change request. Carries proofs and verifiers, never a password.</summary>
		public sealed class ChangePasswordRequest
		{
			/// <summary>Handle from the SRP challenge that proves the current password.</summary>
			public string Handle { get; set; } = "";

			/// <summary>Client public ephemeral from that exchange.</summary>
			public string ClientPublicEphemeral { get; set; } = "";

			/// <summary>Client proof from that exchange.</summary>
			public string ClientProof { get; set; } = "";

			/// <summary>The new SRP salt, derived in the browser.</summary>
			public string NewSalt { get; set; } = "";

			/// <summary>The new SRP verifier, derived in the browser.</summary>
			public string NewVerifier { get; set; } = "";
		}

		/// <summary>Email change request.</summary>
		public sealed class ChangeEmailRequest
		{
			/// <summary>The new contact address.</summary>
			public string Email { get; set; } = "";
		}

		/// <summary>Revokes one of the signed-in account's other sessions.</summary>
		[HttpDelete("sessions/{id:long}")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> RevokeSession(long id)
		{
			string username = User.Identity?.Name;
			var result = await webSessions.RevokeByIdAsync(username, id, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Could not revoke that session." });
			}
			return Ok(new { ok = true });
		}

		/// <summary>Registration request. Carries a salt and verifier, never a password.</summary>
		public sealed class RegisterRequest
		{
			/// <summary>Account name.</summary>
			public string Username { get; set; } = "";

			/// <summary>SRP salt, computed in the browser.</summary>
			public string Salt { get; set; } = "";

			/// <summary>SRP verifier, computed in the browser.</summary>
			public string Verifier { get; set; } = "";

			/// <summary>Contact email for verification.</summary>
			public string Email { get; set; } = "";

			/// <summary>Account holder age, for compliance.</summary>
			public int Age { get; set; }
		}

		/// <summary>Password reset request. Carries an address and nothing else.</summary>
		public sealed class PasswordResetRequest
		{
			/// <summary>The address to send a reset code to, if it has an account.</summary>
			public string Email { get; set; } = "";
		}

		/// <summary>
		/// A reset code on its way to a lookup. In the body, never the URL.
		/// </summary>
		public sealed class PasswordResetCodeRequest
		{
			/// <summary>The code from the reset email.</summary>
			public string Code { get; set; } = "";
		}

		/// <summary>
		/// Reset completion. Carries a code and new credentials, never a password.
		/// </summary>
		public sealed class PasswordResetCompleteRequest
		{
			/// <summary>The code from the reset email.</summary>
			public string Code { get; set; } = "";

			/// <summary>The new SRP salt, generated in the browser.</summary>
			public string Salt { get; set; } = "";

			/// <summary>The new SRP verifier, derived in the browser.</summary>
			public string Verifier { get; set; } = "";
		}

		/// <summary>Verification request.</summary>
		public sealed class VerifyRequest
		{
			/// <summary>Account to verify.</summary>
			public string Username { get; set; } = "";

			/// <summary>The six-digit code from the verification email.</summary>
			public int Code { get; set; }
		}

	}
}
