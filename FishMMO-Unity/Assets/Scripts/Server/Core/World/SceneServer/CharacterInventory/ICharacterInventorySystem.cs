using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for character inventory operations.
	/// Implementations perform item container manipulations and coordinate any
	/// necessary database updates or client notifications.
	/// </summary>
	public interface ICharacterInventorySystem : IServerBehaviour
	{
		/// <summary>
		/// Swaps two item slots within the same container and collects the affected items.
		/// </summary>
		/// <param name="container">The container instance in which the swap occurs.</param>
		/// <param name="fromIndex">Source slot index.</param>
		/// <param name="toIndex">Target slot index.</param>
		/// <param name="affectedItems">Out: list of items whose slot assignments changed and need persistence.</param>
		/// <returns>True when the swap succeeded; otherwise false.</returns>
		bool SwapContainerItems(IItemContainer container, int fromIndex, int toIndex, out List<Item> affectedItems);

		/// <summary>
		/// Swaps items between two containers and collects the affected items along
		/// with any slot deletions that occurred during the cross-container move.
		/// </summary>
		/// <param name="from">Source container.</param>
		/// <param name="to">Destination container.</param>
		/// <param name="fromIndex">Source slot index.</param>
		/// <param name="toIndex">Destination slot index.</param>
		/// <param name="affectedFromItems">Out: items placed into the source container that need persistence.</param>
		/// <param name="deletedFromSlots">Out: slot indices that were vacated in the source container and need deletion.</param>
		/// <param name="affectedToItems">Out: items placed into the destination container that need persistence.</param>
		/// <returns>True when the cross-container swap succeeded; otherwise false.</returns>
		bool SwapContainerItems(IItemContainer from, IItemContainer to, int fromIndex, int toIndex,
			out List<Item> affectedFromItems, out List<long> deletedFromSlots, out List<Item> affectedToItems);

		/// <summary>
		/// Grants an item to one of a character's containers: places it, tells the owning client,
		/// persists it, and hands it the identity the database assigns.
		/// </summary>
		/// <remarks>
		/// <para>
		/// THE grant path. Every server-side source of a new item — pickups, corpse and chest loot,
		/// merchant purchases, mail, quest and achievement rewards, the ECA give action — comes
		/// through here, because it is the only path that completes an item: a fresh item has no
		/// database identity, and until its first write returns one it cannot be equipped (the
		/// attribute ledger declines an id of zero), used, moved, or safely written a second time.
		/// The slot it lands in stays locked until that identity arrives; see
		/// <see cref="TryPersistGrantedItems"/>.
		/// </para>
		/// <para>
		/// All-or-nothing on the container: the whole stack fits or nothing is placed, so a caller
		/// that reserved the item elsewhere (took it off a corpse, charged for it) can put it back
		/// on a false return.
		/// </para>
		/// </remarks>
		/// <param name="character">The character receiving the item.</param>
		/// <param name="item">The item. May be merged into stacks the character already holds.</param>
		/// <param name="container">Which container receives it.</param>
		/// <returns>True when the whole item was placed.</returns>
		bool TryGrantItem(IPlayerCharacter character, Item item, InventoryType container);

		/// <summary>
		/// Persists a grant of items — freshly created items and the existing stacks they merged
		/// into — through the atomic item-batch machinery, so an item the database has never seen
		/// (ID 0) gets the identity the write returns assigned onto the live <see cref="Item"/> and
		/// the owning client is told the final instance id and seed.
		/// </summary>
		/// <remarks>
		/// An item with no identity has its slot LOCKED from here until the identity lands, so
		/// nothing can move, merge, consume or equip it in between. That window is one database
		/// round trip. Without the lock a second write captured in that window inserted a second
		/// row for the same item, which the load path then handed back as two items.
		/// </remarks>
		/// <param name="character">The character the items were granted to.</param>
		/// <param name="modifiedItems">The items the grant touched, as returned by the container add.</param>
		/// <param name="container">The container the items are in.</param>
		/// <param name="operation">Short operation name used in persistence log lines.</param>
		/// <returns>
		/// True when the batch was enqueued normally; false when the bounded queue was full and the
		/// write ran on the fallback path. Never a rollback signal — memory is already authoritative.
		/// </returns>
		bool TryPersistGrantedItems(IPlayerCharacter character, List<Item> modifiedItems, InventoryType container, string operation);

		/// <summary>
		/// Persists inventory rows that changed or ceased to exist outside this system's own
		/// handlers — a stack reduced by a sale or a mail attachment, an item sold or attached
		/// whole — through the same journalled batch every other item write uses.
		/// </summary>
		/// <remarks>
		/// The journal is what orders these against the periodic snapshot. A delete issued around
		/// it could commit after a snapshot captured while the item still existed, and the snapshot
		/// would then put the row back. Callers have already told the client themselves.
		/// </remarks>
		/// <param name="character">The owning character.</param>
		/// <param name="changed">Items whose rows changed, or null.</param>
		/// <param name="removed">Items that were removed, with the versions that authorise the deletes, or null.</param>
		void PersistInventoryChanges(IPlayerCharacter character, IReadOnlyList<Item> changed, IReadOnlyList<RemovedItemRecord> removed);

		/// <summary>
		/// Captures a full snapshot of a departing character's containers, to be awaited by the
		/// character system before it releases the session.
		/// </summary>
		/// <remarks>
		/// Main thread only, while the character is still resident. The returned work writes the
		/// snapshot in one transaction under <paramref name="lease"/>; the caller awaits it before
		/// the release so the next owner reads it. Returns null when there is nothing to capture.
		/// </remarks>
		/// <param name="character">The departing character.</param>
		/// <param name="lease">The session this server still holds for it, already taken out of the live token map.</param>
		/// <returns>The flush to await, or null.</returns>
		Func<Task> CaptureDespawnFlush(IPlayerCharacter character, CharacterSessionInfo? lease);
	}
}
