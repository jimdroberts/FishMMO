using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for the shared placement maths.
	/// </summary>
	/// <remarks>
	/// Client and server both reach their verdict through this code. If they disagree, a player is
	/// shown a placement that is then refused, which reads as the game ignoring them — so the
	/// interesting cases here are the boundaries, where "just inside" and "just outside" part ways.
	/// </remarks>
	[TestFixture]
	public class PlotPlacementTests
	{
		private static Bounds Box(Vector3 center, Vector3 size) => new Bounds(center, size);

		/// <summary>
		/// A 20 x 20 x 10 plot resting on the origin, matching how PlotFoundation builds its volume.
		/// </summary>
		private static Bounds Plot() => Box(new Vector3(0f, 5f, 0f), new Vector3(20f, 10f, 20f));

		[Test]
		public void AStructureWellInside_IsAccepted()
		{
			Assert.IsTrue(PlotPlacement.IsFullyInside(Box(new Vector3(0f, 2f, 0f), new Vector3(4f, 4f, 4f)), Plot()));
		}

		/// <summary>
		/// The reason containment is tested as a box rather than as a point. A structure centred
		/// inside the plot can still have most of itself hanging over the street.
		/// </summary>
		[Test]
		public void AStructureWhoseCentreIsInsideButEdgeIsNot_IsRejected()
		{
			Bounds overhanging = Box(new Vector3(9f, 2f, 0f), new Vector3(6f, 4f, 4f));

			Assert.IsTrue(Plot().Contains(overhanging.center), "the centre really is inside");
			Assert.IsFalse(PlotPlacement.IsFullyInside(overhanging, Plot()), "but the structure is not");
		}

		/// <summary>
		/// Flush against the plot line is inside it. Building right up to the boundary is the
		/// intended use of a plot, not an error.
		/// </summary>
		[Test]
		public void AStructureFlushWithTheEdge_IsAccepted()
		{
			Assert.IsTrue(PlotPlacement.IsFullyInside(Box(new Vector3(8f, 2f, 0f), new Vector3(4f, 4f, 4f)), Plot()));
		}

		[Test]
		public void AStructureTallerThanThePlot_IsRejected()
		{
			Assert.IsFalse(PlotPlacement.IsFullyInside(Box(new Vector3(0f, 10f, 0f), new Vector3(4f, 20f, 4f)), Plot()));
		}

		[Test]
		public void AStructureBelowThePlot_IsRejected()
		{
			Assert.IsFalse(PlotPlacement.IsFullyInside(Box(new Vector3(0f, -2f, 0f), new Vector3(4f, 4f, 4f)), Plot()));
		}

		[Test]
		public void OverlappingStructures_Intersect()
		{
			Bounds a = Box(new Vector3(0f, 2f, 0f), new Vector3(4f, 4f, 4f));
			Bounds b = Box(new Vector3(2f, 2f, 0f), new Vector3(4f, 4f, 4f));

			Assert.IsTrue(PlotPlacement.Intersects(a, b));
		}

		/// <summary>
		/// Touching faces are not an intersection — a wall meeting a wall is how anything gets
		/// built, and reporting it as a collision would make the plot unbuildable.
		/// </summary>
		[Test]
		public void StructuresSharingAFace_DoNotIntersect()
		{
			Bounds a = Box(new Vector3(0f, 2f, 0f), new Vector3(4f, 4f, 4f));
			Bounds b = Box(new Vector3(4f, 2f, 0f), new Vector3(4f, 4f, 4f));

			Assert.IsFalse(PlotPlacement.Intersects(a, b));
		}

		/// <summary>
		/// Stacked pieces are a legitimate build — a second storey sits on the first.
		/// </summary>
		[Test]
		public void StructuresStackedVertically_DoNotIntersect()
		{
			Bounds ground = Box(new Vector3(0f, 2f, 0f), new Vector3(4f, 4f, 4f));
			Bounds upper = Box(new Vector3(0f, 6f, 0f), new Vector3(4f, 4f, 4f));

			Assert.IsFalse(PlotPlacement.Intersects(ground, upper));
		}

		[Test]
		public void LocalAndWorldPositions_RoundTrip()
		{
			Vector3 origin = new Vector3(100f, 20f, -50f);
			Vector3 local = new Vector3(3f, 0f, -4f);

			Vector3 world = PlotPlacement.ToWorld(origin, local);

			Assert.AreEqual(new Vector3(103f, 20f, -54f), world);
			Assert.AreEqual(local, PlotPlacement.ToLocal(origin, world));
		}

		/// <summary>
		/// Builds a template with a given footprint.
		/// </summary>
		private static PlotStructureTemplate Template(float width, float depth, float height)
		{
			PlotStructureTemplate template = ScriptableObject.CreateInstance<PlotStructureTemplate>();
			template.Footprint = new Vector2(width, depth);
			template.Height = height;
			return template;
		}

		[Test]
		public void AnUnrotatedStructure_KeepsItsFootprint()
		{
			Bounds bounds = Template(6f, 2f, 3f).GetBounds(Vector3.zero, 0f);

			Assert.AreEqual(new Vector3(6f, 3f, 2f), bounds.size);
		}

		/// <summary>
		/// A quarter turn puts the long side along the other axis. Tested because the axis swap is
		/// what lets a long piece fit across a plot it would not fit along.
		/// </summary>
		[TestCase(90f)]
		[TestCase(270f)]
		[TestCase(-90f)]
		public void AQuarterTurnedStructure_SwapsItsFootprintAxes(float yaw)
		{
			Bounds bounds = Template(6f, 2f, 3f).GetBounds(Vector3.zero, yaw);

			Assert.AreEqual(new Vector3(2f, 3f, 6f), bounds.size);
		}

		/// <summary>
		/// A yaw arrives from a client and may be several turns round; 450 is 90.
		/// </summary>
		[Test]
		public void AYawBeyondAFullTurn_IsNormalised()
		{
			Bounds bounds = Template(6f, 2f, 3f).GetBounds(Vector3.zero, 450f);

			Assert.AreEqual(new Vector3(2f, 3f, 6f), bounds.size);
		}

		[TestCase(0f)]
		[TestCase(180f)]
		[TestCase(360f)]
		public void AHalfTurnedStructure_KeepsItsFootprint(float yaw)
		{
			Bounds bounds = Template(6f, 2f, 3f).GetBounds(Vector3.zero, yaw);

			Assert.AreEqual(new Vector3(6f, 3f, 2f), bounds.size);
		}

		/// <summary>
		/// The structure rests on the point it is placed at rather than straddling it, so a piece
		/// dropped on the ground is not half buried.
		/// </summary>
		[Test]
		public void AStructure_RestsOnItsPlacementPoint()
		{
			Bounds bounds = Template(4f, 4f, 6f).GetBounds(new Vector3(1f, 10f, 2f), 0f);

			Assert.AreEqual(10f, bounds.min.y, 0.0001f);
			Assert.AreEqual(new Vector3(1f, 13f, 2f), bounds.center);
		}

		/// <summary>
		/// An empty box is contained by everything, so a zero-sized template would pass every
		/// bounds test — including from outside the plot.
		/// </summary>
		[Test]
		public void AZeroSizedTemplate_IsFlooredRatherThanTrusted()
		{
			PlotStructureTemplate template = Template(0f, 0f, 0f);

			Assert.AreEqual(PlotFoundation.MinimumExtent, template.SafeFootprint.x);
			Assert.AreEqual(PlotFoundation.MinimumExtent, template.SafeFootprint.y);
			Assert.AreEqual(PlotFoundation.MinimumExtent, template.SafeHeight);
		}

		[Test]
		public void ANegativeFootprint_IsFloored()
		{
			PlotStructureTemplate template = Template(-8f, -8f, -8f);

			Assert.AreEqual(PlotFoundation.MinimumExtent, template.SafeFootprint.x);
			Assert.AreEqual(PlotFoundation.MinimumExtent, template.SafeHeight);
		}

		/// <summary>
		/// The whole point of the plot volume: a structure that fits inside the plot's footprint is
		/// accepted, and the same structure nudged past the line is not.
		/// </summary>
		[Test]
		public void AStructureNudgedPastThePlotLine_StopsFitting()
		{
			PlotStructureTemplate template = Template(4f, 4f, 4f);

			Assert.IsTrue(PlotPlacement.IsFullyInside(template.GetBounds(new Vector3(8f, 0f, 0f), 0f), Plot()));
			Assert.IsFalse(PlotPlacement.IsFullyInside(template.GetBounds(new Vector3(8.5f, 0f, 0f), 0f), Plot()));
		}
	}
}
