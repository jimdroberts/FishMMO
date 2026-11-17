using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// One billing of a due plot: every tax period that has fallen due, charged as one amount.
	/// </summary>
	/// <remarks>
	/// The default value bills nothing (<see cref="IsDue"/> is false).
	/// </remarks>
	public readonly struct PlotTaxBill
	{
		/// <summary>How many periods the bill covers. Zero when nothing is due.</summary>
		public readonly long Periods;

		/// <summary>
		/// When the first period after this bill falls due. Always later than the moment the bill
		/// was made at, so a plot billed once is not due again until a new period starts.
		/// </summary>
		public readonly DateTime NextDueUtc;

		/// <summary>
		/// What the bill charges: <see cref="Periods"/> times the tax per period, saturating at
		/// <see cref="long.MaxValue"/> rather than wrapping.
		/// </summary>
		public readonly long Amount;

		public PlotTaxBill(long periods, DateTime nextDueUtc, long amount)
		{
			Periods = periods;
			NextDueUtc = nextDueUtc;
			Amount = amount;
		}

		/// <summary>True when the bill covers at least one period.</summary>
		public bool IsDue => Periods > 0;
	}

	/// <summary>
	/// Works out what a due plot is billed: every period that has fallen due, at once.
	/// </summary>
	/// <remarks>
	/// <para>A plot several periods behind — its world unhosted for a while, every server down
	/// across a billing date, the period shortened in configuration — used to be billed one period
	/// per sweep, and the due date moved on by one period each time. So it stayed due, and every
	/// sweep read it again, until it had caught up. It is now billed for all of them together, and
	/// its date moves past the moment billed in the same step.</para>
	///
	/// <para><b>All or nothing.</b> The owner pays the whole amount or is marked unpaid for the
	/// whole bill; nothing is part-paid. That keeps the two charge paths — the in-memory charge for
	/// an owner a server holds, and the batched charge from the stored row for one nobody holds —
	/// answering the same question, "can they cover <see cref="PlotTaxBill.Amount"/>?", with no
	/// partial payment either would have to split and settle.</para>
	///
	/// <para>Both paths take the bill the sweep made here once per plot, so they cannot disagree
	/// about how many periods are owed or where the date goes next. The bill never decides who
	/// wins it: that is still the compare-and-set on the due date the sweep read.</para>
	/// </remarks>
	public static class PlotTaxBilling
	{
		/// <summary>
		/// The bill for a plot due at <paramref name="dueUtc"/>, as of <paramref name="nowUtc"/>.
		/// </summary>
		/// <param name="dueUtc">The due date the sweep read.</param>
		/// <param name="nowUtc">The moment being billed.</param>
		/// <param name="period">How long one tax period lasts. Must be positive.</param>
		/// <param name="taxPerPeriod">The tax for one period. Zero or less bills an amount of zero.</param>
		/// <returns>
		/// Every period whose due date is at or before <paramref name="nowUtc"/>: the one due at
		/// <paramref name="dueUtc"/> and each after it. Nothing when the plot is not yet due, the
		/// period is not positive, or the next date would not fit in a <see cref="DateTime"/>.
		/// </returns>
		public static PlotTaxBill Bill(DateTime dueUtc, DateTime nowUtc, TimeSpan period, long taxPerPeriod)
		{
			long periodTicks = period.Ticks;
			if (periodTicks <= 0 || dueUtc > nowUtc)
			{
				return default;
			}

			/* The due dates at or before now are dueUtc, dueUtc + p, ... dueUtc + k·p with
			 * k = floor(elapsed / p): k + 1 of them. The next date, dueUtc + (k + 1)·p, is then
			 * strictly after now, whatever the remainder. */
			long elapsedTicks = (nowUtc - dueUtc).Ticks;
			long periods = elapsedTicks / periodTicks + 1;

			long headroom = DateTime.MaxValue.Ticks - dueUtc.Ticks;
			if (periods > headroom / periodTicks)
			{
				return default;
			}
			DateTime nextDueUtc = dueUtc.AddTicks(periods * periodTicks);

			long amount;
			if (taxPerPeriod <= 0)
			{
				amount = 0;
			}
			else if (periods > long.MaxValue / taxPerPeriod)
			{
				// More than any balance can hold, so it cannot be paid — never a wrapped, small bill.
				amount = long.MaxValue;
			}
			else
			{
				amount = periods * taxPerPeriod;
			}

			return new PlotTaxBill(periods, nextDueUtc, amount);
		}
	}
}
