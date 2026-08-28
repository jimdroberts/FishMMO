using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.Shared;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.CMS.Server.Controllers
{
	/// <summary>
	/// Admin-only account management API.
	/// Handles banning, access level changes, token revocation, and 2FA resets.
	/// </summary>
	[ApiController]
	[Route("api/[controller]")]
	public class AdminController : ControllerBase
	{
		/// <summary>
		/// Searches for accounts by username or email.
		/// </summary>
		[HttpGet("accounts/search")]
		public async Task<IActionResult> SearchAccounts([FromQuery] string query)
		{
			// TODO: Require admin authentication
			// TODO: Query database for matching accounts

			return Ok(Array.Empty<object>());
		}

		/// <summary>
		/// Bans an account, setting its access level to Banned.
		/// </summary>
		[HttpPost("accounts/{username}/ban")]
		public async Task<IActionResult> BanAccount(string username)
		{
			// TODO: Require admin authentication
			// TODO: Set AccessLevel.Banned in database
			// TODO: Revoke all auth tokens for the account
			// TODO: Persist kick request to disconnect active sessions

			return Ok(new { message = $"Account '{username}' has been banned." });
		}

		/// <summary>
		/// Unbans an account, restoring its access level to Player.
		/// </summary>
		[HttpPost("accounts/{username}/unban")]
		public async Task<IActionResult> UnbanAccount(string username)
		{
			// TODO: Require admin authentication
			// TODO: Set AccessLevel.Player in database

			return Ok(new { message = $"Account '{username}' has been unbanned." });
		}

		/// <summary>
		/// Changes the access level of an account.
		/// </summary>
		[HttpPost("accounts/{username}/access-level")]
		public async Task<IActionResult> SetAccessLevel(string username, [FromBody] SetAccessLevelRequest request)
		{
			// TODO: Require admin authentication
			// TODO: Validate request.AccessLevel is a valid AccessLevel enum value
			// TODO: Update access level in database

			return Ok(new { message = $"Access level for '{username}' set to {request.AccessLevel}." });
		}

		/// <summary>
		/// Revokes all auth tokens for an account, forcing re-authentication.
		/// </summary>
		[HttpPost("accounts/{username}/revoke-tokens")]
		public async Task<IActionResult> RevokeTokens(string username)
		{
			// TODO: Require admin authentication
			// TODO: Revoke all tokens via IAuthTokenService

			return Ok(new { message = $"All tokens for '{username}' have been revoked." });
		}

		/// <summary>
		/// Resets 2FA for an account, removing the TOTP secret and recovery codes.
		/// </summary>
		[HttpPost("accounts/{username}/reset-2fa")]
		public async Task<IActionResult> ResetTwoFactor(string username)
		{
			// TODO: Require admin authentication
			// TODO: Clear TOTP secret and recovery codes from database
			// TODO: Revoke all auth tokens

			return Ok(new { message = $"Two-factor authentication reset for '{username}'." });
		}

		/// <summary>
		/// Forces a password reset for an account by generating a new SRP salt/verifier.
		/// </summary>
		[HttpPost("accounts/{username}/force-password-reset")]
		public async Task<IActionResult> ForcePasswordReset(string username, [FromBody] ForcePasswordResetRequest request)
		{
			if (!Authentication.IsAllowedPassword(request.NewPassword))
				return BadRequest(new { error = Authentication.InvalidPasswordError });

			// TODO: Require admin authentication
			// TODO: Generate new SRP salt/verifier via ClientSrpData
			// TODO: Persist to database
			// TODO: Revoke all auth tokens

			return Ok(new { message = $"Password reset for '{username}'." });
		}
	}

	/// <summary>
	/// Access level change request payload.
	/// </summary>
	public class SetAccessLevelRequest
	{
		/// <summary>
		/// Target access level value (maps to <see cref="AccessLevel"/>).
		/// </summary>
		public byte AccessLevel { get; set; }
	}

	/// <summary>
	/// Admin-initiated password reset request payload.
	/// </summary>
	public class ForcePasswordResetRequest
	{
		/// <summary>
		/// New password to set for the account.
		/// </summary>
		public string NewPassword { get; set; } = "";
	}
}
