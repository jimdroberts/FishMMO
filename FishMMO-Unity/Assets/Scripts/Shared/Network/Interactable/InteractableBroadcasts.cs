using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for requesting to use an interactable object.
	/// Contains the ID of the interactable object.
	/// </summary>
	public struct InteractableBroadcast : IBroadcast
	{
		/// <summary>ID of the interactable object to use.</summary>
		public long InteractableID;
	}

	/// <summary>
	/// Broadcast for interacting with an ability crafter object.
	/// Contains the interactable object's ID.
	/// </summary>
	public struct AbilityCrafterBroadcast : IBroadcast
	{
		/// <summary>ID of the ability crafter object.</summary>
		public long InteractableID;
	}

	/// <summary>
	/// Broadcast for crafting an ability using an interactable object.
	/// Contains the interactable object's ID, template ID, and a list of event IDs.
	/// </summary>
	public struct AbilityCraftBroadcast : IBroadcast
	{
		/// <summary>ID of the interactable object used for crafting.</summary>
		public long InteractableID;
		/// <summary>Template ID of the ability to craft.</summary>
		public int TemplateID;
		/// <summary>List of event IDs associated with the crafting process.</summary>
		public List<int> Events;
	}

	/// <summary>
	/// Broadcast for interacting with a banker object.
	/// No additional data required.
	/// </summary>
	public struct BankerBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for requesting the list of available dungeons from the dungeon finder.
	/// No additional data required.
	/// </summary>
	public struct DungeonFinderListBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for interacting with a dungeon finder object.
	/// Contains the interactable object's ID.
	/// </summary>
	public struct DungeonFinderBroadcast : IBroadcast
	{
		/// <summary>ID of the dungeon finder object.</summary>
		public long InteractableID;
	}

	/// <summary>
	/// Broadcast for interacting with a merchant object.
	/// Contains the interactable object's ID and the merchant's template ID.
	/// </summary>
	public struct MerchantBroadcast : IBroadcast
	{
		/// <summary>ID of the merchant object.</summary>
		public long InteractableID;
		/// <summary>Template ID of the merchant.</summary>
		public int TemplateID;
	}

	/// <summary>
	/// Broadcast for purchasing an item from a merchant.
	/// Contains the interactable object's ID, item ID, index, and tab type.
	/// </summary>
	public struct MerchantPurchaseBroadcast : IBroadcast
	{
		/// <summary>ID of the merchant object.</summary>
		public long InteractableID;
		/// <summary>ID of the item to purchase.</summary>
		public int ID;
		/// <summary>Index of the item in the merchant's inventory.</summary>
		public int Index;
		/// <summary>Type of merchant tab (e.g., buy, sell).</summary>
		public MerchantTabType Type;
	}
}