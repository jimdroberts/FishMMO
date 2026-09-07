using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for per-character discovered-waypoint bitmasks.
	/// </summary>
	/// <remarks>
	/// Not <c>IPersistManyAction</c>: that contract describes a versioned row where the newest
	/// writer wins. This row is a growing bitmask that is merged, never replaced. See
	/// <see cref="CharacterWaypointData"/>.
	/// </remarks>
	public interface ICharacterWaypointService :
		IFetchCollectionByKeyAction<long, CharacterWaypointData>,
		IDeleteByKeyAction<long>
	{
		/// <summary>
		/// OR-merges pages into the stored masks, creating rows that do not exist yet.
		/// </summary>
		/// <param name="pages">The pages to merge. Bits already stored are retained.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>Success, or the failure that stopped the whole batch.</returns>
		/// <remarks>
		/// Idempotent: a retry after an ambiguous failure, or two scene servers writing during a
		/// transfer, converge on the union. A bit can never be cleared through this call; a
		/// character does not un-discover a place.
		/// </remarks>
		Task<DatabaseResult> MergeAsync(IEnumerable<CharacterWaypointData> pages, CancellationToken cancellationToken = default);
	}
}
