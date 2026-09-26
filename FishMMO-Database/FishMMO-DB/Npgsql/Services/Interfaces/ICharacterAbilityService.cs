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
		IPersistManyOwnedAction<CharacterAbilityData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterAbilityData>
	{
		/// <summary>
		/// Persists one ability row, and returns its identity, only while the writer still holds the
		/// character's session claim.
		/// </summary>
		/// <remarks>
		/// The single-row sibling of <see cref="IPersistManyOwnedAction{TItem}.PersistOwnedAsync"/>,
		/// for a grant made while the character is resident: the claim is captured with the request
		/// and checked, under the character's share lock, in the write's own transaction — see
		/// <c>CharacterWriteGate</c>. The ungated <see cref="IPersistAction{T, TKey}.PersistAsync"/>
		/// remains for writers that hold no claim by design.
		/// </remarks>
		/// <param name="abilityData">The row.</param>
		/// <param name="claim">The claim the write was captured under. Must name the row's character.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// The row's identity. Fails with <see cref="DatabaseErrorCodes.Forbidden"/> when the claim is
		/// no longer held, and with <see cref="DatabaseErrorCodes.NotFound"/> when the character is gone.
		/// </returns>
		Task<DatabaseResult<long>> PersistOwnedAsync(CharacterAbilityData abilityData, CharacterSessionLeaseData claim, CancellationToken cancellationToken = default);

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

		/// <summary>
		/// <see cref="DeleteAbilityAsync"/>, admitted only while the writer still holds the
		/// character's session claim.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The forget a resident character asks for. The claim is checked, under the character's
		/// share lock, in the delete's own transaction — see <c>CharacterWriteGate</c>.
		/// </para>
		/// <para>
		/// <paramref name="admitReleased"/> is for a delete that UNDOES a row the same writer wrote
		/// under <paramref name="claim"/> (the grant's revoke): it is then also admitted while the
		/// character holds no claim at all, released and not yet claimed again, because the
		/// character may have left before the revoke could run and nobody has the row loaded. It is
		/// still refused once another session holds the character.
		/// </para>
		/// </remarks>
		/// <param name="characterId">The character that owns the ability.</param>
		/// <param name="abilityId">The row identity, never the template id.</param>
		/// <param name="incomingVersion">The version ceiling; the row is removed when its own version is at or below this.</param>
		/// <param name="claim">The claim the delete was captured under. Must name <paramref name="characterId"/>.</param>
		/// <param name="admitReleased">Also admit an unclaimed character. Only for undoing the writer's own row.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// Success when the row is gone. Fails with <see cref="DatabaseErrorCodes.Forbidden"/> when the
		/// claim is not held (see <paramref name="admitReleased"/>).
		/// </returns>
		Task<DatabaseResult> DeleteAbilityOwnedAsync(long characterId, long abilityId, long incomingVersion, CharacterSessionLeaseData claim, bool admitReleased = false, CancellationToken cancellationToken = default);
	}
}