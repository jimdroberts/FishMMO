using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What it costs to get something back out of a house vault.
	/// </summary>
	/// <remarks>
	/// The design's formula, <c>fee = baseFee * (1 + daysStored * rate)</c>, kept in one place so
	/// the number a player is quoted and the number they are charged come from the same arithmetic.
	/// A quote that disagreed with the charge would look like the game taking more than it said.
	///
	/// <para>Pure, and deliberately not a method on the vault service: the fee is shown in a UI on
	/// the client, checked before the retrieval is attempted, and taken on the server, and none of
	/// those three should be reimplementing it.</para>
	/// </remarks>
	public static class PlotVaultFee
	{
		/// <summary>
		/// Largest fee this will ever quote.
		/// </summary>
		/// <remarks>
		/// The formula grows without bound in the number of days, and nothing stops a row sitting in
		/// a vault for a year. Left uncapped it would eventually overflow the arithmetic, and long
		/// before that it would price a chair above everything the player will earn — which is not a
		/// gold sink, it is a delete button wearing one. Capping keeps retrieval a decision the
		/// player can weigh rather than an answer the game has already given for them.
		/// </remarks>
		public const long MaximumFee = 1_000_000_000L;

		/// <summary>
		/// What retrieving one stored thing costs.
		/// </summary>
		/// <param name="baseFee">The fee charged the moment it is stored. Zero means free, always.</param>
		/// <param name="storedAtUtc">When it went into the vault.</param>
		/// <param name="nowUtc">The moment being quoted.</param>
		/// <param name="ratePerDay">How much of the base fee is added per day stored.</param>
		/// <remarks>
		/// Elapsed time is floored at zero rather than allowed to go negative. A row can be stamped
		/// slightly ahead of a reader's clock — the servers in a cluster do not agree to the
		/// millisecond — and a negative day count would quietly discount the fee, or, with a large
		/// enough rate, invert it into a payment for taking your own furniture back.
		///
		/// <para>Fractional days count. Rounding up to whole days would make something stored a
		/// minute ago cost a full day's interest, and rounding down would make the first day free
		/// and reward exactly the delay the fee exists to discourage.</para>
		/// </remarks>
		public static long Calculate(long baseFee, DateTime storedAtUtc, DateTime nowUtc, float ratePerDay)
		{
			if (baseFee <= 0)
			{
				return 0;
			}

			double days = (nowUtc - storedAtUtc).TotalDays;
			if (days < 0.0 || double.IsNaN(days))
			{
				days = 0.0;
			}

			/* Widened and then rounded to seven decimals, which is where the float noise lives.
			 *
			 * A rate authored as "10% a day" is stored in a <c>real</c> column, and the nearest float
			 * to 0.1 is 0.100000001490116… — widening it to a double preserves that error rather
			 * than removing it. Ten days of it turned a base fee of 100 into 200.0000015, and the
			 * ceiling below, which exists so a small fee cannot round away to nothing, charged 201.
			 * Every clean figure came out one over: the player was quoted 200 and paid 201.
			 *
			 * Seven digits is what a float ever meant, so nothing a server could plausibly author is
			 * lost. NaN is caught before the comparisons, which it would otherwise fail its way past. */
			double rate = ratePerDay;
			if (double.IsNaN(rate) || rate < 0.0)
			{
				rate = 0.0;
			}
			else if (rate > 1e9)
			{
				// A mis-authored rate. Kept finite so the fee cap below can deal with the result.
				rate = 1e9;
			}
			else
			{
				rate = Math.Round(rate, 7);
			}

			double fee = baseFee * (1.0 + (days * rate));

			/* Snapped to a whole number when that is all that separates it from one.
			 *
			 * Cleaning the rate is necessary but not sufficient: 0.2 is not representable as a double
			 * either, so seven days of it turns a base fee of 100 into 240.00000000000003, and the
			 * ceiling would charge 241 for a fee of exactly 240. The tolerance is relative so it
			 * scales with the figure and stays far below a whole currency unit at any size — a fee
			 * that genuinely lands between two whole numbers is still rounded up. */
			double whole = Math.Round(fee);
			if (Math.Abs(fee - whole) <= Math.Abs(fee) * 1e-9)
			{
				fee = whole;
			}

			/* Compared as a double before the cast. A double large enough to overflow a long casts
			 * to an undefined value rather than saturating, so the check has to happen while it is
			 * still a double or the cap would be jumped straight over. */
			if (fee >= MaximumFee)
			{
				return MaximumFee;
			}

			/* Rounded up, and floored at the base fee. A fee that rounded down to zero would make
			 * the vault free for anything cheap enough, which is the one case where a player has
			 * least reason to hurry. */
			long charged = (long)Math.Ceiling(fee);
			return Math.Max(baseFee, charged);
		}

		/// <summary>
		/// The same fee, for a rate authored as a per-day percentage rather than a fraction.
		/// </summary>
		/// <remarks>
		/// A convenience for inspector fields, which read better as "10% a day" than as "0.1".
		/// </remarks>
		public static long CalculateFromPercent(long baseFee, DateTime storedAtUtc, DateTime nowUtc, float percentPerDay)
		{
			return Calculate(baseFee, storedAtUtc, nowUtc, Mathf.Max(0f, percentPerDay) * 0.01f);
		}
	}
}
