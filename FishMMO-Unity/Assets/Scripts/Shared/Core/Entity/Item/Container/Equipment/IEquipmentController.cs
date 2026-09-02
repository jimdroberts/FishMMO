using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for equipment controllers, managing equip/unequip logic and activation of equipment slots.
	/// Extends character behaviour and item container functionality.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Equipment is predicted.</b> The owner does not ask the server to equip and wait; it
	/// queues the request with <see cref="RequestEquip"/> or <see cref="RequestUnequip"/>, the
	/// request rides the next replicate, and both the owner and the server apply it inside that
	/// tick's replicate body. The server's reconcile then confirms the socket. A stale reconcile
	/// — one describing a tick before the request — is restored and the replay re-applies the
	/// request, exactly as it does for movement and abilities. The reliable acknowledgement that
	/// used to carry the answer is gone; there is nothing left for it to answer.
	/// </para>
	/// <para>
	/// The one message that survives is the unequip destination
	/// (<c>EquipmentUnequipItemBroadcast</c>, server to owner only): the container picks the slot
	/// an unequipped item lands in, and although both peers pick deterministically they can
	/// disagree when their copies of the container differ. <see cref="ApplyUnequipDestination"/>
	/// moves the item, by identity, to the slot the server chose.
	/// </para>
	/// </remarks>
	public interface IEquipmentController : ICharacterBehaviour, IItemContainer
	{
		/// <summary>
		/// Fired after an item is successfully equipped. Subscribers receive the item and the slot.
		/// </summary>
		event Action<Item, ItemSlot> OnItemEquipped;

		/// <summary>
		/// Fired after an item is successfully unequipped. Subscribers receive the item and the slot.
		/// </summary>
		event Action<Item, ItemSlot> OnItemUnequipped;

		/// <summary>
		/// Fired on the owner when a queued request has run in the replicate body, whether or not
		/// it was applied. Arguments: the kind, the socket, the container the request named, the
		/// container index (or -1 for an unequip), and whether the move happened.
		/// </summary>
		/// <remarks>
		/// This is how a panel that marked two slots as waiting learns that nothing will change
		/// them. A refusal here is LOCAL — the item was no longer where the request said, or the
		/// socket did not fit it. A refusal by the SERVER is not reported through this event at
		/// all: it arrives as a reconcile that puts the item back, which updates the slots directly.
		/// </remarks>
		event Action<EquipmentRequestKind, ItemSlot, InventoryType, int, bool> OnRequestResolved;

		/// <summary>
		/// Fired on the server after any equip or unequip has been applied to this controller,
		/// from whichever path applied it. The persistence layer is the intended subscriber.
		/// </summary>
		event Action<IEquipmentController, EquipmentChange> OnServerEquipmentChanged;

		/// <summary>
		/// Triggers invoked when this character equips an item. EventData: EquipItemEventData.
		/// </summary>
		List<Trigger> OnEquipTriggers { get; }

		/// <summary>
		/// Triggers invoked when this character unequips an item. EventData: EquipItemEventData.
		/// </summary>
		List<Trigger> OnUnequipTriggers { get; }

		/// <summary>
		/// Activates the item in the specified equipment slot, typically triggering its use effect.
		/// </summary>
		/// <param name="index">The equipment slot index to activate.</param>
		void Activate(int index);

		/// <summary>
		/// Equips the specified item into the given equipment slot, handling swaps and unequips as needed.
		/// </summary>
		/// <remarks>
		/// Applies immediately on whichever peer calls it. Server-side paths that are not the
		/// owner's predicted request — ECA actions, for instance — call this directly; the owner
		/// learns of the change through the reconcile. The owner's own requests must go through
		/// <see cref="RequestEquip"/> instead, so they run inside the replicate tick.
		/// </remarks>
		/// <param name="item">The item to equip.</param>
		/// <param name="inventoryIndex">The index in the source inventory.</param>
		/// <param name="container">The source item container (e.g., inventory or bank).</param>
		/// <param name="toSlot">The equipment slot to equip the item into.</param>
		/// <returns>True if the item was successfully equipped, false otherwise.</returns>
		bool Equip(Item item, int inventoryIndex, IItemContainer container, ItemSlot toSlot);

		/// <summary>
		/// Unequips the item from the specified slot and adds it to the given container (e.g., inventory or bank).
		/// </summary>
		/// <remarks>See <see cref="Equip"/> for when to call this directly.</remarks>
		/// <param name="container">The destination item container.</param>
		/// <param name="slot">The equipment slot to unequip from.</param>
		/// <param name="modifiedItems">The list of items modified during the operation.</param>
		/// <returns>True if the item was successfully unequipped and added, false otherwise.</returns>
		bool Unequip(IItemContainer container, byte slot, out List<Item> modifiedItems);

		/// <summary>
		/// Queues an equip to be applied on the owner's next replicate tick.
		/// </summary>
		/// <remarks>
		/// Owner only. Validates what can be validated locally — the item is where the request says,
		/// it fits the socket, it has a database identity, neither slot is locked — and refuses
		/// without queueing when it cannot. One request is held at a time; a second call before the
		/// first has ridden a replicate replaces it, and the displaced request is reported through
		/// <see cref="OnRequestResolved"/> as not applied.
		/// </remarks>
		/// <param name="item">The item to equip, as the caller currently sees it.</param>
		/// <param name="sourceIndex">Its index in <paramref name="fromContainer"/>.</param>
		/// <param name="fromContainer">The container it is in.</param>
		/// <param name="socket">The equipment socket it is going to.</param>
		/// <returns>True when the request was queued.</returns>
		bool RequestEquip(Item item, int sourceIndex, InventoryType fromContainer, ItemSlot socket);

		/// <summary>
		/// Queues an unequip to be applied on the owner's next replicate tick.
		/// </summary>
		/// <remarks>See <see cref="RequestEquip"/>.</remarks>
		/// <param name="socket">The equipment socket being emptied.</param>
		/// <param name="toContainer">The container the item should land in.</param>
		/// <returns>True when the request was queued.</returns>
		bool RequestUnequip(ItemSlot socket, InventoryType toContainer);

		/// <summary>
		/// Moves an unequipped item to the slot the server put it in, if this peer has it anywhere
		/// else. Owner only; a no-op when the item is already there.
		/// </summary>
		/// <param name="itemID">Identity of the item that was unequipped.</param>
		/// <param name="container">The container the server placed it in.</param>
		/// <param name="slot">The slot within that container.</param>
		void ApplyUnequipDestination(long itemID, InventoryType container, int slot);
	}
}
