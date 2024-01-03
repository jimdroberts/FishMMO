using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Sends the ID of the interactable object to the server as a request to use.
	/// </summary>
	public struct InteractableBroadcast : IBroadcast
	{
		public int InteractableID;
	}

	public struct AbilityCraftBroadcast : IBroadcast
	{
		public int InteractableID;
	}

	public struct BankerBroadcast : IBroadcast
	{
		public int InteractableID;
	}

	public struct MerchantBroadcast : IBroadcast
	{
		public int ID;
	}

	public struct MerchantPurchaseBroadcast : IBroadcast
	{
		public int ID;
		public int Index;
		public MerchantTabType Type;
	}
}