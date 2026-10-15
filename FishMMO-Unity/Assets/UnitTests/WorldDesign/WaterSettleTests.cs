using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>The water built from the finished ground: rivers no higher than their banks, lakes and moats than their rims.</summary>
	[TestFixture]
	public class WaterSettleTests
	{
		private static float[] Fill(int n, System.Func<int, float> at)
		{
			var values = new float[n];
			for (int i = 0; i < n; i++)
			{
				values[i] = at(i);
			}
			return values;
		}

		[Test]
		public void ARiverHeldByItsBanksKeepsTheLevelItWasCutFor()
		{
			float[] planned = Fill(20, i => 10f - 0.2f * i);
			float[] settled = WaterSettle.Levels(planned, Fill(20, _ => 100f), null, float.PositiveInfinity, float.PositiveInfinity);
			Assert.That(settled, Is.EqualTo(planned));
		}

		/// <summary>A bank cut under a rapid lowers its water there and on down as far as the water after it stood higher, never above it.</summary>
		[Test]
		public void ABankCutUnderARapidLowersItsWaterThereAndDownstreamOnly()
		{
			float[] planned = Fill(20, i => 10f - 0.2f * i);
			float[] hold = Fill(20, _ => 100f);
			hold[8] = 7.5f;
			float[] settled = WaterSettle.Levels(planned, hold, null, float.PositiveInfinity, float.PositiveInfinity);
			for (int i = 0; i < 8; i++)
			{
				Assert.That(settled[i], Is.EqualTo(planned[i]), $"point {i} upstream keeps its level");
			}
			Assert.That(settled[8], Is.EqualTo(7.5f), "the water stands at the cut bank");
			for (int i = 9; i < 20; i++)
			{
				Assert.That(settled[i], Is.EqualTo(Mathf.Min(planned[i], 7.5f)).Within(1e-5f), $"point {i}: no higher than the water before it");
				Assert.That(settled[i], Is.LessThanOrEqualTo(settled[i - 1]), "it runs downhill");
			}
		}

		/// <summary>A pool's water is level: one low point of its rim lowers the whole pool to it, and the rapid above it is left.</summary>
		[Test]
		public void APoolStandsAtTheLowestPointOfItsRim()
		{
			// A rapid down to a pool at 8 over points 6..14, then a rapid on down.
			float[] planned = Fill(20, i => i < 6 ? 10f - 0.3f * i : i <= 14 ? 8f : 8f - 0.3f * (i - 14));
			float[] hold = Fill(20, _ => 100f);
			hold[12] = 7.4f;
			float[] settled = WaterSettle.Levels(planned, hold, null, float.PositiveInfinity, float.PositiveInfinity);
			for (int i = 6; i <= 14; i++)
			{
				Assert.That(settled[i], Is.EqualTo(7.4f).Within(1e-5f), $"pool point {i} stands at the rim's low point");
			}
			for (int i = 0; i < 6; i++)
			{
				Assert.That(settled[i], Is.EqualTo(planned[i]), $"rapid point {i} above the pool keeps its level");
			}
		}

		/// <summary>A fall's water and its lip keep their level (the ground beside a brink drops with the water by design); the pool below still settles.</summary>
		[Test]
		public void AFallsLipKeepsItsLevelAndThePoolBelowSettles()
		{
			float[] planned = Fill(12, i => i <= 4 ? 20f : 10f);
			var fixedAt = new bool[12];
			fixedAt[4] = fixedAt[5] = true;
			float[] hold = Fill(12, _ => 100f);
			hold[4] = 18f;
			hold[5] = 0f;
			hold[9] = 9.5f;
			float[] settled = WaterSettle.Levels(planned, hold, fixedAt, float.PositiveInfinity, float.PositiveInfinity);
			Assert.That(settled[4], Is.EqualTo(20f), "the lip");
			Assert.That(settled[5], Is.EqualTo(10f), "the water landing");
			for (int i = 6; i < 12; i++)
			{
				Assert.That(settled[i], Is.EqualTo(9.5f).Within(1e-5f), $"pool point {i}");
			}
			for (int i = 0; i < 4; i++)
			{
				Assert.That(settled[i], Is.EqualTo(20f), $"above the fall, point {i}");
			}
		}

		/// <summary>A tributary meets the river it joins at that river's level: its level end comes down to it, never up.</summary>
		[Test]
		public void ATributaryMeetsTheRiverItJoinsAtItsLevel()
		{
			float[] planned = Fill(10, i => i < 5 ? 12f - 0.4f * i : 10f);
			float[] settled = WaterSettle.Levels(planned, Fill(10, _ => 100f), null, float.PositiveInfinity, 9.6f);
			for (int i = 5; i < 10; i++)
			{
				Assert.That(settled[i], Is.EqualTo(9.6f).Within(1e-5f), $"the level reach at its mouth, point {i}");
			}
			Assert.That(settled[4], Is.EqualTo(planned[4]), "the rapid above it keeps its level");
			float[] higher = WaterSettle.Levels(planned, Fill(10, _ => 100f), null, float.PositiveInfinity, 11f);
			Assert.That(higher, Is.EqualTo(planned), "a river joined higher up raises nothing");
		}

		/// <summary>A basin's water runs out at the lowest point of its rim: the spill is the notch, not the rim's average.</summary>
		[Test]
		public void ABasinSpillsAtTheLowestNotchOfItsRim()
		{
			int w = 30, d = 30;
			var grid = new SceneWaterGrid(w, d, 1f, 0f, 0f);
			var height = new float[w * d];
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					float r = Mathf.Sqrt((x - 15f) * (x - 15f) + (z - 15f) * (z - 15f));
					// A bowl floored at 2, its rim at 6 from 8 out, a notch at 4.5 on its east.
					float h = r < 6f ? 2f : r < 8f ? 2f + 2f * (r - 6f) : 6f;
					if (x > 15 && Mathf.Abs(z - 15) <= 1 && r >= 6f)
					{
						h = Mathf.Min(h, 4.5f);
					}
					height[z * w + x] = h;
				}
			}
			var seeds = new List<int> { 15 * w + 15 };
			// It may stand within 11 m of the middle: past the rim, off it is a way out.
			bool Within(int k) => Mathf.Sqrt((k % w - 15f) * (k % w - 15f) + (k / w - 15f) * (k / w - 15f)) < 11f;
			float spill = WaterSettle.Spill(height, grid, seeds, Within, k => false, 10f, 0, w - 1, 0, d - 1);
			Assert.That(spill, Is.EqualTo(4.5f).Within(1e-4f));
			Assert.That(float.IsPositiveInfinity(WaterSettle.Spill(height, grid, seeds, k => true, k => false, 10f, 0, w - 1, 0, d - 1)), Is.True,
				"the grid's own edge (the scene's) is no way out");
			float sealed_ = WaterSettle.Spill(height, grid, seeds, Within, k => false, 3f, 0, w - 1, 0, d - 1);
			Assert.That(float.IsPositiveInfinity(sealed_), Is.True, "no spill is looked for far over the level asked about");
		}

		/// <summary>
		/// A moat's ditch has one level floor under its lowest bank however the ground slopes, so a single level of water fills
		/// the whole ring: the uphill side is dug deeper, its banks at 45°.
		/// </summary>
		[Test]
		public void AMoatOnASlopeHasOneLevelFloorAllRound()
		{
			const int Spokes = 180;
			var rimIn = new float[Spokes];
			var rimOut = new float[Spokes];
			for (int i = 0; i < Spokes; i++)
			{
				float a = i * Mathf.PI * 2f / Spokes;
				// 3 m higher on the north side than the south.
				rimIn[i] = 20f + 1.5f * Mathf.Cos(a);
				rimOut[i] = 20.2f + 1.5f * Mathf.Cos(a);
			}
			float lowest = float.MaxValue;
			for (int i = 0; i < Spokes; i++)
			{
				lowest = Mathf.Min(lowest, Mathf.Min(rimIn[i], rimOut[i]));
			}
			const float Inner = 30f, Width = 9.5f, Depth = 3.5f;
			float floor = lowest - Depth;
			float level = lowest - MoatShaper.Freeboard;
			for (int k = 0; k < 36; k++)
			{
				float a = k * Mathf.PI * 2f / 36;
				float middle = MoatShaper.Ditch(Mathf.Sin(a) * (Inner + 0.5f * Width), Mathf.Cos(a) * (Inner + 0.5f * Width), Inner, Width, floor, rimIn, rimOut);
				Assert.That(middle, Is.EqualTo(floor).Within(1e-4f), $"the floor's middle at {k * 10}° is the one level");
				Assert.That(middle, Is.LessThan(level - 2f), "under the water by a good depth all round");
				// The banks: down from each lip, at 45° or steeper where the uphill side is deeper than the width allows at 45°.
				float nearInner = MoatShaper.Ditch(Mathf.Sin(a) * (Inner + 0.5f), Mathf.Cos(a) * (Inner + 0.5f), Inner, Width, floor, rimIn, rimOut);
				float lipIn = rimIn[Mathf.RoundToInt(k * 5f) % Spokes];
				Assert.That(nearInner, Is.LessThanOrEqualTo(lipIn - 0.5f + 0.05f), $"the inner bank at {k * 10}° falls at least a metre a metre");
				Assert.That(nearInner, Is.GreaterThan(floor), $"and stands over the floor half a metre in, at {k * 10}°");
				float edgeOfFloor = MoatShaper.Ditch(Mathf.Sin(a) * (Inner + 0.4f * Width), Mathf.Cos(a) * (Inner + 0.4f * Width), Inner, Width, floor, rimIn, rimOut);
				Assert.That(edgeOfFloor, Is.EqualTo(floor).Within(1e-3f), $"the floor reaches out to its share of the width at {k * 10}°");
			}
			Assert.That(float.IsNaN(MoatShaper.Ditch(Inner - 0.1f, 0f, Inner, Width, floor, rimIn, rimOut)), Is.True, "nothing dug inside the ring");
			Assert.That(float.IsNaN(MoatShaper.Ditch(Inner + Width + 0.1f, 0f, Inner, Width, floor, rimIn, rimOut)), Is.True, "nor outside it");
		}

		private static RiverPath Straight(float z, float from, float to, float spacing, float width, float surface, float depth)
		{
			int n = Mathf.RoundToInt((to - from) / spacing) + 1;
			var path = new RiverPath
			{
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = new float[n], Bed = new float[n], Curvature = new float[n], Grain = new float[n],
			};
			for (int i = 0; i < n; i++)
			{
				path.X[i] = from + i * spacing;
				path.Z[i] = z;
				path.S[i] = i * spacing;
				path.Discharge[i] = 10f;
				path.Width[i] = width;
				path.Depth[i] = depth;
				path.Surface[i] = surface;
				path.Bed[i] = surface - depth;
			}
			return path;
		}

		/// <summary>
		/// Ground beside two rivers is held for whichever needs it higher. A wide river's levee falling away 9 m out asks less of
		/// a sample than a narrow river 3 m from it whose water stands lower: kept as the highest water and its distance, the
		/// carve took the wide river's and left the narrow one's water a metre over the hollow beside it (Flo Monolith,
		/// 2026-10-15, a rapid over a scour hole below a fall's pool).
		/// </summary>
		[Test]
		public void GroundBetweenTwoRiversIsHeldForWhicheverNeedsItHigher()
		{
			var settings = new RiverSettings();
			int w = 120, d = 70;
			var grid = new SceneWaterGrid(w, d, 1f, -60f, -20f);
			var height = new float[w * d];
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					float north = grid.NorthOf(z);
					// A hollow between them, under the narrow river's water.
					height[z * w + x] = north >= 15f && north <= 21f ? 5f : 12f;
				}
			}
			RiverPath wide = Straight(0f, -60f, 59f, 1f, 20f, 10f, 2f);
			RiverPath narrow = Straight(24f, -60f, 59f, 1f, 4f, 7.5f, 1f);
			narrow.Id = 1;
			WaterCarver.CarveRivers(height, grid, new List<RiverPath> { wide, narrow }, settings, true);
			for (int x = 20; x < 100; x++)
			{
				int z = Mathf.RoundToInt(19f - grid.OriginZ);
				Assert.That(height[z * w + x], Is.GreaterThanOrEqualTo(7.5f), $"the ground 3 m from the narrow river at x {grid.EastOf(x)} holds its water");
			}
		}
	}
}
