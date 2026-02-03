using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing a character's known abilities.
	/// </summary>
	/// <remarks>
	/// Known ability persistence and deletion should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterKnownAbilityService :
		IPersistManyAction<CharacterKnownAbilityData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterKnownAbilityData>
	{
		/// <summary>
		/// Persists a known ability for the specified character.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="templateId">The ability template ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> PersistAsync(long characterId, int templateId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a known ability for the specified character if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="templateId">The ability template ID.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this delete operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> DeleteAsync(long characterId, int templateId, long incomingVersion, CancellationToken cancellationToken = default);
	}
}