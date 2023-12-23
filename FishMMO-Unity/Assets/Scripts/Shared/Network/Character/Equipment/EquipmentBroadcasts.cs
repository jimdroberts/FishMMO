using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public struct EquipmentSetItemBroadcast : IBroadcast
	{
		public long instanceID;
		public int templateID;
		public int slot;
		public uint stackSize;
	}

	public struct EquipmentSetMultipleItemsBroadcast : IBroadcast
	{
		public List<EquipmentSetItemBroadcast> items;
	}

	public struct EquipmentEquipItemBroadcast : IBroadcast
	{
		public int inventoryIndex;
		public byte slot;
	}

	public struct EquipmentUnequipItemBroadcast : IBroadcast
	{
		public byte slot;
	}
}