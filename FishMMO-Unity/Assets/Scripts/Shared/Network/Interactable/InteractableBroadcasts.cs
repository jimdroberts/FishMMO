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

	/// <summary>
	/// Server → Client broadcast to start a dialogue session.
	/// The client resolves the <see cref="DialogueTemplate"/> from the cache and displays the start node.
	/// </summary>
	public struct DialogueStartBroadcast : IBroadcast
	{
		/// <summary>ID of the dialogue interactable scene object. 0 when triggered via ECA without an interactable.</summary>
		public long InteractableID;
		/// <summary>CachedScriptableObject ID of the DialogueTemplate.</summary>
		public int TemplateID;
		/// <summary>The node ID the client should display first.</summary>
		public int StartNodeId;
		/// <summary>Bitmask of choices the character has previously made in this template.</summary>
		public short CachedChoices;
	}

	/// <summary>
	/// Client → Server broadcast when the player selects a dialogue choice.
	/// The server validates the choice and responds with a result or end broadcast.
	/// </summary>
	public struct DialogueChoiceBroadcast : IBroadcast
	{
		/// <summary>ID of the dialogue interactable scene object.</summary>
		public long InteractableID;
		/// <summary>The node the client is currently displaying (for validation).</summary>
		public int NodeId;
		/// <summary>Index of the selected choice within the node's Choices list.</summary>
		public int ChoiceIndex;
	}

	/// <summary>
	/// Server → Client broadcast when the server accepts a dialogue choice.
	/// Tells the client which node to display next and the updated choice bitmask.
	/// </summary>
	public struct DialogueChoiceResultBroadcast : IBroadcast
	{
		/// <summary>The next node to display. -1 indicates the dialogue has ended.</summary>
		public int NextNodeId;
		/// <summary>Updated bitmask of choices made by the character in this template.</summary>
		public short UpdatedChoices;
	}

	/// <summary>
	/// Server → Client broadcast to forcibly end a dialogue session (e.g., out of range).
	/// </summary>
	public struct DialogueEndBroadcast : IBroadcast
	{
	}
}