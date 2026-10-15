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
			// (EaseBends then takes out the corner the cut leaves: ARiverLineNeverTurnsBackOnItself.)
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

		/// <summary>
		/// No river turns back on itself. Flo Monolith's river 0 turned 146° in one point where river 1 joins it, and 139°
		/// and 86° near the coast between straight legs 40 m long (2026-10-12: "Fix the river issues"). Eased, every
		/// bend has room for its radius, the line resampled finer turns nowhere near a right angle, and it keeps its ends.
		/// </summary>
		[Test]
		public void ARiverLineNeverTurnsBackOnItself()
		{
			var settings = new RiverSettings();
			// Flo's junction: north-west, then back east (146°); its coast: east, 45 m south, 41 m back north-east (143°), on south-east; an Ω's neck cut (two 90° turns).
			var shapes = new (string name, Vector2[] corners, float width)[]
			{
				("hairpin", new[] { new Vector2(0f, 0f), new Vector2(-40f, 70f), new Vector2(60f, 21f) }, 8f),
				("zigzag", new[] { new Vector2(-80f, 0f), new Vector2(0f, 0f), new Vector2(2f, -45f), new Vector2(25f, -11f), new Vector2(90f, -60f) }, 4f),
				("neck", new[] { new Vector2(-60f, -60f), new Vector2(0f, 0f), new Vector2(0f, 60f), new Vector2(10f, 60f), new Vector2(10f, 0f), new Vector2(70f, -60f) }, 6f),
			};
			foreach ((string name, Vector2[] corners, float width) in shapes)
			{
				var x = new List<float>();
				var z = new List<float>();
				var along = new List<float>();
				for (int c = 0; c + 1 < corners.Length; c++)
				{
					int steps = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(corners[c], corners[c + 1]) / 4f));
					for (int k = 0; k < steps; k++)
					{
						Vector2 p = Vector2.Lerp(corners[c], corners[c + 1], k / (float)steps);
						x.Add(p.x);
						z.Add(p.y);
					}
				}
				x.Add(corners[corners.Length - 1].x);
				z.Add(corners[corners.Length - 1].y);
				for (int i = 0; i < x.Count; i++)
				{
					along.Add(i);
				}
				var widths = new float[x.Count];
				for (int i = 0; i < widths.Length; i++)
				{
					widths[i] = width;
				}
				Assert.That(MostTurn(x, z), Is.GreaterThan(85f), $"{name}: the line given turns back on itself");
				float firstX = x[0], firstZ = z[0], lastX = x[x.Count - 1], lastZ = z[z.Count - 1];
				int removed = RiverShaping.EaseBends(x, z, along, widths, settings);
				Assert.That(removed, Is.GreaterThan(0), $"{name}: the bend is cut off");
				Assert.That(along.Count, Is.EqualTo(x.Count), $"{name}: its parameter is carried with it");
				Assert.That(new Vector4(x[0], z[0], x[x.Count - 1], z[z.Count - 1]), Is.EqualTo(new Vector4(firstX, firstZ, lastX, lastZ)), $"{name}: its ends are kept");
				float radius = Mathf.Max(settings.MinBendRadiusMetres, settings.MinBendRadiusWidths * width);
				for (int i = 1; i + 1 < x.Count; i++)
				{
					float turn = Turn(x, z, i) * Mathf.Deg2Rad;
					float chords = 0.5f * (Vector2.Distance(new Vector2(x[i - 1], z[i - 1]), new Vector2(x[i], z[i])) + Vector2.Distance(new Vector2(x[i], z[i]), new Vector2(x[i + 1], z[i + 1])));
					Assert.That(turn * radius, Is.LessThanOrEqualTo(1.2f * chords), $"{name}: point {i} bends tighter than a {radius} m radius");
				}
				RiverShaping.Resample(x, z, along, 2f);
				Assert.That(MostTurn(x, z), Is.LessThan(45f), $"{name}: resampled finer, it still turns nowhere sharply");
			}

			// A line with no tight bend is left exactly as it was.
			var gx = new List<float>();
			var gz = new List<float>();
			var ga = new List<float>();
			for (int i = 0; i <= 60; i++)
			{
				float a = i * 0.04f;
				gx.Add(60f * Mathf.Cos(a));
				gz.Add(60f * Mathf.Sin(a));
				ga.Add(i);
			}
			var copy = new List<float>(gx);
			Assert.That(RiverShaping.EaseBends(gx, gz, ga, null, settings), Is.EqualTo(0));
			Assert.That(gx, Is.EqualTo(copy));
		}

		/// <summary>
		/// The snap to the valley floor never folds the line over a bend. On ground that falls away the same way the whole
		/// search long, every point went most of its reach that way, and a bend's straight stretches moved past its centre and
		/// crossed: the loop Untangle cut, leaving the hairpin. Held to the room on each side, no point passes another.
		/// </summary>
		[Test]
		public void SnappingToTheFloorNeverFoldsTheLineOverABend()
		{
			for (int held = 0; held < 2; held++)
			{
				var x = new List<float>();
				var z = new List<float>();
				var t = new List<float>();
				// East along z = 0, a quarter turn of 50 m radius to the north, then north.
				for (float e = -150f; e < 0f; e += 4f) { x.Add(e); z.Add(0f); }
				for (int k = 0; k <= 20; k++)
				{
					float a = -Mathf.PI / 2f + k * (Mathf.PI / 2f) / 20f;
					x.Add(50f * Mathf.Cos(a)); z.Add(50f + 50f * Mathf.Sin(a));
				}
				for (float n = 54f; n < 200f; n += 4f) { x.Add(50f); z.Add(n); }
				for (int i = 0; i < x.Count; i++) { t.Add(i); }
				RiverShaping.Resample(x, z, t, 4f);
				// Falling toward the north-west everywhere: the lowest ground is always the far edge of the search, inside the bend.
				RiverShaping.SnapToFloor(x, z, (e, n) => 0.2f * (e - n), null, 80f, 0.03f, 60f, held == 1 ? 6f : 0f);
				int crossings = Crossings(x, z);
				if (held == 0)
				{
					Assert.That(crossings, Is.GreaterThan(0), "unheld, the bend's two straights cross: the loop Untangle cuts into a hairpin");
					continue;
				}
				Assert.That(crossings, Is.EqualTo(0), "held, the line never crosses itself");
				Assert.That(MostTurn(x, z), Is.LessThan(90f), "nor turns back on itself");
			}
		}

		/// <summary>
		/// A tributary brought onto the river it joins keeps its spacing and never folds. The ease was measured from each point
		/// to its already moved neighbour, so it fell away within a few points and pulled the last ones apart: Flo Monolith's
		/// river 1 ended in segments of 7, 11, 21 and 40 m where the rest are 2 m (2026-10-12).
		/// </summary>
		[Test]
		public void ATributaryBroughtOntoItsRiverKeepsItsSpacing()
		{
			foreach (Vector2 to in new[] { new Vector2(340f, 10f), new Vector2(270f, -25f), new Vector2(300f, 60f) })
			{
				var x = new List<float>();
				var z = new List<float>();
				for (int i = 0; i <= 150; i++)
				{
					x.Add(2f * i);
					z.Add(0f);
				}
				var q = new float[x.Count];
				var area = new float[x.Count];
				for (int i = 0; i < q.Length; i++)
				{
					q[i] = 1f + 0.01f * i;
					area[i] = 100f + i;
				}
				int n = RiverShaping.EndOnto(x, z, ref q, ref area, to.x, to.y, 2f);
				Assert.That(n, Is.EqualTo(x.Count));
				Assert.That(q.Length, Is.EqualTo(n), $"{to}: its discharge is carried along");
				Assert.That(area.Length, Is.EqualTo(n));
				Assert.That(new Vector2(x[n - 1], z[n - 1]), Is.EqualTo(to), $"{to}: it ends on the river it joins");
				Assert.That(new Vector2(x[0], z[0]), Is.EqualTo(Vector2.zero), $"{to}: its far end is where it was");
				for (int i = 1; i < n; i++)
				{
					float dx = x[i] - x[i - 1], dz = z[i] - z[i - 1];
					float segment = Mathf.Sqrt(dx * dx + dz * dz);
					Assert.That(segment, Is.InRange(1.4f, 2.6f), $"{to}: segment {i} is {segment:0.00} m");
					Assert.That(dx, Is.GreaterThan(0f), $"{to}: segment {i} runs back");
					Assert.That(q[i], Is.GreaterThanOrEqualTo(q[i - 1] - 1e-5f), $"{to}: its discharge grows downstream as it did");
				}
				Assert.That(MostTurn(x, z), Is.LessThan(15f), $"{to}: eased in, not bent");
			}
		}

		/// <summary>The sharpest turn anywhere along a line, degrees.</summary>
		private static float MostTurn(List<float> x, List<float> z)
		{
			float most = 0f;
			for (int i = 1; i + 1 < x.Count; i++)
			{
				most = Mathf.Max(most, Turn(x, z, i));
			}
			return most;
		}

		/// <summary>
		/// A reversal between two long chords is cut off like any other: a line that runs out and back along one straight
		/// chord each way turns back on itself however long the chords are. Taken against the chords' mean length, a 175°
		/// turn between a 39 m and a 30 m chord passed for a gentle bend, and Flo Monolith's river 0 ran 39 m down its own
		/// channel and back up beside it to a fall that dropped under the stretch it had come down (2026-10-15).
		/// </summary>
		[Test]
		public void ARiverLineOutAndBackAlongLongChordsIsCutOff()
		{
			var settings = new RiverSettings();
			var x = new List<float>();
			var z = new List<float>();
			var along = new List<float>();
			void Add(float px, float pz)
			{
				x.Add(px);
				z.Add(pz);
				along.Add(along.Count);
			}
			for (float e = -60f; e < 0f; e += 4f)
			{
				Add(e, 0f);
			}
			// The out-and-back: one chord 39 m south, one 30 m back north beside it, then on east.
			Add(0f, 0f);
			Add(4.8f, -39f);
			Add(3f, -9f);
			for (float e = 7f; e <= 60f; e += 4f)
			{
				Add(e, -9f);
			}
			var widths = new float[x.Count];
			for (int i = 0; i < widths.Length; i++)
			{
				widths[i] = 5.4f;
			}
			Assert.That(MostTurn(x, z), Is.GreaterThan(170f), "the line given doubles back");
			float firstX = x[0], firstZ = z[0], lastX = x[x.Count - 1], lastZ = z[z.Count - 1];
			Assert.That(RiverShaping.EaseBends(x, z, along, widths, settings), Is.GreaterThan(0), "the reversal is cut off");
			Assert.That(new Vector4(x[0], z[0], x[x.Count - 1], z[z.Count - 1]), Is.EqualTo(new Vector4(firstX, firstZ, lastX, lastZ)), "its ends are kept");
			Assert.That(MostTurn(x, z), Is.LessThan(90f), "nowhere does the line turn back on itself");
			for (int i = 0; i < x.Count; i++)
			{
				Assert.That(z[i], Is.GreaterThan(-20f), $"point {i} at ({x[i]:0.0}, {z[i]:0.0}) still runs down the spike");
			}
			RiverShaping.Resample(x, z, along, 2f);
			Assert.That(MostTurn(x, z), Is.LessThan(45f), "resampled finer, it still turns nowhere sharply");
		}

		/// <summary>How far a line turns at point <paramref name="i"/>, degrees.</summary>
		private static float Turn(List<float> x, List<float> z, int i)
		{
			float ax = x[i] - x[i - 1], az = z[i] - z[i - 1], bx = x[i + 1] - x[i], bz = z[i + 1] - z[i];
			return Mathf.Abs(Mathf.Atan2(ax * bz - az * bx, ax * bx + az * bz)) * Mathf.Rad2Deg;
		}

		/// <summary>How many pairs of a line's segments (not neighbours) cross.</summary>
		private static int Crossings(List<float> x, List<float> z)
		{
			int crossings = 0;
			for (int i = 0; i + 1 < x.Count; i++)
			{
				for (int j = i + 2; j + 1 < x.Count; j++)
				{
					float d1 = (x[j] - x[i]) * (z[i + 1] - z[i]) - (z[j] - z[i]) * (x[i + 1] - x[i]);
					float d2 = (x[j + 1] - x[i]) * (z[i + 1] - z[i]) - (z[j + 1] - z[i]) * (x[i + 1] - x[i]);
					float d3 = (x[i] - x[j]) * (z[j + 1] - z[j]) - (z[i] - z[j]) * (x[j + 1] - x[j]);
					float d4 = (x[i + 1] - x[j]) * (z[j + 1] - z[j]) - (z[i + 1] - z[j]) * (x[j + 1] - x[j]);
					if (d1 * d2 < 0f && d3 * d4 < 0f)
					{
						crossings++;
					}
				}
			}
			return crossings;
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

		/// <summary>
		/// A river reaching the sea over a beach crest runs in at the sea's level, not off the crest: Flo Monolith's river 0
		/// held 2.1 m on its crest and dropped to −1.5 m at the shore, and was drawn as a fall into (and under) the sea. The
		/// bluff is sharp (one point) or smoothed over several, as the generator's smoothed floor has it, crossing sea level
		/// part way down: grading only the point where it first met the level left the rest for Knickpoints to gather back
		/// into a ledge.
		/// </summary>
		[TestCase(0f)]
		[TestCase(16f)]
		public void ARiverReachingTheSeaRunsInAtItsLevelNotOffALedge(float bluffMetres)
		{
			var settings = new RiverSettings();
			int n = 160, shore = 120;
			float spacing = 2.2f, seaLevel = 0f, lowWater = seaLevel - settings.IntertidalMetres;
			var floor = new float[n];
			var bank = new float[n];
			var step = new float[n];
			for (int i = 0; i < n; i++)
			{
				// A coastal plain falling gently to a 2.6 m crest, a bluff at the shore, then the sea floor shelving away.
				float s = (i - shore) * spacing;
				float plain = 2.6f - 0.01f * s, sea = -1.3f - 0.03f * s;
				float t = bluffMetres <= 0f ? (i < shore ? 0f : 1f) : Mathf.Clamp01(0.5f + s / bluffMetres);
				floor[i] = Mathf.Lerp(plain, sea, t * t * (3f - 2f * t));
				bank[i] = floor[i] + 1f;
				step[i] = i == 0 ? 0f : spacing;
			}
			RiverPath before = SeaRiver(RiverShaping.Surface(floor, bank, step, float.PositiveInfinity, lowWater, settings, 15f), spacing, settings);
			Assert.That(System.Array.IndexOf(before.Reach, RiverReach.Fall), Is.GreaterThan(0), "without a meet level the bluff is a fall into the sea");
			float[] surface = RiverShaping.Surface(floor, bank, step, float.PositiveInfinity, lowWater, settings, 15f, seaLevel);
			for (int i = 1; i < n; i++)
			{
				Assert.That(surface[i], Is.LessThanOrEqualTo(surface[i - 1] - settings.MinGradient * step[i] + 1e-4f), $"falls at {i}");
			}
			RiverPath after = SeaRiver(surface, spacing, settings);
			for (int i = 0; i < n; i++)
			{
				Assert.That(after.Reach[i], Is.Not.EqualTo(RiverReach.Fall), $"point {i}: graded into the sea, no fall");
			}
			Assert.That(surface[shore], Is.LessThanOrEqualTo(seaLevel), "in the sea at the shore");
			for (int i = 0; i < shore; i++)
			{
				Assert.That(surface[i], Is.GreaterThanOrEqualTo(lowWater - 1e-4f), $"point {i}: never cut under the low-water line inland");
			}
			Assert.That(surface[0], Is.EqualTo(RiverShaping.Surface(floor, bank, step, float.PositiveInfinity, lowWater, settings, 15f)[0]).Within(1e-4f),
				"the river above the ramp is left as it was");
		}

		/// <summary>What Flo Monolith's river 0 carries where it meets the sea, m³/s: about 5 m wide.</summary>
		/// <remarks>
		/// It was 6 m³/s, a river 8 m wide, and the bluff unmet was then no fall: Knickpoints names a point a fall only where
		/// the surface falls 0.3 per metre over a width either side of it, and the bluff's 3.9 m over the 17 m an 8 m river
		/// measures it across is 0.23. A ledge into the sea still, but named a rapid, so the check that the fixture reproduces
		/// Flo's fall failed before the meet level was ever tried (2026-10-12).
		/// </remarks>
		private const float SeaRiverDischarge = 1.3f;

		/// <summary>A straight river along those surfaces, its reaches named (knickpoints and pools included).</summary>
		private static RiverPath SeaRiver(float[] surface, float spacing, RiverSettings settings)
		{
			int n = surface.Length;
			RiverShaping.Size(SeaRiverDischarge, settings, out float width, out float depth);
			var path = new RiverPath
			{
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Bed = new float[n], Curvature = new float[n], Surface = (float[])surface.Clone(), End = RiverEnd.Sea,
			};
			for (int i = 0; i < n; i++)
			{
				path.X[i] = i * spacing;
				path.S[i] = i * spacing;
				path.Width[i] = width;
				path.Depth[i] = depth;
				path.Discharge[i] = SeaRiverDischarge;
				path.Bed[i] = surface[i] - depth;
			}
			RiverShaping.Reaches(path, settings, 7u);
			return path;
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

		/// <summary>
		/// A tributary's mouth flares onto the river it joins only as wide as the stretch of that river standing at the
		/// mouth's level: wide onto slack water, not at all onto rapids, and between in between. Never narrower than its
		/// own channel.
		/// </summary>
		[TestCase(0.001f, 2.2f, 2.2f)]
		[TestCase(0.025f, 1.25f, 1.42f)]
		[TestCase(0.08f, 1f, 1f)]
		public void ATributarysMouthFlaresNoFurtherThanTheRiverItJoinsStandsAtItsLevel(float slope, float least, float most)
		{
			// The joined river runs north along x = 0, falling at the slope; the tributary comes in from the west at its level.
			int m = 61;
			var joinedSurface = new float[m];
			for (int i = 0; i < m; i++)
			{
				joinedSurface[i] = 10f - slope * (i * 2f - 60f);
			}
			RiverPath joined = Line(joinedSurface, 2f, 20f, 6f);
			for (int i = 0; i < m; i++)
			{
				joined.Z[i] = joined.X[i] - 60f;
				joined.X[i] = 0f;
			}
			int n = 31;
			var surface = new float[n];
			for (int i = 0; i < n; i++)
			{
				surface[i] = 10f;
			}
			const float Width = 9f;
			RiverPath tributary = Line(surface, 2f, 5f, Width);
			for (int i = 0; i < n; i++)
			{
				tributary.X[i] -= 60f;
			}
			tributary.End = RiverEnd.Confluence;
			float room = SceneWater.MouthRoom(joined, 0f, 0f, 10f);
			Assert.That(room, Is.EqualTo(SceneWater.MouthLevelMetres / slope).Within(0.05f * SceneWater.MouthLevelMetres / slope + 0.01f).Or.GreaterThanOrEqualTo(60f));
			SceneWater.Flare(tributary, joined);
			float widest = 0f;
			for (int i = 0; i < n; i++)
			{
				Assert.That(tributary.Width[i], Is.GreaterThanOrEqualTo(Width - 1e-4f), $"point {i} narrower than its own channel");
				widest = Mathf.Max(widest, tributary.Width[i]);
			}
			Assert.That(widest / Width, Is.InRange(least - 0.02f, most + 0.02f), $"mouth {widest:0.00} m onto a river falling {slope} (room {room:0.00} m)");
			Assert.That(widest, Is.LessThanOrEqualTo(Mathf.Max(Width, 2f * room) + 1e-3f), "its flat water reaches no further along the river than the river stands at its level");
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
		public void NoBarSettlesOnAFallOrTheRunUpToItsLip()
		{
			var settings = new RiverSettings();
			const int n = 200;
			var surface = new float[n];
			for (int i = 0; i < n; i++)
			{
				surface[i] = 60f - 0.0009f * i - (i > 100 ? 30f : 0f);
			}
			RiverPath river = Line(surface, 3f, 3f, 10f);
			river.Reach = new RiverReach[n];
			river.Reach[101] = RiverReach.Fall;
			for (int i = 0; i < n; i++)
			{
				river.Curvature[i] = 0.02f;
			}
			RiverShaping.Bars(river, settings, out float[] point, out float[] beach, out _);
			Assert.That(point[50], Is.GreaterThan(5f), "far above the fall, the bend's point bar");
			Assert.That(point[100] + beach[100], Is.EqualTo(0f), "nothing at the lip");
			Assert.That(point[101] + beach[101], Is.EqualTo(0f), "nothing on the fall");
			Assert.That(point[95], Is.LessThan(0.5f * point[50]), "thinning out on the run-up to the brink");
			Assert.That(point[104] + beach[104], Is.EqualTo(0f), "nothing in the basin the fall scours");
			Assert.That(point[150], Is.GreaterThan(5f), "and bars again well below it");
		}

		[Test]
		public void AFallsLipIsCarvedAtItsBedNotDownTheDrop()
		{
			// A 59 m ledge: the lip 0.1 m short of a grid line, the next point 2.2 m on at the foot.
			var settings = new RiverSettings();
			int w = 120, d = 60;
			var grid = new SceneWaterGrid(w, d, 2f, -120f, -60f);
			var height = new float[w * d];
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					height[z * w + x] = grid.EastOf(x) <= 0f ? 100f : 41f;
				}
			}
			var xs = new List<float>();
			for (float p = -0.1f - 2.2f * 20f; p < 40f; p += 2.2f)
			{
				xs.Add(p);
			}
			int n = xs.Count;
			var path = new RiverPath
			{
				X = xs.ToArray(), Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = new float[n], Bed = new float[n], Curvature = new float[n], Reach = new RiverReach[n],
			};
			int lip = -1;
			for (int i = 0; i < n; i++)
			{
				path.S[i] = i * 2.2f;
				path.Discharge[i] = 3f;
				path.Width[i] = 4f;
				path.Depth[i] = 0.7f;
				bool high = xs[i] <= 0f;
				path.Surface[i] = high ? 99.5f - 0.001f * i : 40.5f - 0.001f * i;
				path.Bed[i] = path.Surface[i] - 0.7f;
				if (high)
				{
					lip = i;
				}
			}
			path.Reach[lip + 1] = RiverReach.Fall;
			WaterCarver.CarveRivers(height, grid, new[] { path }, settings, true);
			int atLip = grid.SampleAt(0f, 0f), beyond = grid.SampleAt(2f, 0f), past = grid.SampleAt(4f, 0f);
			Assert.That(height[atLip], Is.GreaterThanOrEqualTo(path.Bed[lip] - 0.05f), "the lip stands on its own bed: no hole under the water going over");
			// A cell on, under the water's arc at the brink speed (FallHydraulics: critical flow over a free overfall).
			float q = path.Discharge[lip] / path.Width[lip];
			float brinkSpeed = (float)Math.Pow(9.81 * q, 1.0 / 3.0) / 0.715f;
			float past2 = 2f - path.X[lip], fallen = 9.81f * past2 * past2 / (2f * brinkSpeed * brinkSpeed);
			Assert.That(height[beyond], Is.LessThan(path.Bed[lip] - fallen), "the ground a cell past the lip lies under the water falling off it");
			Assert.That(height[past], Is.LessThan(50f), "and the drop is still there two cells on");
		}

		[Test]
		public void APoolsWaterReachesTheRimOfItsBowlOnTheSideItWasScouredWider()
		{
			// A straight river along x, 6 m wide, its water at 10 m. On its left (+z), beside its middle, the ground lies a metre
			// under the water out to 9 m from the line, then rises to meet it 10 m out (7 m past the bank); elsewhere the banks hold.
			int n = 20;
			var path = new RiverPath
			{
				Perennial = true,
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = new float[n], Bed = new float[n], Curvature = new float[n], Reach = new RiverReach[n],
			};
			for (int i = 0; i < n; i++)
			{
				path.X[i] = i * 2f;
				path.S[i] = i * 2f;
				path.Width[i] = 6f;
				path.Depth[i] = 1.5f;
				path.Surface[i] = 10f;
				path.Bed[i] = 8.5f;
				path.Reach[i] = RiverReach.Pool;
			}
			// Bounded along the river too (x 6…32): a trench running on past both its ends would be open country.
			Func<float, float, float> bowl = (x, z) => z > 0f
				? (z < 3f ? 9f : x > 6f && x < 32f ? (z < 9f ? 9f : 9f + (z - 9f)) : 10.2f)
				: (z > -3f ? 9f : 10.2f);
			RiverShores.Measure(path, bowl, out float[] left, out float[] right);
			Assert.That(left[10], Is.EqualTo(7f).Within(0.6f), "the left water reaches the bowl's rim, 7 m past the bank");
			Assert.That(right[10], Is.EqualTo(0f), "the right bank holds it");

			// Ground under the water on the left as far as can be seen: open country, not a bowl.
			Func<float, float, float> open = (x, z) => z > 0f ? 9f : 10.2f;
			RiverShores.Measure(path, open, out left, out _);
			Assert.That(left[10], Is.EqualTo(0f), "no rim within reach spreads nothing");
		}

		[Test]
		public void APoolsBowlLyingOffItsEndIsReachedToo()
		{
			// A 6 m river along x, its water at 10 m, held by banks either side; a bowl a metre under the water lies off to
			// its left ahead of the point at x = 20, square beside no point, joined to the channel there.
			int n = 20;
			var path = new RiverPath
			{
				Perennial = true,
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = new float[n], Bed = new float[n], Curvature = new float[n], Reach = new RiverReach[n],
			};
			for (int i = 0; i < n; i++)
			{
				path.X[i] = i * 2f;
				path.S[i] = i * 2f;
				path.Width[i] = 6f;
				path.Depth[i] = 1.5f;
				path.Surface[i] = 10f;
				path.Bed[i] = 8.5f;
				path.Reach[i] = RiverReach.Pool;
			}
			Func<float, float, float> ground = (x, z) =>
			{
				if (Math.Abs(z) <= 3f)
				{
					return 8.5f;   // the channel
				}
				// The bowl: a disc 5 m across centred 6 m out to the left, a little downstream of x = 20, reaching the bank.
				float bx = x - 22f, bz = z - 6f;
				return bx * bx + bz * bz < 16f || (z > 0f && z < 4f && Math.Abs(x - 21f) < 2f) ? 9f : 10.5f;
			};
			RiverShores.Measure(path, ground, out float[] left, out float[] right);
			float most = 0f;
			for (int i = 8; i <= 13; i++)
			{
				most = Math.Max(most, left[i]);
			}
			Assert.That(most, Is.GreaterThan(5f), "the points beside the bowl are widened out to its far rim (about 7 m past the bank)");
			Assert.That(right[10], Is.EqualTo(0f), "the right bank holds");
			Assert.That(left[2], Is.EqualTo(0f), "and the left away from the bowl");
		}

		[Test]
		public void ALeveeHoldsTheWaterOutPastItsDrawnMargin()
		{
			// A 3 m stream at 100 m running across the grid at 33°, the ground from half a metre past its left bank 1.5 m under its
			// water, everywhere else well over it. The terrain between samples is their plane, so the ground is read there too.
			var settings = new RiverSettings();
			int w = 120, d = 120;
			const float spacing = 2.1f;
			var grid = new SceneWaterGrid(w, d, spacing, -126f, -126f);
			float dx = MathF.Cos(33f * MathF.PI / 180f), dz = MathF.Sin(33f * MathF.PI / 180f);
			var height = new float[w * d];
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					float left = grid.NorthOf(z) * dx - grid.EastOf(x) * dz;
					height[z * w + x] = left > 2f ? 98.5f : 102f;
				}
			}
			int n = 90;
			var path = new RiverPath
			{
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = new float[n], Bed = new float[n], Curvature = new float[n], Reach = new RiverReach[n],
			};
			for (int i = 0; i < n; i++)
			{
				float along = -99f + i * 2.2f;
				path.X[i] = along * dx;
				path.Z[i] = along * dz;
				path.S[i] = i * 2.2f;
				path.Discharge[i] = 1.5f;
				path.Width[i] = 3f;
				path.Depth[i] = 0.5f;
				path.Surface[i] = 100f;
				path.Bed[i] = 99.5f;
				path.Reach[i] = RiverReach.Run;
			}
			WaterCarver.CarveRivers(height, grid, new[] { path }, settings, true);
			float At(float east, float north)
			{
				float gx = (east + 126f) / spacing, gz = (north + 126f) / spacing;
				int x0 = (int)MathF.Floor(gx), z0 = (int)MathF.Floor(gz);
				float tx = gx - x0, tz = gz - z0;
				float h00 = height[z0 * w + x0], h10 = height[z0 * w + x0 + 1], h01 = height[(z0 + 1) * w + x0], h11 = height[(z0 + 1) * w + x0 + 1];
				// The terrain's own triangles: the cell split along its diagonal.
				return tx + tz <= 1f ? h00 + (h10 - h00) * tx + (h01 - h00) * tz : h11 + (h01 - h11) * (1f - tx) + (h10 - h11) * (1f - tz);
			}
			/* Out past the bank as far as the water's surface is drawn (InlandWaterRenderer.MinBankMarginMetres, 2.6 m): nowhere
			 * deeper under it than the shader draws as the shallow edge (a column under 0.525 m; the ground between the bed's last
			 * sample and the bank's first is their plane, so it slopes down into the water there), and at or over it by the
			 * margin's edge, so the water is closed in where its surface ends. */
			int checkedPoints = 0;
			for (float along = -70f; along <= 70f; along += 0.5f)
			{
				for (float o = 1.5f; o <= 1.5f + 2.6f; o += 0.25f)
				{
					float east = along * dx - o * dz, north = along * dz + o * dx;
					checkedPoints++;
					Assert.That(At(east, north), Is.GreaterThanOrEqualTo(100f - 0.5f), $"the ground at ({east:F1}, {north:F1}), {o - 1.5f:F2} m past the bank, lies deeper under the water than its shallow edge");
				}
				float edgeEast = along * dx - (1.5f + 2.6f) * dz, edgeNorth = along * dz + (1.5f + 2.6f) * dx;
				Assert.That(At(edgeEast, edgeNorth), Is.GreaterThanOrEqualTo(100f - 1e-3f), $"the levee at ({edgeEast:F1}, {edgeNorth:F1}), where the water's surface ends, stands under it");
			}
			Assert.That(checkedPoints, Is.GreaterThan(500));
		}

		[Test]
		public void ARapidsEdgeIsHeldToItsOwnWaterNotTheWaterUpstream()
		{
			// A 3 m stream down a 1-in-5 rapid, across the grid at 33°, cut into ground well above it.
			var settings = new RiverSettings();
			int w = 120, d = 120;
			const float spacing = 2.1f;
			var grid = new SceneWaterGrid(w, d, spacing, -126f, -126f);
			var height = new float[w * d];
			float dx = MathF.Cos(33f * MathF.PI / 180f), dz = MathF.Sin(33f * MathF.PI / 180f);
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					float along = grid.EastOf(x) * dx + grid.NorthOf(z) * dz;
					height[z * w + x] = 103f - 0.2f * along;
				}
			}
			int n = 90;
			var path = new RiverPath
			{
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = new float[n], Bed = new float[n], Curvature = new float[n], Reach = new RiverReach[n],
			};
			for (int i = 0; i < n; i++)
			{
				float along = -99f + i * 2.2f;
				path.X[i] = along * dx;
				path.Z[i] = along * dz;
				path.S[i] = i * 2.2f;
				path.Discharge[i] = 1.5f;
				path.Width[i] = 3f;
				path.Depth[i] = 0.5f;
				path.Surface[i] = 100f - 0.2f * along;
				path.Bed[i] = path.Surface[i] - 0.5f;
				path.Reach[i] = RiverReach.Rapid;
			}
			WaterCarver.CarveRivers(height, grid, new[] { path }, settings, true);
			int edges = 0;
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					float east = grid.EastOf(x), north = grid.NorthOf(z);
					float along = east * dx + north * dz, out_ = Math.Abs(north * dx - east * dz);
					if (along < -80f || along > 80f || out_ < 1.5f || out_ > 1.5f + spacing)
					{
						continue;
					}
					edges++;
					float water = 100f - 0.2f * along;
					Assert.That(height[z * w + x], Is.LessThanOrEqualTo(water + 0.05f),
						$"the edge at ({east:F1}, {north:F1}) stands {height[z * w + x] - water:F2} m over the water beside it");
				}
			}
			Assert.That(edges, Is.GreaterThan(50));
		}

		[Test]
		public void APondedStretchRaisesNoLeveeInsideItsLake()
		{
			// A river held 6 m above a flat valley floor by the water it runs on into: unponded, its levees wall it in.
			var settings = new RiverSettings();
			int w = 200, d = 100;
			var grid = new SceneWaterGrid(w, d, 2f, -200f, -100f);
			var flat = new float[w * d];
			for (int i = 0; i < flat.Length; i++)
			{
				flat[i] = 10f;
			}
			RiverPath Held()
			{
				RiverPath path = StraightRiver(-180f, 180f, 0f, flat, grid, settings);
				for (int i = 0; i < path.Count; i++)
				{
					path.Surface[i] = 16f;
					path.Bed[i] = 15f;
				}
				return path;
			}
			var walled = (float[])flat.Clone();
			WaterCarver.CarveRivers(walled, grid, new[] { Held() }, settings, true);
			Assert.That(walled.Max(), Is.GreaterThan(15f), "unponded, the levee holds the water above the floor");

			grid = new SceneWaterGrid(w, d, 2f, -200f, -100f);
			var ponded = (float[])flat.Clone();
			RiverPath inLake = Held();
			inLake.PondUntil = inLake.Count;
			WaterCarver.CarveRivers(ponded, grid, new[] { inLake }, settings, true);
			Assert.That(ponded.Max(), Is.LessThanOrEqualTo(10f + 1e-3f), "ponded, the lake holds it: no wall stands in the lake");
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
