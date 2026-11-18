using System;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for the tax sweep's rule.
	/// </summary>
	/// <remarks>
	/// This rule had a bug that these tests exist to prevent coming back. The due date advances on
	/// every billing <em>attempt</em>, because that pinned advance is what stops two scene servers
	/// charging the same period — so it moves whether or not any money was collected. An earlier
	/// version measured the grace period against that date, which meant a plot never looked more
	/// than one period overdue: an owner who never paid kept their land forever, and nothing was
	/// ever reclaimed.
	///
	/// <para>Grace is therefore measured from the first missed payment, which is what
	/// <c>TaxDelinquentSinceUtc</c> records.</para>
	/// </remarks>
	[TestFixture]
	public class PlotTaxDecisionTests
	{
		private static readonly DateTime Now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
		private static readonly TimeSpan Grace = TimeSpan.FromDays(14);

		private const long SomeCharacter = 42;
		private const long SomeGuild = 7;

		[Test]
		public void AnOwnerWhoIsUpToDate_IsCharged()
		{
			Assert.AreEqual(
				PlotTaxAction.Charge,
				PlotTaxDecision.Decide(SomeCharacter, 0, null, Now, Grace));
		}

		/// <summary>
		/// Missing a payment does not cost the owner their house — that is what a grace period is.
		/// </summary>
		[Test]
		public void AnOwnerRecentlyDelinquent_IsChargedAgain()
		{
			DateTime missedYesterday = Now - TimeSpan.FromDays(1);

			Assert.AreEqual(
				PlotTaxAction.Charge,
				PlotTaxDecision.Decide(SomeCharacter, 0, missedYesterday, Now, Grace));
		}

		[Test]
		public void AnOwnerDelinquentPastTheGracePeriod_LosesThePlot()
		{
			DateTime missedLongAgo = Now - TimeSpan.FromDays(15);

			Assert.AreEqual(
				PlotTaxAction.Reclaim,
				PlotTaxDecision.Decide(SomeCharacter, 0, missedLongAgo, Now, Grace));
		}

		/// <summary>
		/// Exactly at the grace boundary the plot is taken. The alternative is a plot that is never
		/// quite reclaimable when the sweep interval happens to land on the boundary.
		/// </summary>
		[Test]
		public void AnOwnerDelinquentExactlyTheGracePeriod_LosesThePlot()
		{
			Assert.AreEqual(
				PlotTaxAction.Reclaim,
				PlotTaxDecision.Decide(SomeCharacter, 0, Now - Grace, Now, Grace));
		}

		/// <summary>
		/// The regression this fixture is named for.
		/// </summary>
		/// <remarks>
		/// An owner who has never paid accumulates delinquency from their first miss. If grace were
		/// measured from the constantly-advancing due date instead, this case would read as "only
		/// one period overdue" forever and never reclaim.
		/// </remarks>
		[Test]
		public void AnOwnerWhoNeverPays_EventuallyLosesThePlot()
		{
			DateTime firstMiss = Now - TimeSpan.FromDays(90);

			Assert.AreEqual(
				PlotTaxAction.Reclaim,
				PlotTaxDecision.Decide(SomeCharacter, 0, firstMiss, Now, Grace));
		}

		/// <summary>
		/// Guild land is deferred, never charged and never confiscated. Guilds have no balance, so
		/// charging would bill a member personally for land they do not own, and reclaiming would
		/// take every guild plot on the server.
		/// </summary>
		[Test]
		public void GuildOwnedLand_IsDeferred()
		{
			Assert.AreEqual(
				PlotTaxAction.Defer,
				PlotTaxDecision.Decide(0, SomeGuild, null, Now, Grace));
		}

		/// <summary>
		/// A guild plot that somehow carries a delinquency mark is still reclaimed, because the
		/// grace check runs before the owner is identified. Left charged-but-never-reclaimable it
		/// would be land nothing could ever recover.
		/// </summary>
		[Test]
		public void GuildOwnedLandPastGrace_IsStillReclaimed()
		{
			DateTime missedLongAgo = Now - TimeSpan.FromDays(30);

			Assert.AreEqual(
				PlotTaxAction.Reclaim,
				PlotTaxDecision.Decide(0, SomeGuild, missedLongAgo, Now, Grace));
		}

		[Test]
		public void UnownedLand_IsLeftAlone()
		{
			Assert.AreEqual(
				PlotTaxAction.None,
				PlotTaxDecision.Decide(0, 0, null, Now, Grace));
		}

		/// <summary>
		/// A zero grace period means an unpaid plot is taken on the next sweep. Supported, because a
		/// server may want it, and it must not accidentally mean "never".
		/// </summary>
		[Test]
		public void AZeroGracePeriod_ReclaimsOnTheFirstMiss()
		{
			Assert.AreEqual(
				PlotTaxAction.Reclaim,
				PlotTaxDecision.Decide(SomeCharacter, 0, Now, Now, TimeSpan.Zero));
		}

		/// <summary>
		/// Clearing the mark on payment has to actually restore the owner to good standing, or a
		/// single missed payment would eventually cost them the plot however much they paid after.
		/// </summary>
		[Test]
		public void ClearingDelinquency_RestoresGoodStanding()
		{
			DateTime longOverdue = Now - TimeSpan.FromDays(90);

			Assert.AreEqual(
				PlotTaxAction.Reclaim,
				PlotTaxDecision.Decide(SomeCharacter, 0, longOverdue, Now, Grace),
				"still marked");

			Assert.AreEqual(
				PlotTaxAction.Charge,
				PlotTaxDecision.Decide(SomeCharacter, 0, null, Now, Grace),
				"mark cleared by a successful payment");
		}
	}
}
