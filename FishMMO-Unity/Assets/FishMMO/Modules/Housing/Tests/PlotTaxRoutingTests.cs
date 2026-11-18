using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for which path a due plot takes through the tax sweep.
	/// </summary>
	/// <remarks>
	/// The rule these pin is the one the housing README calls correctness rather than optimisation:
	/// an owner this server holds is charged through their in-memory balance, never through their
	/// stored row, because their next save writes the row back over the debit and the money quietly
	/// returns. The batched offline charge must therefore never be handed a held owner — and every
	/// owner NOT held here must go to it, because its assertion under the row lock is the only thing
	/// that may decide between charging the row and leaving the plot to whoever holds them. The
	/// sweep this replaced had no such split: it left every owner held elsewhere for a holder that,
	/// for an owner in a dungeon, never swept that world at all.
	/// </remarks>
	[TestFixture]
	public class PlotTaxRoutingTests
	{
		[Test]
		public void AnOwnerHeldHere_IsChargedInMemory()
		{
			Assert.AreEqual(PlotTaxRoute.ChargeHere, PlotTaxRouting.Route(PlotTaxAction.Charge, ownerHeldHere: true),
				"Charging a held owner's stored row would be overwritten by their next save.");
		}

		[Test]
		public void AnOwnerNotHeldHere_GoesToTheOfflineBatch()
		{
			Assert.AreEqual(PlotTaxRoute.ChargeOffline, PlotTaxRouting.Route(PlotTaxAction.Charge, ownerHeldHere: false),
				"Only the offline charge's locked assertion may decide whether nobody, or some other server, holds them.");
		}

		/// <summary>
		/// Reclamation is decided by the grace clock alone and takes the land, not the money, so
		/// who holds the owner has no say in it.
		/// </summary>
		[TestCase(true)]
		[TestCase(false)]
		public void APlotPastItsGrace_IsReclaimedWhoeverHoldsTheOwner(bool ownerHeldHere)
		{
			Assert.AreEqual(PlotTaxRoute.Reclaim, PlotTaxRouting.Route(PlotTaxAction.Reclaim, ownerHeldHere));
		}

		[TestCase(true)]
		[TestCase(false)]
		public void GuildLand_IsDeferredNotCharged(bool ownerHeldHere)
		{
			Assert.AreEqual(PlotTaxRoute.Defer, PlotTaxRouting.Route(PlotTaxAction.Defer, ownerHeldHere),
				"Guilds have no treasury; charging would bill some member for land they do not own.");
		}

		[TestCase(true)]
		[TestCase(false)]
		public void APlotTheRuleLeavesAlone_IsLeftAlone(bool ownerHeldHere)
		{
			Assert.AreEqual(PlotTaxRoute.None, PlotTaxRouting.Route(PlotTaxAction.None, ownerHeldHere));
		}

		/// <summary>
		/// Pins the whole table end to end: every decision, both ways, lands where the sweep
		/// expects it, and a held owner never reaches the offline charge.
		/// </summary>
		[Test]
		public void AHeldOwner_NeverReachesTheOfflineCharge()
		{
			foreach (PlotTaxAction action in System.Enum.GetValues(typeof(PlotTaxAction)))
			{
				Assert.AreNotEqual(PlotTaxRoute.ChargeOffline, PlotTaxRouting.Route(action, ownerHeldHere: true),
					$"{action} for a held owner must not be charged from the stored row.");
			}
		}
	}
}
