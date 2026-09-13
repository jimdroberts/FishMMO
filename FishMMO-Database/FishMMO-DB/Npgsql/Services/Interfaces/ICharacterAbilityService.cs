using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character abilities.
	/// </summary>
	/// <remarks>
	/// Ability persistence and deletion should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterAbilityService :
		ICountByKeyAction<long>,
		IPersistAction<CharacterAbilityData, long>,
		IPersistManyAction<CharacterAbilityData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterAbilityData>
	{
		/// <summary>
		/// Deletes a single ability record for a character if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="characterId">The character ID that owns the ability.</param>
		/// <param name="abilityId">The ability identifier.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this delete operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> DeleteAsync(long characterId, long abilityId, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes one ability outright, so the character may craft the same template again.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Distinct from <see cref="DeleteAsync(long, long, long, CancellationToken)"/>, which leaves
		/// a tombstone. A forgotten ability is not a record of anything — it is the character no
		/// longer having it — and the tombstone is what would stand in the way of re-crafting it.
		/// The upsert keys on <c>(character_id, template_id)</c> and refuses to drop below the
		/// surviving row's version, so a soft delete that stamped the caller's version into the row
		/// would leave it holding a version no fresh craft could outrank, and the upsert's own
		/// <c>id &lt;= 0</c> guard would then reject the re-craft as a stale write. Forget-then-craft
		/// is a flow the craft panel explicitly advertises.
		/// </para>
		/// <para>
		/// This mirrors <c>ICharacterItemService.DeleteItemAsync</c>, which removed a vacated row
		/// outright for the same reason.
		/// </para>
		/// </remarks>
		/// <param name="characterId">The character that owns the ability. Checked, so one character cannot delete another's row.</param>
		/// <param name="abilityId">The row identity, never the template id.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this delete. The row is removed when its own version is at or below this.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		Task<DatabaseResult> DeleteAbilityAsync(long characterId, long abilityId, long incomingVersion, CancellationToken cancellationToken = default);
	}
}