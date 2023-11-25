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

	public struct AbilityCraftBroadcast : IBroadcast
	{
		public long InteractableID;
	}

	public struct MerchantBroadcast : IBroadcast
	{
		public long InteractableID;
		public List<int> Abilities;
		public List<int> AbilityEvents;
		public List<int> Items;
	}
}