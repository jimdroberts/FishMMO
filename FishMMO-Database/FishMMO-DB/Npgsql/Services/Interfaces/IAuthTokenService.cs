using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing authentication tokens.
	/// The LoginServer issues tokens after successful SRP authentication.
	/// WorldServers and SceneServers validate tokens cryptographically and check for revocation.
	/// </summary>
	public interface IAuthTokenService
	{
		/// <summary>
		/// Records a newly issued authentication token.
		/// Called by the LoginServer after successful SRP authentication.
		/// </summary>
		/// <param name="tokenHash">SHA-256 hex hash of the signed token blob.</param>
		/// <param name="accountName">The account the token was issued for.</param>
		/// <param name="loginServerId">The LoginServer that issued the token.</param>
		/// <param name="expiresUtc">When the token expires.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<AuthTokenData>> IssueAsync(
			string tokenHash,
			string accountName,
			long loginServerId,
			DateTime expiresUtc,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches a token record by its hash for revocation checks.
		/// Returns failure if the token is not found.
		/// </summary>
		Task<DatabaseResult<AuthTokenData>> FetchByHashAsync(
			string tokenHash,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Revokes a specific token by its hash.
		/// </summary>
		Task<DatabaseResult> RevokeByHashAsync(
			string tokenHash,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Revokes all active tokens for a specific account.
		/// Used when an account is banned, password is changed, or force-logout is required.
		/// </summary>
		Task<DatabaseResult> RevokeAllForAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes expired tokens older than the specified cutoff.
		/// Returns the number of tokens deleted.
		/// Called periodically for database maintenance.
		/// </summary>
		Task<DatabaseResult<int>> CleanupExpiredAsync(
			DateTime cutoffUtc,
			CancellationToken cancellationToken = default);
	}
}