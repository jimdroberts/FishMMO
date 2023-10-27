using FishNet.Broadcast;

namespace FishMMO.Shared
{
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