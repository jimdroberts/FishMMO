using System;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for purchasing an item from a merchant.
	/// Contains the interactable object's ID, item ID, index, and tab type.
	/// </summary>
	public struct MerchantPurchaseBroadcast : IBroadcast
	{
		/// <summary>ID of the merchant object.</summary>
		/// <remarks>
		/// <b>The merchant template id is not sent.</b> The server resolves the interactable from
		/// this id and reads the template off the merchant it finds — which it did already, only to
		/// compare the answer against a number the client had supplied. That comparison guarded
		/// nothing: a client that named a different template was refused by the comparison, and a
		/// client that named the right one was still held to the resolved merchant. What actually
		/// guards a purchase is <c>InteractableResolver.Resolve</c> picking the merchant out of an
		/// object that may carry several interactables, and <c>CanInteract</c> — which is where the
		/// range and corpse gates live. Both are untouched.
		/// </remarks>
		public long InteractableID;
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
	/// Server -> Client result of a <see cref="MerchantPurchaseBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// The same contract the sell path already keeps, for the same reason: the client arms a
	/// watchdog when it submits a purchase, and a handler that simply returns leaves that
	/// watchdog to expire into "No reply from the server" — which describes a network fault
	/// rather than a request the server understood and refused, and invites the player to retry
	/// something that cannot succeed. Every exit from the purchase handler sends one of these.
	/// </remarks>
	public struct MerchantPurchaseResultBroadcast : IBroadcast
	{
		/// <summary>The entry index the request named.</summary>
		public int Index;
		/// <summary>The tab the request named.</summary>
		public MerchantTabType Type;
		/// <summary>True when the purchase went through.</summary>
		public bool Success;
		/// <summary>Why the purchase was refused. <see cref="MerchantPurchaseFailure.None"/> on success.</summary>
		public MerchantPurchaseFailure Failure;
		/// <summary>Quantity actually granted, after server-side clamping.</summary>
		public int Quantity;
		/// <summary>Currency actually charged.</summary>
		public long Charged;
	}

	/// <summary>Why a merchant purchase was refused.</summary>
	public enum MerchantPurchaseFailure : byte
	{
		/// <summary>No failure; the purchase succeeded.</summary>
		None = 0,
		/// <summary>The merchant, character or session no longer validates.</summary>
		Unavailable,
		/// <summary>The entry index or tab did not resolve to something purchasable.</summary>
		InvalidEntry,
		/// <summary>
		/// The entry is priced below zero, which is not a price. A price of ZERO is free and is
		/// sold normally — it used to land here, and since <c>Price</c> is an int defaulting to 0
		/// that made every unedited template unsellable.
		/// </summary>
		NotForSale,
		/// <summary>The character cannot afford the requested quantity.</summary>
		InsufficientFunds,
		/// <summary>There is no room to receive the goods.</summary>
		NoRoom,
		/// <summary>The character already knows the ability or ability event on offer.</summary>
		AlreadyKnown,
		/// <summary>
		/// The character holds as many usable abilities as the server allows. Its own reason
		/// rather than <see cref="NoRoom"/>, because the remedy is different: nothing in the bags
		/// needs clearing, and the merchant panel says so.
		/// </summary>
		AbilityLimit,
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
