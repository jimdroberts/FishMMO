using System;
using FishNet.Broadcast;

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
		public int[] Events;
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

	// ──────────────────────────────────────────
	//  Mailbox
	// ──────────────────────────────────────────

	/// <summary>
	/// Server → Client broadcast to open the mailbox UI.
	/// </summary>
	public struct MailboxBroadcast : IBroadcast
	{
		/// <summary>ID of the mailbox interactable.</summary>
		public long InteractableID;
	}

	/// <summary>
	/// Client → Server broadcast requesting the character's mail list.
	/// </summary>
	public struct MailFetchBroadcast : IBroadcast
	{
		/// <summary>ID of the mailbox interactable the player is using.</summary>
		public long InteractableID;
	}

	/// <summary>
	/// Server → Client broadcast containing the character's mail list.
	/// </summary>
	public struct MailListBroadcast : IBroadcast
	{
		/// <summary>List of mail entries.</summary>
		public MailEntryData[] Entries;
	}

	/// <summary>
	/// Data structure for a single mail entry within a <see cref="MailListBroadcast"/>.
	/// </summary>
	[Serializable]
	public struct MailEntryData
	{
		/// <summary>Unique mail ID.</summary>
		public long ID;
		/// <summary>Display name of the sender.</summary>
		public string SenderName;
		/// <summary>Mail subject line.</summary>
		public string Subject;
		/// <summary>Mail body text.</summary>
		public string Body;
		/// <summary>Whether this mail has been read.</summary>
		public bool Read;
		/// <summary>Template ID of the attached item, or 0 if none.</summary>
		public int ItemTemplateID;
		/// <summary>Currency amount attached, or 0 if none.</summary>
		public int CurrencyAmount;
	}

	/// <summary>
	/// Client → Server broadcast to send mail to another player.
	/// </summary>
	public struct MailSendBroadcast : IBroadcast
	{
		/// <summary>ID of the mailbox interactable the player is using.</summary>
		public long InteractableID;
		/// <summary>Name of the recipient character.</summary>
		public string RecipientName;
		/// <summary>Mail subject line.</summary>
		public string Subject;
		/// <summary>Mail body text.</summary>
		public string Body;
	}

	/// <summary>
	/// Client → Server broadcast to delete a mail entry.
	/// </summary>
	public struct MailDeleteBroadcast : IBroadcast
	{
		/// <summary>ID of the mailbox interactable the player is using.</summary>
		public long InteractableID;
		/// <summary>ID of the mail to delete.</summary>
		public long MailID;
	}

	// ──────────────────────────────────────────
	//  Shrine
	// ──────────────────────────────────────────

	/// <summary>
	/// Server → Client broadcast when a shrine effect is applied. Used for VFX/SFX feedback.
	/// </summary>
	public struct ShrineBroadcast : IBroadcast
	{
		/// <summary>ID of the shrine interactable.</summary>
		public long InteractableID;
		/// <summary>Template ID of the shrine.</summary>
		public int TemplateID;
	}

	// ──────────────────────────────────────────
	//  Switch
	// ──────────────────────────────────────────

	/// <summary>
	/// Server → Client broadcast when a switch is toggled. Communicates the new state.
	/// </summary>
	public struct SwitchStateBroadcast : IBroadcast
	{
		/// <summary>ID of the switch interactable.</summary>
		public long InteractableID;
		/// <summary>True if the switch target is now activated.</summary>
		public bool Activated;
	}

	// ──────────────────────────────────────────
	//  Lore Object
	// ──────────────────────────────────────────

	/// <summary>
	/// Server → Client broadcast to display the UILore window.
	/// The client resolves the <see cref="LoreObjectTemplate"/> from the cache.
	/// </summary>
	public struct LoreObjectBroadcast : IBroadcast
	{
		/// <summary>ID of the lore object interactable.</summary>
		public long InteractableID;
		/// <summary>Template ID of the lore object.</summary>
		public int TemplateID;
	}

	// ──────────────────────────────────────────
	//  Gathering Node
	// ──────────────────────────────────────────

	/// <summary>
	/// Server → Client broadcast when gathering starts. Used for progress bar display.
	/// </summary>
	public struct GatheringNodeBroadcast : IBroadcast
	{
		/// <summary>ID of the gathering node interactable.</summary>
		public long InteractableID;
		/// <summary>Template ID of the gathering node.</summary>
		public int TemplateID;
		/// <summary>Time in seconds the gathering action takes.</summary>
		public float GatherTimeSeconds;
	}

	// ──────────────────────────────────────────
	//  Capture Point
	// ──────────────────────────────────────────

	/// <summary>
	/// Server → Client broadcast when a capture point's state changes.
	/// </summary>
	public struct CapturePointUpdateBroadcast : IBroadcast
	{
		/// <summary>ID of the capture point interactable.</summary>
		public long InteractableID;
		/// <summary>Template ID of the capture point.</summary>
		public int TemplateID;
		/// <summary>Character ID of the current owner, or 0 if neutral.</summary>
		public long OwnerCharacterID;
		/// <summary>Current objective state.</summary>
		public ObjectiveState State;
		/// <summary>Current capture progress (interactions applied).</summary>
		public int CaptureProgress;
		/// <summary>Total interactions required to capture.</summary>
		public int InteractionsToCapture;
	}

	// ──────────────────────────────────────────
	//  Container
	// ──────────────────────────────────────────

	/// <summary>
	/// Data structure for a single item slot within a <see cref="ContainerOpenBroadcast"/>.
	/// </summary>
	[Serializable]
	public struct ContainerSlotData
	{
		/// <summary>Slot index in the container.</summary>
		public int Slot;
		/// <summary>Template ID of the item in this slot.</summary>
		public int TemplateID;
		/// <summary>Stack amount of the item in this slot.</summary>
		public uint Amount;
	}

	/// <summary>
	/// Server → Client broadcast to open the container UI with its current contents.
	/// </summary>
	public struct ContainerOpenBroadcast : IBroadcast
	{
		/// <summary>ID of the container interactable.</summary>
		public long InteractableID;
		/// <summary>Template ID of the container.</summary>
		public int TemplateID;
		/// <summary>List of filled item slots.</summary>
		public ContainerSlotData[] Items;
	}

	/// <summary>
	/// Client → Server broadcast requesting to take an item from a container slot.
	/// </summary>
	public struct ContainerTakeItemBroadcast : IBroadcast
	{
		/// <summary>ID of the container interactable.</summary>
		public long InteractableID;
		/// <summary>Slot index to take the item from.</summary>
		public int Slot;
	}
}
