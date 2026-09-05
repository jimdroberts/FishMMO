using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for the plot lifecycle.
	/// </summary>
	[TestFixture]
	public class PlotStateTests
	{
		[Test]
		public void StoredValues_AreStable()
		{
			/* The numbers are persisted in the plot row and sent to clients. Renumbering them would
			 * silently reinterpret every stored plot and every already-built client, so they are
			 * pinned here rather than left to whatever order the enum happens to be written in. */
			Assert.AreEqual(0, (int)PlotState.Empty);
			Assert.AreEqual(1, (int)PlotState.Building);
			Assert.AreEqual(2, (int)PlotState.Occupied);
			Assert.AreEqual(3, (int)PlotState.Abandoned);
		}

		[Test]
		public void BothUnownedStates_AreClaimable()
		{
			Assert.IsTrue(PlotState.Empty.IsClaimable());
			Assert.IsTrue(PlotState.Abandoned.IsClaimable(),
				"Abandoned land must be claimable, or one player not paying removes a plot from the world forever.");
		}

		[Test]
		public void HeldStates_AreNotClaimable()
		{
			Assert.IsFalse(PlotState.Building.IsClaimable());
			Assert.IsFalse(PlotState.Occupied.IsClaimable());
		}

		[Test]
		public void ClaimingLeadsToBuilding_AndReleasingBackToEmpty()
		{
			Assert.AreEqual(PlotState.Building, PlotStateExtensions.OnClaimed());
			Assert.AreEqual(PlotState.Empty, PlotStateExtensions.OnReleased(),
				"A deliberate release leaves a bare lot, not the abandoned-house visuals of land somebody lost.");
		}

		[Test]
		public void UnrecognisedStoredValues_ReadAsEmpty()
		{
			/* A row written by a newer build can hold a value this one has no name for. Cast
			 * blindly it would produce a PlotState matching none of the branches that decide
			 * access — a plot that answers "no" to everything, including the questions that should
			 * be yes. Empty is the reading that grants the least. */
			Assert.AreEqual(PlotState.Empty, PlotStateExtensions.FromStored(99));
			Assert.AreEqual(PlotState.Empty, PlotStateExtensions.FromStored(-1));
		}

		[Test]
		public void RecognisedStoredValues_RoundTrip()
		{
			Assert.AreEqual(PlotState.Building, PlotStateExtensions.FromStored((int)PlotState.Building));
			Assert.AreEqual(PlotState.Occupied, PlotStateExtensions.FromStored((int)PlotState.Occupied));
			Assert.AreEqual(PlotState.Abandoned, PlotStateExtensions.FromStored((int)PlotState.Abandoned));
		}
	}
}
