using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing a character's guild membership state.
	/// </summary>
	/// <remarks>
	/// Guild membership updates should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterGuildService :
		ICountByKeyAction<long>,
		IDeleteByKeyVersionedAction<long>,
		IFetchByKeyAction<long, CharacterGuildData?>,
		IFetchManyByKeyAction<long, CharacterGuildData>
	{
		/// <summary>
		/// Persists the provided guild membership data, enforcing capacity limits.
		/// </summary>
		/// <param name="guildData">The guild membership data to persist.</param>
		/// <param name="maxCapacity">The maximum number of members allowed in the guild.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> PersistAsync(CharacterGuildData guildData, int maxCapacity, CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates a character's guild rank if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="guildId">The guild ID.</param>
		/// <param name="rank">The new rank.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this update operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> UpdateRankAsync(long characterId, long guildId, byte rank, long incomingVersion, CancellationToken cancellationToken = default);
	}
}