using System;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for what it costs to get something back out of a house vault.
	/// </summary>
	/// <remarks>
	/// The formula is <c>baseFee * (1 + daysStored * rate)</c>. It is quoted to the player in one
	/// place and charged in another, so the arithmetic lives in one function — and these pin the
	/// edges of it, which is where a fee turns into either free storage or a bill nobody can pay.
	/// </remarks>
	[TestFixture]
	public class PlotVaultFeeTests
	{
		private static readonly DateTime Stored = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

		[Test]
		public void RetrievingImmediately_CostsTheBaseFee()
		{
			Assert.AreEqual(100, PlotVaultFee.Calculate(100, Stored, Stored, 0.1f));
		}

		[Test]
		public void TenDaysAtTenPercent_Doubles()
		{
			long fee = PlotVaultFee.Calculate(100, Stored, Stored.AddDays(10), 0.1f);

			Assert.AreEqual(200, fee, "100 * (1 + 10 * 0.1) is 200.");
		}

		[Test]
		public void FractionalDaysCount()
		{
			/* Rounding up to whole days would charge a full day's interest on something stored a
			 * minute ago; rounding down would make the first day free and reward exactly the delay
			 * the fee exists to discourage. */
			long fee = PlotVaultFee.Calculate(1000, Stored, Stored.AddHours(12), 1.0f);

			Assert.AreEqual(1500, fee, "Half a day at 100% a day is half the base fee added.");
		}

		[Test]
		public void AZeroBaseFee_IsAlwaysFree()
		{
			Assert.AreEqual(0, PlotVaultFee.Calculate(0, Stored, Stored.AddDays(1000), 10f),
				"A server that sets no base fee has switched retrieval fees off; time must not reintroduce one.");
		}

		[Test]
		public void AZeroRate_NeverGrows()
		{
			Assert.AreEqual(250, PlotVaultFee.Calculate(250, Stored, Stored.AddDays(365), 0f));
		}

		[Test]
		public void AClockSkewedRowIsNotDiscounted()
		{
			/* Servers in a cluster do not agree to the millisecond, so a row can be stamped slightly
			 * ahead of the reader's clock. A negative day count would quietly discount the fee, and
			 * with a large enough rate would invert it into a payment for taking your own furniture
			 * back. */
			long fee = PlotVaultFee.Calculate(100, Stored, Stored.AddHours(-1), 10f);

			Assert.AreEqual(100, fee, "Time that has not passed must not reduce the fee below its base.");
		}

		[Test]
		public void ANegativeRate_IsTreatedAsZero()
		{
			Assert.AreEqual(100, PlotVaultFee.Calculate(100, Stored, Stored.AddDays(30), -5f));
		}

		[Test]
		public void AVeryOldRow_IsCappedRatherThanOverflowing()
		{
			/* The formula grows without bound and nothing stops a row sitting in a vault for years.
			 * Uncapped it eventually overflows the arithmetic, and long before that it prices a
			 * chair above everything the player will ever earn — which is not a gold sink, it is a
			 * delete button wearing one. */
			long fee = PlotVaultFee.Calculate(1_000_000, Stored, Stored.AddDays(100_000), 100f);

			Assert.AreEqual(PlotVaultFee.MaximumFee, fee);
		}

		[Test]
		public void TheCapIsNotJumpedOverByTheCast()
		{
			// A double large enough to overflow a long casts to an undefined value rather than
			// saturating, so the cap has to be applied while the figure is still a double.
			long fee = PlotVaultFee.Calculate(long.MaxValue / 2, Stored, Stored.AddDays(10), 1f);

			Assert.AreEqual(PlotVaultFee.MaximumFee, fee);
			Assert.Greater(fee, 0, "An overflowed cast would come back negative.");
		}

		/// <summary>
		/// A rate authored as a round number must produce a round fee.
		/// </summary>
		/// <remarks>
		/// This caught a real bug. The rate is stored in a <c>real</c> column, so it arrives as a
		/// float — and the nearest float to 0.1 is 0.100000001490116…, which widening to a double
		/// preserves. Ten days of it turned a base fee of 100 into 200.0000015, and the ceiling that
		/// stops small fees rounding away to nothing turned that into 201. Every clean figure came
		/// out one over: the player was quoted 200 and charged 201.
		///
		/// <para>These cases sweep the rates and durations a server is actually likely to author,
		/// because the failure was not in any one of them — it was in all of them at once, and only
		/// ever by one.</para>
		/// </remarks>
		[Test]
		public void RoundRatesProduceRoundFees_AndAreNeverOneOver()
		{
			(long baseFee, int days, float rate, long expected)[] cases =
			{
				(100, 10, 0.1f, 200),
				(100, 20, 0.1f, 300),
				(1_000, 10, 0.1f, 2_000),
				(1_000_000, 10, 0.1f, 2_000_000),
				(250, 4, 0.25f, 500),
				(100, 1, 1f, 200),
				(500, 2, 0.5f, 1_000),
				(100, 7, 0.2f, 240),
			};

			foreach ((long baseFee, int days, float rate, long expected) in cases)
			{
				long fee = PlotVaultFee.Calculate(baseFee, Stored, Stored.AddDays(days), rate);

				Assert.AreEqual(expected, fee,
					$"{baseFee} at {rate} a day for {days} days should be exactly {expected}.");
			}
		}

		/// <summary>
		/// The ceiling is still there for figures that genuinely are not whole.
		/// </summary>
		/// <remarks>
		/// The fix for the rounding bug must not become a licence to round fees down. A fee that
		/// truly lands between two whole numbers is charged at the higher one, so nothing is ever
		/// handed back for free.
		/// </remarks>
		[Test]
		public void AGenuinelyFractionalFee_IsStillRoundedUp()
		{
			long fee = PlotVaultFee.Calculate(100, Stored, Stored.AddHours(1), 0.1f);

			Assert.AreEqual(101, fee, "100 * (1 + (1/24) * 0.1) is 100.4166…, which must cost 101.");
		}

		[Test]
		public void PercentAndFractionAgree()
		{
			long asFraction = PlotVaultFee.Calculate(500, Stored, Stored.AddDays(3), 0.25f);
			long asPercent = PlotVaultFee.CalculateFromPercent(500, Stored, Stored.AddDays(3), 25f);

			Assert.AreEqual(asFraction, asPercent, "The inspector-friendly overload must not be a second formula.");
		}
	}
}
