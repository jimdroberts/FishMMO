using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Why a trade request did not produce a trade session.
	/// </summary>
	public enum TradeRequestFailure : byte
	{
		/// <summary>No failure. Never sent; present so a zeroed struct is visibly not a refusal.</summary>
		None = 0,

		/// <summary>The target is not on this scene server, or cannot act right now.</summary>
		TargetUnavailable = 1,

		/// <summary>The target is already trading or already has a pending invitation.</summary>
		TargetBusy = 2,

		/// <summary>The requester is already trading or already has an invitation out.</summary>
		SelfBusy = 3,

		/// <summary>The two characters are not within trading range of each other.</summary>
		OutOfRange = 4,

		/// <summary>The target declined.</summary>
		Declined = 5,

		/// <summary>The target did not answer before the invitation expired.</summary>
		Expired = 6,

		/// <summary>The requester is sending requests faster than the server accepts them.</summary>
		Throttled = 7,

		/// <summary>The requester cannot act right now (dead, stunned, teleporting).</summary>
		CannotAct = 8,
	}

	/// <summary>
	/// Why a trade session ended.
	/// </summary>
	/// <remarks>
	/// Every session ends with exactly one of these, delivered to both parties. The reasons are
	/// receiver-shaped: the party that cancelled is told <see cref="Cancelled"/> and the other
	/// party <see cref="PartnerCancelled"/>, so a client never has to work out which side it was
	/// on from a character id.
	/// </remarks>
	public enum TradeCloseReason : byte
	{
		/// <summary>Both sides accepted and the exchange was applied.</summary>
		Completed = 0,

		/// <summary>You cancelled.</summary>
		Cancelled = 1,

		/// <summary>The other party cancelled.</summary>
		PartnerCancelled = 2,

		/// <summary>The two of you moved out of trading range.</summary>
		OutOfRange = 3,

		/// <summary>The other party left the scene or disconnected.</summary>
		PartnerLeft = 4,

		/// <summary>One of you can no longer act (dead, stunned, teleporting).</summary>
		CannotAct = 5,

		/// <summary>One side has no room for what the other offered.</summary>
		NoRoom = 6,

		/// <summary>The server could not validate the exchange at the moment of completion.</summary>
		Failed = 7,

		/// <summary>The server is shutting the trade system down.</summary>
		ServerShutdown = 8,
	}

	/// <summary>
	/// Why the server refused a change to the table.
	/// </summary>
	/// <remarks>
	/// Every refusal is answered, never swallowed: a request that changes nothing and says
	/// nothing reads to the player as a window that does not work. The refusal travels with
	/// the current table so the client is corrected and told why in one step.
	/// </remarks>
	public enum TradeRefusalReason : byte
	{
		None = 0,

		/// <summary>The session is not open for changes (committing, or already closed).</summary>
		NotOpen = 1,

		/// <summary>You cannot act right now (dead, stunned, teleporting).</summary>
		CannotAct = 2,

		/// <summary>No such inventory slot, or nothing in it.</summary>
		InvalidSlot = 3,

		/// <summary>The item is reserved by something else (a consumable activating, a pending write).</summary>
		ItemLocked = 4,

		/// <summary>The item has not been written to the database yet and cannot be traded until it is.</summary>
		ItemNotReady = 5,

		/// <summary>The quantity does not fit the stack.</summary>
		BadAmount = 6,

		/// <summary>That slot is already on the table.</summary>
		AlreadyOffered = 7,

		/// <summary>Your side of the table is full.</summary>
		TableFull = 8,

		/// <summary>Nothing from that slot is on the table.</summary>
		NotOffered = 9,

		/// <summary>You do not hold that much currency.</summary>
		InsufficientCurrency = 10,

		/// <summary>Currency cannot be traded on this server.</summary>
		NoCurrency = 11,

		/// <summary>The table changed before your accept arrived; look again.</summary>
		StaleVersion = 12,

		/// <summary>You do not have enough free bag slots for what they are offering.</summary>
		NoRoom = 13,

		/// <summary>They do not have enough free bag slots for what you are offering.</summary>
		PartnerNoRoom = 14,

		/// <summary>Both sides have confirmed, so the offers are frozen. Revoke to change yours.</summary>
		TableLocked = 15,

		/// <summary>Nothing to accept yet: both sides must confirm their offers first.</summary>
		NotConfirmed = 16,
	}

	/// <summary>
	/// Tells one party the server refused their last change, and why.
	/// </summary>
	/// <remarks>
	/// Sent to the party that asked, alongside a fresh <see cref="TradeStateBroadcast"/>.
	/// The other party is not told: their table did not change.
	/// </remarks>
	public struct TradeRefusedBroadcast : IBroadcast
	{
		/// <summary>Why the change was refused.</summary>
		public TradeRefusalReason Reason;
	}

	/// <summary>
	/// One item on the table, as the server describes it to both parties.
	/// </summary>
	/// <remarks>
	/// Carries enough to draw the item on a client that does not hold it — the partner's offer
	/// is made of items the receiving client has never seen — and the inventory slot so the
	/// owning client can lock and unlock its own bag without a second lookup. The
	/// <see cref="ItemID"/> is informational; nothing on the client may send it back as a claim.
	/// </remarks>
	public struct TradeOfferEntry
	{
		/// <summary>The offering character's inventory slot the item sits in.</summary>
		public int Slot;

		/// <summary>The item's durable identity, for display and tooltips only.</summary>
		public long ItemID;

		/// <summary>Item template.</summary>
		public int TemplateID;

		/// <summary>Generation seed, so a generated item's tooltip rolls the same stats on both clients.</summary>
		public int Seed;

		/// <summary>Quantity offered out of the stack. 1 for a non-stackable item.</summary>
		public uint Amount;
	}

	// ── Client → server ─────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Asks the server to invite another player to trade.
	/// </summary>
	public struct TradeRequestBroadcast : IBroadcast
	{
		/// <summary>The character to invite.</summary>
		public long TargetCharacterID;
	}

	/// <summary>
	/// Answers a <see cref="TradeInviteBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// Names the requester rather than being an empty struct, for the reason
	/// <see cref="PartyAcceptInviteBroadcast"/> gives: a dialog left open past the invitation's
	/// expiry must not accept whoever invites next. The server checks the id against its own
	/// pending record and refuses a mismatch; it is a claim to be verified, never a value to be
	/// trusted.
	/// </remarks>
	public struct TradeRequestResponseBroadcast : IBroadcast
	{
		/// <summary>The character whose invitation is being answered.</summary>
		public long RequesterCharacterID;

		/// <summary>True to open the trade, false to decline.</summary>
		public bool Accept;
	}

	/// <summary>
	/// Puts an inventory item on the table.
	/// </summary>
	/// <remarks>
	/// Names a slot and a quantity, never an item. The server resolves what is in the slot and
	/// what it is worth; the client cannot name a template, a seed or an identity.
	/// </remarks>
	public struct TradeOfferItemBroadcast : IBroadcast
	{
		/// <summary>Inventory slot of the item to offer.</summary>
		public int Slot;

		/// <summary>How many of a stack to offer. Zero means the whole stack.</summary>
		public uint Amount;
	}

	/// <summary>
	/// Takes an offered item back off the table.
	/// </summary>
	public struct TradeWithdrawItemBroadcast : IBroadcast
	{
		/// <summary>Inventory slot of the offered item.</summary>
		public int Slot;
	}

	/// <summary>
	/// Sets the currency this side is offering. Replaces, not adds.
	/// </summary>
	public struct TradeSetCurrencyBroadcast : IBroadcast
	{
		/// <summary>Amount offered. Zero withdraws a currency offer.</summary>
		public long Amount;
	}

	/// <summary>
	/// Declares this side's offer final, or takes that declaration back.
	/// </summary>
	/// <remarks>
	/// Stage one of two. Once BOTH sides have confirmed, the table is frozen — neither party
	/// can add, withdraw or re-price anything — and only then does
	/// <see cref="TradeAcceptBroadcast"/> mean anything. Revoking unfreezes the table and
	/// clears both acceptances, so a player can always change their mind, in full view of the
	/// other player, but never invisibly at the last instant.
	/// </remarks>
	public struct TradeConfirmBroadcast : IBroadcast
	{
		/// <summary>True to confirm, false to revoke a confirmation.</summary>
		public bool Confirm;

		/// <summary>The <see cref="TradeStateBroadcast.Version"/> the client is confirming.</summary>
		public uint Version;
	}

	/// <summary>
	/// Accepts, or un-accepts, the frozen table.
	/// </summary>
	/// <remarks>
	/// Stage two of two: refused until both sides have confirmed. Carries the state version the
	/// client was looking at, which the server checks against its own — so an accept sent
	/// before a revoke-change-reconfirm cycle cannot land as consent to a table the player
	/// never saw. The server also clears both confirmations and both acceptances on every
	/// change to either offer, so this is a second lock on the same door rather than the only one.
	/// </remarks>
	public struct TradeAcceptBroadcast : IBroadcast
	{
		/// <summary>True to accept, false to withdraw an acceptance.</summary>
		public bool Accept;

		/// <summary>The <see cref="TradeStateBroadcast.Version"/> the client is accepting.</summary>
		public uint Version;
	}

	/// <summary>
	/// Cancels the trade, or withdraws a pending invitation this client sent.
	/// </summary>
	public struct TradeCancelBroadcast : IBroadcast
	{
	}

	// ── Server → client ─────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Tells a player someone wants to trade with them.
	/// </summary>
	public struct TradeInviteBroadcast : IBroadcast
	{
		/// <summary>Who is asking.</summary>
		public long RequesterCharacterID;

		/// <summary>Their display name, so the prompt needs no name lookup.</summary>
		public string RequesterName;

		/// <summary>How long the invitation stays open, so the client can drop its prompt in step with the server.</summary>
		public float ExpiresInSeconds;
	}

	/// <summary>
	/// Tells the requester their invitation did not turn into a trade.
	/// </summary>
	/// <remarks>
	/// A trade that does open is announced by <see cref="TradeOpenedBroadcast"/> instead; this
	/// is only ever a refusal, so <see cref="Failure"/> is never <see cref="TradeRequestFailure.None"/>.
	/// </remarks>
	public struct TradeRequestResultBroadcast : IBroadcast
	{
		/// <summary>The character that was invited.</summary>
		public long TargetCharacterID;

		/// <summary>Why no trade opened.</summary>
		public TradeRequestFailure Failure;
	}

	/// <summary>
	/// Opens the trade window on both clients.
	/// </summary>
	public struct TradeOpenedBroadcast : IBroadcast
	{
		/// <summary>The other party.</summary>
		public long PartnerCharacterID;

		/// <summary>The other party's display name.</summary>
		public string PartnerName;

		/// <summary>
		/// The range, in metres, beyond which the server closes the trade. Sent so the client can
		/// close its own window the moment the players separate rather than waiting for the
		/// server's next range check; the server's check is the one that counts.
		/// </summary>
		public float MaxDistance;
	}

	/// <summary>
	/// The whole table, as seen from the receiving side.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The full state every time rather than deltas. A table holds at most a handful of items,
	/// so the saving would be small, and a client that has missed nothing still repaints from
	/// scratch — which is also what makes it trivially correct after a tree rebuild.
	/// </para>
	/// <para>
	/// Receiver-shaped: the same change produces two different messages, one per party, each
	/// saying "own" and "partner" from that party's point of view. Neither client has to know
	/// which side of the session it is.
	/// </para>
	/// </remarks>
	public struct TradeStateBroadcast : IBroadcast
	{
		/// <summary>
		/// Increments on every change to either offer. Quoted back by
		/// <see cref="TradeConfirmBroadcast"/> and <see cref="TradeAcceptBroadcast"/>.
		/// </summary>
		public uint Version;

		/// <summary>What this client's character is offering.</summary>
		public TradeOfferEntry[] OwnOffer;

		/// <summary>Currency this client's character is offering.</summary>
		public long OwnCurrency;

		/// <summary>Whether this client's character has declared its offer final.</summary>
		public bool OwnConfirmed;

		/// <summary>Whether this client's character has accepted the frozen table.</summary>
		public bool OwnAccepted;

		/// <summary>What the other party is offering.</summary>
		public TradeOfferEntry[] PartnerOffer;

		/// <summary>Currency the other party is offering.</summary>
		public long PartnerCurrency;

		/// <summary>Whether the other party has declared its offer final.</summary>
		public bool PartnerConfirmed;

		/// <summary>Whether the other party has accepted the frozen table.</summary>
		public bool PartnerAccepted;
	}

	/// <summary>
	/// Closes the trade window on a client and says why.
	/// </summary>
	public struct TradeClosedBroadcast : IBroadcast
	{
		/// <summary>Why the session ended, from this client's point of view.</summary>
		public TradeCloseReason Reason;
	}
}
