using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.Shared;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.CMS.Server.Controllers
{
	/// <summary>
	/// Client-facing account management API.
	/// Handles registration, password changes, email verification, and 2FA management.
	/// </summary>
	[ApiController]
	[Route("api/[controller]")]
	public class AccountController : ControllerBase
	{
		/// <summary>
		/// Registers a new account. Generates SRP salt and verifier from provided credentials.
		/// </summary>
		[HttpPost("register")]
		public async Task<IActionResult> Register([FromBody] RegisterRequest request)
		{
			if (!Authentication.IsAllowedUsername(request.Username))
				return BadRequest(new { error = Authentication.InvalidUsernameError });

			if (!Authentication.IsAllowedPassword(request.Password))
				return BadRequest(new { error = Authentication.InvalidPasswordError });

			if (string.IsNullOrWhiteSpace(request.Email) || !Authentication.IsAllowedEmailUsername(request.Email))
				return BadRequest(new { error = "Invalid email address." });

			// TODO: Generate SRP salt/verifier via ClientSrpData
			// TODO: Persist account via FishMMO-Database IAccountService
			// TODO: Generate and store TOTP secret, return setup data
			// TODO: Send verification email

			return Ok(new { message = "Account created. Please check your email for verification." });
		}

		/// <summary>
		/// Verifies a registered account using the verification code sent via email.
		/// </summary>
		[HttpPost("verify")]
		public async Task<IActionResult> VerifyAccount([FromBody] VerifyRequest request)
		{
			if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Code))
				return BadRequest(new { error = "Username and verification code are required." });

			// TODO: Verify code against database
			// TODO: Mark account as verified

			return Ok(new { message = "Account verified successfully." });
		}

		/// <summary>
		/// Changes the password for an authenticated account. Generates a new SRP salt/verifier.
		/// </summary>
		[HttpPost("change-password")]
		public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
		{
			if (!Authentication.IsAllowedPassword(request.NewPassword))
				return BadRequest(new { error = Authentication.InvalidPasswordError });

			// TODO: Authenticate caller (JWT/session)
			// TODO: Verify current password via SRP
			// TODO: Generate new SRP salt/verifier
			// TODO: Persist to database
			// TODO: Revoke all existing auth tokens

			return Ok(new { message = "Password changed successfully." });
		}

		/// <summary>
		/// Retrieves 2FA setup data (otpauth URI and recovery codes) for a new or existing account.
		/// </summary>
		[HttpPost("2fa/setup")]
		public async Task<IActionResult> SetupTwoFactor()
		{
			// TODO: Authenticate caller
			// TODO: Generate TOTP secret via CryptoHelper.TwoFactor.GenerateTotpSecret()
			// TODO: Encrypt and store via CryptoHelper.TwoFactor.EncryptTotpSecret()
			// TODO: Generate recovery codes via CryptoHelper.TwoFactor.GenerateRecoveryCodes()
			// TODO: Hash and store recovery codes
			// TODO: Build otpauth URI via CryptoHelper.TwoFactor.BuildOtpauthUri()

			return Ok(new { otpauthUri = "", recoveryCodes = Array.Empty<string>() });
		}
	}

	/// <summary>
	/// Registration request payload.
	/// </summary>
	public class RegisterRequest
	{
		/// <summary>
		/// Account username.
		/// </summary>
		public string Username { get; set; } = "";

		/// <summary>
		/// Account password. Used to generate SRP salt/verifier server-side.
		/// </summary>
		public string Password { get; set; } = "";

		/// <summary>
		/// Account email address for verification.
		/// </summary>
		public string Email { get; set; } = "";

		/// <summary>
		/// Account holder age for compliance checks.
		/// </summary>
		public int Age { get; set; }
	}

	/// <summary>
	/// Account verification request payload.
	/// </summary>
	public class VerifyRequest
	{
		/// <summary>
		/// Account username to verify.
		/// </summary>
		public string Username { get; set; } = "";

		/// <summary>
		/// Verification code received via email.
		/// </summary>
		public string Code { get; set; } = "";
	}

	/// <summary>
	/// Password change request payload.
	/// </summary>
	public class ChangePasswordRequest
	{
		/// <summary>
		/// Current account password for verification.
		/// </summary>
		public string CurrentPassword { get; set; } = "";

		/// <summary>
		/// New password to set.
		/// </summary>
		public string NewPassword { get; set; } = "";
	}
}
