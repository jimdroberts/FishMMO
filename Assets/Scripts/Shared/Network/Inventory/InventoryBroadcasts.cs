using FishNet.Broadcast;
using System.Collections.Generic;

public struct InventorySetItemBroadcast : IBroadcast
{
	public ulong instanceID;
	public int templateID;
	public int seed;
	public int slot;
	public uint stackSize;
	public bool generateAttributes;
}

public struct InventorySetMultipleItemsBroadcast : IBroadcast
{
	public List<InventorySetItemBroadcast> items;
}

public struct InventoryRemoveItemBroadcast : IBroadcast
{
	public int slot;
}

public struct InventoryMoveItemBroadcast : IBroadcast
{
	public int fromSlot;
	public int toSlot;
}