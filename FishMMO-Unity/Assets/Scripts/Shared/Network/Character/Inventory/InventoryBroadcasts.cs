using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for setting a single item in the character's inventory.
	/// Contains all data needed to place or update an item in an inventory slot.
	/// </summary>
	public struct InventorySetItemBroadcast : IBroadcast
	{
		/// <summary>Unique instance ID of the item.</summary>
		public long InstanceID;
		/// <summary>Template ID of the item type.</summary>
		public int TemplateID;
		/// <summary>Slot index in the inventory.</summary>
		public int Slot;
		/// <summary>Seed value for item randomization or uniqueness.</summary>
		public int Seed;
		/// <summary>Stack size of the item.</summary>
		public uint StackSize;
	}

	/// <summary>
	/// Broadcast for setting multiple items in the character's inventory at once.
	/// Used for bulk updates or synchronization.
	/// </summary>
	public struct InventorySetMultipleItemsBroadcast : IBroadcast
	{
		/// <summary>List of items to set in the inventory.</summary>
		public List<InventorySetItemBroadcast> Items;
	}

	/// <summary>
	/// Broadcast for removing an item from a specific inventory slot.
	/// </summary>
	public struct InventoryRemoveItemBroadcast : IBroadcast
	{
		/// <summary>Slot index to remove the item from.</summary>
		public int Slot;
	}

	/// <summary>
	/// Broadcast for swapping two item slots in the inventory or between inventories.
	/// </summary>
	public struct InventorySwapItemSlotsBroadcast : IBroadcast
	{
		/// <summary>Source slot index.</summary>
		public int From;
		/// <summary>Destination slot index.</summary>
		public int To;
		/// <summary>Type of inventory the item is being moved from.</summary>
		public InventoryType FromInventory;
	}
}