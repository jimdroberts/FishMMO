using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for arena queue locks: the deserter penalty.
	/// </summary>
	public interface IArenaPenaltyService
	{
		/// <summary>Reads the locks that are still in force for the given characters.</summary>
		Task<DatabaseResult<IReadOnlyList<ArenaPenaltyData>>> FetchActiveAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default);

		/// <summary>Locks a character out of the arena queue until an instant. Replaces an earlier lock.</summary>
		Task<DatabaseResult<bool>> SetAsync(long characterId, DateTime lockedUntilUtc, string reason, CancellationToken cancellationToken = default);

		/// <summary>Removes a character's lock, if any. For a disconnected player who came back in time.</summary>
		Task<DatabaseResult<bool>> ClearAsync(long characterId, CancellationToken cancellationToken = default);
	}
}
