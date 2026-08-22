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
		/// Persists a friend or block relationship for the specified character.
		/// </summary>
		/// <param name="characterId">The owning character ID.</param>
		/// <param name="friendCharacterId">The friend character ID.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this persist operation.</param>
		/// <param name="isBlocked">When true the relationship is a block; when false it is a friend.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> PersistAsync(long characterId, long friendCharacterId, long incomingVersion, bool isBlocked, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a friend relationship for the specified character if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="characterId">The owning character ID.</param>
		/// <param name="friendCharacterId">The friend character ID.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this delete operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> DeleteAsync(long characterId, long friendCharacterId, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// Determines whether <paramref name="characterId"/> has blocked
		/// <paramref name="otherCharacterId"/>.
		/// </summary>
		/// <param name="characterId">The character who may own a block entry.</param>
		/// <param name="otherCharacterId">The character who may be blocked.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> that is true when an active row exists marking
		/// <paramref name="otherCharacterId"/> as blocked by <paramref name="characterId"/>.
		/// </returns>
		/// <remarks>
		/// The <c>is_blocked</c> column has existed since the friend table was introduced and
		/// nothing has ever read it — every block a player recorded was written and then ignored,
		/// so blocking someone did not stop them inviting or whispering. This is the read side.
		/// The check is deliberately one-directional: it answers "has A blocked B", so the caller
		/// must ask it about the TARGET of an unwanted action, not about the initiator.
		/// </remarks>
		Task<DatabaseResult<bool>> IsBlockedAsync(long characterId, long otherCharacterId, CancellationToken cancellationToken = default);
	}
}