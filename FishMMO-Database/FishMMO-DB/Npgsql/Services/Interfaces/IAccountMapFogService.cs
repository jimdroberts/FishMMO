using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for account-wide fog of war (<c>account_map_fog</c>, issue #241).
	/// </summary>
	public interface IAccountMapFogService
	{
		/// <summary>The account's encoded fog for one scene, or null when it has never entered it.</summary>
		Task<DatabaseResult<byte[]>> FetchAsync(string accountName, string sceneName, CancellationToken cancellationToken = default);

		/// <summary>Every scene the account has fog for, with its explored count.</summary>
		Task<DatabaseResult<IReadOnlyList<AccountMapFogSummary>>> FetchScenesAsync(string accountName, CancellationToken cancellationToken = default);

		/// <summary>
		/// Merges into the stored fog for one scene, inside one transaction: the row is locked, <paramref name="merge"/> is
		/// handed what is stored (null when nothing is) and returns what to write.
		/// </summary>
		/// <remarks>
		/// The database cannot OR an encoding it does not understand, so the merge is the caller's; locking the row first is
		/// what makes two writers (a combat-logout body and the account's next character) converge on the union rather
		/// than the last one winning. The merge may run more than once if the transaction is retried, so it must be pure.
		/// </remarks>
		Task<DatabaseResult> MergeAsync(string accountName, string sceneName, Func<byte[], AccountMapFogMerge> merge, CancellationToken cancellationToken = default);
	}
}
