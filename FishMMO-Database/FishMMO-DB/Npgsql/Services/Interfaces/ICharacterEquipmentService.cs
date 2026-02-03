using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character equipment.
	/// </summary>
	/// <remarks>
	/// Equipment persistence and deletion should be version-gated via the logical <c>Version</c>
	/// so newer authoritative updates win and stale updates are rejected.
	/// </remarks>
	public interface ICharacterEquipmentService :
		IPersistAction<CharacterEquipmentData, long>,
		IPersistManyAction<CharacterEquipmentData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterEquipmentData>
	{
		/// <summary>
		/// Deletes a single equipment slot for a character if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="characterId">The character ID that owns the equipment slot.</param>
		/// <param name="slot">The equipment slot identifier.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this delete operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> DeleteAsync(long characterId, int slot, long incomingVersion, CancellationToken cancellationToken = default);
	}
}