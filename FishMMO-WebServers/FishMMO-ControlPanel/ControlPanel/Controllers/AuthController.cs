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
	/// Sign-in, two-factor, step-up and sign-out.
	/// </summary>
	/// <remarks>
	/// The password never reaches this controller. The browser runs SRP-6a against the same
	/// parameters the game client uses and sends a public ephemeral and a proof; the challenge
	/// endpoint takes a username and nothing else.
	/// </remarks>
	[ApiController]
	[Route("api/auth")]
	[EnableRateLimiting("Auth")]
	public sealed class AuthController : ControllerBase
	{
		private readonly SrpLoginService srp;
		private readonly TwoFactorService twoFactor;
		private readonly PanelSessionManager sessions;
		private readonly IWebHostEnvironment environment;
		private readonly ILogger<AuthController> log;

		public AuthController(
			SrpLoginService srp,
			TwoFactorService twoFactor,
			PanelSessionManager sessions,
			IWebHostEnvironment environment,
			ILogger<AuthController> log)
		{
			this.srp = srp;
			this.twoFactor = twoFactor;
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
		[HttpPost("srp/proof")]
		[AllowAnonymous]
		public async Task<IActionResult> Proof([FromBody] ProofRequest request)
		{
			if (request == null)
			{
				return Unauthorized(new { error = "That username and password do not match an account." });
			}

			var result = srp.Proof(request.Handle, request.ClientPublicEphemeral, request.ClientProof);
			if (!result.Ok)
			{
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

			if (User.FindFirstValue(PanelClaims.TwoFactorSatisfied) == "true")
			{
				return BadRequest(new { error = "This session has already completed two-factor." });
			}

			if (!await twoFactor.VerifyAsync(username, request.Code, HttpContext.RequestAborted))
			{
				// One message for a wrong code, a used recovery code and a replayed window alike.
				return Unauthorized(new { error = "That code is not valid." });
			}

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

			if (!await twoFactor.VerifyAsync(username, request.Code, HttpContext.RequestAborted))
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

		/// <summary>Describes the current session, or null when there is none.</summary>
		[HttpGet("session")]
		[AllowAnonymous]
		public IActionResult Session()
		{
			if (User.Identity?.IsAuthenticated != true)
			{
				return Ok((object)null);
			}

			byte accessLevel = GetAccessLevel();
			bool twoFactorSatisfied = User.FindFirstValue(PanelClaims.TwoFactorSatisfied) == "true";
			bool totpEnrolled = User.FindFirstValue(PanelClaims.TotpEnrolled) == "true";

			DateTime? lastStepUp = null;
			if (DateTime.TryParse(User.FindFirstValue(PanelClaims.LastStepUp), null,
					System.Globalization.DateTimeStyles.RoundtripKind, out DateTime parsed))
			{
				lastStepUp = parsed;
			}

			return Ok(Describe(
				User.Identity.Name, accessLevel, twoFactorSatisfied, totpEnrolled,
				lastStepUp, null));
		}

		/// <summary>Ends this session.</summary>
		[HttpPost("logout")]
		[AllowAnonymous]
		public async Task<IActionResult> Logout()
		{
			string hash = User.FindFirstValue(PanelClaims.SessionHash);
			if (hash != null)
			{
				await sessions.RevokeAsync(hash, HttpContext.RequestAborted);
			}
			Response.Cookies.Delete(PanelSessionManager.CookieName);
			return Ok(new { ok = true });
		}

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
