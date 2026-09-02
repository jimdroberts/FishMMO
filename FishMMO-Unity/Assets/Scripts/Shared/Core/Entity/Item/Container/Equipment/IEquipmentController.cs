using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for equipment controllers, managing equip/unequip logic and activation of equipment slots.
	/// Extends character behaviour and item container functionality.
	/// </summary>
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
		/// <param name="item">The item to equip.</param>
		/// <param name="inventoryIndex">The index in the source inventory.</param>
		/// <param name="container">The source item container (e.g., inventory or bank).</param>
		/// <param name="toSlot">The equipment slot to equip the item into.</param>
		/// <returns>True if the item was successfully equipped, false otherwise.</returns>
		bool Equip(Item item, int inventoryIndex, IItemContainer container, ItemSlot toSlot);

		/// <summary>
		/// Unequips the item from the specified slot and adds it to the given container (e.g., inventory or bank).
		/// </summary>
		/// <param name="container">The destination item container.</param>
		/// <param name="slot">The equipment slot to unequip from.</param>
		/// <param name="modifiedItems">The list of items modified during the operation.</param>
		/// <returns>True if the item was successfully unequipped and added, false otherwise.</returns>
		bool Unequip(IItemContainer container, byte slot, out List<Item> modifiedItems);

		/// <summary>
		/// Records that an equip has been asked for, before the request goes out.
		/// </summary>
		/// <remarks>
		/// The reconcile can land before the server's acknowledgement does, and when it does it has
		/// to know where the item was headed. Without a record it guesses, and a guess is only
		/// right for the container it guesses.
		/// </remarks>
		/// <param name="item">The item being equipped.</param>
		/// <param name="inventoryIndex">Slot it is coming from.</param>
		/// <param name="fromInventory">Container it is coming from.</param>
		/// <param name="toSlot">Equipment slot it is going to.</param>
		void NotifyEquipRequested(Item item, int inventoryIndex, InventoryType fromInventory, ItemSlot toSlot);

		/// <summary>
		/// Records that an unequip has been asked for, before the request goes out.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is what tells a reconcile which container the item is meant to end up in. Without
		/// it the reconcile empties the equipment slot and puts the item in the first container
		/// with room — the inventory — and the acknowledgement that arrives afterwards finds the
		/// slot already empty and declines to act. The server has the item in the bank, the client
		/// shows it in the inventory, and the two never reconcile.
		/// </para>
		/// <para>
		/// So it must be called by whatever sends the request, and before it is sent.
		/// </para>
		/// </remarks>
		/// <param name="slot">Equipment slot being emptied.</param>
		/// <param name="toInventory">Container the item is meant to land in.</param>
		void NotifyUnequipRequested(ItemSlot slot, InventoryType toInventory);

		/// <summary>
		/// Forgets a recorded request, for when the server refuses it.
		/// </summary>
		/// <remarks>
		/// A record left behind outlives the request it describes and would steer the next
		/// reconcile for that slot.
		/// </remarks>
		/// <param name="slot">Equipment slot whose pending request is abandoned.</param>
		void ClearPendingRequest(ItemSlot slot);
	}
}