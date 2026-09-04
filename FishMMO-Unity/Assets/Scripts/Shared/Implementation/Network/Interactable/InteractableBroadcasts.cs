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
	/// Client → Server broadcast asking for the instances joinable at one difficulty.
	/// </summary>
	/// <remarks>
	/// Sent when the panel opens, when the player changes difficulty tab, and when they press
	/// Refresh — never on a timer. The list is only interesting while somebody is looking at it,
	/// and a panel that polls turns every open finder on the shard into standing database load.
	/// <para>
	/// Debounced per connection on the server. The reply is a database query the client controls
	/// the timing of, which is the shape of request that has to be rate limited whether or not
	/// the client cooperates.
	/// </para>
	/// </remarks>
	public struct DungeonFinderListBroadcast : IBroadcast
	{
		/// <summary>ID of the dungeon entrance the player is standing at.</summary>
		public long InteractableID;

		/// <summary>Difficulty index being browsed.</summary>
		public int Difficulty;
	}

	/// <summary>
	/// One joinable instance, as offered in the dungeon finder's list.
	/// </summary>
	/// <remarks>
	/// Deliberately thin. It names who is running it and how full it is — enough to choose
	/// between rows — and nothing that would let the list be used to track where a particular
	/// player is: no character IDs, no scene handles, and no members beyond the leader's name.
	/// </remarks>
	[Serializable]
	public struct DungeonInstanceEntry
	{
		/// <summary>Instance row ID. The only identity a join request may name.</summary>
		public long InstanceID;

		/// <summary>Name of the party leader running it, or empty when it cannot be resolved.</summary>
		public string LeaderName;

		/// <summary>How many characters are currently inside.</summary>
		public int MemberCount;

		/// <summary>Capacity at this difficulty.</summary>
		public int MaxMembers;

		/// <summary>Seconds until it closes on its own, or 0 when not yet known.</summary>
		public int RemainingSeconds;

		/// <summary>True while the instance is still being loaded and cannot be entered yet.</summary>
		public bool IsLoading;

		/// <summary>True when this is the requesting character's own party's instance.</summary>
		public bool IsOwnParty;
	}

	/// <summary>
	/// Server → Client reply listing the instances joinable at one difficulty.
	/// </summary>
	/// <remarks>
	/// Sent from every exit of the list handler, including the refused and the empty ones. The
	/// panel disables its list while a request is outstanding — the guard that stops Refresh being
	/// held down — and a handler that returned silently would leave the list disabled for the rest
	/// of the window's life. The same contract the merchant and container paths follow.
	/// </remarks>
	public struct DungeonFinderListResultBroadcast : IBroadcast
	{
		/// <summary>The entrance the request named.</summary>
		public long InteractableID;

		/// <summary>The difficulty the request named, echoed so a late reply can be discarded.</summary>
		public int Difficulty;

		/// <summary>Joinable instances. Empty when there are none, or when the request was refused.</summary>
		public DungeonInstanceEntry[] Instances;

		/// <summary>Why the list is empty, when it is empty for a reason worth saying.</summary>
		public DungeonListFailureReason Reason;
	}

	/// <summary>
	/// Why a dungeon finder list request produced nothing.
	/// </summary>
	public enum DungeonListFailureReason : byte
	{
		/// <summary>The list is the true answer, whether or not it is empty.</summary>
		None = 0,
		/// <summary>The entrance no longer exists, or the player has walked away from it.</summary>
		NoEntrance = 1,
		/// <summary>The dungeon does not offer the difficulty that was asked for.</summary>
		UnknownDifficulty = 2,
		/// <summary>Asked for again too soon. The previous list is still the current one.</summary>
		OnCooldown = 3,
		/// <summary>The server could not read the list; the client should offer Refresh again.</summary>
		ServerError = 4,
	}

	/// <summary>
	/// Client → Server broadcast asking to open a new instance of a dungeon.
	/// </summary>
	/// <remarks>
	/// Names a difficulty and a visibility and nothing about the instance itself. Everything that
	/// decides whether it is allowed — the party's existing instance, the difficulty's level and
	/// party requirements, the character's state — is resolved server-side from the entrance and
	/// the roster, never taken from this message.
	/// </remarks>
	public struct DungeonFinderCreateBroadcast : IBroadcast
	{
		/// <summary>ID of the dungeon entrance scene object.</summary>
		public long InteractableID;

		/// <summary>Difficulty index to open it at. Validated against the dungeon's own list.</summary>
		public int Difficulty;

		/// <summary>True to open it hidden from the finder's public list.</summary>
		public bool IsPrivate;
	}

	/// <summary>
	/// Client → Server broadcast asking to join somebody else's instance.
	/// </summary>
	/// <remarks>
	/// Joining an instance run by another group also joins their party, so this is a social
	/// action as well as a transfer and is refused for a character who already has a party of
	/// their own to leave first. The instance ID is checked against what the finder would
	/// actually have offered — public, not full, the right dungeon — rather than trusted, because
	/// a row ID is guessable and the panel is not the only thing that can send this.
	/// </remarks>
	public struct DungeonFinderJoinBroadcast : IBroadcast
	{
		/// <summary>ID of the dungeon entrance scene object.</summary>
		public long InteractableID;

		/// <summary>Instance to join, from a <see cref="DungeonInstanceEntry"/>.</summary>
		public long InstanceID;
	}

	/// <summary>
	/// Client → Server broadcast asking the group finder to find the player a group for one
	/// dungeon at one difficulty.
	/// </summary>
	/// <remarks>
	/// Sent from the dungeon finder panel, so it names the entrance and is validated like every
	/// other finder request: the player must be standing at it. They must then <em>stay</em> at
	/// it — the server drops a waiter who walks away, and the panel leaves the queue when it is
	/// closed — so that being moved into the dungeon is never a surprise to somebody who has
	/// wandered off. Refused, accepted and every later change is answered with a
	/// <see cref="GroupFinderStatusBroadcast"/>.
	/// </remarks>
	public struct GroupFinderQueueBroadcast : IBroadcast
	{
		/// <summary>ID of the dungeon entrance scene object.</summary>
		public long InteractableID;

		/// <summary>Difficulty index to queue for. Validated against the dungeon's own list.</summary>
		public int Difficulty;
	}

	/// <summary>
	/// Client → Server broadcast leaving the group finder queue.
	/// </summary>
	/// <remarks>
	/// Carries nothing: a character is in at most one queue. Sent by the finder panel's Leave
	/// Queue button and, implicitly, by closing the panel. Refused when the group has already
	/// formed — see <see cref="GroupFinderState.Matched"/>.
	/// </remarks>
	public struct GroupFinderLeaveBroadcast : IBroadcast
	{
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
	/// Server → Client broadcast reporting the character's group finder status.
	/// </summary>
	/// <remarks>
	/// Sent whenever the status changes: on the reply to a queue request, whenever the count of
	/// waiting players moves, when a group forms, and when the character leaves or is removed.
	/// Not sent on a timer while nothing changes. It carries enough for the finder panel to draw
	/// the wait — which dungeon, how many are waiting out of how many are needed — and nothing
	/// that identifies the other people waiting.
	/// </remarks>
	public struct GroupFinderStatusBroadcast : IBroadcast
	{
		/// <summary>Where the character stands.</summary>
		public GroupFinderState State;

		/// <summary>
		/// Which queue this is about: <see cref="SceneType.Group"/> for the dungeon group finder,
		/// <see cref="SceneType.PvP"/> for the arena board. One character is in at most one queue,
		/// so a status for one kind also means "not in the other". Each panel draws only its own.
		/// </summary>
		public SceneType Kind;

		/// <summary>Template ID of the dungeon queued for, or 0 when the entrance had none.</summary>
		public int DungeonTemplateID;

		/// <summary>Template ID of the arena queued for, when <see cref="Kind"/> is PvP.</summary>
		public int ArenaTemplateID;

		/// <summary>Scene name of the dungeon, so the panel can name it even without a template.</summary>
		public string SceneName;

		/// <summary>Difficulty index queued for.</summary>
		public int Difficulty;

		/// <summary>How many players are waiting for this dungeon at this difficulty, including this one.</summary>
		public int WaitingCount;

		/// <summary>How many the finder needs before it opens a run.</summary>
		public int GroupSize;

		/// <summary>Why the state is <see cref="GroupFinderState.None"/>, when it is for a reason worth saying.</summary>
		public GroupFinderRefusalReason Reason;
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
		/// <summary>
		/// How many of the item to buy. Clamped server-side; only ever a quantity, never a price.
		/// </summary>
		/// <remarks>
		/// The unit price is looked up from the merchant's own template on the server and the
		/// total is multiplied there. Nothing about cost travels on this message — a client that
		/// asks for a thousand of something simply gets its request clamped to what the stack and
		/// its purse allow.
		/// </remarks>
		public int Quantity;
	}

	/// <summary>
	/// Client → Server broadcast to sell an inventory item to a merchant.
	/// </summary>
	/// <remarks>
	/// Carries a slot index and a quantity and nothing else. The server resolves the item in that
	/// slot itself, takes the price from that item's template, and applies the merchant template's
	/// own multiplier — so neither the item's identity nor its value is ever taken from the
	/// client. This mirrors how the rest of the item trust boundary works: client to server
	/// carries slot indices and enums, never IDs or values.
	/// </remarks>
	public struct MerchantSellBroadcast : IBroadcast
	{
		/// <summary>ID of the merchant object.</summary>
		public long InteractableID;
		/// <summary>Inventory slot index holding the item being sold.</summary>
		public int Slot;
		/// <summary>How many to sell out of that slot. Clamped server-side to the stack size.</summary>
		public int Quantity;
	}

	/// <summary>
	/// Server → Client broadcast acknowledging or refusing a merchant sale.
	/// </summary>
	/// <remarks>
	/// The sell path needs an explicit reply for the same reason every other item operation does:
	/// the client holds a pending lock on the slot it submitted, and a handler that simply returns
	/// leaves that lock held forever. Every exit from the sell handler sends one of these.
	/// </remarks>
	public struct MerchantSellResultBroadcast : IBroadcast
	{
		/// <summary>The inventory slot the request named.</summary>
		public int Slot;
		/// <summary>True when the sale went through.</summary>
		public bool Success;
		/// <summary>Quantity actually sold, after server-side clamping.</summary>
		public int Quantity;
		/// <summary>Currency actually paid out.</summary>
		public int Payout;
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
	/// <remarks>
	/// The attachment is named the same way a merchant sale is: an inventory <em>slot</em> and a
	/// quantity, never an item ID and never a value. The server resolves what is actually in that
	/// slot, takes it out of the sender's inventory itself, and attaches what it removed — so a
	/// forged message can only ever name a slot the sender does not have, or more of a stack than
	/// they hold, both of which are clamped or refused.
	/// </remarks>
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
		/// <summary>Inventory slot holding the item to attach, or -1 for no item.</summary>
		public int AttachmentSlot;
		/// <summary>How much of that slot's stack to attach. Clamped server-side.</summary>
		public int AttachmentQuantity;
		/// <summary>Currency to attach. Clamped server-side to what the sender holds.</summary>
		public int CurrencyAttachment;
	}

	/// <summary>
	/// Server → Client reply to a mail send.
	/// </summary>
	/// <remarks>
	/// Sent from every exit. The client disables its send control while a request is outstanding —
	/// the double-submit guard that stops one mis-timed click mailing a stack twice — and a
	/// handler that returned silently would leave that control disabled for good. Same contract as
	/// <see cref="MerchantSellResultBroadcast"/>.
	/// </remarks>
	public struct MailSendResultBroadcast : IBroadcast
	{
		/// <summary>True when the mail was accepted for delivery.</summary>
		public bool Success;
		/// <summary>Why the send was refused.</summary>
		public MailFailureReason Reason;
	}

	/// <summary>
	/// Client → Server broadcast claiming one mail's attachment.
	/// </summary>
	public struct MailClaimAttachmentBroadcast : IBroadcast
	{
		/// <summary>ID of the mailbox interactable the player is using.</summary>
		public long InteractableID;
		/// <summary>ID of the mail whose attachment is being claimed.</summary>
		public long MailID;
	}

	/// <summary>
	/// Server → Client reply to an attachment claim.
	/// </summary>
	public struct MailClaimResultBroadcast : IBroadcast
	{
		/// <summary>The mail the request named.</summary>
		public long MailID;
		/// <summary>True when something was actually transferred.</summary>
		public bool Success;
		/// <summary>Why the claim was refused.</summary>
		public MailFailureReason Reason;
	}

	/// <summary>
	/// Why a mail send or attachment claim was refused.
	/// </summary>
	public enum MailFailureReason : byte
	{
		/// <summary>The request succeeded; no failure.</summary>
		None = 0,
		/// <summary>The server could not process the request.</summary>
		ServerError = 1,
		/// <summary>The player is not near a mailbox, or it no longer exists.</summary>
		NoMailbox = 2,
		/// <summary>The named recipient does not exist.</summary>
		NoRecipient = 3,
		/// <summary>The subject or body was empty, too long, or otherwise rejected.</summary>
		InvalidMessage = 4,
		/// <summary>The named inventory slot was empty or locked.</summary>
		InvalidAttachment = 5,
		/// <summary>The sender does not hold enough currency to attach that much.</summary>
		NotEnoughCurrency = 6,
		/// <summary>The mail had nothing attached, or it was already claimed.</summary>
		NothingToClaim = 7,
		/// <summary>The claimer's inventory had no room for the attachment.</summary>
		InventoryFull = 8,
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

	// ──────────────────────────────────────────
	//  Corpse Loot
	// ──────────────────────────────────────────

	/// <summary>
	/// One item slot on a corpse, as sent to a looter.
	/// </summary>
	/// <remarks>
	/// Carries the slot index explicitly rather than relying on array position, because emptied
	/// slots are omitted from the array but must keep their identity — the index is what a take
	/// request names, and it has to survive the round trip unchanged.
	/// </remarks>
	[Serializable]
	public struct CorpseLootSlotData
	{
		/// <summary>Slot index on the corpse.</summary>
		public int Slot;
		/// <summary>Template ID of the item in this slot.</summary>
		public int TemplateID;
		/// <summary>Stack amount in this slot.</summary>
		public uint Amount;
	}

	/// <summary>
	/// Server → Client broadcast opening (or refreshing) a corpse's loot window.
	/// </summary>
	/// <remarks>
	/// Sent both on the initial interaction and after every successful take by ANY looter, because
	/// the pile is shared: what one player removes has to disappear from everyone else's window.
	/// Re-sending the whole contents rather than a delta keeps the client a pure view of server
	/// state, so a dropped or reordered update cannot leave two players disagreeing about what is
	/// still on the body.
	/// </remarks>
	public struct CorpseLootBroadcast : IBroadcast
	{
		/// <summary>Scene object ID of the corpse.</summary>
		public long InteractableID;
		/// <summary>Display name of the corpse.</summary>
		public string CorpseName;
		/// <summary>Filled item slots. Emptied slots are omitted.</summary>
		public CorpseLootSlotData[] Items;
		/// <summary>Currency remaining on the corpse.</summary>
		public long Currency;
	}

	/// <summary>
	/// Client → Server broadcast requesting one item from a corpse.
	/// </summary>
	public struct CorpseLootTakeItemBroadcast : IBroadcast
	{
		/// <summary>Scene object ID of the corpse.</summary>
		public long InteractableID;
		/// <summary>Slot index being taken.</summary>
		public int Slot;
	}

	/// <summary>
	/// Client → Server broadcast requesting the currency on a corpse.
	/// </summary>
	public struct CorpseLootTakeCurrencyBroadcast : IBroadcast
	{
		/// <summary>Scene object ID of the corpse.</summary>
		public long InteractableID;
	}

	/// <summary>
	/// Client → Server broadcast requesting everything the corpse holds.
	/// </summary>
	public struct CorpseLootTakeAllBroadcast : IBroadcast
	{
		/// <summary>Scene object ID of the corpse.</summary>
		public long InteractableID;
	}

	/// <summary>
	/// Server → Client reply to any corpse take request.
	/// </summary>
	/// <remarks>
	/// Every exit from every take handler sends one of these, successful or not. The client marks
	/// the slot it submitted as pending and will not send another request for it until an answer
	/// arrives, so a handler that simply returned would leave that slot locked for the rest of the
	/// window's life — the same contract the merchant sell path follows.
	/// </remarks>
	public struct CorpseLootResultBroadcast : IBroadcast
	{
		/// <summary>Scene object ID of the corpse.</summary>
		public long InteractableID;
		/// <summary>The slot the request named, or -1 for currency and take-all.</summary>
		public int Slot;
		/// <summary>True when at least one thing was actually transferred.</summary>
		public bool Success;
		/// <summary>Why the request was refused, for client feedback.</summary>
		public CorpseLootFailureReason Reason;
	}

	/// <summary>
	/// Client → Server broadcast saying the player closed a corpse's loot window.
	/// </summary>
	public struct CorpseLootCloseBroadcast : IBroadcast
	{
		/// <summary>Scene object ID of the corpse.</summary>
		public long InteractableID;
	}

	/// <summary>
	/// Server → Client broadcast forcing a corpse's loot window shut.
	/// </summary>
	/// <remarks>
	/// Sent when the corpse decays, empties, or the looter walks out of range. Without it the
	/// window would outlive the scene object ID it refers to, and every button in it would submit
	/// requests against an ID that no longer resolves.
	/// </remarks>
	public struct CorpseLootCloseWindowBroadcast : IBroadcast
	{
		/// <summary>Scene object ID of the corpse whose window should close.</summary>
		public long InteractableID;
	}

	/// <summary>
	/// Why a corpse loot request was refused.
	/// </summary>
	public enum CorpseLootFailureReason : byte
	{
		/// <summary>The request succeeded; no failure.</summary>
		None = 0,
		/// <summary>The corpse no longer exists, or has already decayed.</summary>
		NoCorpse = 1,
		/// <summary>The player did not contribute to the kill.</summary>
		NotEligible = 2,
		/// <summary>The player is too far from the corpse.</summary>
		OutOfRange = 3,
		/// <summary>The slot was already empty — usually another looter got there first.</summary>
		AlreadyTaken = 4,
		/// <summary>The player's inventory had no room.</summary>
		InventoryFull = 5,
		/// <summary>The server could not process the request.</summary>
		ServerError = 6,
	}
}
