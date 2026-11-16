using System;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for <see cref="PlotRentPrepay"/>: what paying rent ahead costs and how far it may reach.
	/// </summary>
	[TestFixture]
	public class PlotRentPrepayTests
	{
		private static readonly DateTime Now = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
		private static readonly TimeSpan Week = TimeSpan.FromDays(7);

		[Test]
		public void PayingAhead_CostsWholePeriods_AndMovesTheDueDateByThem()
		{
			DateTime due = Now.AddDays(3);
			PlotRentPrepay.Quote quote = PlotRentPrepay.Price(due, Now, Week, 50, 3);
			Assert.IsTrue(quote.IsValid);
			Assert.AreEqual(150, quote.Amount);
			Assert.AreEqual(due.AddDays(21), quote.NextDueUtc);
		}

		[Test]
		public void TheDueDateNeverPassesTheCap()
		{
			DateTime due = Now.AddDays(3);
			int allowed = PlotRentPrepay.PeriodsAllowed(due, Now, Week);
			Assert.That(due.AddDays(7 * allowed), Is.LessThanOrEqualTo(Now.AddDays(7 * PlotRentPrepay.MaxPeriodsAhead)));
			Assert.That(due.AddDays(7 * (allowed + 1)), Is.GreaterThan(Now.AddDays(7 * PlotRentPrepay.MaxPeriodsAhead)));
			Assert.IsFalse(PlotRentPrepay.Price(due, Now, Week, 50, allowed + 1).IsValid);
		}

		[Test]
		public void AnOverdueOwner_MayPayTheFullCap()
		{
			DateTime due = Now.AddDays(-2);
			Assert.AreEqual(PlotRentPrepay.MaxPeriodsAhead, PlotRentPrepay.PeriodsAllowed(due, Now, Week));
		}

		[Test]
		public void APlotPaidToTheCap_TakesNoMore()
		{
			DateTime due = Now.AddDays(7 * PlotRentPrepay.MaxPeriodsAhead);
			Assert.AreEqual(0, PlotRentPrepay.PeriodsAllowed(due, Now, Week));
			Assert.IsFalse(PlotRentPrepay.Price(due, Now, Week, 50, 1).IsValid);
		}

		[Test]
		public void NoRent_OrNoPeriods_IsNothingToPay()
		{
			Assert.IsFalse(PlotRentPrepay.Price(Now, Now, Week, 0, 1).IsValid);
			Assert.IsFalse(PlotRentPrepay.Price(Now, Now, Week, 50, 0).IsValid);
			Assert.IsFalse(PlotRentPrepay.Price(Now, Now, TimeSpan.Zero, 50, 1).IsValid);
		}
	}
}
