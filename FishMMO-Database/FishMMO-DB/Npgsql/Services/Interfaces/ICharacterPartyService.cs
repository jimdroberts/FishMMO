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
	}
}