using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character friend relationships.
	/// </summary>
	/// <remarks>
	/// Friend link deletion is expected to be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterFriendService :
		ICountByKeyAction<long>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterFriendData>
	{
		/// <summary>
		/// Persists a friend relationship for the specified character.
		/// </summary>
		/// <param name="characterId">The owning character ID.</param>
		/// <param name="friendCharacterId">The friend character ID.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this persist operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> PersistAsync(long characterId, long friendCharacterId, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a friend relationship for the specified character if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="characterId">The owning character ID.</param>
		/// <param name="friendCharacterId">The friend character ID.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this delete operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> DeleteAsync(long characterId, long friendCharacterId, long incomingVersion, CancellationToken cancellationToken = default);
	}
}