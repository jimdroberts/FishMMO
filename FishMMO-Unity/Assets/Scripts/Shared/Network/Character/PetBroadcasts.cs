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

	public struct PetFollowBroadcast : IBroadcast
	{
	}

	public struct PetStayBroadcast : IBroadcast
	{
	}

	public struct PetSummonBroadcast : IBroadcast
	{
	}
	
	public struct PetReleaseBroadcast : IBroadcast
	{
	}
}