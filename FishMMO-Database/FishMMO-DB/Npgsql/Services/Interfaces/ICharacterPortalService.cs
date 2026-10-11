using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for the per-character portal activation bitmasks
	/// (<c>character_portals</c>).
	/// </summary>
	/// <remarks>
	/// The same contract as <see cref="ICharacterWaypointService"/>: a growing bitmask that is
	/// merged, never replaced. See <see cref="CharacterPortalData"/>.
	/// </remarks>
	public interface ICharacterPortalService :
		IFetchCollectionByKeyAction<long, CharacterPortalData>,
		IDeleteByKeyAction<long>
	{
		/// <summary>
		/// OR-merges pages into the stored masks, creating rows that do not exist yet.
		/// </summary>
		/// <param name="pages">The pages to merge. Bits already stored are retained.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>Success, or the failure that stopped the whole batch.</returns>
		Task<DatabaseResult> MergeAsync(IEnumerable<CharacterPortalData> pages, CancellationToken cancellationToken = default);
	}
}
