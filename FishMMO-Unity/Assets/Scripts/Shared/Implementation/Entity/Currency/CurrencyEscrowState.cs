namespace FishMMO.Shared
{
	/// <summary>
	/// Where currency sits during a transaction.
	/// </summary>
	/// <remarks>
	/// A transaction that takes money and gives something back has a window between the two
	/// halves. Without a record of that window, a crash inside it leaves the deduction persisted
	/// and nothing granted — the money is simply gone, with nothing to say a transaction was ever
	/// in flight. These states name the window so it can be reconciled instead.
	/// </remarks>
	public enum CurrencyEscrowState
	{
		/// <summary>
		/// Taken from the player's balance and not yet settled.
		/// </summary>
		/// <remarks>
		/// Anything left in this state after a restart is an interrupted transaction and must be
		/// returned: the grant either never happened or cannot be confirmed, and returning is the
		/// only outcome that cannot silently take money.
		/// </remarks>
		Held = 0,

		/// <summary>
		/// Settled in favour of the transaction. The currency has left the economy.
		/// </summary>
		Absorbed = 1,

		/// <summary>
		/// Settled back to the player. The transaction did not complete.
		/// </summary>
		Returned = 2,
	}

	/// <summary>
	/// What a hold was taken for.
	/// </summary>
	/// <remarks>
	/// Recorded per hold so the escrow table doubles as an economy ledger: which sinks are
	/// draining currency and at what rate is a question an MMO wants to answer and currently
	/// cannot, because nothing records where spent money went.
	/// </remarks>
	public enum CurrencyEscrowReason
	{
		/// <summary>Unclassified. Present so a missing value is visible rather than silently a purchase.</summary>
		Unknown = 0,

		/// <summary>Buying an item from a merchant.</summary>
		MerchantPurchase = 1,

		/// <summary>Learning an ability or event from a trainer.</summary>
		AbilityLearn = 2,

		/// <summary>Crafting an ability.</summary>
		AbilityCraft = 3,

		/// <summary>Currency attached to a mail.</summary>
		MailAttachment = 4,

		/// <summary>Purchasing land.</summary>
		LandPurchase = 5,

		/// <summary>A recurring charge against owned land.</summary>
		LandTax = 6,

		/// <summary>A player-to-player trade.</summary>
		PlayerTrade = 7,
	}
}
