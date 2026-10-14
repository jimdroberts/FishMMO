using System;
using System.Collections.Generic;
using System.Diagnostics;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using Random = System.Random;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The observer registry's proximity grid. Ranking through it must produce, for every viewer, exactly the candidates
	/// a scan of every object produced, and every nearest-viewer distance the AI can read (within the declared reach)
	/// must be bit-for-bit the one the full scan measured. These tests replay the registry's own partition and query
	/// arithmetic against a brute-force scan on random layouts.
	/// </summary>
	public class ObserverProximityGridTests
	{
		private struct Thing
		{
			public Vector3 Position;
			/// <summary>The range that admits it as a candidate; 0 for an object without a distance condition.</summary>
			public float Range;
			public bool Viewer;
			public float ViewerRange;
		}

		private static List<Thing> Layout(Random random, int count, float extent)
		{
			var things = new List<Thing>(count);
			for (int i = 0; i < count; i++)
			{
				int kind = random.Next(20);
				float x = (float)(random.NextDouble() * 2.0 - 1.0) * extent;
				float z = (float)(random.NextDouble() * 2.0 - 1.0) * extent;
				if (random.Next(10) == 0)
				{
					// Clumps, so cells hold many and ranges overlap.
					x = Mathf.Round(x / 50f) * 50f + (float)random.NextDouble();
					z = Mathf.Round(z / 50f) * 50f + (float)random.NextDouble();
				}
				things.Add(new Thing
				{
					Position = new Vector3(x, (float)(random.NextDouble() * 200.0 - 100.0), z),
					Range = kind == 0 ? 1000f : kind == 1 ? 0f : kind < 5 ? 40f : 100f,
					Viewer = random.Next(4) == 0,
					ViewerRange = random.Next(5) == 0 ? 40f : 100f,
				});
			}
			return things;
		}

		/// <summary>The registry's admitting range for a pair.</summary>
		private static float Admitting(Thing observed, Thing viewer) => observed.Range > 0f ? observed.Range : viewer.ViewerRange;

		private sealed class Result
		{
			public readonly Dictionary<int, HashSet<int>> Candidates = new Dictionary<int, HashSet<int>>();
			public readonly float[] Nearest;

			public Result(int count)
			{
				Nearest = new float[count];
				for (int i = 0; i < count; i++)
				{
					Nearest[i] = float.PositiveInfinity;
				}
			}
		}

		/// <summary>The old pass: every viewer against every object.</summary>
		private static Result BruteForce(List<Thing> things)
		{
			var result = new Result(things.Count);
			for (int v = 0; v < things.Count; v++)
			{
				if (!things[v].Viewer)
				{
					continue;
				}
				var candidates = new HashSet<int>();
				for (int o = 0; o < things.Count; o++)
				{
					if (o == v)
					{
						continue;
					}
					float distance = Vector3.Distance(things[v].Position, things[o].Position);
					if (distance < result.Nearest[o])
					{
						result.Nearest[o] = distance;
					}
					if (distance <= Admitting(things[o], things[v]))
					{
						candidates.Add(o);
					}
				}
				result.Candidates[v] = candidates;
			}
			return result;
		}

		/// <summary>The new pass, as the registry does it: wide objects scanned, the rest through the grid.</summary>
		private static Result Grid(List<Thing> things, float reach, ObserverProximityGrid grid)
		{
			var result = new Result(things.Count);
			var gridIndex = new List<int>();
			var positions = new List<Vector3>();
			var wide = new List<int>();
			float maxRange = 0f;
			bool unranged = false;
			for (int i = 0; i < things.Count; i++)
			{
				if (things[i].Range > ObserverStreamingPolicy.WideRangeThreshold)
				{
					wide.Add(i);
					continue;
				}
				gridIndex.Add(i);
				positions.Add(things[i].Position);
				if (things[i].Range > 0f)
				{
					maxRange = Mathf.Max(maxRange, things[i].Range);
				}
				else
				{
					unranged = true;
				}
			}
			grid.Build(positions, Mathf.Max(16f, maxRange));
			var nearby = new List<int>();
			for (int v = 0; v < things.Count; v++)
			{
				if (!things[v].Viewer)
				{
					continue;
				}
				var candidates = new HashSet<int>();
				float query = Mathf.Max(maxRange, unranged ? things[v].ViewerRange : 0f);
				grid.Query(things[v].Position, Mathf.Max(query, reach), nearby);
				var seen = new HashSet<int>();
				void Consider(int o)
				{
					Assert.IsTrue(seen.Add(o), "an object is offered to a viewer once");
					if (o == v)
					{
						return;
					}
					float distance = Vector3.Distance(things[v].Position, things[o].Position);
					if (distance < result.Nearest[o])
					{
						result.Nearest[o] = distance;
					}
					if (distance <= Admitting(things[o], things[v]))
					{
						candidates.Add(o);
					}
				}
				foreach (int n in nearby)
				{
					Consider(gridIndex[n]);
				}
				foreach (int w in wide)
				{
					Consider(w);
				}
				result.Candidates[v] = candidates;
			}
			return result;
		}

		[Test]
		public void GridCandidacy_EqualsBruteForce_AndNearestIsExactWithinReach()
		{
			var grid = new ObserverProximityGrid();
			for (int seed = 1; seed <= 12; seed++)
			{
				var random = new Random(seed);
				List<Thing> things = Layout(random, 300 + seed * 40, seed % 3 == 0 ? 300f : 1500f);
				// The AI's default far band (300 m) as the registry pads it, and a short one.
				float reach = seed % 2 == 0 ? Mathf.Sqrt(90000f) * 1.001f + 1f : 50f;
				Result expected = BruteForce(things);
				Result actual = Grid(things, reach, grid);

				foreach (KeyValuePair<int, HashSet<int>> viewer in expected.Candidates)
				{
					Assert.IsTrue(viewer.Value.SetEquals(actual.Candidates[viewer.Key]), $"seed {seed}: viewer {viewer.Key}'s candidates differ");
				}
				for (int i = 0; i < things.Count; i++)
				{
					if (expected.Nearest[i] <= reach)
					{
						Assert.AreEqual(expected.Nearest[i], actual.Nearest[i], $"seed {seed}: object {i}'s nearest viewer within reach");
					}
					else
					{
						Assert.Greater(actual.Nearest[i], reach, $"seed {seed}: object {i} beyond reach still reads as beyond it");
					}
				}
			}
		}

		[Test]
		public void InfiniteReach_MeasuresEveryDistanceExactly()
		{
			var random = new Random(77);
			List<Thing> things = Layout(random, 400, 3000f);
			Result expected = BruteForce(things);
			Result actual = Grid(things, float.PositiveInfinity, new ObserverProximityGrid());
			for (int i = 0; i < things.Count; i++)
			{
				Assert.AreEqual(expected.Nearest[i], actual.Nearest[i]);
			}
		}

		[Test]
		public void Query_IsASupersetOfEveryPointWithinTheRadius()
		{
			var random = new Random(4);
			var positions = new List<Vector3>();
			for (int i = 0; i < 2000; i++)
			{
				positions.Add(new Vector3((float)(random.NextDouble() - 0.5) * 2000f, (float)(random.NextDouble() - 0.5) * 50f, (float)(random.NextDouble() - 0.5) * 2000f));
			}
			var grid = new ObserverProximityGrid();
			grid.Build(positions, 100f);
			var found = new List<int>();
			for (int q = 0; q < 500; q++)
			{
				Vector3 at = positions[random.Next(positions.Count)] + new Vector3((float)random.NextDouble() * 10f, 0f, 0f);
				float radius = (float)random.NextDouble() * 400f;
				grid.Query(at, radius, found);
				var set = new HashSet<int>(found);
				Assert.AreEqual(found.Count, set.Count, "no duplicates");
				for (int i = 0; i < positions.Count; i++)
				{
					if (Vector3.Distance(at, positions[i]) <= radius)
					{
						Assert.IsTrue(set.Contains(i));
					}
				}
			}
		}

		[Test]
		public void SiteEmitterIndex_IsASupersetOfEveryEmitterWithinReach()
		{
			var random = new Random(8);
			var emitters = new List<SceneEmitter>();
			for (int i = 0; i < 3000; i++)
			{
				emitters.Add(new SceneEmitter { Position = new Vector3((float)(random.NextDouble() - 0.5) * 3000f, (float)random.NextDouble() * 80f, (float)(random.NextDouble() - 0.5) * 3000f) });
			}
			var index = new SceneSiteEmitters.EmitterIndex(emitters);
			var found = new List<int>();
			int answered = 0;
			for (int q = 0; q < 300; q++)
			{
				Vector3 at = emitters[random.Next(emitters.Count)].Position;
				float reach = 78f;
				if (!index.Query(at, reach, found))
				{
					continue;
				}
				answered++;
				var set = new HashSet<int>(found);
				for (int i = 0; i < emitters.Count; i++)
				{
					if ((emitters[i].Position - at).sqrMagnitude <= reach * reach)
					{
						Assert.IsTrue(set.Contains(i));
					}
				}
			}
			Assert.Greater(answered, 0, "the index answers ordinary queries itself");
		}

		/// <summary>One ranking pass's pair loop, before and after: 200 viewers among 700 objects over a few kilometres.</summary>
		[Test]
		public void Benchmark_PairLoop_BruteForceVersusGrid()
		{
			var random = new Random(21);
			List<Thing> things = Layout(random, 900, 2000f);
			int viewers = 0;
			for (int i = 0; i < things.Count; i++)
			{
				viewers += things[i].Viewer ? 1 : 0;
			}
			const int passes = 40;
			float reach = Mathf.Sqrt(90000f) * 1.001f + 1f;
			var grid = new ObserverProximityGrid();
			BruteForce(things);
			Grid(things, reach, grid);

			double sink = 0;
			Stopwatch old = Stopwatch.StartNew();
			for (int p = 0; p < passes; p++)
			{
				for (int v = 0; v < things.Count; v++)
				{
					if (!things[v].Viewer)
					{
						continue;
					}
					for (int o = 0; o < things.Count; o++)
					{
						float distance = Vector3.Distance(things[v].Position, things[o].Position);
						if (distance <= Admitting(things[o], things[v]))
						{
							sink += distance;
						}
					}
				}
			}
			old.Stop();

			var positions = new List<Vector3>();
			var gridIndex = new List<int>();
			var wide = new List<int>();
			var nearby = new List<int>();
			Stopwatch now = Stopwatch.StartNew();
			for (int p = 0; p < passes; p++)
			{
				positions.Clear();
				gridIndex.Clear();
				wide.Clear();
				for (int i = 0; i < things.Count; i++)
				{
					if (things[i].Range > ObserverStreamingPolicy.WideRangeThreshold)
					{
						wide.Add(i);
						continue;
					}
					gridIndex.Add(i);
					positions.Add(things[i].Position);
				}
				grid.Build(positions, 100f);
				for (int v = 0; v < things.Count; v++)
				{
					if (!things[v].Viewer)
					{
						continue;
					}
					grid.Query(things[v].Position, reach, nearby);
					for (int n = 0; n < nearby.Count; n++)
					{
						int o = gridIndex[nearby[n]];
						float distance = Vector3.Distance(things[v].Position, things[o].Position);
						if (distance <= Admitting(things[o], things[v]))
						{
							sink += distance;
						}
					}
					for (int w = 0; w < wide.Count; w++)
					{
						float distance = Vector3.Distance(things[v].Position, things[wide[w]].Position);
						if (distance <= Admitting(things[wide[w]], things[v]))
						{
							sink += distance;
						}
					}
				}
			}
			now.Stop();
			double oldMs = old.Elapsed.TotalMilliseconds / passes, newMs = now.Elapsed.TotalMilliseconds / passes;
			Console.WriteLine($"[ObserverProximityGrid] {viewers} viewers x {things.Count} objects: full scan {oldMs:F3} ms/pass, grid {newMs:F3} ms/pass ({oldMs / Math.Max(newMs, 1e-6):F1}x) [{sink:F0}]");
			Assert.Less(newMs, oldMs);
		}
	}
}
