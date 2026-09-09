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

		/// <summary>
		/// Runs a two-character exchange — a completed player trade — as ONE database
		/// transaction whose commit is the point of truth, so a crash at any instant leaves the
		/// database holding either the whole trade or none of it, and memory never diverges
		/// from that in a direction that duplicates anything.
		/// </summary>
		/// <remarks>
		/// <para>
		/// THE PROTOCOL, in order, because the order is the guarantee:
		/// </para>
		/// <list type="number">
		///   <item><description>
		///     Worker: open the transaction and take BOTH characters' session row locks, in
		///     ascending id order. From here no other write for either character can commit
		///     until this transaction ends: every write takes the same lock.
		///   </description></item>
		///   <item><description>
		///     Main thread, while the locks are held: <paramref name="applyOnMainThread"/>
		///     re-validates and mutates memory (or returns null to abort with nothing changed),
		///     and the resulting rows are captured with their journal sequences and versions
		///     stamped NOW. Every write captured before this instant therefore carries a lower
		///     sequence and lower versions than the trade; every write captured after carries
		///     the trade's content. The slots in <see cref="ItemExchangeLeg.TouchedSlots"/> are
		///     locked until the outcome is known.
		///   </description></item>
		///   <item><description>
		///     Worker: write both characters' rows and the ledger, commit.
		///   </description></item>
		///   <item><description>
		///     Main thread: unlock, then <paramref name="onFinished"/>(true). On any refusal:
		///     roll back, unlock, <paramref name="onFinished"/>(false) — the caller undoes its
		///     memory mutation exactly, which the locks make possible — then every write
		///     captured from memory while the exchange was applied is VOIDED in the journal,
		///     because its content described a trade that never happened, and both characters
		///     are reconciled from the restored memory.
		///   </description></item>
		/// </list>
		/// <para>
		/// Crash analysis: before the commit, memory is lost and the database rolls back — the
		/// trade never happened for either side. After the commit, the database holds the whole
		/// trade for both. A write captured before the apply that lands after the commit is
		/// older by sequence and version and is refused. There is no interleaving in which one
		/// character's half is durable and the other's is not.
		/// </para>
		/// </remarks>
		/// <param name="first">One party.</param>
		/// <param name="second">The other party.</param>
		/// <param name="applyOnMainThread">
		/// Called on the main thread while both row locks are held. Returns the two legs
		/// (<paramref name="first"/>'s then <paramref name="second"/>'s) after mutating memory,
		/// or null to abort before anything was mutated.
		/// </param>
		/// <param name="onFinished">
		/// Called on the main thread once, with true when the transaction committed and false
		/// when it was aborted or rolled back. Only called with false after a null apply, or
		/// with the memory mutation still in place and every touched slot already unlocked, so
		/// the caller can undo it.
		/// </param>
		/// <param name="operation">Short operation name used in persistence log lines.</param>
		/// <returns>True when the exchange was accepted for execution. False means nothing will run and nothing was changed.</returns>
		bool TryRunExchange(IPlayerCharacter first, IPlayerCharacter second,
			Func<ItemExchangeLeg[]> applyOnMainThread, Action<bool> onFinished, string operation);

		/// <summary>
		/// Tells the owning client which inventory slots an operation outside this system
		/// changed: <paramref name="emptied"/> slots are cleared, then <paramref name="set"/>
		/// items are written to their slots.
		/// </summary>
		/// <remarks>
		/// Removes go first, and only for slots that are still empty, because an exchange can
		/// vacate a slot and fill it again in the same step.
		/// </remarks>
		void NotifyInventorySlots(IPlayerCharacter character, IReadOnlyList<Item> set, IReadOnlyList<int> emptied);
	}
}
