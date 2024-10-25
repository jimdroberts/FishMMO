using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public struct EquipmentSetItemBroadcast : IBroadcast
	{
		public long InstanceID;
		public int TemplateID;
		public int Slot;
		public int Seed;
		public uint StackSize;
	}

	public struct EquipmentSetMultipleItemsBroadcast : IBroadcast
	{
		public List<EquipmentSetItemBroadcast> Items;
	}

	public struct EquipmentEquipItemBroadcast : IBroadcast
	{
		public int InventoryIndex;
		public byte Slot;
		public InventoryType FromInventory;
	}

	public struct EquipmentUnequipItemBroadcast : IBroadcast
	{
		public byte Slot;
		public InventoryType ToInventory;
	}
}