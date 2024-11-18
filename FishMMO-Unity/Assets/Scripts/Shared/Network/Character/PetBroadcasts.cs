using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct PetAddBroadcast : IBroadcast
	{
		public long ID;
	}

	public struct PetRemoveBroadcast : IBroadcast
	{
	}
}