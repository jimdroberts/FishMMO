using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// What paying rent ahead costs, and where it moves the due date.
	/// </summary>
	/// <remarks>
	/// <para>Paying ahead buys whole periods: <c>periods × rent</c>, moving the due date that many periods
	/// later. It follows the rule every other payment follows (<see cref="PlotTaxBilling"/>): a payment that
	/// lands clears any missed-payment mark, so a prepaying owner who had fallen behind is back in good
	/// standing.</para>
	///
	/// <para>How far ahead is capped (<see cref="MaxPeriodsAhead"/> periods past now), so rent cannot be
	/// paid once and forgotten: a house nobody visits for months still comes up for its rent, and the
	/// cap is what keeps "abandoned" meaning something.</para>
	///
	/// <para>Pure, and the same on both sides of the wire: the panel quotes from it, and the server
	/// refuses a request whose amount it would not have quoted.</para>
	/// </remarks>
	public static class PlotRentPrepay
	{
		/// <summary>The furthest ahead rent may be paid, in periods past now.</summary>
		public const int MaxPeriodsAhead = 8;

		/// <summary>One prepayment.</summary>
		public readonly struct Quote
		{
			public readonly int Periods;
			public readonly long Amount;
			public readonly DateTime NextDueUtc;

			public Quote(int periods, long amount, DateTime nextDueUtc)
			{
				Periods = periods;
				Amount = amount;
				NextDueUtc = nextDueUtc;
			}

			/// <summary>True when there is something to pay.</summary>
			public bool IsValid => Periods > 0 && Amount > 0;
		}

		/// <summary>
		/// How many more periods may be paid now: enough to bring the due date to at most
		/// <see cref="MaxPeriodsAhead"/> periods past <paramref name="nowUtc"/>.
		/// </summary>
		public static int PeriodsAllowed(DateTime dueUtc, DateTime nowUtc, TimeSpan period)
		{
			if (period.Ticks <= 0)
			{
				return 0;
			}
			long limitTicks = nowUtc.Ticks + period.Ticks * MaxPeriodsAhead;
			long roomTicks = limitTicks - dueUtc.Ticks;
			if (roomTicks < period.Ticks)
			{
				return 0;
			}
			long periods = roomTicks / period.Ticks;
			return (int)Math.Min(MaxPeriodsAhead, periods);
		}

		/// <summary>
		/// Prices paying <paramref name="periods"/> periods ahead; the default (invalid) quote when that is
		/// more than <see cref="PeriodsAllowed"/>, or when there is no rent.
		/// </summary>
		public static Quote Price(DateTime dueUtc, DateTime nowUtc, TimeSpan period, long rentPerPeriod, int periods)
		{
			if (rentPerPeriod <= 0 || periods <= 0 || periods > PeriodsAllowed(dueUtc, nowUtc, period))
			{
				return default;
			}
			if (periods > long.MaxValue / rentPerPeriod)
			{
				return default;
			}
			return new Quote(periods, periods * rentPerPeriod, dueUtc.AddTicks(period.Ticks * periods));
		}
	}
}
