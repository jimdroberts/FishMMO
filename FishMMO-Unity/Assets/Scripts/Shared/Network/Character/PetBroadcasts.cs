using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct PetAddBroadcast : IBroadcast
	{
		public int PetID;
	}

	public struct PetRemoveBroadcast : IBroadcast
	{
	}
}