using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing a character's party membership state.
	/// </summary>
	/// <remarks>
	/// Party membership updates should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterPartyService :
		ICountByKeyAction<long>,
		IDeleteByKeyVersionedAction<long>,
		IFetchByKeyAction<long, CharacterPartyData?>,
		IFetchManyByKeyAction<long, CharacterPartyData>
	{
		/// <summary>
		/// Persists the provided party membership data, enforcing capacity limits.
		/// </summary>
		/// <param name="partyData">The party membership data to persist.</param>
		/// <param name="maxCapacity">The maximum number of members allowed in the party.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> PersistAsync(CharacterPartyData partyData, int maxCapacity, CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates a character's party rank if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="partyId">The party ID.</param>
		/// <param name="rank">The new rank.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this update operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> UpdateRankAsync(long characterId, long partyId, byte rank, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// Returns the party's members who currently hold a live session.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Exists so party leadership can be repaired convergently rather than only on the events
		/// that break it. A scene server knows which characters IT hosts and nothing about the
		/// rest of the shard, so an absent leader — one who disconnected, or crashed, or whose
		/// server died — is invisible to every server that could do something about it. Without
		/// this the party is stuck: it HAS a leader, so nothing that merely counts leaders sees a
		/// problem, and that leader is not there to invite, kick, promote, or close the instance
		/// the party is holding open.
		/// </para>
		/// <para>
		/// "Online" is the same definition the account session checks use, and it is the strict
		/// one on purpose. A lapsed lease does not count, which is what lets a party recover from
		/// a scene server dying rather than waiting for it to come back. A character running out
		/// a combat-logout timer does not count either: its session is still claimed so its body
		/// stays authoritative, but the player is gone, and leadership must follow the player.
		/// </para>
		/// </remarks>
		/// <param name="partyId">The party to inspect.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>The character IDs of members with a live session; empty when none are online.</returns>
		Task<DatabaseResult<IReadOnlyList<long>>> FetchOnlineMemberIdsAsync(long partyId, CancellationToken cancellationToken = default);
	}
}