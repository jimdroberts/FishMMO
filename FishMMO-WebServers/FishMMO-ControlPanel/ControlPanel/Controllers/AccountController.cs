using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using FishMMO.Auth.Core;
using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Shared;
using AccessLevel = FishMMO.Auth.Core.AccessLevel;
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
		private readonly RegistrationFormTokenService formTokens;
		private readonly IBetaCodeService betaCodes;
		private readonly VerificationOptions verification;
		private readonly BetaOptions beta;
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
			RegistrationFormTokenService formTokens,
			IBetaCodeService betaCodes,
			VerificationOptions verification,
			BetaOptions beta,
			ILogger<AccountController> log)
		{
			this.formTokens = formTokens;
			this.betaCodes = betaCodes;
			this.verification = verification;
			this.beta = beta;
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

			/* The honeypot. A missing, forged or expired form token, a form submitted faster than a
			 * person can fill one in, or a value in any decoy field is refused with EXACTLY the answer a
			 * genuine creation failure gets, and a line in the log. Nothing in the response says why;
			 * a bot told which check tripped is a bot that passes it next time. See
			 * RegistrationFormTokenService and the README. */
			string refusal = formTokens.Check(request.FormToken, DecoyValues(request.Extra));
			if (refusal != null)
			{
				log.LogWarning("Registration refused by the form check from {Ip}: {Reason}.",
					HttpContext.Connection.RemoteIpAddress?.ToString(), refusal);
				return BadRequest(new { error = AccountRegistrationService.CouldNotCreateError, field = (string)null });
			}

			var channels = AccountVerificationChannels.None;
			if (request.VerifyEmail) channels |= AccountVerificationChannels.Email;
			if (request.VerifySms) channels |= AccountVerificationChannels.Sms;
			if (request.VerifyDiscord) channels |= AccountVerificationChannels.Discord;

			var result = await registration.RegisterAsync(new AccountRegistrationService.RegistrationInput(
				request.Username,
				request.Salt,
				request.Verifier,
				request.Email,
				request.Age,
				new AccountProfileData
				{
					Phone = request.Phone,
					RealName = request.RealName,
					Country = request.Country,
					Address = request.Address,
					ReferralAccount = request.ReferralAccount,
					DiscordUsername = request.DiscordUsername,
					VerificationChannels = channels,
				},
				request.BetaCode), HttpContext.RequestAborted);

			if (!result.Ok)
			{
				// `field` names the input the message belongs beside, when it belongs to one.
				return BadRequest(new { error = result.Error, field = result.Field });
			}

			string[] pending = AccountRegistrationService.ChannelNames(result.VerificationPending);
			string message = result.AutoVerified
				? "Account created and verified."
				: RegisteredMessage(result.VerificationPending);

			// The otpauth URI and the recovery codes are returned exactly once, here, and are
			// never retrievable again — the same contract the in-game TwoFactorSetup broadcast
			// has. If the player loses them they re-enrol.
			return Ok(new
			{
				message,
				autoVerified = result.AutoVerified,
				requiresVerification = !result.AutoVerified,
				verificationPending = pending,
				otpauthUri = result.OtpauthUri,
				recoveryCodes = result.RecoveryCodes,
				betaRedeemed = result.BetaRedeemed,
				betaWarning = result.BetaWarning,
				// Set when the account was created without two-factor; nothing to hand over is shown then.
				twoFactorWarning = result.TwoFactorWarning,
			});
		}

		/// <summary>
		/// Issues a registration form: a signed token and the randomised decoy fields to render.
		/// </summary>
		/// <remarks>
		/// Rate-limited under the registration bucket, because a token is only ever wanted by
		/// somebody about to register. The in-game client gets nothing like it — bots there speak the
		/// protocol, not the page — see the README.
		/// </remarks>
		[HttpGet("register/form")]
		[AllowAnonymous]
		[EnableRateLimiting("Register")]
		public IActionResult RegistrationForm()
		{
			var form = formTokens.Issue();
			return Ok(new
			{
				token = form.Token,
				decoys = form.Decoys.Select(d => new { name = d.Name, label = d.Label }),
			});
		}

		/// <summary>What a new, unverified account is told about the codes on their way.</summary>
		/// <remarks>
		/// The Discord code is the one that does not arrive on its own: the bot can only message a member
		/// of the game's Discord server, so the player is told to join, and that the code comes once.
		/// </remarks>
		private static string RegisteredMessage(AccountVerificationChannels pending)
		{
			var places = new List<string>(3);
			if ((pending & AccountVerificationChannels.Email) != 0) places.Add("your email address");
			if ((pending & AccountVerificationChannels.Sms) != 0) places.Add("your phone");
			if ((pending & AccountVerificationChannels.Discord) != 0) places.Add("your Discord account");
			if (places.Count == 0)
			{
				places.Add("your email address");
			}

			string message = places.Count == 1
				? $"Account created. Enter the code sent to {places[0]} to verify it."
				: $"Account created. A code is on its way to {string.Join(", ", places.Take(places.Count - 1))} and {places[^1]}; any one of them verifies the account.";
			if ((pending & AccountVerificationChannels.Discord) != 0)
			{
				message += " The Discord code arrives once, as a direct message from the game's Discord bot, after you join the game's Discord server.";
			}
			return message;
		}

		/// <summary>The decoy values out of the unmatched body properties, as text.</summary>
		private static Dictionary<string, string> DecoyValues(Dictionary<string, JsonElement> extra)
		{
			var values = new Dictionary<string, string>(StringComparer.Ordinal);
			if (extra == null)
			{
				return values;
			}
			foreach (var pair in extra)
			{
				values[pair.Key] = pair.Value.ValueKind switch
				{
					JsonValueKind.String => pair.Value.GetString(),
					JsonValueKind.Null or JsonValueKind.Undefined => "",
					// A decoy is a text box; anything that is not a string or null was put there by something that is not one.
					_ => pair.Value.GetRawText(),
				};
			}
			return values;
		}

		/// <summary>Redeems a verification code from any channel.</summary>
		/// <remarks>
		/// <para>
		/// One endpoint for every channel, because any one correct code verifies the account: the player
		/// types whichever code reached them first, and the database finds which channel it belongs to.
		/// There used to be a <c>verify-phone</c> twin; it is gone with the every-channel rule.
		/// </para>
		/// <para>
		/// Every failure — wrong code, unknown account, verified account, the third wrong code opening a
		/// support ticket — is one 400 with <see cref="AccountRegistrationService.InvalidCodeError"/>.
		/// </para>
		/// </remarks>
		[HttpPost("verify")]
		[AllowAnonymous]
		[EnableRateLimiting("Register")]
		public async Task<IActionResult> Verify([FromBody] VerifyRequest request)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.Username) || request.Code <= 0)
			{
				return BadRequest(new { error = "A username and verification code are required." });
			}

			var (ok, error, retry) = await registration.VerifyAsync(request.Username.Trim(), request.Code, HttpContext.RequestAborted);
			if (!ok)
			{
				// A database fault, which was not counted as a wrong code; the same answer for every account.
				return retry ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { error }) : BadRequest(new { error });
			}
			return Ok(new
			{
				message = "Account verified. You can sign in now.",
				verified = true,
				outstanding = Array.Empty<string>(),
			});
		}

		/// <summary>
		/// Sends a fresh verification code for one channel, if the account is owed one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Always the same 200 and the same words: unknown account, verified account, a channel it
		/// never chose, or a cooldown still running. Anything else would say which names exist.
		/// </para>
		/// <para>
		/// Discord is refused with a 400, for every account alike: its code is sent once, as one DM, and
		/// is never re-sent. Because the refusal does not depend on the account, it says nothing about one.
		/// </para>
		/// </remarks>
		[HttpPost("verify/resend")]
		[AllowAnonymous]
		[EnableRateLimiting("Register")]
		public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request)
		{
			var channel = AccountRegistrationService.ParseChannel(request?.Channel);
			if (request == null || string.IsNullOrWhiteSpace(request.Username) || channel == AccountVerificationChannels.None)
			{
				return BadRequest(new { error = "A username and a channel (email or sms) are required." });
			}
			if (channel == AccountVerificationChannels.Discord)
			{
				return BadRequest(new { error = "The Discord code is sent once, as a direct message from the game's Discord bot, and cannot be re-sent. Enter it, or ask for a new email or SMS code." });
			}

			await registration.ResendAsync(request.Username.Trim(), channel, HttpContext.RequestAborted);
			return Ok(new
			{
				message = $"If that account is waiting for a {(channel == AccountVerificationChannels.Sms ? "phone" : "email")} code, a new one is on its way. " +
						  $"Codes can be resent every {VerificationResendThrottle.Cooldown.TotalMinutes:0} minutes; the newest code is the one that works.",
			});
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

			/* Whether everything was signed out is said, not assumed: the reset stands either way, but
			 * somebody resetting a password they think is compromised needs to know if a session
			 * survived it. Only the code's holder gets this answer, so it tells nobody anything new. */
			return Ok(new
			{
				message = result.SignedOutEverywhere
					? "Password reset. Every browser and game client signed in to this account has been signed out."
					: "Password reset, but not everything signed in to this account could be signed out. Sign in and change the password once more to end every session.",
				signedOutEverywhere = result.SignedOutEverywhere,
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
				/* Registration's optional fields and gates. The browser marks the beta code required from
				 * this, and offers only the verification channels this server actually verifies. */
				betaRequired = beta.Enabled,
				betaCode = new { length = BetaCodeFormat.DisplayLength, hint = "XXXX-XXXX-XXXX" },
				verification = new
				{
					email = verification.Email,
					sms = verification.Sms,
					discord = verification.Discord,
					// Only ever an https:// link; Program.cs drops anything else.
					discordInviteUrl = verification.DiscordInviteUrl,
					autoVerify = options.AutoVerifyAccounts || verification.VerifiesNothing,
				},
				profile = new
				{
					realNameMaxLength = AccountProfileRules.MaxRealNameLength,
					countryMaxLength = AccountProfileRules.MaxCountryLength,
					addressMaxLength = AccountProfileRules.MaxAddressLength,
					phoneHint = "+44 7700 900123",
					discordUsernameMaxLength = AccountProfileRules.MaxDiscordTagLength,
					discordUsernameHint = "fishfan or FishFan#1234",
				},
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
				return DatabaseReplies.Failure(this, result, log, "Your account could not be loaded. Try again shortly.",
					notFound: "That account no longer exists.");
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
				emailVerified = data.EmailVerified,
				phone = data.Phone,
				phoneVerified = data.PhoneVerified,
				discordUsername = data.DiscordUsername,
				discordVerified = data.DiscordVerified,
				verificationChannels = AccountRegistrationService.ChannelNames((AccountVerificationChannels)data.VerificationChannels),
				totpEnabled = data.TotpEnabled,
				totpVerifiedAtUtc = data.TotpVerifiedAt,
				lastLoginUtc = data.LastLogin,
			});
		}

		/// <summary>The beta codes the signed-in account has redeemed.</summary>
		[HttpGet("beta")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> MyBetaCodes()
		{
			string username = User.Identity?.Name;
			var result = await betaCodes.FetchForAccountAsync(username, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Your beta codes could not be loaded." });
			}
			return Ok(new
			{
				gateEnabled = beta.Enabled,
				codes = result.Data.Select(c => new
				{
					code = c.Code,
					program = c.Program,
					redeemedUtc = c.RedeemedUtc,
					revoked = c.CodeRevoked,
				}),
			});
		}

		/// <summary>
		/// Redeems a beta code for the signed-in account.
		/// </summary>
		/// <remarks>
		/// For an account made before the gate went up, or whose redemption at registration lost a race
		/// for a code's last use. The service's refusal is passed through unaltered: it deliberately
		/// gives one message for every kind of bad code.
		/// </remarks>
		[HttpPost("beta/redeem")]
		[Authorize(Policy = PanelPolicies.Self)]
		[EnableRateLimiting("Auth")]
		public async Task<IActionResult> RedeemBetaCode([FromBody] RedeemBetaCodeRequest request)
		{
			string username = User.Identity?.Name;
			if (string.IsNullOrWhiteSpace(request?.Code))
			{
				return BadRequest(new { error = "A beta code is required." });
			}

			var result = await betaCodes.RedeemAsync(username, request.Code.Trim(), HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				/* Every refusal — NOT_FOUND included — stays one 400 with the service's words: a 404
				 * for an unknown code beside a 400 for a used-up one would tell a script which guesses
				 * were real codes. Only a database fault is answered differently, because its text is
				 * the exception's, not the service's, and says nothing about the code. */
				if (DatabaseReplies.IsFault(result.ErrorCode))
				{
					return DatabaseReplies.Failure(this, result, log, "Your beta code could not be checked. Try again shortly.");
				}
				return BadRequest(new { error = result.ErrorMessage ?? AccountRegistrationService.InvalidBetaCodeError });
			}
			return Ok(new { message = $"Beta code redeemed for {result.Data.Program}.", program = result.Data.Program });
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
			 * because an exclusion was computed wrongly.
			 *
			 * A password is changed because somebody may have it, so a revocation that fails is
			 * logged and SAID. It used to fold into a count of zero while the reply announced that
			 * every other browser had been signed out — the one sentence the account holder acts on. */
			var revoked = await sessions.RevokeAllAsync(username, HttpContext.RequestAborted);
			if (!revoked.IsSuccess)
			{
				log.LogError("Password changed for '{User}' but panel sessions were NOT revoked: [{Code}] {Message}",
					username, revoked.ErrorCode, revoked.ErrorMessage);
			}
			int revokedSessions = revoked.IsSuccess ? revoked.Data : 0;
			bool everythingSignedOut = revoked.IsSuccess && result.GameTokensRevoked;

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
				message = everythingSignedOut
					? "Password changed. Every other browser and game client has been signed out."
					: "Password changed, but not everything signed in to this account could be signed out. " +
					  "Sign out your other sessions below, or change the password again shortly.",
				revokedSessions = Math.Max(0, revokedSessions - 1),
				// False when another browser or game client may still be signed in.
				everythingSignedOut,
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
			/* The address changed either way, and the account is unverified from here. When no code
			 * went out, the player is sent to Resend rather than told to wait for one. */
			return Ok(new
			{
				message = result.VerificationSent
					? "Email changed. Check the new address for a verification code."
					: "Email changed, but a verification code could not be sent. Use Resend on the sign-in page to get one.",
				verificationSent = result.VerificationSent,
			});
		}

		/// <summary>Starts two-factor enrolment. Does not enable it.</summary>
		/// <remarks>
		/// <para>
		/// For an account whose two-factor is already ON this is a re-enrolment, and it replaces the
		/// live authenticator and every recovery code the moment it runs: the secret it writes is the
		/// one sign-in reads. That is the same weight as disabling two-factor or regenerating the codes,
		/// so it takes the same fresh step-up those do. Without it, a borrowed signed-in browser could
		/// swap the owner's authenticator for one of its own.
		/// </para>
		/// <para>
		/// A first enrolment needs no step-up — there is no authenticator to prove, and nothing is
		/// enabled until <c>2fa/confirm</c>.
		/// </para>
		/// </remarks>
		[HttpPost("2fa/setup")]
		[Authorize(Policy = PanelPolicies.Self)]
		public async Task<IActionResult> BeginTwoFactorSetup()
		{
			string username = User.Identity?.Name;
			if (User.FindFirstValue(PanelClaims.TotpEnrolled) == "true" && !HasFreshStepUp())
			{
				return StatusCode(StatusCodes.Status428PreconditionRequired, new
				{
					error = "Replacing your authenticator needs a fresh two-factor code.",
				});
			}

			var setup = await selfService.BeginTwoFactorSetupAsync(username, HttpContext.RequestAborted);
			if (!setup.Ok)
			{
				return BadRequest(new { error = setup.Error });
			}
			// Shown once. For a first enrolment two-factor is not on until the code is confirmed below.
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

			/// <summary>Optional phone number, with its country code. Required when verifying by SMS.</summary>
			public string Phone { get; set; }

			/// <summary>Optional beta code. Required while the beta gate is on.</summary>
			public string BetaCode { get; set; }

			/// <summary>Optional country or region.</summary>
			public string Country { get; set; }

			/// <summary>Optional real name.</summary>
			public string RealName { get; set; }

			/// <summary>Optional postal address.</summary>
			public string Address { get; set; }

			/// <summary>Optional name of the account that referred this one.</summary>
			public string ReferralAccount { get; set; }

			/// <summary>Verify with an emailed code. Choosing no channel means email.</summary>
			public bool VerifyEmail { get; set; }

			/// <summary>Verify with a texted code. Needs <see cref="Phone"/>.</summary>
			public bool VerifySms { get; set; }

			/// <summary>Verify with a code sent once as a Discord DM by the game's bot. Needs <see cref="DiscordUsername"/>.</summary>
			public bool VerifyDiscord { get; set; }

			/// <summary>Optional Discord username, as <c>fishfan</c> or <c>FishFan#1234</c>. Required when verifying by Discord.</summary>
			public string DiscordUsername { get; set; }

			/// <summary>The signed token from <c>register/form</c>.</summary>
			public string FormToken { get; set; }

			/// <summary>
			/// Every body property this model does not name — which is where the randomised decoy fields
			/// arrive, under names only the token knows.
			/// </summary>
			[JsonExtensionData]
			public Dictionary<string, JsonElement> Extra { get; set; }
		}

		/// <summary>A resend request: which account, and which channel.</summary>
		public sealed class ResendVerificationRequest
		{
			/// <summary>Account name.</summary>
			public string Username { get; set; } = "";

			/// <summary><c>email</c> or <c>sms</c>. <c>discord</c> is refused: that code is never re-sent.</summary>
			public string Channel { get; set; } = "";
		}

		/// <summary>A beta code to redeem.</summary>
		public sealed class RedeemBetaCodeRequest
		{
			/// <summary>The code, in any spacing or case.</summary>
			public string Code { get; set; } = "";
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

			/// <summary>The six-digit code from any verification message: email, SMS or Discord DM.</summary>
			public int Code { get; set; }
		}

	}
}
