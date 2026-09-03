using FishNet.Broadcast;

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
		public InventorySetItemBroadcast[] Items;
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

	/// <summary>
	/// Client request to split part of a stack off into an inventory slot. Issue #198.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Shaped like <see cref="InventorySwapItemSlotsBroadcast"/> on purpose: the destination is
	/// always an inventory slot, <see cref="FromInventory"/> names where the stack is, and the
	/// two indices travel in the same fields — so the failure message, the client's pending-slot
	/// bookkeeping and the server's banker check all treat it exactly as they treat a swap.
	/// </para>
	/// <para>
	/// <b>Never echoed.</b> A swap is acknowledged by echoing the request, because the client can
	/// apply a swap from the two indices alone. A split creates an item the client has never seen,
	/// so the server answers with the ordinary set-slot messages for both slots instead, the same
	/// way it reports a granted item.
	/// </para>
	/// </remarks>
	public struct InventorySplitItemBroadcast : IBroadcast
	{
		/// <summary>Slot holding the stack being split, in <see cref="FromInventory"/>.</summary>
		public int From;
		/// <summary>Inventory slot the split half goes to: empty, or a matching stack with room.</summary>
		public int To;
		/// <summary>How much to take. At least 1 and less than the stack holds; anything else is refused.</summary>
		public uint Amount;
		/// <summary>Container holding the stack being split.</summary>
		public InventoryType FromInventory;
	}
}