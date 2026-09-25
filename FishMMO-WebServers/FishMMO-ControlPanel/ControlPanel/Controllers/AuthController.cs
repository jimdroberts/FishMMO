using System.Globalization;
using System.Security.Claims;
using FishMMO.Auth.Core;
using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Sign-in, two-factor, step-up, the self-service two-factor reset, and sign-out.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The password never reaches this controller. The browser runs SRP-6a against the same
	/// parameters the game client uses and sends a public ephemeral and a proof; the challenge
	/// endpoint takes a username and nothing else.
	/// </para>
	/// <para>
	/// Nothing here is audited: it is a player (or an operator) authenticating as themselves, on
	/// anonymous or <see cref="PanelPolicies.TwoFactorPending"/> endpoints the audit filter excludes.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/auth")]
	[EnableRateLimiting("Auth")]
	public sealed class AuthController : ControllerBase
	{
		/// <summary>Status for a two-factor step that is locked. 423, so the browser never mistakes it for a step-up 428.</summary>
		private const int LockedStatus = StatusCodes.Status423Locked;

		private readonly SrpLoginService srp;
		private readonly TwoFactorService twoFactor;
		private readonly TwoFactorResetFlow reset;
		private readonly TwoFactorResetOptions resetOptions;
		private readonly PanelSessionManager sessions;
		private readonly IWebHostEnvironment environment;
		private readonly ILogger<AuthController> log;

		public AuthController(
			SrpLoginService srp,
			TwoFactorService twoFactor,
			TwoFactorResetFlow reset,
			TwoFactorResetOptions resetOptions,
			PanelSessionManager sessions,
			IWebHostEnvironment environment,
			ILogger<AuthController> log)
		{
			this.srp = srp;
			this.twoFactor = twoFactor;
			this.reset = reset;
			this.resetOptions = resetOptions;
			this.sessions = sessions;
			this.environment = environment;
			this.log = log;
		}

		/// <summary>SRP message one.</summary>
		[HttpPost("srp/challenge")]
		[AllowAnonymous]
		public async Task<IActionResult> Challenge([FromBody] ChallengeRequest request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.Username))
			{
				return BadRequest(new { error = "A username is required." });
			}

			var result = await srp.ChallengeAsync(request.Username.Trim(), HttpContext.RequestAborted);
			if (result == null)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Try again shortly." });
			}

			return Ok(new
			{
				handle = result.Handle,
				salt = result.Salt,
				serverPublicEphemeral = result.ServerPublicEphemeral,
			});
		}

		/// <summary>SRP message two.</summary>
		/// <remarks>
		/// A locked account, a wrong password, an unknown account and a banned one all answer 401 with
		/// <see cref="SrpLoginService.GenericFailure"/>. The one different answer — 403, stage
		/// <c>verification-required</c> — is given only after a CORRECT proof, so it reveals nothing to
		/// anyone who does not already hold the password.
		/// </remarks>
		[HttpPost("srp/proof")]
		[AllowAnonymous]
		public async Task<IActionResult> Proof([FromBody] ProofRequest request)
		{
			if (request == null)
			{
				return Unauthorized(new { error = SrpLoginService.GenericFailure });
			}

			var result = await srp.SignInProofAsync(request.Handle, request.ClientPublicEphemeral, request.ClientProof, HttpContext.RequestAborted);
			if (!result.Ok)
			{
				if (result.VerificationRequired)
				{
					return StatusCode(StatusCodes.Status403Forbidden, new
					{
						error = result.Error,
						stage = "verification-required",
						username = result.Username,
						outstanding = AccountRegistrationService.ChannelNames(result.Outstanding),
					});
				}
				return Unauthorized(new { error = result.Error });
			}

			// Two-factor is required above Player. An elevated account that has not enrolled gets
			// a Player-shaped session and is told why by api/auth/session.
			bool needsTwoFactor = result.TotpEnabled;

			var issued = await sessions.IssueAsync(
				result.Username,
				result.AccessLevel,
				twoFactorSatisfied: !needsTwoFactor,
				ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
				userAgent: Request.Headers.UserAgent.ToString(),
				HttpContext.RequestAborted);

			if (!issued.Ok)
			{
				log.LogError("Could not issue a session for '{User}': {Error}", result.Username, issued.Error);
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Try again shortly." });
			}

			SetSessionCookie(issued.SessionId, issued.Session.ExpiresUtc);

			return Ok(new
			{
				stage = needsTwoFactor ? "two-factor" : "signed-in",
				username = result.Username,
				serverProof = result.ServerProof,
				session = needsTwoFactor ? null : Describe(result.Username, result.AccessLevel, true, true, DateTime.UtcNow, issued.Session.ExpiresUtc),
			});
		}

		/// <summary>Completes a pending sign-in with a TOTP or recovery code.</summary>
		/// <remarks>
		/// Counted and locked under <see cref="AuthLockoutOptions"/>. A good code also cancels any
		/// pending self-service two-factor reset, and the holder is emailed that it was.
		/// </remarks>
		[HttpPost("2fa/verify")]
		[Authorize(Policy = PanelPolicies.TwoFactorPending)]
		public async Task<IActionResult> VerifyTwoFactor([FromBody] CodeRequest request)
		{
			string username = User.Identity?.Name;
			string hash = User.FindFirstValue(PanelClaims.SessionHash);

			if (request == null || string.IsNullOrWhiteSpace(request.Code) || username == null || hash == null)
			{
				return BadRequest(new { error = "A code is required." });
			}

			if (IsTwoFactorSatisfied())
			{
				return BadRequest(new { error = "This session has already completed two-factor." });
			}

			var check = await twoFactor.VerifyForSignInAsync(username, request.Code, authenticatorOnly: false, HttpContext.RequestAborted);
			if (check.Verdict == TwoFactorService.Verdict.Unavailable)
			{
				return Unavailable();
			}
			if (check.Verdict == TwoFactorService.Verdict.Locked)
			{
				return StatusCode(LockedStatus, LockedBody(check.LockedUntilUtc));
			}
			if (check.Verdict != TwoFactorService.Verdict.Ok)
			{
				// One message for a wrong code, a used recovery code and a replayed window alike.
				return Unauthorized(new { error = "That code is not valid." });
			}

			return await PromoteAsync(username, hash);
		}

		/// <summary>Re-proves the authenticator for a destructive action.</summary>
		[HttpPost("step-up")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> StepUp([FromBody] CodeRequest request)
		{
			string username = User.Identity?.Name;
			string hash = User.FindFirstValue(PanelClaims.SessionHash);

			if (request == null || string.IsNullOrWhiteSpace(request.Code) || username == null || hash == null)
			{
				return BadRequest(new { error = "A code is required." });
			}

			var check = await twoFactor.VerifyForSignInAsync(username, request.Code, authenticatorOnly: false, HttpContext.RequestAborted);
			if (check.Verdict == TwoFactorService.Verdict.Unavailable)
			{
				return Unavailable();
			}
			if (check.Verdict == TwoFactorService.Verdict.Locked)
			{
				return StatusCode(LockedStatus, LockedBody(check.LockedUntilUtc));
			}
			if (check.Verdict != TwoFactorService.Verdict.Ok)
			{
				return Unauthorized(new { error = "That code is not valid." });
			}

			if (!await sessions.StepUpAsync(hash, HttpContext.RequestAborted))
			{
				log.LogError("Could not record a step-up for '{User}' after a valid code.", username);
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Try again shortly." });
			}
			return Ok(new { ok = true, until = DateTime.UtcNow + PanelPolicies.StepUpWindow });
		}

		// ── Self-service two-factor reset ───────────────────────────────────────
		/* For the holder who has lost the authenticator AND every recovery code. Only a session that
		 * has proven the password reaches these. The rules — the waiting period, cancellation by any
		 * normal sign-in, never satisfying two-factor on completion, never leaving the account with
		 * two-factor cleared — are kept in TwoFactorResetFlow, not here. */

		/// <summary>The pending reset for this signed-in-by-password session's account, if any.</summary>
		[HttpGet("2fa/reset")]
		[Authorize(Policy = PanelPolicies.TwoFactorPending)]
		public async Task<IActionResult> GetTwoFactorReset()
		{
			string username = User.Identity?.Name;
			var (read, pending) = await reset.PendingAsync(username, HttpContext.RequestAborted);
			if (!read)
			{
				/* Not "no reset pending": a player told their request has gone may ask again, or give up
				 * on one that is still counting down. */
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Your reset could not be checked right now. Try again shortly." });
			}
			return Ok(TwoFactorResetFlow.Describe(pending, resetOptions));
		}

		/// <summary>
		/// Asks for a delayed two-factor reset. Asking again returns the same request; the clock never restarts.
		/// </summary>
		[HttpPost("2fa/reset/request")]
		[Authorize(Policy = PanelPolicies.TwoFactorPending)]
		public async Task<IActionResult> RequestTwoFactorReset()
		{
			string username = User.Identity?.Name;
			if (IsTwoFactorSatisfied())
			{
				return BadRequest(new { error = "This session has already completed two-factor, so the authenticator is not lost." });
			}

			var (request, error) = await reset.RequestAsync(username, HttpContext.Connection.RemoteIpAddress?.ToString(), HttpContext.RequestAborted);
			if (request == null)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error });
			}

			return Ok(new
			{
				message = "Two-factor reset requested. It takes effect after the waiting period. An email has been sent to the address on the account; " +
						  "signing in with the authenticator or a recovery code before then cancels it.",
				reset = TwoFactorResetFlow.Describe(request, resetOptions),
			});
		}

		/// <summary>
		/// Uses an effective reset: the old factor is replaced and the new one handed over. Does NOT sign in.
		/// </summary>
		/// <remarks>
		/// Every panel session on the account is revoked, this one included, and this browser is given a
		/// fresh session that has proven the password only. It becomes a signed-in session through
		/// <c>2fa/reset/confirm</c> with a code from the new authenticator, and not before.
		/// </remarks>
		[HttpPost("2fa/reset/complete")]
		[Authorize(Policy = PanelPolicies.TwoFactorPending)]
		public async Task<IActionResult> CompleteTwoFactorReset()
		{
			string username = User.Identity?.Name;
			if (IsTwoFactorSatisfied())
			{
				return BadRequest(new { error = "This session has already completed two-factor." });
			}

			var outcome = await reset.CompleteAsync(username, HttpContext.RequestAborted);
			switch (outcome.Status)
			{
				case TwoFactorResetFlow.CompletionStatus.NoRequest:
					return NotFound(new { error = outcome.Error });
				case TwoFactorResetFlow.CompletionStatus.NotYetEffective:
					return Conflict(new { error = outcome.Error, effectiveUtc = outcome.EffectiveUtc });
				case TwoFactorResetFlow.CompletionStatus.Failed:
					return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = outcome.Error });
			}

			var reissued = await sessions.IssueAsync(
				username, GetAccessLevel(), twoFactorSatisfied: false,
				HttpContext.Connection.RemoteIpAddress?.ToString(),
				Request.Headers.UserAgent.ToString(), HttpContext.RequestAborted);
			if (reissued.Ok)
			{
				SetSessionCookie(reissued.SessionId, reissued.Session.ExpiresUtc);
			}
			else
			{
				log.LogWarning("Two-factor reset completed for '{User}' but a pending session could not be reissued.", username);
				Response.Cookies.Delete(PanelSessionManager.CookieName);
			}

			// Shown once, exactly as registration shows them. Nothing here is retrievable again.
			return Ok(new
			{
				stage = "two-factor-reset",
				username,
				otpauthUri = outcome.OtpauthUri,
				recoveryCodes = outcome.RecoveryCodes,
				// When true the browser must sign in again (with the password) and confirm from there.
				signedOut = !reissued.Ok,
				// False when some other browser or game client may still be signed in; see TwoFactorResetFlow.
				otherSessionsSignedOut = outcome.SignedOutElsewhere,
			});
		}

		/// <summary>
		/// Finishes a completed reset with a code from the NEW authenticator, and only then signs in.
		/// </summary>
		/// <remarks>
		/// Recovery codes are refused here: the point of this step is proving the new authenticator
		/// was actually saved, and a recovery code shown on the same screen proves nothing about that.
		/// </remarks>
		[HttpPost("2fa/reset/confirm")]
		[Authorize(Policy = PanelPolicies.TwoFactorPending)]
		public async Task<IActionResult> ConfirmTwoFactorReset([FromBody] CodeRequest request)
		{
			string username = User.Identity?.Name;
			string hash = User.FindFirstValue(PanelClaims.SessionHash);
			if (request == null || string.IsNullOrWhiteSpace(request.Code) || username == null || hash == null)
			{
				return BadRequest(new { error = "A code is required." });
			}
			if (IsTwoFactorSatisfied())
			{
				return BadRequest(new { error = "This session has already completed two-factor." });
			}

			var check = await twoFactor.VerifyForSignInAsync(username, request.Code, authenticatorOnly: true, HttpContext.RequestAborted);
			if (check.Verdict == TwoFactorService.Verdict.Unavailable)
			{
				return Unavailable();
			}
			if (check.Verdict == TwoFactorService.Verdict.Locked)
			{
				return StatusCode(LockedStatus, LockedBody(check.LockedUntilUtc));
			}
			if (check.Verdict != TwoFactorService.Verdict.Ok)
			{
				return Unauthorized(new { error = "That code is not valid. Enter the six-digit code your NEW authenticator shows." });
			}

			return await PromoteAsync(username, hash);
		}

		/// <summary>Describes the current session, or null when there is none.</summary>
		[HttpGet("session")]
		[AllowAnonymous]
		public IActionResult Session()
		{
			if (User.Identity?.IsAuthenticated != true)
			{
				/* "Not signed in" and "could not tell" are different answers. The handler keeps the
				 * cookie when the database fails; answering null here would show the sign-in screen
				 * to somebody whose session is fine and merely could not be read. */
				if (HttpContext.Items.ContainsKey(PanelAuthenticationHandler.UnavailableKey))
				{
					return Unavailable();
				}
				return Ok((object)null);
			}

			byte accessLevel = GetAccessLevel();
			bool twoFactorSatisfied = IsTwoFactorSatisfied();
			bool totpEnrolled = User.FindFirstValue(PanelClaims.TotpEnrolled) == "true";

			DateTime? lastStepUp = null;
			if (DateTime.TryParse(User.FindFirstValue(PanelClaims.LastStepUp), null,
					DateTimeStyles.RoundtripKind, out DateTime parsed))
			{
				lastStepUp = parsed;
			}

			return Ok(Describe(
				User.Identity.Name, accessLevel, twoFactorSatisfied, totpEnrolled,
				lastStepUp, null));
		}

		/// <summary>Ends this session.</summary>
		/// <remarks>
		/// The cookie is deleted whatever happens, but "signed out" is only answered once the session
		/// row is revoked. Deleting the cookie ends this browser's copy; the row is what ends every
		/// other copy of it, and a logout that reported success while the row lived on left a copied
		/// cookie working until the idle timeout.
		/// </remarks>
		[HttpPost("logout")]
		[AllowAnonymous]
		public async Task<IActionResult> Logout()
		{
			/* From the cookie when there is no authenticated session to read it from — which is also the
			 * case when the database could not authenticate this request. Revoking by the cookie's own
			 * hash can only ever end the session the caller already holds. */
			string hash = User.FindFirstValue(PanelClaims.SessionHash);
			if (hash == null &&
				Request.Cookies.TryGetValue(PanelSessionManager.CookieName, out string cookie) &&
				!string.IsNullOrWhiteSpace(cookie))
			{
				hash = PanelSessionManager.Hash(cookie);
			}
			bool revoked = hash == null || await sessions.RevokeAsync(hash, HttpContext.RequestAborted);
			Response.Cookies.Delete(PanelSessionManager.CookieName);
			if (!revoked)
			{
				log.LogError("Sign-out for '{User}' could not revoke the session on the server.", User.Identity?.Name);
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new
				{
					error = "This browser is signed out, but the session could not be ended on the server. It ends by itself after a short idle period; sign in and out again to end it now.",
				});
			}
			return Ok(new { ok = true });
		}

		private async Task<IActionResult> PromoteAsync(string username, string hash)
		{
			byte accessLevel = GetAccessLevel();
			if (!await sessions.PromoteAsync(hash, accessLevel, HttpContext.RequestAborted))
			{
				// The code was right but the session did not persist. Saying "signed in" here
				// would hand back a session every later policy check refuses.
				log.LogError("Could not promote the session for '{User}' after a valid code.", username);
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Try again shortly." });
			}

			DateTime expiresUtc = DateTime.UtcNow + PanelSessionManager.LifetimeFor(accessLevel);
			// Re-issue the cookie so its own expiry matches the promoted session's.
			if (Request.Cookies.TryGetValue(PanelSessionManager.CookieName, out string sessionId))
			{
				SetSessionCookie(sessionId, expiresUtc);
			}

			return Ok(new
			{
				stage = "signed-in",
				session = Describe(username, accessLevel, true, true, DateTime.UtcNow, expiresUtc),
			});
		}

		/// <summary>
		/// A locked two-factor step, stated plainly. The session has proven the password, so there is
		/// no account-existence secret left to keep.
		/// </summary>
		private static object LockedBody(DateTime? lockedUntilUtc) => new
		{
			error = lockedUntilUtc.HasValue
				? $"Two-factor sign-in is locked after repeated wrong codes, until {DateTime.SpecifyKind(lockedUntilUtc.Value, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}."
				: "Two-factor sign-in is locked after repeated wrong codes. Try again later.",
			locked = true,
			lockedUntilUtc,
		};

		private bool IsTwoFactorSatisfied() => User.FindFirstValue(PanelClaims.TwoFactorSatisfied) == "true";

		/// <summary>The answer when the database could not decide: never "invalid", never "locked".</summary>
		private IActionResult Unavailable() =>
			StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Try again shortly." });

		private byte GetAccessLevel()
		{
			return byte.TryParse(User.FindFirstValue(PanelClaims.AccessLevel), out byte level) ? level : (byte)0;
		}

		private void SetSessionCookie(string sessionId, DateTime expiresUtc)
		{
			Response.Cookies.Append(
				PanelSessionManager.CookieName,
				sessionId,
				PanelSessionManager.BuildCookieOptions(expiresUtc, environment.IsDevelopment()));
		}

		private static object Describe(
			string username, byte accessLevel, bool twoFactorSatisfied, bool totpEnrolled,
			DateTime? lastStepUp, DateTime? expiresUtc)
		{
			bool stepUpValid = lastStepUp.HasValue && DateTime.UtcNow - lastStepUp.Value <= PanelPolicies.StepUpWindow;

			return new
			{
				username,
				accessLevel,
				levelName = ((AccessLevel)accessLevel).ToString(),
				twoFactorSatisfied,
				totpEnabled = totpEnrolled,
				// An elevated account without an authenticator can reach only its own pages.
				// The panel says so rather than silently hiding half the interface.
				twoFactorRequired = accessLevel >= (byte)AccessLevel.GameMaster && !totpEnrolled,
				stepUpValidUntil = stepUpValid ? lastStepUp.Value + PanelPolicies.StepUpWindow : (DateTime?)null,
				expiresUtc,
			};
		}

		/// <summary>Body of the SRP challenge request. Deliberately has no password field.</summary>
		public sealed class ChallengeRequest
		{
			/// <summary>Account name to begin an exchange for.</summary>
			public string Username { get; set; }
		}

		/// <summary>Body of the SRP proof request.</summary>
		public sealed class ProofRequest
		{
			/// <summary>Opaque handle from the challenge response.</summary>
			public string Handle { get; set; }

			/// <summary>The client's public ephemeral, as lowercase hex.</summary>
			public string ClientPublicEphemeral { get; set; }

			/// <summary>The client's proof, as lowercase hex.</summary>
			public string ClientProof { get; set; }
		}

		/// <summary>Body of a code submission.</summary>
		public sealed class CodeRequest
		{
			/// <summary>A six-digit TOTP code, or a recovery code in XXXX-XXXX-XXXX-XXXX form.</summary>
			public string Code { get; set; }
		}
	}
}
