using System;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for what a due plot is billed: every period that has fallen due, at once.
	/// </summary>
	/// <remarks>
	/// A plot several periods behind used to be billed one period per sweep, its date moved on by
	/// one period each time, so it stayed due and every sweep read it again until it had caught up.
	/// The bill these pin is what both charge paths — the in-memory charge for an owner a server
	/// holds and the batched charge from the stored row — are handed, so they cannot disagree about
	/// how many periods are owed, what that costs, or where the date goes next.
	/// </remarks>
	[TestFixture]
	public class PlotTaxBillingTests
	{
		private static readonly DateTime Due = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
		private static readonly TimeSpan Week = TimeSpan.FromDays(7);
		private const long Tax = 250;

		[Test]
		public void APlotDueRightNow_IsBilledOnePeriod()
		{
			PlotTaxBill bill = PlotTaxBilling.Bill(Due, Due, Week, Tax);

			Assert.IsTrue(bill.IsDue);
			Assert.AreEqual(1, bill.Periods);
			Assert.AreEqual(Due + Week, bill.NextDueUtc);
			Assert.AreEqual(Tax, bill.Amount);
		}

		[Test]
		public void APlotPartWayThroughItsFirstPeriod_IsStillBilledOnePeriod()
		{
			PlotTaxBill bill = PlotTaxBilling.Bill(Due, Due + TimeSpan.FromDays(3), Week, Tax);

			Assert.AreEqual(1, bill.Periods);
			Assert.AreEqual(Due + Week, bill.NextDueUtc);
		}

		/// <summary>
		/// The dates due at or before now are Due, Due + 1w and Due + 2w: three periods, and the
		/// next date is the first one after now.
		/// </summary>
		[Test]
		public void APlotWhoseThirdDateIsNow_IsBilledThreePeriods()
		{
			PlotTaxBill bill = PlotTaxBilling.Bill(Due, Due + Week + Week, Week, Tax);

			Assert.AreEqual(3, bill.Periods);
			Assert.AreEqual(Due + TimeSpan.FromDays(21), bill.NextDueUtc);
			Assert.AreEqual(3 * Tax, bill.Amount);
		}

		[Test]
		public void APlotOneTickShortOfItsThirdDate_IsBilledTwoPeriods()
		{
			DateTime now = Due + Week + Week - TimeSpan.FromTicks(1);
			PlotTaxBill bill = PlotTaxBilling.Bill(Due, now, Week, Tax);

			Assert.AreEqual(2, bill.Periods);
			Assert.AreEqual(Due + Week + Week, bill.NextDueUtc,
				"The third date has not arrived, so it is the next one — one tick away, not billed yet.");
		}

		[Test]
		public void APlotNotYetDue_IsBilledNothing()
		{
			PlotTaxBill bill = PlotTaxBilling.Bill(Due, Due - TimeSpan.FromTicks(1), Week, Tax);

			Assert.IsFalse(bill.IsDue);
			Assert.AreEqual(0, bill.Periods);
			Assert.AreEqual(0, bill.Amount);
		}

		[TestCase(0)]
		[TestCase(-1)]
		public void ANonPositivePeriod_BillsNothing(long periodTicks)
		{
			Assert.IsFalse(PlotTaxBilling.Bill(Due, Due + Week, TimeSpan.FromTicks(periodTicks), Tax).IsDue,
				"A period that never ends would leave the plot permanently due.");
		}

		/// <summary>
		/// The invariant both charge paths lean on: after one bill the plot is not due again until
		/// a new period starts, and its dates stay on the grid they started on.
		/// </summary>
		[Test]
		public void TheNextDate_IsAlwaysAfterNow_AndOnThePeriodGrid()
		{
			TimeSpan period = TimeSpan.FromHours(37.5);
			for (int minutes = 0; minutes < 60 * 24 * 40; minutes += 97)
			{
				DateTime now = Due + TimeSpan.FromMinutes(minutes);
				PlotTaxBill bill = PlotTaxBilling.Bill(Due, now, period, Tax);

				Assert.IsTrue(bill.IsDue, $"due at +{minutes}m");
				Assert.Greater(bill.NextDueUtc, now, $"the next date must be after now at +{minutes}m");
				Assert.LessOrEqual(bill.NextDueUtc - now, period, $"and no more than one period after it at +{minutes}m");
				Assert.AreEqual(0, (bill.NextDueUtc - Due).Ticks % period.Ticks, $"on the grid at +{minutes}m");
				Assert.AreEqual((bill.NextDueUtc - Due).Ticks / period.Ticks, bill.Periods,
					$"every period between the due date and the next one is billed at +{minutes}m");
			}
		}

		[Test]
		public void ABillTooLargeForALong_SaturatesRatherThanWrapping()
		{
			PlotTaxBill bill = PlotTaxBilling.Bill(Due, Due + TimeSpan.FromDays(70), Week, long.MaxValue / 2);

			Assert.AreEqual(11, bill.Periods);
			Assert.AreEqual(long.MaxValue, bill.Amount,
				"A wrapped amount could be small, or negative, and be paid; a saturated one cannot be.");
		}

		/// <summary>
		/// Guild land is deferred with the same bill: the date moves past every period due, and the
		/// amount is never charged.
		/// </summary>
		[Test]
		public void NoTax_StillMovesTheDate()
		{
			PlotTaxBill bill = PlotTaxBilling.Bill(Due, Due + TimeSpan.FromDays(15), Week, 0);

			Assert.AreEqual(3, bill.Periods);
			Assert.AreEqual(Due + TimeSpan.FromDays(21), bill.NextDueUtc);
			Assert.AreEqual(0, bill.Amount);
		}

		[Test]
		public void ANextDateBeyondTheCalendar_BillsNothing()
		{
			DateTime due = DateTime.MaxValue - TimeSpan.FromDays(3);
			Assert.IsFalse(PlotTaxBilling.Bill(due, due, Week, Tax).IsDue,
				"A date that cannot be moved on is left alone rather than thrown on.");
		}
	}
}
