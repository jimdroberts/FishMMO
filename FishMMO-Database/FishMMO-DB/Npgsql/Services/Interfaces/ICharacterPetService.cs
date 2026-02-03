using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing a character's pet.
	/// </summary>
	/// <remarks>
	/// Pet persistence and deletion should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterPetService :
		IPersistAction<CharacterPetData>,
		IPersistManyAction<CharacterPetData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchByKeyAction<long, CharacterPetData?>
	{
		/// <summary>
		/// Fetches the spawned pet for the specified character.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the spawned pet on success, or <c>null</c> when not spawned.
		/// </returns>
		Task<DatabaseResult<CharacterPetData?>> FetchSpawnedAsync(long characterId, CancellationToken cancellationToken = default);
	}
}