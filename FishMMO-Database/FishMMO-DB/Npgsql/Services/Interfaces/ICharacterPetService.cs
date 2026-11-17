using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing a character's pets, one row per pet slot.
	/// </summary>
	/// <remarks>
	/// Pet persistence and deletion should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterPetService :
		IPersistAction<CharacterPetData>,
		IPersistManyAction<CharacterPetData>,
		IPersistManyOwnedAction<CharacterPetData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterPetData>
	{
		/// <summary>
		/// Fetches the pets the specified character had out, in slot order.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the spawned pets on success; empty when none was out.
		/// </returns>
		Task<DatabaseResult<IReadOnlyList<CharacterPetData>>> FetchSpawnedAsync(long characterId, CancellationToken cancellationToken = default);
	}
}