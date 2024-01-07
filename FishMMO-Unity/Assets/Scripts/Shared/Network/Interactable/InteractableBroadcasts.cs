using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Sends the ID of the interactable object to the server as a request to use.
	/// </summary>
	public struct InteractableBroadcast : IBroadcast
	{
		public int interactableID;
	}

	public struct AbilityCrafterBroadcast : IBroadcast
	{
		public int interactableID;
	}

	public struct AbilityCraftBroadcast : IBroadcast
	{
		public int templateID;
		public List<int> events;
	}

	public struct BankerBroadcast : IBroadcast
	{
		public int interactableID;
	}

	public struct MerchantBroadcast : IBroadcast
	{
		public int id;
	}

	public struct MerchantPurchaseBroadcast : IBroadcast
	{
		public int id;
		public int index;
		public MerchantTabType type;
	}
}