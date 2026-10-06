using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Rivers and lakes: the sphere's grid, where water runs and stands by its water balance, a river's
	/// surface never rising, its channel carved under its water, and its lakes flooding only where they may.
	/// </summary>
	[TestFixture]
	public class HydrologyTests
	{
		[Test]
		public void TheCubeSphereCoversTheSphereWithSymmetricNeighbours()
		{
			var grid = new CubeSphereGrid(40, 30000.0);
			double total = grid.Area.Sum();
			Assert.That(total / (4 * Math.PI * 30000.0 * 30000.0), Is.EqualTo(1.0).Within(1e-9), "cell areas sum to the sphere's");
			Assert.That(grid.Area.Max() / grid.Area.Min(), Is.LessThan(1.5), "cells within half again of each other's area");
			for (int c = 0; c < grid.Count; c++)
			{
				Assert.That(grid.CellOf(grid.Centre[c * 3], grid.Centre[c * 3 + 1], grid.Centre[c * 3 + 2]), Is.EqualTo(c), "a cell's centre falls in it");
				int count = grid.NeighbourStart[c + 1] - grid.NeighbourStart[c];
				Assert.That(count, Is.InRange(7, 8));
				for (int k = grid.NeighbourStart[c]; k < grid.NeighbourStart[c + 1]; k++)
				{
					int m = grid.Neighbours[k];
					bool back = false;
					for (int j = grid.NeighbourStart[m]; j < grid.NeighbourStart[m + 1]; j++)
					{
						back |= grid.Neighbours[j] == c;
					}
					Assert.That(back, Is.True, $"{m} lists {c} back");
				}
			}
		}

		[Test]
		public void WaterIsConservedAndRiversRunDownhill()
		{
			var grid = new CubeSphereGrid(64, 30000.0);
			int count = grid.Count;
			var height = new float[count];
			var sea = new bool[count];
			var rain = new float[count];
			var runoff = new float[count];
			var evaporation = new float[count];
			for (int c = 0; c < count; c++)
			{
				double x = grid.Centre[c * 3], y = grid.Centre[c * 3 + 1], z = grid.Centre[c * 3 + 2];
				height[c] = (float)(900.0 * (Math.Sin(x * 5.1 + 1.3) * Math.Cos(y * 4.3) + 0.5 * Math.Sin(z * 9.7 + x * 3.1) + 0.25 * Math.Cos(y * 17.0 + z * 11.0)) + 150.0);
				sea[c] = height[c] < 0f;
				rain[c] = x > 0 ? 1.3f : 0.2f;
				evaporation[c] = x > 0 ? 0.9f : 2.0f;
				runoff[c] = WaterBudget.RunoffMetres(rain[c], evaporation[c]);
			}
			DrainageResult result = DrainageSolver.Solve(height, grid.Area, grid.NeighbourStart, grid.Neighbours, grid.NeighbourMetres, sea, null, rain, runoff, evaporation, new DrainageSettings());
			Assert.That((result.ToOutlets + result.Evaporated) / result.Runoff, Is.EqualTo(1.0).Within(1e-4), "every m³ reaches the sea or a lake's surface evaporates it");
			Assert.That(result.Rivers.Count, Is.GreaterThan(0));
			foreach (DrainageRiver river in result.Rivers)
			{
				for (int v = 1; v < river.Cells.Length; v++)
				{
					Assert.That(result.Receiver[river.Cells[v - 1]], Is.EqualTo(river.Cells[v]), "a river follows its receivers");
					Assert.That(result.Filled[river.Cells[v]], Is.LessThanOrEqualTo(result.Filled[river.Cells[v - 1]]), "and never climbs the filled ground");
				}
			}
			foreach (DrainageLake lake in result.Lakes)
			{
				Assert.That(lake.Level, Is.InRange(lake.Floor - 1e-3f, lake.SpillLevel + 1e-3f));
			}
		}

		[Test]
		public void AWorldWhereNothingFallsHasNoRiversWashesOrLakes()
		{
			// An airless moon, or an ice moon frozen through: rugged ground, hollows everywhere, no rain.
			var grid = new CubeSphereGrid(48, 30000.0);
			int count = grid.Count;
			var height = new float[count];
			var nothing = new float[count];
			var evaporation = new float[count];
			var sea = new bool[count];
			for (int c = 0; c < count; c++)
			{
				double x = grid.Centre[c * 3], y = grid.Centre[c * 3 + 1], z = grid.Centre[c * 3 + 2];
				height[c] = (float)(900.0 * (Math.Sin(x * 7.1) * Math.Cos(y * 6.3 + z * 2.0) + 0.5 * Math.Sin(z * 13.7 + x * 5.1)));
				evaporation[c] = 0.05f;
			}
			DrainageResult result = DrainageSolver.Solve(height, grid.Area, grid.NeighbourStart, grid.Neighbours, grid.NeighbourMetres, sea, null, nothing, nothing, evaporation, new DrainageSettings());
			Assert.That(result.Rivers, Is.Empty, "no rivers, and no dry washes: no rain ever gathered");
			Assert.That(result.Lakes, Is.Empty, "no lakes: no water reaches the hollows");
		}

		[Test]
		public void AKarstSinkholesWaterComesOutAgainAtASpringDownhill()
		{
			// A slope down to the sea at x = 0, with a karst pit 3 km up it that swallows what reaches it.
			int w = 80, d = 40;
			float cell = 50f;
			int count = w * d, pit = 20 * w + 60;
			var height = new float[count];
			var area = new double[count];
			var sea = new bool[count];
			var sink = new bool[count];
			var rain = new float[count];
			var runoff = new float[count];
			var evaporation = new float[count];
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					int c = z * w + x;
					float rx = (x - 60) * cell, rz = (z - 20) * cell, r = Mathf.Sqrt(rx * rx + rz * rz);
					height[c] = 0.02f * x * cell - (r < 300f ? 20f * (1f - r / 300f) : 0f);
					area[c] = cell * cell;
					sea[c] = x == 0;
					rain[c] = 1.2f;
					evaporation[c] = 0.6f;
					runoff[c] = WaterBudget.RunoffMetres(rain[c], evaporation[c]);
				}
			}
			sink[pit] = true;
			Neighbours(w, d, cell, out int[] start, out int[] neighbours, out float[] metres);
			DrainageResult lost = DrainageSolver.Solve(height, area, start, neighbours, metres, sea, sink, rain, runoff, evaporation,
				new DrainageSettings { BreachMetres = 0f, SpringReachMetres = 0f });
			DrainageResult resurging = DrainageSolver.Solve(height, area, start, neighbours, metres, sea, sink, rain, runoff, evaporation,
				new DrainageSettings { BreachMetres = 0f });
			// What reaches the sea: the discharge into its cells (a sinkhole is an outlet too, so ToOutlets counts both).
			double ToSea(DrainageResult result)
			{
				double total = 0;
				for (int c = 0; c < count; c++)
				{
					total += sea[c] ? result.Discharge[c] : 0f;
				}
				return total;
			}
			Assert.That(ToSea(lost), Is.LessThan(0.99 * lost.Runoff), "without springs the pit's catchment is lost to the ground");
			Assert.That(ToSea(resurging), Is.EqualTo(resurging.Runoff).Within(resurging.Runoff * 1e-3), "with them every drop reaches the sea");
			Assert.That(resurging.Discharge[pit], Is.GreaterThan(0f), "the pit still takes its water in");
		}

		[TestCase(true)]
		[TestCase(false)]
		public void ALakeStandsWhereItsWaterBalancePutsIt(bool humid)
		{
			int w = 160, d = 100;
			float cell = 50f;
			int count = w * d;
			var height = new float[count];
			var area = new double[count];
			var sea = new bool[count];
			var rain = new float[count];
			var runoff = new float[count];
			var evaporation = new float[count];
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					int c = z * w + x;
					float rx = (x - 110) * cell, rz = (z - 50) * cell, r = Mathf.Sqrt(rx * rx + rz * rz);
					height[c] = 5f + 0.004f * x * cell + (r < 1500f ? -40f * (1f - (r / 1500f) * (r / 1500f)) : 0f) + 12f * Mathf.Exp(-Mathf.Pow((r - 1550f) / 250f, 2f));
					area[c] = cell * cell;
					sea[c] = x == 0;
					rain[c] = humid ? 1.4f : 0.6f;
					evaporation[c] = humid ? 0.8f : 1.4f;
					runoff[c] = WaterBudget.RunoffMetres(rain[c], evaporation[c]);
				}
			}
			Neighbours(w, d, cell, out int[] start, out int[] neighbours, out float[] metres);
			DrainageResult result = DrainageSolver.Solve(height, area, start, neighbours, metres, sea, null, rain, runoff, evaporation,
				new DrainageSettings { MinLakeAreaSquareMetres = 50_000, MinRiverDischarge = 0.05f, BreachMetres = 20f });
			DrainageLake lake = result.Lakes.OrderByDescending(l => l.Cells.Length).First();
			if (humid)
			{
				Assert.That(lake.Level, Is.EqualTo(lake.SpillLevel).Within(1e-3f), "a humid lake fills to its outlet");
				Assert.That(lake.Outflow, Is.GreaterThan(0f));
				Assert.That(lake.Breach, Is.GreaterThan(0f).And.LessThan(lake.SpillLevel + lake.Breach - lake.Floor), "which its outflow has cut part way into its sill");
				Assert.That(result.Rivers.Any(r => r.Start == RiverEnd.Lake && r.StartLake == lake.Id), Is.True, "and a river leaves it");
			}
			else
			{
				Assert.That(lake.Outflow, Is.EqualTo(0f), "an arid lake is terminal");
				Assert.That(lake.Level, Is.LessThan(lake.SpillLevel - 1f));
				double loss = 0;
				foreach (int c in lake.Cells)
				{
					if (height[c] < lake.Level)
					{
						loss += (runoff[c] - rain[c] + evaporation[c]) * area[c] / DrainageSolver.SecondsPerYear;
					}
				}
				Assert.That(loss / lake.Inflow, Is.EqualTo(1.0).Within(0.1), "its surface loses what reaches it");
			}
		}

		[Test]
		public void ARiversSurfaceNeverRises()
		{
			var settings = new RiverSettings();
			int n = 500;
			var floor = new float[n];
			var step = new float[n];
			var random = new System.Random(5);
			for (int i = 0; i < n; i++)
			{
				float s = i * 4f;
				floor[i] = 60f - 0.01f * s + 3f * Mathf.Exp(-Mathf.Pow((s - 900f) / 60f, 2f)) - 2.5f * Mathf.Exp(-Mathf.Pow((s - 1500f) / 80f, 2f)) + (float)(random.NextDouble() - 0.5) * 0.6f;
				step[i] = i == 0 ? 0f : 4f;
			}
			var bank = new float[n];
			for (int i = 0; i < n; i++)
			{
				// A metre over the floor, but low through the pool at 1500 m.
				bank[i] = floor[i] + 1f + 2.5f * Mathf.Exp(-Mathf.Pow((i * 4f - 1500f) / 80f, 2f));
			}
			float[] surface = RiverShaping.Surface(floor, bank, step, float.PositiveInfinity, 30f, settings);
			for (int i = 0; i < n; i++)
			{
				Assert.That(surface[i], Is.LessThanOrEqualTo(Mathf.Max(30f, bank[i] - settings.InsetMetres) + 1e-4f), "never over its lower bank");
			}
			for (int i = 1; i < n; i++)
			{
				Assert.That(surface[i], Is.LessThanOrEqualTo(surface[i - 1] - settings.MinGradient * step[i] + 1e-4f), $"falls at {i}");
				Assert.That(surface[i], Is.GreaterThanOrEqualTo(30f - 1e-4f), "never below what it ends in");
			}
		}

		/// <summary>
		/// A tributary whose valley lies lower than the river it joins is not raised to that river's level over its
		/// whole length (Flo Monolith's river 1 stood 16 m over its own floor for 1.1 km): only where its banks hold it.
		/// </summary>
		/// <summary>
		/// A line that loops across itself (Flo Monolith's main river crossed itself 36 times) has the loop cut out: no
		/// point of what is left comes back within its channel's width of itself.
		/// </summary>
		[Test]
		public void ARiverLineThatCrossesItselfHasItsLoopCutOut()
		{
			var x = new List<float>();
			var z = new List<float>();
			var along = new List<float>();
			// East along z = 0, a full loop of radius 20 m round (100, 20), then on east.
			for (float e = 0f; e < 100f; e += 4f) { x.Add(e); z.Add(0f); }
			for (int k = 0; k <= 32; k++)
			{
				float a = -Mathf.PI / 2f + k / 32f * Mathf.PI * 2f;
				x.Add(100f + 20f * Mathf.Cos(a) - k * 0.2f); z.Add(20f + 20f * Mathf.Sin(a));
			}
			for (float e = 104f; e < 200f; e += 4f) { x.Add(e); z.Add(0f); }
			for (int i = 0; i < x.Count; i++) { along.Add(i); }
			var width = new float[x.Count];
			for (int i = 0; i < width.Length; i++) { width[i] = 6f; }
			int before = x.Count;
			int removed = RiverShaping.Untangle(x, z, along, width, 4f);
			Assert.That(removed, Is.GreaterThan(20), "the loop is gone");
			Assert.That(x.Count, Is.EqualTo(before - removed));
			Assert.That(along.Count, Is.EqualTo(x.Count), "its parameter is cut with it");
			for (int i = 0; i < x.Count; i++)
			{
				float arc = 0f;
				for (int j = i + 1; j < x.Count; j++)
				{
					arc += Mathf.Sqrt((x[j] - x[j - 1]) * (x[j] - x[j - 1]) + (z[j] - z[j - 1]) * (z[j] - z[j - 1]));
					if (arc > 30f)
					{
						float d = Mathf.Sqrt((x[j] - x[i]) * (x[j] - x[i]) + (z[j] - z[i]) * (z[j] - z[i]));
						Assert.That(d, Is.GreaterThan(6f), $"points {i} and {j} meet again");
					}
				}
			}
		}

		[Test]
		public void ARiverEndingInHigherWaterIsNeverRaisedOverItsOwnBanks()
		{
			var settings = new RiverSettings();
			int n = 200;
			var floor = new float[n];
			var bank = new float[n];
			var step = new float[n];
			for (int i = 0; i < n; i++)
			{
				// A valley floor at about 150 m, rising over its last 20 points to banks above the junction's 166 m.
				float rise = i < n - 20 ? 0f : (i - (n - 20)) * 1.2f;
				floor[i] = 151f - 0.002f * i * 4f + rise;
				bank[i] = floor[i] + 1f;
				step[i] = i == 0 ? 0f : 4f;
			}
			float[] surface = RiverShaping.Surface(floor, bank, step, float.PositiveInfinity, 166f, settings);
			for (int i = 0; i < n; i++)
			{
				float banks = Mathf.Max(floor[i], bank[i]) - settings.InsetMetres;
				Assert.That(surface[i], Is.LessThanOrEqualTo(Mathf.Max(banks, 166f) + 1e-3f), $"point {i}");
				if (banks < 166f && i < n - 20)
				{
					Assert.That(surface[i], Is.LessThanOrEqualTo(banks + 1e-3f), $"point {i}: on its own ground, not held at the junction's level");
				}
			}
		}

		[Test]
		public void AChannelIsCarvedUnderItsWaterAndItsBanksStandNoSteeperThanAllowed()
		{
			var settings = new RiverSettings();
			int w = 400, d = 300;
			var grid = new SceneWaterGrid(w, d, 2f, -400f, -300f);
			var height = new float[w * d];
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					float px = grid.EastOf(x);
					height[z * w + x] = 40f - 0.01f * px + 12f * Mathf.Exp(-Mathf.Pow(px / 40f, 2f));
				}
			}
			var before = (float[])height.Clone();
			RiverPath path = StraightRiver(-400f, 400f, 0f, height, grid, settings);
			WaterCarver.CarveRivers(height, grid, new[] { path }, settings, true);
			float bankTan = Mathf.Tan(settings.BankDegrees * Mathf.Deg2Rad);
			int channel = 0;
			for (int z = 0; z < d - 1; z++)
			{
				for (int x = 0; x < w - 1; x++)
				{
					int i = z * w + x;
					if (grid.Kind[i] == WaterKind.River)
					{
						channel++;
						Assert.That(height[i], Is.LessThanOrEqualTo(grid.Level[i] + 1e-3f), "the channel lies under the water");
					}
					if (grid.Kind[i] == WaterKind.Bank && grid.Kind[i + w] == WaterKind.Bank && height[i] < before[i] - 0.5f && height[i + w] < before[i + w] - 0.5f)
					{
						Assert.That(Mathf.Abs(height[i + w] - height[i]) / 2f, Is.LessThanOrEqualTo(bankTan * 1.15f), "a cut bank stands no steeper than its angle");
					}
					if (Mathf.Abs(grid.NorthOf(z)) > path.Width[0] / 2f + settings.MaxCorridorMetres + 2f)
					{
						Assert.That(height[i], Is.EqualTo(before[i]), "ground past the corridor is left alone");
					}
				}
			}
			Assert.That(channel, Is.GreaterThan(500));
		}

		[Test]
		public void ALakeFloodsOnlyLowGroundItMayCoverAndSealsItsEdge()
		{
			int w = 200, d = 200;
			var grid = new SceneWaterGrid(w, d, 2f, -200f, -200f);
			var height = new float[w * d];
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					float px = grid.EastOf(x), pz = grid.NorthOf(z);
					// A trough running off east: the lake may only stand within 80 m of the middle.
					height[z * w + x] = 10f + 0.0005f * (px * px + 4f * pz * pz) * (px < 0f ? 1f : 0.05f);
				}
			}
			Func<int, bool> may = i =>
			{
				float px = grid.EastOf(i % w), pz = grid.NorthOf(i / w);
				return px * px + pz * pz < 80f * 80f;
			};
			int covered = WaterCarver.FloodLake(height, grid, 13f, new[] { grid.SampleAt(0f, 0f) }, may, 0, 0.4f, 4f, true);
			Assert.That(covered, Is.GreaterThan(100));
			for (int i = 0; i < w * d; i++)
			{
				if (grid.Kind[i] == WaterKind.Lake)
				{
					Assert.That(height[i], Is.LessThan(13f));
					Assert.That(may(i), Is.True, "the flood stays where the lake may be");
				}
			}
			// Where the trough left the allowed ground below the level, a sill now holds the water.
			Assert.That(height[grid.SampleAt(80f, 0f)], Is.GreaterThanOrEqualTo(13f), "the first sample past where it may stand is raised into a sill");
			Assert.That(grid.Kind[grid.SampleAt(120f, 0f)], Is.EqualTo(WaterKind.None), "and no water gets past it");
		}

		[Test]
		public void TheWaterSurvivesTheAssetRoundTrip()
		{
			var settings = new RiverSettings();
			var grid = new SceneWaterGrid(100, 100, 2f, -100f, -100f);
			var height = new float[100 * 100];
			for (int i = 0; i < height.Length; i++)
			{
				height[i] = 20f;
			}
			var water = new SceneWater(settings);
			water.Rivers.Add(StraightRiver(-100f, 100f, 0f, height, grid, settings));
			water.Lakes.Add(new SceneLake { Id = 0, PlanetLake = 4, Level = 18f, SpillLevel = 19f, Terminal = true, Seeds = new List<Vector2> { new Vector2(5f, 6f) }, Bounds = new Rect(-50, -50, 100, 100) });
			var asset = ScriptableObject.CreateInstance<SceneHydrology>();
			try
			{
				water.WriteTo(asset);
				SceneWater back = SceneWater.FromAsset(asset);
				Assert.That(back.Rivers.Count, Is.EqualTo(1));
				Assert.That(back.Lakes.Count, Is.EqualTo(1));
				RiverPath a = water.Rivers[0], b = back.Rivers[0];
				Assert.That(b.Count, Is.EqualTo(a.Count));
				for (int i = 0; i < a.Count; i++)
				{
					Assert.That(b.Surface[i], Is.EqualTo(a.Surface[i]));
					Assert.That(b.X[i], Is.EqualTo(a.X[i]));
					Assert.That(b.Width[i], Is.EqualTo(a.Width[i]));
				}
				Assert.That(back.Lakes[0].Level, Is.EqualTo(18f));
				Assert.That(back.Lakes[0].Terminal, Is.True);
				Assert.That(asset.Rivers[0].Speed[0], Is.GreaterThan(0f), "speed from continuity");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(asset);
			}
		}

		[Test]
		public void HumidLandRunsOffMoreThanAridLand()
		{
			float wet = WaterBudget.RunoffMetres(2.0f, 1.0f) / 2.0f;
			float dry = WaterBudget.RunoffMetres(0.3f, 2.0f) / 0.3f;
			Assert.That(wet, Is.GreaterThan(0.4f), "a rainforest sheds much of its rain");
			Assert.That(dry, Is.LessThan(0.05f), "a desert almost none");
			Assert.That(WaterBudget.PotentialEvaporationMetres(30f), Is.GreaterThan(WaterBudget.PotentialEvaporationMetres(5f)));
		}

		private static RiverPath Line(float[] surface, float spacing, float discharge, float width)
		{
			int n = surface.Length;
			var path = new RiverPath { X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n], Surface = surface, Bed = new float[n], Curvature = new float[n] };
			for (int i = 0; i < n; i++)
			{
				path.X[i] = i * spacing;
				path.S[i] = i * spacing;
				path.Discharge[i] = discharge;
				path.Width[i] = width;
				path.Depth[i] = 1f;
				path.Bed[i] = surface[i] - 1f;
			}
			return path;
		}

		[Test]
		public void SteepWaterRunsShallowAndFastGentleWaterDeepAndSlow()
		{
			var settings = new RiverSettings();
			var surface = new float[600];
			for (int i = 1; i < surface.Length; i++)
			{
				surface[i] = surface[i - 1] - (i < 300 ? 0.05f : 0.0005f) * 4f;
			}
			RiverPath river = Line(surface, 4f, 4f, 9f);
			RiverShaping.Reaches(river, settings, 7);
			float steepDepth = 0f, gentleDepth = 0f, steepSpeed = 0f, gentleSpeed = 0f;
			int riffles = 0, pools = 0;
			for (int i = 20; i < 280; i++)
			{
				steepDepth += river.Depth[i] / 260f;
				steepSpeed += river.SpeedAt(i) / 260f;
				Assert.That(river.Reach[i], Is.EqualTo(RiverReach.Rapid), "a 5% reach is white water");
			}
			for (int i = 320; i < 580; i++)
			{
				gentleDepth += river.Depth[i] / 260f;
				gentleSpeed += river.SpeedAt(i) / 260f;
				riffles += river.Reach[i] == RiverReach.Riffle ? 1 : 0;
				pools += river.Reach[i] == RiverReach.Pool ? 1 : 0;
			}
			Assert.That(river.Width[150], Is.LessThan(river.Width[450] * 0.75f), "steep water runs in a narrower channel");
			Assert.That(steepDepth, Is.LessThanOrEqualTo(gentleDepth), "no deeper");
			Assert.That(steepSpeed, Is.GreaterThan(gentleSpeed * 1.5f), "and faster, though its roughness holds it back (Jarrett)");
			Assert.That(riffles, Is.GreaterThan(20), "a gentle reach has riffles");
			Assert.That(pools, Is.GreaterThan(20), "and pools between them");
		}

		[Test]
		public void LooseBanksMakeWideShallowRiversAndRootedBanksNarrowDeepOnes()
		{
			var settings = new RiverSettings();
			RiverShaping.Size(4f, 0f, settings, out float bareWidth, out float bareDepth);
			RiverShaping.Size(4f, 0.5f, settings, out float width, out float depth);
			RiverShaping.Size(4f, 1f, settings, out float rootedWidth, out float rootedDepth);
			Assert.That(bareWidth, Is.GreaterThan(width * 1.5f));
			Assert.That(bareDepth, Is.LessThan(depth));
			Assert.That(rootedWidth, Is.LessThan(width * 0.7f));
			Assert.That(rootedDepth, Is.GreaterThan(depth));
		}

		[Test]
		public void GentleWaterLaysSandBarsOnTheInsideOfBendsAndSteepWaterGravel()
		{
			var settings = new RiverSettings();
			int n = 400;
			var gentleSurface = new float[n];
			var steepSurface = new float[n];
			for (int i = 0; i < n; i++)
			{
				gentleSurface[i] = 20f - 0.0003f * 3f * i;
				steepSurface[i] = 50f - 0.03f * 3f * i;
			}
			RiverPath gentle = Line(gentleSurface, 3f, 3f, 10f);
			RiverPath steep = Line(steepSurface, 3f, 3f, 10f);
			for (int i = 0; i < n; i++)
			{
				gentle.Curvature[i] = 0.02f;
				steep.Curvature[i] = 0.02f;
			}
			RiverShaping.Reaches(gentle, settings, 3);
			RiverShaping.Reaches(steep, settings, 3);
			RiverShaping.Bars(gentle, settings, out float[] point, out float[] beach, out float[] sand);
			Assert.That(point.Average(), Is.GreaterThan(5f), "a sharp bend grows a point bar");
			Assert.That(beach.Average(), Is.GreaterThan(1f), "a gentle river has beaches");
			Assert.That(sand.Average(), Is.GreaterThan(0.8f), "gentle water moves only sand and fine gravel");
			RiverShaping.Bars(steep, settings, out _, out float[] steepBeach, out float[] gravel);
			Assert.That(gravel.Average(), Is.LessThan(0.2f), "steep water moves stones, and its bars are gravel");
			Assert.That(steepBeach.Max(), Is.EqualTo(0f), "and it lays no beaches");
			Assert.That(steep.Grain[n / 2], Is.GreaterThan(gentle.Grain[n / 2] * 20f), "Shields: the force on the bed grows with depth × slope");
		}

		[Test]
		public void ATerminalLakeIsRingedBySaltFlat()
		{
			int w = 160, d = 160;
			var grid = new SceneWaterGrid(w, d, 2f, -160f, -160f);
			var height = new float[w * d];
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					float px = grid.EastOf(x), pz = grid.NorthOf(z);
					height[z * w + x] = 10f + 0.0001f * (px * px + pz * pz);
				}
			}
			WaterCarver.FloodLake(height, grid, 10.5f, new[] { grid.SampleAt(0f, 0f) }, i => true, 0, 0.4f, 4f, false);
			int marked = WaterCarver.MarkPlaya(height, grid, 0, 10.5f, 1.2f, i => true);
			Assert.That(marked, Is.GreaterThan(100));
			for (int i = 0; i < height.Length; i++)
			{
				if (grid.Kind[i] == WaterKind.Playa)
				{
					Assert.That(height[i], Is.InRange(10.5f, 11.7f), "between its level and 1.2 m above");
				}
			}
		}

		[Test]
		public void BouldersLieOnlyInFastWaterAndRepeat()
		{
			var surface = new float[500];
			for (int i = 1; i < surface.Length; i++)
			{
				surface[i] = surface[i - 1] - (i < 250 ? 0.04f : 0.0004f) * 4f;
			}
			RiverPath river = Line(surface, 4f, 6f, 10f);
			RiverShaping.Reaches(river, new RiverSettings(), 1);
			var water = new SceneWater();
			water.Rivers.Add(river);
			List<RiverBoulder> a = RiverBoulders.Plan(water, (x, z) => 0f, null, 42u);
			List<RiverBoulder> b = RiverBoulders.Plan(water, (x, z) => 0f, null, 42u);
			Assert.That(a.Count, Is.GreaterThan(10), "a fast reach carries boulders");
			Assert.That(b.Count, Is.EqualTo(a.Count));
			for (int i = 0; i < a.Count; i++)
			{
				Assert.That(b[i].Position, Is.EqualTo(a[i].Position), "the same seed places them the same");
				Assert.That(a[i].Position.x, Is.LessThan(250 * 4f + 20f), "none in the slow reach");
				Assert.That(a[i].Prefab, Does.StartWith("Boulder_Grey_"));
			}
		}

		private static RiverPath StraightRiver(float from, float to, float north, float[] height, SceneWaterGrid grid, RiverSettings settings)
		{
			var x = new List<float>();
			for (float p = from; p <= to; p += 3f)
			{
				x.Add(p);
			}
			int n = x.Count;
			RiverShaping.Size(6f, settings, out float width, out float depth);
			var path = new RiverPath { X = x.ToArray(), Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n], Bed = new float[n], Curvature = new float[n] };
			var floor = new float[n];
			var step = new float[n];
			for (int i = 0; i < n; i++)
			{
				path.Z[i] = north;
				path.Width[i] = width;
				path.Depth[i] = depth;
				path.Discharge[i] = 6f;
				int s = grid.SampleAt(Mathf.Clamp(x[i], grid.OriginX, grid.EastOf(grid.Width - 1)), north);
				floor[i] = height[Mathf.Max(0, s)];
				step[i] = i == 0 ? 0f : 3f;
				path.S[i] = i * 3f;
			}
			path.Surface = RiverShaping.Surface(floor, floor, step, float.PositiveInfinity, float.NegativeInfinity, settings);
			for (int i = 0; i < n; i++)
			{
				path.Bed[i] = path.Surface[i] - depth;
			}
			return path;
		}

		private static void Neighbours(int w, int d, float cell, out int[] start, out int[] neighbours, out float[] metres)
		{
			var s = new List<int>();
			var nb = new List<int>();
			var m = new List<float>();
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					s.Add(nb.Count);
					for (int dz = -1; dz <= 1; dz++)
					{
						for (int dx = -1; dx <= 1; dx++)
						{
							if ((dx == 0 && dz == 0) || x + dx < 0 || z + dz < 0 || x + dx >= w || z + dz >= d)
							{
								continue;
							}
							nb.Add((z + dz) * w + x + dx);
							m.Add(cell * (dx != 0 && dz != 0 ? 1.41421356f : 1f));
						}
					}
				}
			}
			s.Add(nb.Count);
			start = s.ToArray();
			neighbours = nb.ToArray();
			metres = m.ToArray();
		}
	}
}
