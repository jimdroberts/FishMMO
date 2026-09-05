namespace FishMMO.Shared
{
	/// <summary>
	/// How a currency movement ended.
	/// </summary>
	/// <remarks>
	/// A ledger row is written once, after the transaction it describes has already resolved and
	/// its deduction has been persisted, so the outcome is known at the moment of writing and the
	/// row is never revisited.
	///
	/// <para>This is deliberately NOT an escrow. An escrow holds funds across an in-flight
	/// transaction so an interrupted one can be recovered, which requires the hold and the balance
	/// deduction to commit together. They cannot here: the deduction goes through the in-memory
	/// attribute controller and an asynchronous persistence queue, not a transaction this code can
	/// join. A hold recorded outside that transaction is not recoverable state — it is a row that
	/// says a transaction happened, and returning money on the strength of it either pays out
	/// currency that was never taken or refunds a purchase that completed. Recording the settled
	/// outcome directly removes the window in which either could be believed.</para>
	/// </remarks>
	public enum CurrencyMovementState
	{
		/// <summary>
		/// No outcome recorded. The column default, and never written by any path.
		/// </summary>
		/// <remarks>
		/// A row in this state means an INSERT omitted the outcome, which is a bug rather than a
		/// transaction awaiting settlement. Present so that case is visible instead of being
		/// silently counted as currency leaving the economy.
		/// </remarks>
		Unsettled = 0,

		/// <summary>
		/// The transaction completed. The currency has left the economy.
		/// </summary>
		Absorbed = 1,

		/// <summary>
		/// The transaction did not complete and the currency was given back.
		/// </summary>
		Returned = 2,
	}

	/// <summary>
	/// What a currency movement was for.
	/// </summary>
	/// <remarks>
	/// Recorded per movement so the table answers which sinks drain currency and at what rate —
	/// a question an MMO wants to ask and currently cannot, because nothing records where spent
	/// money went.
	/// </remarks>
	public enum CurrencyMovementReason
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

		/// <summary>The fee for founding a guild. Issue #186.</summary>
		GuildCreation = 8,

		/// <summary>
		/// Buying something back out of a house vault after land was reclaimed.
		/// </summary>
		/// <remarks>
		/// Its own reason rather than folded into <see cref="LandTax"/>. They are the same system
		/// but not the same sink, and telling them apart is the point of recording a reason at all:
		/// tax measures what holding land costs, while this measures what losing it costs — and a
		/// designer tuning one wants to see the other move separately.
		/// </remarks>
		HouseVaultFee = 9,
	}
}
