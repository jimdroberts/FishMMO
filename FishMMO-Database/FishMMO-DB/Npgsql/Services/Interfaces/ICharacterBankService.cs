using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character bank items.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This service uses optimistic concurrency via the logical <c>Version</c> stored on each bank item.
	/// Persist and delete operations are expected to be version-gated so newer authoritative updates win
	/// and stale writes are rejected.
	/// </para>
	/// <para>
	/// The inherited action interfaces define the core operations:
	/// - <c>PersistAsync</c> persists one bank slot and returns its row ID.
	/// - <c>PersistAsync</c> (many) persists a batch of bank slots.
	/// - <c>DeleteAsync</c> (versioned) deletes all bank items for a character.
	/// - <c>FetchAsync</c> returns the active (non-deleted) bank items for a character.
	/// </para>
	/// </remarks>
	public interface ICharacterBankService :
		IPersistAction<CharacterBankData, long>,
		IPersistManyAction<CharacterBankData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterBankData>
	{
		/// <summary>
		/// Deletes a single bank slot for a character if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="characterId">The character ID that owns the bank item.</param>
		/// <param name="slot">The bank slot identifier.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this delete operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		/// <remarks>
		/// Implementations should perform a single atomic, version-gated update (soft delete) to avoid races.
		/// If the row exists but the version is stale, implementations should return a stale-state failure.
		/// </remarks>
		Task<DatabaseResult> DeleteAsync(long characterId, int slot, long incomingVersion, CancellationToken cancellationToken = default);
	}
}