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
	}
}
