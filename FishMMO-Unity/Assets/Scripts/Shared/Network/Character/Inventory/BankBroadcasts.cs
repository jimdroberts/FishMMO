using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public struct BankSetItemBroadcast : IBroadcast
	{
		public long InstanceID;
		public int TemplateID;
		public int Slot;
		public int Seed;
		public uint StackSize;
	}

	public struct BankSetMultipleItemsBroadcast : IBroadcast
	{
		public List<BankSetItemBroadcast> Items;
	}

	public struct BankRemoveItemBroadcast : IBroadcast
	{
		public int Slot;
	}

	public struct BankSwapItemSlotsBroadcast : IBroadcast
	{
		public int From;
		public int To;
		public InventoryType FromInventory;
	}
}