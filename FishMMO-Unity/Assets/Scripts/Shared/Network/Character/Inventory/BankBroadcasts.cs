using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public struct BankSetItemBroadcast : IBroadcast
	{
		public long instanceID;
		public int templateID;
		public int slot;
		public int seed;
		public uint stackSize;
	}

	public struct BankSetMultipleItemsBroadcast : IBroadcast
	{
		public List<BankSetItemBroadcast> items;
	}

	public struct BankRemoveItemBroadcast : IBroadcast
	{
		public int slot;
	}

	public struct BankSwapItemSlotsBroadcast : IBroadcast
	{
		public int from;
		public int to;
		public InventoryType fromInventory;
	}
}