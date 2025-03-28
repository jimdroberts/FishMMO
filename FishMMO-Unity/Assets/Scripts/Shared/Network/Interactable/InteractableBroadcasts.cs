using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Sends the ID of the interactable object to the server as a request to use.
	/// </summary>
	public struct InteractableBroadcast : IBroadcast
	{
		public long InteractableID;
	}

	public struct AbilityCrafterBroadcast : IBroadcast
	{
		public long InteractableID;
	}

	public struct AbilityCraftBroadcast : IBroadcast
	{
		public long InteractableID;
		public int TemplateID;
		public List<int> Events;
	}

	public struct BankerBroadcast : IBroadcast
	{
	}

	public struct DungeonFinderListBroadcast : IBroadcast
	{
	}

	public struct DungeonFinderBroadcast : IBroadcast
	{
	}

	public struct MerchantBroadcast : IBroadcast
	{
		public long InteractableID;
		public int TemplateID;
	}

	public struct MerchantPurchaseBroadcast : IBroadcast
	{
		public long InteractableID;
		public int ID;
		public int Index;
		public MerchantTabType Type;
	}
}