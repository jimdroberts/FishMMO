using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for setting a single item in the equipment inventory.
	/// Contains all data needed to place or update an item in an equipment slot.
	/// </summary>
	public struct EquipmentSetItemBroadcast : IBroadcast
	{
		/// <summary>Unique instance ID of the item.</summary>
		public long InstanceID;
		/// <summary>Template ID of the item type.</summary>
		public int TemplateID;
		/// <summary>Slot index in the equipment inventory.</summary>
		public int Slot;
		/// <summary>Seed value for item randomization or uniqueness.</summary>
		public int Seed;
		/// <summary>Stack size of the item.</summary>
		public uint StackSize;
	}

	/// <summary>
	/// Broadcast for setting multiple items in the equipment inventory at once.
	/// Used for bulk updates or synchronization.
	/// </summary>
	public struct EquipmentSetMultipleItemsBroadcast : IBroadcast
	{
		/// <summary>List of items to set in the equipment inventory.</summary>
		public List<EquipmentSetItemBroadcast> Items;
	}

	/// <summary>
	/// Broadcast for equipping an item from an inventory slot to an equipment slot.
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
	/// </summary>
	public struct EquipmentUnequipItemBroadcast : IBroadcast
	{
		/// <summary>Equipment slot to unequip the item from.</summary>
		public byte Slot;
		/// <summary>Type of inventory the item is being moved to.</summary>
		public InventoryType ToInventory;
	}
}