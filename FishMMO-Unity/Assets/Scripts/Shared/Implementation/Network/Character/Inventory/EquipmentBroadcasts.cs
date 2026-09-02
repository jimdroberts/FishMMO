using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for equipping an item from an inventory slot to an equipment slot.
	/// Sent client→server to request an equip, echoed server→client as acknowledgement.
	/// </summary>
	public struct EquipmentEquipItemBroadcast : IBroadcast
	{
		/// <summary>Index of the item in the inventory.</summary>
		public int InventoryIndex;
		/// <summary>Equipment slot to equip the item to.</summary>
		public byte Slot;
		/// <summary>Type of inventory the item is being equipped from.</summary>
		public InventoryType FromInventory;
	}

	/// <summary>
	/// Broadcast for unequipping an item from an equipment slot to an inventory slot.
	/// Sent client→server to request an unequip, echoed server→client as acknowledgement.
	/// </summary>
	public struct EquipmentUnequipItemBroadcast : IBroadcast
	{
		/// <summary>Equipment slot to unequip the item from.</summary>
		public byte Slot;
		/// <summary>Type of inventory the item is being moved to.</summary>
		public InventoryType ToInventory;
		/// <summary>
		/// Slot within <see cref="ToInventory"/> the item ended up in. Server to client only.
		/// </summary>
		/// <remarks>
		/// The request does not name a destination slot -- the server picks one, because only it
		/// knows what the container really holds. That left the client picking its own when a
		/// reconcile beat the acknowledgement back, and the two had no reason to agree: an unequip
		/// could land in inventory slot 0 on the server and slot 5 on the client, after which every
		/// later request naming slot 5 was refused for a slot the server sees as empty. The item
		/// looked movable within the inventory only because the client was rearranging its own copy.
		///
		/// So the answer travels back with the acknowledgement. A client that guessed is corrected;
		/// a client that has not placed it yet puts it straight where it belongs. -1 on the request
		/// leg, where it has no meaning.
		/// </remarks>
		public int ToSlot;
	}
}
