using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// One bill to charge an owner nobody is hosting, as the sweep read and priced it.
	/// </summary>
	/// <remarks>
	/// <para>Carries the due date the sweep read because that date is the pin: the charge only
	/// lands on a plot that still holds it, so a bill another server has charged since the read is
	/// left alone rather than charged twice.</para>
	/// <para>A bill may cover several periods — every one that has fallen due (the Unity side's
	/// <c>PlotTaxBilling</c>) — so its amount and the date it moves the plot to are the sweep's,
	/// per charge, and the charge is all or nothing: the owner pays <see cref="Amount"/> or is
	/// marked unpaid.</para>
	/// </remarks>
	public struct PlotTaxCharge
	{
		/// <summary>The plot being billed.</summary>
		public readonly long PlotID;

		/// <summary>The owning character the sweep read. The charge is pinned to it.</summary>
		public readonly long OwnerCharacterID;

		/// <summary>The due date the sweep read. The charge is pinned to it.</summary>
		public readonly DateTime DueUtc;

		/// <summary>
		/// When the first period after this bill falls due. Must be later than <see cref="DueUtc"/>.
		/// </summary>
		public readonly DateTime NextDueUtc;

		/// <summary>What the bill charges, for every period it covers. Must be greater than zero.</summary>
		public readonly long Amount;

		public PlotTaxCharge(long plotID, long ownerCharacterID, DateTime dueUtc, DateTime nextDueUtc, long amount)
		{
			PlotID = plotID;
			OwnerCharacterID = ownerCharacterID;
			DueUtc = dueUtc;
			NextDueUtc = nextDueUtc;
			Amount = amount;
		}
	}

	/// <summary>
	/// How one offline tax charge ended.
	/// </summary>
	public enum PlotTaxChargeOutcome : byte
	{
		/// <summary>The bill was won and the owner paid all of it; any earlier missed-payment mark came off in the same commit.</summary>
		Paid = 0,

		/// <summary>The bill was won and the owner could not pay all of it; nothing was taken, and the missed-payment mark was set, keeping an earlier one.</summary>
		Unpaid = 1,

		/// <summary>A server holds the owner's session. Nothing was charged; the plot is not retried until the deferral passes.</summary>
		OwnedElsewhere = 2,

		/// <summary>
		/// Not attempted this time: the plot or its owner was locked by another transaction, or the
		/// period had already moved on. Nothing changed; the next sweep sees it again if it is still due.
		/// </summary>
		Skipped = 3,

		/// <summary>
		/// The period was won but the owner's character is deleted or gone, so it cannot pay. Treated
		/// exactly as <see cref="Unpaid"/>: the mark is set and the grace period runs.
		/// </summary>
		OwnerGone = 4,
	}

	/// <summary>
	/// The outcome of one <see cref="PlotTaxCharge"/>.
	/// </summary>
	public struct PlotTaxChargeResult
	{
		/// <summary>The plot the charge was for.</summary>
		public readonly long PlotID;

		/// <summary>The owner the charge was for.</summary>
		public readonly long OwnerCharacterID;

		/// <summary>How it ended.</summary>
		public readonly PlotTaxChargeOutcome Outcome;

		public PlotTaxChargeResult(long plotID, long ownerCharacterID, PlotTaxChargeOutcome outcome)
		{
			PlotID = plotID;
			OwnerCharacterID = ownerCharacterID;
			Outcome = outcome;
		}
	}
}
