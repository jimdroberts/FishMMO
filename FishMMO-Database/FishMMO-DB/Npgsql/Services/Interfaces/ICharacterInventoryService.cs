using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character inventory items.
	/// </summary>
	/// <remarks>
	/// Inventory item persistence and deletion should be version-gated via the logical <c>Version</c>
	/// to ensure stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterInventoryService :
		IPersistAction<CharacterInventoryData, long>,
		IPersistManyAction<CharacterInventoryData>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterInventoryData>
	{
		/// <summary>
		/// Deletes a single inventory slot for a character if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="characterId">The character ID that owns the inventory item.</param>
		/// <param name="slot">The inventory slot identifier.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this delete operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> DeleteAsync(long characterId, int slot, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// Replaces the character's entire inventory with an authoritative snapshot of the server's
		/// in-memory state: every slot present in <paramref name="items"/> is written, and every row
		/// for this character whose slot is NOT present is removed.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the backstop for the incremental per-slot writes. Those writes can be silently
		/// rejected (stale version, a dropped async work item, a handler that returned early), and
		/// because nothing else ever wrote items, a rejection meant permanent loss at the next login.
		/// A snapshot taken on character save downgrades every one of those failures to a transient
		/// glitch that survives at most until the next save tick.
		/// </para>
		/// <para>
		/// The upsert here is deliberately NOT version-gated. Version gating is precisely the
		/// mechanism that makes an incremental write disappear, so gating the backstop as well would
		/// defeat its purpose. It is safe because the snapshot is authoritative by construction: it
		/// states, for one container of one character at one instant, exactly which slots hold what.
		/// It cannot mint an item — the only rows it writes are rows the server currently believes in
		/// — and the prune cannot lose one, because any row it deletes is a row the server believes
		/// does not exist.
		/// </para>
		/// <para>
		/// ORDERING REQUIREMENT: the caller must enqueue this through the same per-character key as
		/// the incremental writes so that the two are serialised FIFO, otherwise an in-flight snapshot
		/// can land after a newer incremental write and roll it back. It also must only be issued by a
		/// server that currently owns the character's session; there is no session guard inside this
		/// method.
		/// </para>
		/// </remarks>
		/// <param name="characterId">The character whose inventory is being replaced.</param>
		/// <param name="items">Every occupied slot. An empty or null collection empties the container.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> SaveSnapshotAsync(long characterId, IEnumerable<CharacterInventoryData> items, CancellationToken cancellationToken = default);
	}
}