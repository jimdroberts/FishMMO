using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public struct InventorySetItemBroadcast : IBroadcast
	{
		public ulong instanceID;
		public int templateID;
		public int seed;
		public int slot;
		public uint stackSize;
	}

	public struct InventorySetMultipleItemsBroadcast : IBroadcast
	{
		public List<InventorySetItemBroadcast> items;
	}

	public struct InventoryRemoveItemBroadcast : IBroadcast
	{
		public int slot;
	}

	public struct InventorySwapItemSlotsBroadcast : IBroadcast
	{
		public int from;
		public int to;
	}
}