using System;
using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The ground a fall is carved into (<see cref="FallBasin"/>, <see cref="WaterCarver.CarveRivers"/>): the brink is a
	/// notch in a headwall that curves round the pool, not one straight wall ruled across the whole cut; nothing stands at
	/// the lip's level out over the drop beside the curtain; the basin's walls wander rather than run as ruled planes; and the
	/// water still runs over its lip, into its pool, held by its banks, the same on every cut.
	/// </summary>
	/// <remarks>Pure arithmetic on arrays: no Unity object is made.</remarks>
	[TestFixture]
	public class FallBasinTests
	{
		private const float Spacing = 2f;
		private const int GridWidth = 160, GridDepth = 120;
		private const float OriginX = -160f, OriginZ = -120f;
		private const float PlateauMetres = 100f;
		private const float DropMetres = 30f;
		private const float ScarpMetres = 40f;

		/// <summary>
		/// A level plateau at 100 m breaking off at x = 0 down a 40 m scarp to a plain 30 m lower, level across (z), so a
		/// carve that rules its walls leaves them perfectly straight.
		/// </summary>
		private static float Ground(float x, float z)
		{
			if (x < 0f)
			{
				return PlateauMetres;
			}
			if (x < ScarpMetres)
			{
				return PlateauMetres - DropMetres * (x / ScarpMetres);
			}
			return PlateauMetres - DropMetres - 0.01f * (x - ScarpMetres);
		}

		/// <summary>
		/// A river along x every 2.5 m, its surface a little under the ground and never rising, shaped by
		/// <see cref="RiverShaping.Reaches"/>: its drop down the scarp gathered into a fall at the plateau's edge, a pool
		/// scoured under it.
		/// </summary>
		private static RiverPath River(RiverSettings settings, float width = 6f)
		{
			const float step = 2.5f;
			var xs = new List<float>();
			for (float p = -150f; p <= 150f; p += step)
			{
				xs.Add(p);
			}
			int n = xs.Count;
			var river = new RiverPath
			{
				Id = 7,
				X = xs.ToArray(), Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = new float[n], Bed = new float[n], Curvature = new float[n],
			};
			float previous = float.MaxValue;
			for (int i = 0; i < n; i++)
			{
				river.S[i] = i * step;
				river.Discharge[i] = width * width / 9f;
				river.Width[i] = width;
				river.Depth[i] = 0.8f;
				float surface = Math.Min(Ground(xs[i], 0f) - 0.6f, previous - 0.0005f);
				previous = surface;
				river.Surface[i] = surface;
				river.Bed[i] = surface - 0.8f;
			}
			RiverShaping.Reaches(river, settings, 1234u);
			return river;
		}

		private static float[] Plain()
		{
			var height = new float[GridWidth * GridDepth];
			for (int z = 0; z < GridDepth; z++)
			{
				for (int x = 0; x < GridWidth; x++)
				{
					height[z * GridWidth + x] = Ground(OriginX + x * Spacing, OriginZ + z * Spacing);
				}
			}
			return height;
		}

		private static float[] Carve(RiverSettings settings, RiverPath river, out SceneWaterGrid grid)
		{
			grid = new SceneWaterGrid(GridWidth, GridDepth, Spacing, OriginX, OriginZ);
			float[] height = Plain();
			WaterCarver.CarveRivers(height, grid, new[] { river }, settings, true);
			return height;
		}

		private static FallBasin BasinOf(RiverPath river, RiverSettings settings)
		{
			FallBasin[] basins = FallBasin.Along(river, settings, Spacing);
			Assert.That(basins, Is.Not.Null, "the scarp makes a fall");
			foreach (FallBasin basin in basins)
			{
				if (basin != null)
				{
					return basin;
				}
			}
			Assert.Fail("no basin");
			return null;
		}

		private static int Column(float x) => (int)Math.Round((x - OriginX) / Spacing);
		private static int Row(float z) => (int)Math.Round((z - OriginZ) / Spacing);

		/// <summary>How far out from the river's middle the ground at <paramref name="x"/> is cut deeper than <paramref name="depth"/>, metres (the further side).</summary>
		private static float CutReach(float[] height, float[] plain, float x, float depth)
		{
			int column = Column(x);
			float reach = 0f;
			for (int z = 0; z < GridDepth; z++)
			{
				int i = z * GridWidth + column;
				if (plain[i] - height[i] > depth)
				{
					reach = Math.Max(reach, Math.Abs(OriginZ + z * Spacing));
				}
			}
			return reach;
		}

		/// <summary>The first x downstream of the lip at which the ground at <paramref name="z"/> is cut deeper than <paramref name="depth"/>: where the headwall's brink crosses that line.</summary>
		private static float BrinkAt(float[] height, float[] plain, float lipX, float z, float depth)
		{
			int row = Row(z);
			for (int column = Column(lipX - 2f); column < GridWidth; column++)
			{
				int i = row * GridWidth + column;
				if (plain[i] - height[i] > depth)
				{
					return OriginX + column * Spacing;
				}
			}
			return float.PositiveInfinity;
		}

		/// <summary>
		/// Past the step the deep cut is the notch and the walls beside it, not the drop ruled across the whole cut; and the
		/// headwall's brink stands further downstream the further out across it is: it curves round the pool.
		/// </summary>
		private static void AssertNotchInCurvedHeadwall(float width)
		{
			var settings = new RiverSettings();
			RiverPath river = River(settings, width);
			FallBasin basin = BasinOf(river, settings);
			float[] height = Carve(settings, river, out _);
			float[] plain = Plain();
			float drop = basin.LipLevel - river.Surface[basin.Foot];
			Assert.That(drop, Is.GreaterThan(20f), "a tall fall at the plateau's edge");
			float lipX = river.X[basin.Lip];
			float atStep = CutReach(height, plain, river.X[basin.Lip + 1] + Spacing, 0.4f * drop);
			Assert.That(atStep, Is.LessThanOrEqualTo(basin.HalfLip + 2f * settings.FallNotchBlendMetres + Spacing),
				"past the brink the deep cut is the notch and the walls beside it, not the drop ruled tens of metres across the cut");
			float middle = BrinkAt(height, plain, lipX, 0f, 0.4f * drop);
			float out1 = BrinkAt(height, plain, lipX, basin.HalfLip + 0.7f * basin.Radius, 0.4f * drop);
			float out2 = BrinkAt(height, plain, lipX, -(basin.HalfLip + 0.7f * basin.Radius), 0.4f * drop);
			Assert.That(middle, Is.LessThan(lipX + 0.5f * basin.Radius), "the water drops at its lip");
			Assert.That(Math.Min(out1, out2), Is.GreaterThanOrEqualTo(middle + 2f), "the headwall's brink curves downstream round the pool");
		}

		[Test]
		public void TheBrinkIsANotchInACurvedHeadwallNotAWallAcrossTheCut()
		{
			AssertNotchInCurvedHeadwall(6f);
		}

		[Test]
		public void AWideFallsHeadwallCurvesToo()
		{
			AssertNotchInCurvedHeadwall(20f);
		}

		[Test]
		public void NothingStandsAtTheLipsLevelOutOverTheDropBesideTheCurtain()
		{
			var settings = new RiverSettings();
			RiverPath river = River(settings);
			FallBasin basin = BasinOf(river, settings);
			float[] height = Carve(settings, river, out SceneWaterGrid grid);
			float drop = basin.LipLevel - river.Surface[basin.Foot];
			// The first column of ground past the step, beside the curtain within a few metres of the water's edge.
			float x = river.X[basin.Lip + 1] + 0.5f * Spacing;
			int column = Column(x);
			/* Within two samples of the lip the brink rounds over (FallBasin.UnderBrink) and the notch's sides rise from it as
			 * walls: there well under the lip's level, at least half as far as the water beside it has fallen at its full brink
			 * speed; past that, well down the drop. */
			float past = OriginX + column * Spacing - river.X[basin.Lip];
			float q = river.Discharge[basin.Lip] / river.Width[basin.Lip];
			float brinkSpeed = (float)Math.Pow(9.81 * q, 1.0 / 3.0) / 0.715f;
			float under = past < basin.BrinkReach ? 0.5f * 9.81f * past * past / (2f * brinkSpeed * brinkSpeed) : 0.5f * drop;
			int checkedCells = 0;
			for (float z = basin.HalfLip + 0.5f; z <= basin.HalfLip + 3f; z += Spacing)
			{
				foreach (float side in new[] { -1f, 1f })
				{
					int i = Row(side * z) * GridWidth + column;
					if (grid.Kind[i] == WaterKind.River)
					{
						continue;
					}
					checkedCells++;
					Assert.That(height[i], Is.LessThan(basin.LipLevel - under),
						$"at ({grid.EastOf(column)}, {grid.NorthOf(i / GridWidth)}) the ground stands {height[i] - (basin.LipLevel - drop):F1} m over the pool: a shelf or levee at the lip, a slot for the curtain");
				}
			}
			Assert.That(checkedCells, Is.GreaterThan(0));
		}

		[Test]
		public void TheGorgesWallsStandAsRockAndWanderRatherThanRunAsRuledBanks()
		{
			var settings = new RiverSettings();
			RiverPath river = River(settings);
			FallBasin basin = BasinOf(river, settings);
			float[] height = Carve(settings, river, out SceneWaterGrid grid);
			float[] plain = Plain();
			// The gorge below the headwall's circle, before the basin fades: each row's steepest cut wall either side.
			float from = river.X[basin.Lip] + basin.Radius + Spacing, to = river.X[basin.Lip] + basin.FadeStart;
			var steepest = new List<double>();
			for (int column = Column(from); column <= Column(to); column++)
			{
				foreach (int side in new[] { -1, 1 })
				{
					double most = 0;
					for (int z = Row(0f); z + side >= 0 && z + side < GridDepth; z += side)
					{
						int i = z * GridWidth + column, j = (z + side) * GridWidth + column;
						if (grid.Kind[i] == WaterKind.River || grid.Kind[j] == WaterKind.River || plain[j] - height[j] < 1f)
						{
							continue;
						}
						most = Math.Max(most, Math.Atan(Math.Abs(height[j] - height[i]) / Spacing) * 180.0 / Math.PI);
					}
					if (most > 0)
					{
						steepest.Add(most);
					}
				}
			}
			Assert.That(steepest.Count, Is.GreaterThan(6), "a gorge long enough to measure");
			double mean = 0, max = 0;
			foreach (double d in steepest)
			{
				mean += d;
				max = Math.Max(max, d);
			}
			mean /= steepest.Count;
			double spread = 0;
			foreach (double d in steepest)
			{
				spread += (d - mean) * (d - mean);
			}
			spread = Math.Sqrt(spread / steepest.Count);
			Assert.That(mean, Is.GreaterThan(settings.BankDegrees + 6f), $"the walls stand at {mean:F0}°: a cut bank's slope, not rock");
			Assert.That(spread, Is.GreaterThan(3.0), $"the walls' steepness wanders by {spread:F1}° about {mean:F0}°: ruled planes");
			Assert.That(max, Is.LessThanOrEqualTo(settings.FallWallDegrees + settings.FallWallJitterDegrees + 8f), $"a gorge wall at {max:F0}°: sheer terrain past the walls' own steepness");
		}

		[Test]
		public void TheBasinsBrinkAndWallsWanderTheSameEveryTime()
		{
			var settings = new RiverSettings();
			FallBasin basin = BasinOf(River(settings), settings);
			double least = double.MaxValue, most = double.MinValue, leastSetback = double.MaxValue, mostSetback = double.MinValue;
			float across = basin.HalfLip + 2f * settings.FallNotchBlendMetres + 1f;
			for (float x = -60f; x <= 60f; x += 1.5f)
			{
				double degrees = Math.Atan(basin.WallSlope(across, x, 3f, 0.78f)) * 180.0 / Math.PI;
				least = Math.Min(least, degrees);
				most = Math.Max(most, degrees);
				float setback = basin.Setback(across, x, 3f);
				leastSetback = Math.Min(leastSetback, setback);
				mostSetback = Math.Max(mostSetback, setback);
				Assert.That(basin.WallSlope(across, x, 3f, 0.78f), Is.EqualTo(basin.WallSlope(across, x, 3f, 0.78f)), "the same place, the same wall");
			}
			Assert.That(most - least, Is.GreaterThan(6.0), "the walls' steepness wanders round the basin");
			Assert.That(least, Is.GreaterThanOrEqualTo(settings.FallWallDegrees - settings.FallWallJitterDegrees - 1e-3));
			Assert.That(most, Is.LessThanOrEqualTo(settings.FallWallDegrees + settings.FallWallJitterDegrees + 1e-3));
			Assert.That(mostSetback - leastSetback, Is.GreaterThan(0.5), "the brink wanders in and out");
			// Not at the notch: the lip is where the water leaves, exactly.
			Assert.That(basin.Setback(basin.HalfLip, 11f, 5f), Is.EqualTo(0f).Within(1e-4f));
			Assert.That(basin.Setback(basin.HalfLip, -23f, 5f), Is.EqualTo(0f).Within(1e-4f));
		}

		[Test]
		public void TheWaterStillRunsOverItsLipIntoItsPoolHeldByItsBanks()
		{
			var settings = new RiverSettings();
			RiverPath river = River(settings);
			FallBasin basin = BasinOf(river, settings);
			float[] height = Carve(settings, river, out SceneWaterGrid grid);
			// The lip stands on its own bed: no hole under the water going over.
			int lip = grid.SampleAt(river.X[basin.Lip], 0f);
			float lipPast = Math.Max(0f, grid.EastOf(lip % GridWidth) - river.X[basin.Lip]);
			Assert.That(height[lip], Is.GreaterThanOrEqualTo(river.Bed[basin.Lip] - FallBasin.UnderBrink(basin.Jet, basin.BrinkClearance, Spacing, lipPast) - 1e-3f));
			Assert.That(height[lip], Is.LessThanOrEqualTo(river.Surface[basin.Lip] + 1e-3f), "and under its water");
			float pool = river.Surface[basin.Foot];
			int river0 = 0, held = 0;
			for (int z = 1; z < GridDepth - 1; z++)
			{
				for (int x = 1; x < GridWidth - 1; x++)
				{
					int i = z * GridWidth + x;
					if (grid.Kind[i] != WaterKind.River)
					{
						continue;
					}
					river0++;
					Assert.That(height[i], Is.LessThanOrEqualTo(grid.Level[i] + 1e-3f), "the channel lies under its water");
					// The pool's banks hold it: no dry ground beside it lower than its water.
					if (Math.Abs(grid.Level[i] - pool) > 0.05f)
					{
						continue;
					}
					foreach (int j in new[] { i + 1, i - 1, i + GridWidth, i - GridWidth })
					{
						if (grid.Kind[j] != WaterKind.River && grid.Kind[j] != WaterKind.Lake)
						{
							held++;
							// The bank's edge sample stands just under its water (RiverSettings.EdgeShelfMetres), the levee past it over.
							Assert.That(height[j], Is.GreaterThanOrEqualTo(pool - settings.EdgeShelfMetres - 1e-3f),
								$"the pool leaks past ({grid.EastOf(j % GridWidth)}, {grid.NorthOf(j / GridWidth)})");
						}
					}
				}
			}
			Assert.That(river0, Is.GreaterThan(100));
			Assert.That(held, Is.GreaterThan(10), "the pool has banks to check");
		}

		[Test]
		public void TheBrinkRoundsOverUnderTheWaterFallingOffIt()
		{
			var settings = new RiverSettings();
			RiverPath river = River(settings);
			FallBasin basin = BasinOf(river, settings);
			float[] height = Carve(settings, river, out SceneWaterGrid grid);
			float lipX = river.X[basin.Lip], nextX = river.X[basin.Lip + 1];
			// The water's own arc at the brink speed (FallHydraulics: critical flow over a free overfall).
			float q = river.Discharge[basin.Lip] / river.Width[basin.Lip];
			float brinkSpeed = (float)Math.Pow(9.81 * q, 1.0 / 3.0) / 0.715f;
			int rows = 0, rounded = 0;
			for (float z = -basin.HalfLip + 0.5f; z <= basin.HalfLip - 0.5f; z += Spacing)
			{
				int row = Row(z);
				int behind = Column(lipX);
				while (OriginX + behind * Spacing > lipX)
				{
					behind--;
				}
				float brink = height[row * GridWidth + behind];
				for (int column = behind + 1; OriginX + column * Spacing <= nextX; column++)
				{
					float x = OriginX + column * Spacing - lipX;
					float fallen = 9.81f * x * x / (2f * brinkSpeed * brinkSpeed);
					float ground = height[row * GridWidth + column];
					Assert.That(ground, Is.LessThan(brink - fallen),
						$"{x:F1} m past the lip at {z:F1} across the ground stands in the water falling off it");
					rows++;
					if (ground > basin.LipLevel - 0.5f * (basin.LipLevel - river.Surface[basin.Foot]))
					{
						rounded++;
					}
				}
			}
			Assert.That(rows, Is.GreaterThan(0), "samples past the lip to check");
			Assert.That(rounded, Is.GreaterThan(0), "the brink rounds over rather than dropping a cell sheer to the pool");
		}

		[Test]
		public void AFallsBrinkAndFaceArePaintedRockAndTheGroundAwayFromItIsNot()
		{
			var settings = new RiverSettings();
			RiverPath river = River(settings);
			FallBasin basin = BasinOf(river, settings);
			var paint = new FallRockPaint(new[] { river }, settings, Spacing);
			Assert.That(paint.Count, Is.EqualTo(1));
			float lipX = river.X[basin.Lip];
			Assert.That(paint.RockAt(lipX + 1f, 0f, 20f), Is.EqualTo(1f), "the brink the water rounds over, however gentle");
			Assert.That(paint.RockAt(lipX - 0.5f, basin.HalfLip + 0.5f, 10f), Is.EqualTo(1f), "and its last metre beside the water");
			Assert.That(paint.RockAt(lipX + 6f, basin.HalfLip + 2f, 65f), Is.EqualTo(1f), "the steep face beside the curtain");
			Assert.That(paint.RockAt(lipX + 6f, basin.HalfLip + 2f, 25f), Is.EqualTo(0f), "a gentle bank at the face's foot stays ground");
			Assert.That(paint.RockAt(lipX - 20f, 0f, 60f), Is.EqualTo(0f), "a steep bank upstream is the slope rule's");
			Assert.That(paint.RockAt(lipX + 6f, basin.HalfLip + 30f, 65f), Is.EqualTo(0f), "and so is a cliff well out from the fall");
		}

		[Test]
		public void ANarrowRiversEdgeIsNeverPinchedByTheGroundBesideIt()
		{
			// A stream 3.4 m wide on 2 m samples: every sample within a sample's spacing of its edge is at or under its water,
			// so the terrain's plane between samples cannot rise into the water short of its width (RiverSettings.EdgeShelfMetres).
			var settings = new RiverSettings();
			RiverPath river = River(settings, 3.4f);
			FallBasin basin = BasinOf(river, settings);
			float[] height = Carve(settings, river, out _);
			int checkedCells = 0;
			for (int k = 1; k + 1 < basin.Lip - 2; k++)
			{
				for (int column = Column(river.X[k]); column < Column(river.X[k + 1]); column++)
				{
					float x = OriginX + column * Spacing;
					float t = Math.Clamp((x - river.X[k]) / Math.Max(1e-3f, river.X[k + 1] - river.X[k]), 0f, 1f);
					float level = river.Surface[k] + (river.Surface[k + 1] - river.Surface[k]) * t;
					float half = 0.5f * (river.Width[k] + (river.Width[k + 1] - river.Width[k]) * t);
					for (int z = 0; z < GridDepth; z++)
					{
						float north = OriginZ + z * Spacing;
						if (Math.Abs(north) < half + 0.99f * Spacing)
						{
							checkedCells++;
							Assert.That(height[z * GridWidth + column], Is.LessThanOrEqualTo(level + 1e-3f),
								$"({x}, {north}) stands {height[z * GridWidth + column] - level:0.00} m into the water ({half:0.0} m half-width)");
						}
					}
				}
			}
			Assert.That(checkedCells, Is.GreaterThan(20));
		}

		[Test]
		public void TheLipsBanksUpstreamAreLeftAsTheRiverCutThem()
		{
			var settings = new RiverSettings();
			RiverPath river = River(settings);
			FallBasin basin = BasinOf(river, settings);
			float[] height = Carve(settings, river, out _);
			float[] plain = Plain();
			float lipX = river.X[basin.Lip];
			// The run up to the brink, which lays no bar (RiverShaping.Bars): its banks stand over the lip's water.
			float from = lipX - RiverShaping.BrinkClearMetres;
			float half = 0f;
			for (int k = 0; k <= basin.Lip; k++)
			{
				if (river.X[k] >= from - Spacing)
				{
					half = Math.Max(half, 0.5f * river.Width[k]);
				}
			}
			int checkedCells = 0;
			for (int z = 0; z < GridDepth; z++)
			{
				for (int column = Column(from); column <= Column(lipX - 1f); column++)
				{
					int i = z * GridWidth + column;
					if (Math.Abs(OriginZ + z * Spacing) > half + 1f)
					{
						checkedCells++;
						// The bank's edge sample just under the water (RiverSettings.EdgeShelfMetres); never cut away under it.
						Assert.That(height[i], Is.GreaterThanOrEqualTo(Math.Min(plain[i], basin.LipLevel) - settings.EdgeShelfMetres - 1e-3f),
							$"({OriginX + column * Spacing}, {OriginZ + z * Spacing}) above the brink is cut under the lip's water");
					}
				}
			}
			Assert.That(checkedCells, Is.GreaterThan(100));
		}

		[Test]
		public void TheSameRiverIsCarvedTheSameEveryTime()
		{
			var settings = new RiverSettings();
			float[] a = Carve(settings, River(settings), out _);
			float[] b = Carve(settings, River(settings), out _);
			Assert.That(b, Is.EqualTo(a));
		}
	}
}
