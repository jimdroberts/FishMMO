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
	/// The prop collider streamer's chunk bookkeeping. Holding chunks by cell, counted, with late release, must keep
	/// collision everywhere the old per-tick recompute round every focus's exact position did (a superset at every step),
	/// and the counts must always equal a recount from scratch.
	/// </summary>
	public class PropChunkLedgerTests
	{
		private const float Cell = PropColliderStreamer.CellMetres;

		/// <summary>A random scene: about half the chunks of a square have props.</summary>
		private static HashSet<long> PropCells(Random random, int halfSpan, double density)
		{
			var cells = new HashSet<long>();
			for (int z = -halfSpan; z < halfSpan; z++)
			{
				for (int x = -halfSpan; x < halfSpan; x++)
				{
					if (random.NextDouble() < density)
					{
						cells.Add(PropChunkLedger.Key(x, z));
					}
				}
			}
			return cells;
		}

		/// <summary>A coordinate, now and then exactly on a chunk edge or a hair either side of one.</summary>
		private static float Coordinate(Random random, float extent)
		{
			float value = (float)(random.NextDouble() * 2.0 - 1.0) * extent;
			switch (random.Next(8))
			{
				case 0: return Mathf.Round(value / Cell) * Cell;
				case 1: return BitDecrement(Mathf.Round(value / Cell) * Cell);
				case 2: return BitIncrement(Mathf.Round(value / Cell) * Cell);
				default: return value;
			}
		}

		private static float BitIncrement(float value) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) + (value >= 0f ? 1 : -1));

		private static float BitDecrement(float value) => value == 0f ? -float.Epsilon : BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) - (value > 0f ? 1 : -1));

		/// <summary>What the streamer wanted before: chunks with props within each focus's radius of its exact position.</summary>
		private static HashSet<long> OldWanted(HashSet<long> props, List<(Vector3 at, float radius)> foci)
		{
			var wanted = new HashSet<long>();
			var cells = new List<(long, float)>();
			foreach ((Vector3 at, float radius) in foci)
			{
				PropChunkLedger.PointCells(at, radius, Cell, cells);
				foreach ((long cell, float _) in cells)
				{
					if (props.Contains(cell))
					{
						wanted.Add(cell);
					}
				}
			}
			return wanted;
		}

		private static void Walk(int seed, float margin, Action<PropChunkLedger, PropChunkLedger.Hold[], List<(Vector3 at, float radius)>> check)
		{
			var random = new Random(seed);
			HashSet<long> props = PropCells(random, 40, 0.5);
			var ledger = new PropChunkLedger(Cell, props);
			float[] radii = { 40f, 96f, 128f, 100f };
			int count = 12;
			var holds = new PropChunkLedger.Hold[count];
			var foci = new List<(Vector3 at, float radius)>();
			for (int f = 0; f < count; f++)
			{
				holds[f] = new PropChunkLedger.Hold();
				foci.Add((new Vector3(Coordinate(random, 1000f), 0f, Coordinate(random, 1000f)), radii[f % radii.Length]));
			}
			for (int step = 0; step < 400; step++)
			{
				for (int f = 0; f < count; f++)
				{
					(Vector3 at, float radius) = foci[f];
					int move = random.Next(20);
					if (move == 0)
					{
						at = new Vector3(Coordinate(random, 1200f), 0f, Coordinate(random, 1200f));
					}
					else if (move < 12)
					{
						at += new Vector3((float)(random.NextDouble() - 0.5) * 24f, 0f, (float)(random.NextDouble() - 0.5) * 24f);
					}
					if (random.Next(50) == 0)
					{
						radius = radii[random.Next(radii.Length)];
					}
					foci[f] = (at, radius);
					ledger.Place(holds[f], at, radius, margin);
				}
				check(ledger, holds, foci);
				ledger.Acquired.Clear();
				ledger.Released.Clear();
			}
		}

		[Test]
		public void HeldChunks_AreASupersetOfThePerPointRecompute_AtEveryStep()
		{
			for (int seed = 1; seed <= 6; seed++)
			{
				Walk(seed, Cell, (ledger, holds, foci) =>
				{
					foreach (long cell in OldWanted(ledger.PropCells, foci))
					{
						Assert.IsTrue(ledger.IsWanted(cell), $"seed {seed}: a chunk the old recompute wanted was not held");
					}
				});
			}
		}

		[Test]
		public void ReferenceCounts_EqualARecountOfTheHolds()
		{
			Walk(11, Cell, (ledger, holds, foci) =>
			{
				var recount = new Dictionary<long, int>();
				foreach (PropChunkLedger.Hold hold in holds)
				{
					foreach (long cell in hold.Cells)
					{
						recount.TryGetValue(cell, out int n);
						recount[cell] = n + 1;
					}
				}
				Assert.AreEqual(recount.Count, ledger.WantedCount);
				foreach (KeyValuePair<long, int> pair in recount)
				{
					Assert.AreEqual(pair.Value, ledger.References(pair.Key));
				}
			});
		}

		[Test]
		public void AHold_KeepsItsCellRadius_AndNothingBeyondTheReleaseBand()
		{
			Walk(23, Cell, (ledger, holds, foci) =>
			{
				var near = new List<long>();
				for (int f = 0; f < holds.Length; f++)
				{
					PropChunkLedger.Hold hold = holds[f];
					ledger.CellsNear(hold.X, hold.Z, hold.Radius, near);
					foreach (long cell in near)
					{
						Assert.IsTrue(hold.Cells.Contains(cell), "every chunk within the radius of the cell is held");
					}
					foreach (long cell in hold.Cells)
					{
						PropChunkLedger.Unkey(cell, out int x, out int z);
						Assert.LessOrEqual(PropChunkLedger.CellGap(x - hold.X, z - hold.Z, Cell), hold.Radius + Cell, "nothing is held past the release band");
					}
				}
			});
		}

		/// <summary>With no release margin, the incremental counts are exactly a full recompute of the cell rule.</summary>
		[Test]
		public void WithoutAMargin_TheIncrementalSet_EqualsAFullRecomputeByCell()
		{
			Walk(37, 0f, (ledger, holds, foci) =>
			{
				var fresh = new PropChunkLedger(Cell, ledger.PropCells);
				for (int f = 0; f < foci.Count; f++)
				{
					fresh.Place(new PropChunkLedger.Hold(), foci[f].at, foci[f].radius, 0f);
				}
				Assert.AreEqual(fresh.WantedCount, ledger.WantedCount);
				foreach (long cell in fresh.Wanted)
				{
					Assert.IsTrue(ledger.IsWanted(cell));
				}
			});
		}

		/// <summary>The immediate pass may skip a settled focus: its cell's near chunks cover every point in the cell.</summary>
		[Test]
		public void CellsNear_CoversThePointRule_ForEveryPointInTheCell()
		{
			var random = new Random(5);
			HashSet<long> props = PropCells(random, 10, 1.0);
			var ledger = new PropChunkLedger(Cell, props);
			var near = new List<long>();
			var point = new List<(long, float)>();
			for (int i = 0; i < 4000; i++)
			{
				var at = new Vector3(Coordinate(random, 150f), 0f, Coordinate(random, 150f));
				float radius = random.Next(2) == 0 ? 40f : (float)random.NextDouble() * 130f;
				ledger.CellsNear(Mathf.FloorToInt(at.x / Cell), Mathf.FloorToInt(at.z / Cell), radius, near);
				var set = new HashSet<long>(near);
				PropChunkLedger.PointCells(at, radius, Cell, point);
				foreach ((long cell, float _) in point)
				{
					if (props.Contains(cell))
					{
						Assert.IsTrue(set.Contains(cell), $"point {at.x},{at.z} r {radius}");
					}
				}
			}
		}

		/// <summary>
		/// The play-mode streamer case: props one per chunk along x, a 100 m focus at the first, then moved 480 m. Every
		/// chunk it left is released on that one placement, and only the chunks round the new cell are held.
		/// </summary>
		[Test]
		public void AFarMove_ReleasesEveryChunkLeftBehind_OnThatPlacement()
		{
			var props = new HashSet<long>();
			for (int x = 0; x < 32; x++)
			{
				props.Add(PropChunkLedger.Key(x, 0));
			}
			var ledger = new PropChunkLedger(Cell, props);
			var hold = new PropChunkLedger.Hold();
			Assert.IsTrue(ledger.Place(hold, new Vector3(16f, 0f, 16f), 100f, Cell));
			var first = new HashSet<long>(ledger.Wanted);
			Assert.IsTrue(first.Contains(PropChunkLedger.Key(0, 0)));
			Assert.AreEqual(5, first.Count, "cells 0-4: within 100 m of chunk 0's square");
			ledger.Acquired.Clear();
			ledger.Released.Clear();

			Assert.IsTrue(ledger.Place(hold, new Vector3(496f, 0f, 16f), 100f, Cell), "a new cell is a new placement");
			Assert.IsFalse(ledger.IsWanted(PropChunkLedger.Key(0, 0)), "the chunk at the old spot is no longer wanted");
			CollectionAssert.AreEquivalent(first, ledger.Released, "every chunk left behind is released at once");
			for (int x = 0; x < 32; x++)
			{
				Assert.AreEqual(x >= 11 && x <= 19, ledger.IsWanted(PropChunkLedger.Key(x, 0)), $"chunk {x}");
			}
		}

		[Test]
		public void Drop_LetsGoOfEverything()
		{
			var random = new Random(3);
			var ledger = new PropChunkLedger(Cell, PropCells(random, 20, 0.7));
			var a = new PropChunkLedger.Hold();
			var b = new PropChunkLedger.Hold();
			ledger.Place(a, new Vector3(10f, 0f, 10f), 96f, Cell);
			ledger.Place(b, new Vector3(40f, 0f, 10f), 96f, Cell);
			int shared = 0;
			foreach (long cell in a.Cells)
			{
				shared += b.Cells.Contains(cell) ? 1 : 0;
			}
			Assert.Greater(shared, 0);
			ledger.Released.Clear();
			ledger.Drop(a);
			Assert.AreEqual(b.Cells.Count, ledger.WantedCount, "what both held stays wanted for the other");
			ledger.Drop(b);
			Assert.AreEqual(0, ledger.WantedCount);
			Assert.IsFalse(a.Placed);
		}

		/// <summary>
		/// Timing of one 0.1 s tick, before and after: the old tick recomputed every focus's chunks twice (the immediate
		/// pass and the full one, each a hash per chunk) and scanned every chunk; the new one places each focus, which
		/// is a compare unless it crossed a cell. Characters walk at 5 m/s.
		/// </summary>
		[Test]
		public void Benchmark_TickCost_OldRecomputeVersusLedger()
		{
			var random = new Random(9);
			HashSet<long> props = PropCells(random, 64, 0.6);
			const int count = 400;
			const int ticks = 300;
			var positions = new Vector3[count];
			var headings = new Vector3[count];
			for (int f = 0; f < count; f++)
			{
				positions[f] = new Vector3((float)(random.NextDouble() - 0.5) * 3000f, 0f, (float)(random.NextDouble() - 0.5) * 3000f);
				float angle = (float)(random.NextDouble() * Math.PI * 2.0);
				headings[f] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.5f;
			}

			// Old: two full recomputes per tick plus the release scan over every chunk.
			var wanted = new HashSet<long>();
			var chunks = new HashSet<long>();
			var scratch = new List<long>();
			var cells = new List<(long, float)>();
			Vector3[] at = (Vector3[])positions.Clone();
			Stopwatch old = Stopwatch.StartNew();
			for (int t = 0; t < ticks; t++)
			{
				for (int f = 0; f < count; f++)
				{
					at[f] += headings[f];
				}
				foreach (float reach in new[] { 40f, -1f })
				{
					wanted.Clear();
					for (int f = 0; f < count; f++)
					{
						PropChunkLedger.PointCells(at[f], reach >= 0f ? reach : 96f, Cell, cells);
						foreach ((long cell, float _) in cells)
						{
							if (props.Contains(cell) && wanted.Add(cell) && !chunks.Contains(cell))
							{
								chunks.Add(cell);
							}
						}
					}
					if (reach < 0f)
					{
						scratch.Clear();
						foreach (long cell in chunks)
						{
							if (!wanted.Contains(cell))
							{
								scratch.Add(cell);
							}
						}
						foreach (long cell in scratch)
						{
							chunks.Remove(cell);
						}
					}
				}
			}
			old.Stop();

			var ledger = new PropChunkLedger(Cell, props);
			var holds = new PropChunkLedger.Hold[count];
			for (int f = 0; f < count; f++)
			{
				holds[f] = new PropChunkLedger.Hold();
			}
			at = (Vector3[])positions.Clone();
			var near = new List<long>();
			Stopwatch now = Stopwatch.StartNew();
			for (int t = 0; t < ticks; t++)
			{
				for (int f = 0; f < count; f++)
				{
					at[f] += headings[f];
					if (ledger.Place(holds[f], at[f], 96f, Cell))
					{
						// A focus that crossed a cell is checked by the immediate pass once.
						ledger.CellsNear(holds[f].X, holds[f].Z, 40f, near);
					}
				}
				ledger.Acquired.Clear();
				ledger.Released.Clear();
			}
			now.Stop();

			double oldMs = old.Elapsed.TotalMilliseconds / ticks, newMs = now.Elapsed.TotalMilliseconds / ticks;
			Console.WriteLine($"[PropChunkLedger] {count} foci: old tick {oldMs:F3} ms, ledger tick {newMs:F3} ms ({oldMs / Math.Max(newMs, 1e-6):F1}x); held {ledger.WantedCount} chunks vs {chunks.Count} before");
			Assert.Less(newMs, oldMs);
		}
	}
}
