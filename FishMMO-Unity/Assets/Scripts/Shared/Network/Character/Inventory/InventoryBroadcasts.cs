using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public struct InventorySetItemBroadcast : IBroadcast
	{
		public long InstanceID;
		public int TemplateID;
		public int Slot;
		public int Seed;
		public uint StackSize;
	}

	public struct InventorySetMultipleItemsBroadcast : IBroadcast
	{
		public List<InventorySetItemBroadcast> Items;
	}

	public struct InventoryRemoveItemBroadcast : IBroadcast
	{
		public int Slot;
	}

	public struct InventorySwapItemSlotsBroadcast : IBroadcast
	{
		public int From;
		public int To;
		public InventoryType FromInventory;
	}
}