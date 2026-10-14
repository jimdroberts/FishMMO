#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Where a scene's ground stands over its own rivers' water: stretches the terrain buries, so the water is drawn under
	/// the ground and a fall's curtain lands on a hillside with no pool and no river out of it.
	/// </summary>
	/// <remarks>
	/// Flo Monolith's cut of 2026-10-13 left the 70 m fall's plunge pool and the 100 m of river below it under a hillside
	/// 30–40 m deep, though its own hydrology put the water there and the carve, run again on those very lines over that
	/// very ground, cuts them to the bed. Nothing in the cut after the carve was found to raise the ground that far. The
	/// generator therefore measures this after every stage that writes the ground (<see cref="SceneGenerator"/>), so a cut
	/// names the stage that buried a river, and lays the water again where the shaped ground already buries it.
	/// </remarks>
	public static class RiverBurial
	{
		/// <summary>How far the ground must stand over the water's surface at a river's centre line to count as burying it, metres.</summary>
		public const float BuriedMetres = 1f;

		/// <summary>How many points in a row must be buried to count: a fall's own points stand over the face, and one point is noise.</summary>
		public const int MinRun = 3;

		/// <summary>One buried stretch of one river.</summary>
		public readonly struct Run
		{
			public readonly int River;
			public readonly int First;
			public readonly int Last;
			/// <summary>The most the ground stands over the water along it, metres.</summary>
			public readonly float Deepest;

			public Run(int river, int first, int last, float deepest)
			{
				River = river;
				First = first;
				Last = last;
				Deepest = deepest;
			}

			public int Points => Last - First + 1;

			public override string ToString() => $"river {River} points {First}–{Last} (to {Deepest:0.0} m under)";
		}

		/// <summary>
		/// The buried stretches of every perennial river: runs of at least <see cref="MinRun"/> points, falls left out, where
		/// <paramref name="groundAt"/>(east, north) stands more than <see cref="BuriedMetres"/> over the water.
		/// </summary>
		public static List<Run> Find(IReadOnlyList<RiverPath> rivers, Func<float, float, float> groundAt)
		{
			var runs = new List<Run>();
			if (rivers == null || groundAt == null)
			{
				return runs;
			}
			foreach (RiverPath river in rivers)
			{
				if (river == null || !river.Perennial || river.Count == 0)
				{
					continue;
				}
				int first = -1;
				float deepest = 0f;
				for (int i = 0; i <= river.Count; i++)
				{
					bool buried = false;
					if (i < river.Count && (river.Reach == null || river.Reach[i] != RiverReach.Fall))
					{
						float over = groundAt(river.X[i], river.Z[i]) - river.Surface[i];
						if (over > BuriedMetres)
						{
							buried = true;
							deepest = first < 0 ? over : Math.Max(deepest, over);
						}
					}
					if (buried && first < 0)
					{
						first = i;
					}
					else if (!buried && first >= 0)
					{
						if (i - first >= MinRun)
						{
							runs.Add(new Run(river.Id, first, i - 1, deepest));
						}
						first = -1;
					}
				}
			}
			return runs;
		}

		/// <summary>How far out past each bank the water's surface is drawn, metres (InlandWaterRenderer.MinBankMarginMetres): what a bank must hold.</summary>
		public const float BankHoldMetres = 2.6f;

		/// <summary>
		/// The points of every perennial river (falls and the points either side of them left out) where the ground between
		/// 1.5 m past a bank and <see cref="BankHoldMetres"/> past it lies under the water: the bank does not hold the
		/// water out to where it is drawn, so its surface ends over sunken ground. Measured after every stage that writes
		/// the ground, as the burial is, so the cut names the stage that breaks a bank.
		/// </summary>
		public static int Breaches(IReadOnlyList<RiverPath> rivers, Func<float, float, float> groundAt, List<string> worst = null)
		{
			int count = 0;
			if (rivers == null || groundAt == null)
			{
				return 0;
			}
			foreach (RiverPath river in rivers)
			{
				if (river == null || !river.Perennial || river.Count < 3)
				{
					continue;
				}
				for (int i = 1; i + 1 < river.Count; i++)
				{
					bool nearFall = false;
					for (int k = Math.Max(0, i - 2); k <= Math.Min(river.Count - 1, i + 2) && river.Reach != null; k++)
					{
						nearFall |= river.Reach[k] == RiverReach.Fall;
					}
					if (nearFall)
					{
						continue;
					}
					float dx = river.X[i + 1] - river.X[i - 1], dz = river.Z[i + 1] - river.Z[i - 1];
					float length = (float)Math.Sqrt(dx * dx + dz * dz);
					if (length < 1e-4f)
					{
						continue;
					}
					float lx = -dz / length, lz = dx / length, half = 0.5f * river.Width[i];
					bool breached = false;
					float under = 0f;
					for (int side = -1; side <= 1; side += 2)
					{
						for (float o = 1.5f; o <= BankHoldMetres + 1e-3f; o += 0.55f)
						{
							float g = groundAt(river.X[i] + lx * side * (half + o), river.Z[i] + lz * side * (half + o));
							// Past the edge's own shelf under the water (RiverSettings.EdgeShelfMetres, 5 cm).
							if (g < river.Surface[i] - 0.1f)
							{
								breached = true;
								under = Math.Max(under, river.Surface[i] - g);
							}
						}
					}
					if (breached)
					{
						count++;
						if (worst != null && worst.Count < 8 && under > 0.3f)
						{
							worst.Add($"river {river.Id} point {i} ({river.X[i]:0}, {river.Z[i]:0}) {under:0.00} m under");
						}
					}
				}
			}
			return count;
		}

		/// <summary>The points of every run together.</summary>
		public static int Points(List<Run> runs)
		{
			int sum = 0;
			foreach (Run run in runs)
			{
				sum += run.Points;
			}
			return sum;
		}

		/// <summary>A note for the cut's log: what is buried after <paramref name="stage"/>, the worst stretches first.</summary>
		public static string Describe(string stage, List<Run> runs)
		{
			var sorted = new List<Run>(runs);
			sorted.Sort((a, b) => b.Points.CompareTo(a.Points));
			var parts = new List<string>();
			for (int i = 0; i < sorted.Count && i < 6; i++)
			{
				parts.Add(sorted[i].ToString());
			}
			return $"Rivers buried after {stage}: {Points(runs)} point(s) in {runs.Count} stretch(es): {string.Join("; ", parts)}{(sorted.Count > 6 ? "; …" : string.Empty)}.";
		}
	}
}
#endif
