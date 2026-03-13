using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing two-factor recovery codes.
	/// Recovery codes are one-time-use fallbacks when TOTP is unavailable.
	/// The server hashes codes before passing them to this layer.
	/// </summary>
	public interface ITwoFactorRecoveryCodeService
	{
		/// <summary>
		/// Batch-inserts a set of pre-hashed recovery codes for an account.
		/// Called during TOTP enrollment or recovery code regeneration.
		/// </summary>
		/// <param name="accountName">The account to associate codes with.</param>
		/// <param name="codeHashes">Pre-hashed recovery codes to store.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult> PersistManyAsync(
			string accountName,
			IReadOnlyList<string> codeHashes,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches all unused recovery codes for an account.
		/// </summary>
		Task<DatabaseResult<List<TwoFactorRecoveryCodeData>>> FetchUnusedByAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Atomically marks a single unused recovery code as consumed by setting used_at.
		/// Returns success only if the code was found and was previously unused.
		/// </summary>
		/// <param name="accountName">The account that owns the code.</param>
		/// <param name="codeHash">The hashed code to consume.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult> ConsumeCodeAsync(
			string accountName,
			string codeHash,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all recovery codes (used and unused) for an account.
		/// Called when TOTP is disabled or recovery codes are regenerated.
		/// </summary>
		Task<DatabaseResult> DeleteAllForAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default);
	}
}