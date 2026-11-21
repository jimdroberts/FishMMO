using System;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	// Moved from the core InteractableBroadcasts.cs with the World Interactables module (2026-11-20).

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
