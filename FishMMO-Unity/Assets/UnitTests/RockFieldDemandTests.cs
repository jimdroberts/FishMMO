using System.Collections.Generic;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// RockContactField draws only the fields its grid can read (the shapes of rocks whose box meets the demand region),
	/// nearest first. The image is the one every field drawn gives as long as the region holds every grid the gather can
	/// place before the wanted set is measured again, and a rock's box is the gather's own.
	/// </summary>
	public class RockFieldDemandTests
	{
		/// <summary>The gather's grid round a camera at <paramref name="eye"/> (RockContactField.OnBeginContextRendering).</summary>
		private static Bounds Grid(Vector3 eye, Vector3 extent, float cell)
		{
			Vector3 min = eye - extent * 0.5f;
			min = new Vector3(Mathf.Floor(min.x / cell), Mathf.Floor(min.y / cell), Mathf.Floor(min.z / cell)) * cell;
			var grid = new Bounds();
			grid.SetMinMax(min, min + extent);
			return grid;
		}

		private static bool Contains(Bounds outer, Bounds inner) =>
			outer.min.x <= inner.min.x && outer.min.y <= inner.min.y && outer.min.z <= inner.min.z &&
			outer.max.x >= inner.max.x && outer.max.y >= inner.max.y && outer.max.z >= inner.max.z;

		[Test]
		public void TheRegion_HoldsEveryGridTheGatherCanPlace_UntilTheCameraHasMovedHalfTheMargin()
		{
			var random = new System.Random(20261104);
			var margin = new Vector3(RockContactField.DemandMarginXZ, RockContactField.DemandMarginY, RockContactField.DemandMarginXZ);
			Vector3 half = margin * 0.5f;
			for (int trial = 0; trial < 20000; trial++)
			{
				// The field's own grid (64 × 32 × 64 cells of 2 m) first, then any.
				float cell = trial == 0 ? RockContactField.CellSize : 0.5f + (float)random.NextDouble() * 4f;
				Vector3 extent = trial == 0 ? new Vector3(64f, 32f, 64f) * RockContactField.CellSize
					: new Vector3(random.Next(8, 128), random.Next(8, 64), random.Next(8, 128)) * cell;
				var eye = new Vector3((float)random.NextDouble() * 8000f - 4000f, (float)random.NextDouble() * 1200f - 200f, (float)random.NextDouble() * 8000f - 4000f);
				Bounds region = RockFieldDemand.Region(eye, extent, cell, margin);
				// Up to half the margin along every axis (a corner of the cube the measurement is kept for, sometimes).
				var step = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f);
				if (trial % 4 == 0)
				{
					step = new Vector3(Mathf.Sign(step.x), Mathf.Sign(step.y), Mathf.Sign(step.z));
				}
				Vector3 moved = eye + Vector3.Scale(step, half);
				LogAssert.IsTrue(Contains(region, Grid(moved, extent, cell)), $"trial {trial}: the grid at {moved} leaves the region measured at {eye}");
			}
		}

		[Test]
		public void ARocksBox_IsTheGathersBox_AndItsDistanceIsMeasuredFromTheEye()
		{
			var random = new System.Random(1104);
			var region = new Bounds(Vector3.zero, new Vector3(300f, 200f, 300f));
			int inside = 0, outside = 0;
			for (int trial = 0; trial < 5000; trial++)
			{
				var local = new Bounds(new Vector3((float)random.NextDouble() * 4f - 2f, (float)random.NextDouble() * 6f, (float)random.NextDouble() * 4f - 2f),
					new Vector3(1f + (float)random.NextDouble() * 20f, 1f + (float)random.NextDouble() * 15f, 1f + (float)random.NextDouble() * 20f));
				var at = new Vector3((float)random.NextDouble() * 600f - 300f, (float)random.NextDouble() * 400f - 200f, (float)random.NextDouble() * 600f - 300f);
				Quaternion rotation = Quaternion.Euler((float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f);
				var scale = new Vector3(0.5f + (float)random.NextDouble() * 2f, 0.5f + (float)random.NextDouble() * 2f, 0.5f + (float)random.NextDouble() * 2f);
				Matrix4x4 m = Matrix4x4.TRS(at, rotation, scale);
				float reach = (float)random.NextDouble() * 0.5f;
				var eye = new Vector3((float)random.NextDouble() * 200f - 100f, (float)random.NextDouble() * 100f, (float)random.NextDouble() * 200f - 100f);

				// The world box by its eight corners, grown by the reach.
				Bounds world = default;
				for (int c = 0; c < 8; c++)
				{
					Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((c & 1) != 0 ? 1f : -1f, (c & 2) != 0 ? 1f : -1f, (c & 4) != 0 ? 1f : -1f));
					Vector3 p = m.MultiplyPoint3x4(corner);
					if (c == 0) world = new Bounds(p, Vector3.zero); else world.Encapsulate(p);
				}
				world.Expand(2f * reach);

				RockFieldDemand.WorldBox(FishInstance.From(m), local.center, local.extents, out Vector3 boxLo, out Vector3 boxHi);
				float d = RockFieldDemand.BoxDistance(boxLo, boxHi, reach, region, eye);
				Vector3 lo = world.min, hi = world.max;
				bool meets = lo.x <= region.max.x && lo.y <= region.max.y && lo.z <= region.max.z && hi.x >= region.min.x && hi.y >= region.min.y && hi.z >= region.min.z;
				if (meets)
				{
					inside++;
					Vector3 gap = Vector3.Max(Vector3.zero, Vector3.Max(lo - eye, eye - hi));
					float expected = gap.magnitude;
					LogAssert.IsTrue(Mathf.Abs(d - expected) <= 1e-3f * (1f + expected), $"trial {trial}: distance {d}, expected {expected}");
				}
				else
				{
					outside++;
					LogAssert.IsTrue(float.IsPositiveInfinity(d), $"trial {trial}: a box missing the region was wanted ({d})");
				}
			}
			LogAssert.IsTrue(inside > 500 && outside > 500, $"exercised: {inside} inside, {outside} outside");
		}

		[Test]
		public void TheColumns_WantEveryShapeTheRocksWant_NoFartherThanItsNearestRock()
		{
			var random = new System.Random(4111);
			var boxes = new RockBoxes();
			const int shapes = 40;
			for (int trial = 0; trial < 60; trial++)
			{
				boxes.Clear();
				var rocks = new List<(int Shape, Vector3 Lo, Vector3 Hi)>();
				int count = 200 + random.Next(2000);
				for (int k = 0; k < count; k++)
				{
					var centre = new Vector3((float)random.NextDouble() * 3000f - 1500f, (float)random.NextDouble() * 300f, (float)random.NextDouble() * 3000f - 1500f);
					// Mostly rocks of a few metres, some cliffs up to 120 m across (wider than a column).
					float size = random.Next(10) == 0 ? 20f + (float)random.NextDouble() * 100f : 0.5f + (float)random.NextDouble() * 8f;
					var half = new Vector3(size * (0.3f + (float)random.NextDouble()), size * (0.3f + (float)random.NextDouble()), size * (0.3f + (float)random.NextDouble())) * 0.5f;
					int shape = random.Next(shapes);
					rocks.Add((shape, centre - half, centre + half));
					boxes.Add(shape, centre - half, centre + half);
				}
				var eye = new Vector3((float)random.NextDouble() * 3000f - 1500f, (float)random.NextDouble() * 400f, (float)random.NextDouble() * 3000f - 1500f);
				float reach = (float)random.NextDouble() * 0.6f;
				Bounds region = RockFieldDemand.Region(eye, new Vector3(128f, 64f, 128f), 2f, new Vector3(RockContactField.DemandMarginXZ, RockContactField.DemandMarginY, RockContactField.DemandMarginXZ));
				var brute = new float[shapes];
				var folded = new float[shapes];
				for (int i = 0; i < shapes; i++)
				{
					brute[i] = folded[i] = float.PositiveInfinity;
				}
				foreach (var rock in rocks)
				{
					brute[rock.Shape] = Mathf.Min(brute[rock.Shape], RockFieldDemand.BoxDistance(rock.Lo, rock.Hi, reach, region, eye));
				}
				boxes.Fold(region, eye, reach, folded);
				LogAssert.IsTrue(boxes.Rocks == count, $"trial {trial}: {boxes.Rocks} of {count} rocks held");
				for (int i = 0; i < shapes; i++)
				{
					if (!float.IsPositiveInfinity(brute[i]))
					{
						LogAssert.IsTrue(folded[i] <= brute[i] + 1e-3f, $"trial {trial}: shape {i} wanted at {brute[i]} by its rocks, {folded[i]} by the columns");
					}
				}
			}
		}

		[Test]
		public void TheBake_DrawsTheNearestWantedShape_AndMeasuresAgainBeforeTheGridLeavesTheRegion()
		{
			string field = System.IO.File.ReadAllText("Assets/Scripts/Client/World/Terrain/RockContactField.cs");
			LogAssert.IsTrue(field.Contains("if (!shapes[k].Built && demand[k] < nearest)"), "the bake takes the nearest wanted shape");
			LogAssert.IsTrue(field.Contains("Vector3 half = DemandMargin * 0.5f;") && field.Contains("Mathf.Abs(eye.y - demandEye.y) <= half.y"), "the wanted set is measured again at half the margin, per axis");
			// int.MinValue as "never" overflowed frameCount − it to a negative: the boxes were never gathered, nothing wanted.
			LogAssert.IsTrue(field.Contains("boxesFrame = NeverGathered") && !field.Contains("boxesFrame = int.MinValue"), "the first frame gathers the rocks' boxes");
			LogAssert.IsTrue(field.Contains("RefreshDemand(camera.transform.position);\n\t\t\t\tBuildNext();") ||
				field.Contains("RefreshDemand(camera.transform.position);\r\n\t\t\t\tBuildNext();"), "measured before each bake step");
			string compute = System.IO.File.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishRockField.compute");
			LogAssert.IsTrue(compute.Contains("float3 extent = mul(abs(m), bSize.xyz * 0.5) + _RockReach;"), "the gather's rock box (RockBoxDistance mirrors it)");
		}
	}
}
