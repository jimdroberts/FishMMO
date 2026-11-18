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
	/// Broadcast for interacting with a banker object.
	/// No additional data required.
	/// </summary>
	public struct BankerBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Server → Client broadcast opening the dungeon finder for one entrance.
	/// </summary>
	/// <remarks>
	/// Carries the entrance's identity and the dungeon's template ID and nothing else. The
	/// description, the artwork and the whole difficulty list are resolved client-side from the
	/// template cache, so opening a panel costs one <c>int</c> on the wire rather than a copy of
	/// the dungeon's configuration — and the two sides can never disagree about the rules,
	/// because they are reading the same asset.
	/// <para>
	/// Nothing is armed server-side by this message. It opens a window; every action the window
	/// offers is a separate request that is authorised on its own.
	/// </para>
	/// </remarks>
	public struct DungeonFinderBroadcast : IBroadcast
	{
		/// <summary>ID of the dungeon entrance scene object.</summary>
		public long InteractableID;

		/// <summary>Template ID of the dungeon, or 0 when the entrance has none configured.</summary>
		public int DungeonTemplateID;
	}


	/// <summary>
	/// Where a character stands with the group finder.
	/// </summary>
	public enum GroupFinderState : byte
	{
		/// <summary>Not in the queue. <see cref="GroupFinderStatusBroadcast.Reason"/> says why, when there is a why.</summary>
		None = 0,

		/// <summary>Waiting for enough players, or for an open run with room.</summary>
		Waiting = 1,

		/// <summary>
		/// A group formed and the character has been placed in its party. The transfer into the
		/// run follows on the server's next pump — immediately if the character is free to
		/// travel, otherwise as soon as they are. Leaving is no longer possible from here.
		/// </summary>
		Matched = 2,
	}

	/// <summary>
	/// Why the group finder refused, or ended, a character's request.
	/// </summary>
	public enum GroupFinderRefusalReason : byte
	{
		/// <summary>Nothing to explain.</summary>
		None = 0,
		/// <summary>The entrance no longer exists, or the player has walked away from it.</summary>
		NoEntrance = 1,
		/// <summary>The dungeon does not offer the difficulty that was asked for.</summary>
		UnknownDifficulty = 2,
		/// <summary>Find Group is not offered at this difficulty, or the dungeon cannot seat a group.</summary>
		NotAvailable = 3,
		/// <summary>The character is already inside instanced content.</summary>
		InInstance = 4,
		/// <summary>
		/// The character is in a party with other people. Matching joins a party the finder
		/// builds, so the player must leave theirs first — or have its leader open the dungeon
		/// to others, which is how a partial group fills its empty slots.
		/// </summary>
		InParty = 5,
		/// <summary>The character, or a party they were in, still holds an open instance.</summary>
		HoldsInstance = 6,
		/// <summary>Asked again too soon.</summary>
		OnCooldown = 7,
		/// <summary>The server could not complete the request; try again.</summary>
		ServerError = 8,
		/// <summary>The player left the queue.</summary>
		Left = 9,
		/// <summary>Removed from the queue because the character joined a party by other means.</summary>
		JoinedParty = 10,
		/// <summary>Removed from the queue because the character entered an instance by other means.</summary>
		EnteredInstance = 11,
		/// <summary>
		/// A group formed, but the character stayed untransferable — in combat, dead — for
		/// longer than the server allows, and the group went on without them.
		/// </summary>
		GroupLeftWithoutYou = 12,
		/// <summary>The queue entry disappeared server-side: a server restart, or a stale-row sweep.</summary>
		Removed = 13,
		/// <summary>
		/// Removed from the queue because the character walked away from the entrance. Waiting is
		/// done standing at the dungeon, so that being moved into it is never a surprise.
		/// </summary>
		LeftEntrance = 14,
		/// <summary>Only the party leader may queue the party for an arena.</summary>
		NotPartyLeader = 15,
		/// <summary>The party has more members than a team of the chosen format seats.</summary>
		PartyTooLarge = 16,
		/// <summary>A party member is not at the board, or not on this scene server.</summary>
		PartyNotPresent = 17,
		/// <summary>A party member is inside an instance, holds one, or is seated in a live arena match.</summary>
		PartyMemberBusy = 18,
		/// <summary>Locked out of the arena queue for deserting a match or declining a ready check.</summary>
		QueueLocked = 19,
		/// <summary>Ranked arenas: a pre-made party must fill a whole team of the format.</summary>
		PartyMustFillTeam = 20,
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
	/// Server → Client broadcast to start a dialogue session.
	/// The client resolves the <see cref="DialogueTemplate"/> from the cache and displays the start node.
	/// </summary>
	public struct DialogueStartBroadcast : IBroadcast
	{
		/// <summary>ID of the dialogue interactable scene object. 0 when triggered via ECA without an interactable.</summary>
		public long InteractableID;
		/// <summary>CachedScriptableObject ID of the DialogueTemplate.</summary>
		/// <remarks>
		/// <b>The start node is not sent.</b> It is <c>DialogueTemplate.StartNodeId</c>, authored on
		/// the same template the client resolves from this id — and the server opens the session on
		/// that same authored value, so a second copy on the wire could only ever agree with it.
		/// </remarks>
		public int TemplateID;
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

	// ──────────────────────────────────────────
	//  Shrine
	// ──────────────────────────────────────────

	/// <summary>
	/// Server → Client broadcast answering a shrine interaction.
	/// </summary>
	/// <remarks>
	/// Sent on refusal as well as on success. A shrine now has a per-character cooldown, and a
	/// refusal that sent nothing back would be indistinguishable from a lost packet — the player
	/// presses the key and the world does not react, which is the dead-keypress failure this
	/// codebase has had to chase out of several other paths. <see cref="RemainingCooldownSeconds"/>
	/// is what lets the client say <em>why</em> rather than merely that nothing happened.
	/// </remarks>
	public struct ShrineBroadcast : IBroadcast
	{
		/// <summary>ID of the shrine interactable.</summary>
		public long InteractableID;
		/// <summary>Template ID of the shrine.</summary>
		public int TemplateID;
		/// <summary>True when the shrine's effects were actually applied.</summary>
		public bool Success;
		/// <summary>Seconds until this character may use the shrine again. 0 when it is ready.</summary>
		public float RemainingCooldownSeconds;
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
		/// <summary>The server tick the change began at, for a target that plays it out (<see cref="ITimedSwitchTarget"/>).</summary>
		public uint ChangeTick;
		/// <summary>How far along its travel that target stood when the change began, 0..1.</summary>
		public float ChangeTravel;
		/// <summary>The server tick its resting pose turns solid from (<see cref="ITimedSwitchTarget.SolidTick"/>; 0: not yet).</summary>
		public uint SolidTick;
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

	/// <summary>
	/// Server → Client reply to a container take request.
	/// </summary>
	/// <remarks>
	/// The take handler had no reply of any kind, so a client could not tell a refusal from a lost
	/// packet and had nothing to release a per-slot pending lock on. Every exit now sends one, and
	/// a successful take is followed by a fresh <see cref="ContainerOpenBroadcast"/> — the same
	/// snapshot-not-delta contract corpse loot uses, so a client that missed an update still
	/// converges on what the container actually holds.
	/// </remarks>
	public struct ContainerTakeResultBroadcast : IBroadcast
	{
		/// <summary>ID of the container interactable.</summary>
		public long InteractableID;
		/// <summary>The slot the request named.</summary>
		public int Slot;
		/// <summary>True when the item was transferred.</summary>
		public bool Success;
		/// <summary>Why the take was refused.</summary>
		public ContainerFailureReason Reason;
	}

	/// <summary>
	/// Why a container take was refused.
	/// </summary>
	public enum ContainerFailureReason : byte
	{
		/// <summary>The request succeeded; no failure.</summary>
		None = 0,
		/// <summary>The container no longer exists or is out of range.</summary>
		NoContainer = 1,
		/// <summary>The slot was empty — usually somebody else got there first.</summary>
		AlreadyTaken = 2,
		/// <summary>The player's inventory had no room.</summary>
		InventoryFull = 3,
		/// <summary>The server could not process the request.</summary>
		ServerError = 4,
	}
}
