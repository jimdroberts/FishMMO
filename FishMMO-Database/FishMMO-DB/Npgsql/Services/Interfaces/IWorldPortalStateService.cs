using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for portals opened for the whole world (<c>world_portal_state</c>).
	/// </summary>
	/// <remarks>
	/// Fetched per scene, because a scene server reads the rows of each scene it loads. Written by
	/// merge: see <see cref="WorldPortalStateData"/>.
	/// </remarks>
	public interface IWorldPortalStateService :
		IFetchCollectionByKeyAction<string, WorldPortalStateData>
	{
		/// <summary>
		/// Merges openings into the stored rows: <c>permanent</c> ORs and <c>active_until</c> takes
		/// the later moment. Creates rows that do not exist yet.
		/// </summary>
		/// <param name="states">The openings to merge.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>Success, or the failure that stopped the whole batch.</returns>
		/// <remarks>Idempotent: a retry after an ambiguous failure changes nothing.</remarks>
		Task<DatabaseResult> MergeAsync(IEnumerable<WorldPortalStateData> states, CancellationToken cancellationToken = default);
	}
}
