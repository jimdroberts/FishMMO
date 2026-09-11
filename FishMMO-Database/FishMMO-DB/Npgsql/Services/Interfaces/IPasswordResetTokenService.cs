using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Outstanding "forgot my password" tokens.
	/// </summary>
	/// <remarks>
	/// Every method takes the SHA-256 hex of the emailed token, never the token itself — hashing
	/// is the caller's job, so the redeemable value never reaches this layer at all. This mirrors
	/// <see cref="IWebSessionService"/>, which takes session hashes for the same reason.
	/// </remarks>
	public interface IPasswordResetTokenService
	{
		/// <summary>
		/// Records a newly issued reset token.
		/// </summary>
		/// <param name="tokenHash">SHA-256 hex of the emailed token.</param>
		/// <param name="accountName">Account the token resets.</param>
		/// <param name="expiresUtc">When the token stops being redeemable.</param>
		/// <param name="requestedIp">Client address that asked, or null.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<PasswordResetTokenData>> IssueAsync(
			string tokenHash,
			string accountName,
			DateTime expiresUtc,
			string? requestedIp,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches a token row by hash, whatever its state.
		/// </summary>
		/// <remarks>
		/// Returns the row for an expired or already-used token too; the caller applies those
		/// checks. Read-only — use <see cref="RedeemAsync"/> to actually spend a token, because
		/// only that path marks it used atomically.
		/// </remarks>
		Task<DatabaseResult<PasswordResetTokenData>> FetchByHashAsync(
			string tokenHash,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Spends a token: marks it used and returns the account it belongs to.
		/// </summary>
		/// <remarks>
		/// One conditional UPDATE — <c>used_utc IS NULL AND expires_utc &gt; now</c> — so the
		/// check and the mark cannot be separated by a racing second redemption. A token that is
		/// unknown, expired or already used all fail the same way, with DB_NOT_FOUND; the caller
		/// must not distinguish them to the browser either.
		/// </remarks>
		/// <returns>The account name on success, DB_NOT_FOUND when the token is not spendable.</returns>
		Task<DatabaseResult<string>> RedeemAsync(
			string tokenHash,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks every outstanding token for an account used, optionally sparing one.
		/// </summary>
		/// <remarks>
		/// Called after a successful redemption. Without it, an attacker who requested a reset
		/// earlier would still hold a live token after the account holder had reset their
		/// password — which is exactly the case a reset exists to close.
		/// </remarks>
		/// <param name="accountName">Account whose tokens go.</param>
		/// <param name="exceptTokenHash">A hash to leave alone, or null to invalidate all.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>How many rows were invalidated.</returns>
		Task<DatabaseResult<int>> InvalidateAllForAccountAsync(
			string accountName,
			string? exceptTokenHash,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Whether the account has been issued a token since <paramref name="sinceUtc"/>.
		/// </summary>
		/// <remarks>
		/// The resend cooldown. It exists so the request endpoint cannot be used to flood
		/// somebody's inbox, and it must never change what the endpoint answers.
		/// </remarks>
		Task<DatabaseResult<bool>> HasRecentForAccountAsync(
			string accountName,
			DateTime sinceUtc,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes tokens that expired before the cutoff. Returns how many rows went.
		/// </summary>
		Task<DatabaseResult<int>> CleanupExpiredAsync(
			DateTime cutoffUtc,
			CancellationToken cancellationToken = default);
	}
}
