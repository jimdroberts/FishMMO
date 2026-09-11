using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Control Panel browser sessions.
	/// </summary>
	/// <remarks>
	/// Every method takes the SHA-256 hex of the session identifier, never the identifier itself.
	/// Hashing is the caller's job so the raw value never reaches this layer at all.
	/// </remarks>
	public interface IWebSessionService
	{
		/// <summary>
		/// Records a newly issued session.
		/// </summary>
		/// <param name="sessionHash">SHA-256 hex of the session identifier.</param>
		/// <param name="accountName">Account the session authenticates.</param>
		/// <param name="accessLevel">The account's access level at issue.</param>
		/// <param name="twoFactorSatisfied">Whether two-factor is already satisfied.</param>
		/// <param name="expiresUtc">Absolute expiry.</param>
		/// <param name="ipAddress">Client address, or null.</param>
		/// <param name="userAgent">Client user agent, or null.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<WebSessionData>> IssueAsync(
			string sessionHash,
			string accountName,
			byte accessLevel,
			bool twoFactorSatisfied,
			DateTime expiresUtc,
			string ipAddress,
			string userAgent,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches a session by hash. Returns failure when it does not exist; the caller still has
		/// to apply the revoked, expiry and idle checks.
		/// </summary>
		Task<DatabaseResult<WebSessionData>> FetchByHashAsync(
			string sessionHash,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Stamps last-seen on a live session, extending its idle window.
		/// </summary>
		Task<DatabaseResult> TouchAsync(
			string sessionHash,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks two-factor satisfied and stamps the step-up time, promoting a pending session.
		/// </summary>
		Task<DatabaseResult> PersistTwoFactorSatisfiedAsync(
			string sessionHash,
			DateTime expiresUtc,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Stamps a fresh step-up, restarting the window for destructive actions.
		/// </summary>
		Task<DatabaseResult> PersistStepUpAsync(
			string sessionHash,
			CancellationToken cancellationToken = default);

		/// <summary>Revokes one session.</summary>
		Task<DatabaseResult> RevokeByHashAsync(
			string sessionHash,
			CancellationToken cancellationToken = default);

		/// <summary>Revokes one session by its surrogate id, for the operator's session list.</summary>
		Task<DatabaseResult> RevokeByIdAsync(
			string accountName,
			long id,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Revokes every session for an account. Called when the account is banned, its access
		/// level changes, its password changes, or its tokens are revoked.
		/// </summary>
		Task<DatabaseResult<int>> RevokeAllForAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default);

		/// <summary>Lists the live sessions for an account, newest first.</summary>
		Task<DatabaseResult<List<WebSessionData>>> FetchActiveForAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes sessions that expired before the cutoff. Returns how many rows went.
		/// </summary>
		Task<DatabaseResult<int>> CleanupExpiredAsync(
			DateTime cutoffUtc,
			CancellationToken cancellationToken = default);
	}
}
