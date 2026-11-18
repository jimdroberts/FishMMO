using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for where somebody is put when they may no longer be where they are.
	/// </summary>
	/// <remarks>
	/// Access rules enforced only at the doorway are rules a player defeats by standing still, so
	/// eviction is what makes "may I be here" the same question as "may I come in". The arithmetic
	/// is where the mistakes are: get it wrong and a player ends up inside a wall, under the terrain,
	/// or pinned to a boundary being evicted over and over.
	/// </remarks>
	[TestFixture]
	public class PlotEvictionTests
	{
		/// <summary>A 16x16 plot, 12 tall, centred on the origin and resting on y = 0.</summary>
		private static Bounds Plot => new Bounds(new Vector3(0f, 6f, 0f), new Vector3(16f, 12f, 16f));

		[Test]
		public void SomebodyOutside_IsLeftAlone()
		{
			Vector3 outside = new Vector3(50f, 0f, 50f);

			Assert.AreEqual(outside, PlotEviction.NearestExit(Plot, outside),
				"Moving somebody who is already outside would be the surprise, not leaving them.");
		}

		[Test]
		public void SomebodyNearTheEastEdge_LeavesEastwards()
		{
			Vector3 exit = PlotEviction.NearestExit(Plot, new Vector3(7f, 0f, 0f));

			Assert.Greater(exit.x, 8f, "They should be put out through the nearest side.");
			Assert.AreEqual(0f, exit.z, 0.001f, "The other axis should not move.");
		}

		[Test]
		public void SomebodyNearTheWestEdge_LeavesWestwards()
		{
			Vector3 exit = PlotEviction.NearestExit(Plot, new Vector3(-7f, 0f, 0f));

			Assert.Less(exit.x, -8f);
		}

		[Test]
		public void SomebodyNearTheNorthEdge_LeavesNorthwards()
		{
			Vector3 exit = PlotEviction.NearestExit(Plot, new Vector3(0f, 0f, 7f));

			Assert.Greater(exit.z, 8f);
			Assert.AreEqual(0f, exit.x, 0.001f);
		}

		[Test]
		public void HeightIsNeverChanged()
		{
			/* The nearest face to somebody standing on the ground floor is very often the floor, so
			 * a nearest-face eviction would drop players through the terrain. Height is left exactly
			 * as it was, which keeps them on whatever they were standing on. */
			Vector3 exit = PlotEviction.NearestExit(Plot, new Vector3(7f, 3.25f, 0f));

			Assert.AreEqual(3.25f, exit.y, 0.0001f);
		}

		[Test]
		public void TheExitIsOutsideTheFootprint()
		{
			// The property that matters: whatever the geometry, one eviction must be enough.
			Vector3 exit = PlotEviction.NearestExit(Plot, new Vector3(7f, 0f, 7f));

			Assert.IsFalse(PlotEviction.IsInsideFootprint(Plot, exit),
				"Landing on the edge would read as still inside, and the player would be evicted again next sweep.");
		}

		[Test]
		public void DeadCentre_ResolvesDeterministically()
		{
			/* Somebody in the middle of a square plot is equidistant from all four sides. Two callers
			 * reaching different answers for the same player is how one system evicts them east
			 * while another decides they are still inside. */
			Vector3 first = PlotEviction.NearestExit(Plot, Vector3.zero);
			Vector3 second = PlotEviction.NearestExit(Plot, Vector3.zero);

			Assert.AreEqual(first, second);
			Assert.IsFalse(PlotEviction.IsInsideFootprint(Plot, first));
		}

		[Test]
		public void TheFootprintIgnoresHeight()
		{
			/* A plot's volume stops a dozen metres up. Somebody on the roof of their own house, or
			 * falling past it, is over the plot and outside the box at the same time — and treating
			 * them as outside would leave the one position from which a barred player can sit and
			 * watch. */
			Assert.IsTrue(PlotEviction.IsInsideFootprint(Plot, new Vector3(0f, 500f, 0f)));
			Assert.IsTrue(PlotEviction.IsInsideFootprint(Plot, new Vector3(0f, -500f, 0f)));
		}

		[Test]
		public void APointOnTheBoundaryIsNotInside()
		{
			Assert.IsFalse(PlotEviction.IsInsideFootprint(Plot, new Vector3(8f, 0f, 0f)),
				"Exactly on the line is out, so an eviction that lands somebody there is not undone by the next sweep.");
		}

		[Test]
		public void AnOffCentrePlot_IsHandledInWorldSpace()
		{
			// Plots are authored wherever the designer drops them; nothing may assume the origin.
			Bounds offCentre = new Bounds(new Vector3(100f, 6f, -40f), new Vector3(16f, 12f, 16f));
			Vector3 inside = new Vector3(106f, 0f, -40f);

			Vector3 exit = PlotEviction.NearestExit(offCentre, inside);

			Assert.IsFalse(PlotEviction.IsInsideFootprint(offCentre, exit));
			Assert.Greater(exit.x, 108f);
		}
	}
}
