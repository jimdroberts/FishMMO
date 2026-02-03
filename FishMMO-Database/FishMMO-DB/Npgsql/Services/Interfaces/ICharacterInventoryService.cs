using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character inventory items.
	/// </summary>
	/// <remarks>
	/// Inventory item persistence and deletion should be version-gated via the logical <c>Version</c>
	/// to ensure stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterInventoryService :
		IPersistAction<CharacterInventoryData, long>,
		IPersistManyAction<CharacterInventoryData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterInventoryData>
	{
		/// <summary>
		/// Deletes a single inventory slot for a character if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="characterId">The character ID that owns the inventory item.</param>
		/// <param name="slot">The inventory slot identifier.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this delete operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> DeleteAsync(long characterId, int slot, long incomingVersion, CancellationToken cancellationToken = default);
	}
}