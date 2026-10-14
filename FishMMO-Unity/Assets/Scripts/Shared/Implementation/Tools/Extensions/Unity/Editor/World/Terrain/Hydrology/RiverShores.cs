#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Where each river's water meets the finished ground on either side, past its channel's width: what the water is drawn,
	/// swum and stood in out to.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> A river is drawn and asked about from its line and one width per point, laid evenly either side. A plunge
	/// pool's bowl is scoured wider than that, and not evenly: below the 70 m fall on Flo Monolith the ground on one side
	/// stood 1.4–2 m under the pool out to 11–13 m from the line, where the water was drawn to 10.4 m, so the pool's surface
	/// ended in a straight edge over sunken ground (2026-10-13). The carve cannot raise that ground without a shelf round the
	/// pool, and the bowl is the pool: the water reaches to its rim.
	/// </para>
	/// <para>
	/// <b>How.</b> Measured once the ground is finished (the sites, paths, paint and talus all move it). The water is flooded
	/// out from its channel over the ground, each cell taking the level of the channel nearest it, into every cell under that
	/// level, as far as <see cref="MaxMetres"/>; each flooded cell then widens the side of the point it lies beside. Walked
	/// straight out from each point instead, a bowl's lobe lying off a pool's end, beside no point square on, was never seen
	/// and its water never drawn (2026-10-13 coverage map, RiverCoverageProbe).
	/// </para>
	/// <para>
	/// Only a bowl that closes: water still spreading at <see cref="MaxMetres"/> is open country (a lake's flat, a breached
	/// bank) and widens nothing. The shallow fringe every bank has, which the drawn margin already covers, widens nothing.
	/// Nothing floods from a fall's falling water, and nothing from above a lip into the ground round its drop: that water
	/// goes over the lip as the curtain. The pool under it floods its bowl as any pool does.
	/// </para>
	/// <para>Pure arithmetic: no Unity object is touched.</para>
	/// </remarks>
	public static class RiverShores
	{
		/// <summary>The furthest the water is carried past a bank, metres.</summary>
		public const float MaxMetres = 16f;
		/// <summary>
		/// The flood's cells, metres: half a ground sample or finer. A hollow behind a bank flush with the water is joined to
		/// the channel through a strip under a metre wide, which metre cells stepped over and never flooded.
		/// </summary>
		public const float CellMetres = 0.5f;
		/// <summary>How far under the water the ground must lie to be under it, metres.</summary>
		public const float UnderMetres = 0.02f;
		/// <summary>
		/// What the river's drawn margin already covers past its banks: water no deeper than this, metres, out to
		/// <see cref="MarginCoversMetres"/> (InlandWaterRenderer draws shallow water there to its shore).
		/// </summary>
		public const float MarginCoversDepthMetres = 0.35f;
		public const float MarginCoversMetres = 2f;
		/// <summary>How far round a fall's drop, past its half-width, nothing floods, metres.</summary>
		public const float FallClearMetres = 8f;

		/// <summary>
		/// Per point of <paramref name="river"/>, how far past its half-width its water reaches on its left (looking downstream)
		/// and on its right, metres; zero where the bank holds it. Null for a dry wash.
		/// </summary>
		/// <param name="ground">The finished ground at (east, north), scene metres.</param>
		public static void Measure(RiverPath river, Func<float, float, float> ground, out float[] left, out float[] right)
		{
			left = null;
			right = null;
			int n = river != null ? river.Count : 0;
			if (n < 2 || ground == null || !river.Perennial)
			{
				return;
			}
			List<(int lip, int foot)> falls = Falls(river);

			// The cells: over the river's points, out past its widest half and the furthest the water is carried.
			float widest = 0f;
			for (int i = 0; i < n; i++)
			{
				widest = Math.Max(widest, 0.5f * river.Width[i]);
			}
			float pad = widest + MaxMetres + 2f * CellMetres;
			float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
			for (int i = 0; i < n; i++)
			{
				minX = Math.Min(minX, river.X[i]); maxX = Math.Max(maxX, river.X[i]);
				minZ = Math.Min(minZ, river.Z[i]); maxZ = Math.Max(maxZ, river.Z[i]);
			}
			float x0 = minX - pad, z0 = minZ - pad;
			int w = (int)Math.Ceiling((maxX - minX + 2f * pad) / CellMetres), d = (int)Math.Ceiling((maxZ - minZ + 2f * pad) / CellMetres);
			if ((long)w * d > 80_000_000L)
			{
				return;
			}
			var level = new float[w * d];
			var travelled = new float[w * d];
			var source = new int[w * d];
			var groundAt = new float[w * d];
			var seedOffset = new float[w * d];
			for (int i = 0; i < level.Length; i++)
			{
				level[i] = float.NaN;
				groundAt[i] = float.NaN;
				seedOffset[i] = float.PositiveInfinity;
			}
			float Ground(int cell)
			{
				if (float.IsNaN(groundAt[cell]))
				{
					groundAt[cell] = ground(x0 + (cell % w + 0.5f) * CellMetres, z0 + (cell / w + 0.5f) * CellMetres);
				}
				return groundAt[cell];
			}

			// The seeds: every cell inside the channel, at the water's level there, its nearest point its source.
			var queue = new Queue<int>();
			// Per cell, the fall (an index into falls) whose drop it lies round; −1 for none.
			var fallZone = new int[w * d];
			for (int c = 0; c < fallZone.Length; c++)
			{
				fallZone[c] = -1;
			}
			for (int f = 0; f < falls.Count; f++)
			{
				(int lip, int foot) = falls[f];
				for (int v = lip; v < foot; v++)
				{
					MarkZone(fallZone, f, river.X[v], river.Z[v], river.X[v + 1], river.Z[v + 1], 0.5f * Math.Max(river.Width[v], river.Width[v + 1]) + FallClearMetres, x0, z0, w, d);
				}
			}
			for (int v = 0; v + 1 < n; v++)
			{
				float ax = river.X[v], az = river.Z[v], bx = river.X[v + 1], bz = river.Z[v + 1];
				float ex = bx - ax, ez = bz - az, lengthSq = Math.Max(1e-6f, ex * ex + ez * ez);
				float reach = Math.Max(river.Width[v], river.Width[v + 1]) * 0.5f;
				// The falling water seeds nothing: from the lip to the foot.
				if (river.Reach != null && (river.Reach[v + 1] == RiverReach.Fall || river.Reach[v] == RiverReach.Fall))
				{
					continue;
				}
				int ix0 = Math.Max(0, (int)((Math.Min(ax, bx) - reach - x0) / CellMetres)), ix1 = Math.Min(w - 1, (int)((Math.Max(ax, bx) + reach - x0) / CellMetres));
				int iz0 = Math.Max(0, (int)((Math.Min(az, bz) - reach - z0) / CellMetres)), iz1 = Math.Min(d - 1, (int)((Math.Max(az, bz) + reach - z0) / CellMetres));
				for (int z = iz0; z <= iz1; z++)
				{
					for (int x = ix0; x <= ix1; x++)
					{
						float px = x0 + (x + 0.5f) * CellMetres, pz = z0 + (z + 0.5f) * CellMetres;
						float t = Math.Max(0f, Math.Min(1f, ((px - ax) * ex + (pz - az) * ez) / lengthSq));
						float cx = ax + ex * t - px, cz = az + ez * t - pz;
						float distance = (float)Math.Sqrt(cx * cx + cz * cz);
						int c = z * w + x;
						float half = 0.5f * (river.Width[v] + (river.Width[v + 1] - river.Width[v]) * t);
						if (distance <= half && distance < seedOffset[c])
						{
							seedOffset[c] = distance;
							level[c] = river.Surface[v] + (river.Surface[v + 1] - river.Surface[v]) * t;
							source[c] = t < 0.5f ? v : v + 1;
							travelled[c] = 0f;
						}
					}
				}
			}
			for (int c = 0; c < level.Length; c++)
			{
				if (!float.IsNaN(level[c]))
				{
					queue.Enqueue(c);
				}
			}

			// The flood: each cell the level of the nearest channel (every seed starts at once), into ground under it.
			var flooded = new List<int>();
			while (queue.Count > 0)
			{
				int c = queue.Dequeue();
				int x = c % w, z = c / w;
				for (int k = 0; k < 4; k++)
				{
					int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
					if (nx < 0 || nz < 0 || nx >= w || nz >= d)
					{
						continue;
					}
					int j = nz * w + nx;
					if (!float.IsNaN(level[j]) || travelled[c] + CellMetres > MaxMetres)
					{
						continue;
					}
					// From above a fall's lip, not into the ground round its drop.
					if (fallZone[j] >= 0 && source[c] <= falls[fallZone[j]].lip)
					{
						continue;
					}
					float g = Ground(j);
					if (float.IsNaN(g) || g >= level[c] - UnderMetres)
					{
						continue;
					}
					level[j] = level[c];
					source[j] = source[c];
					travelled[j] = travelled[c] + CellMetres;
					flooded.Add(j);
					queue.Enqueue(j);
				}
			}

			/* Open country, as a whole: every flooded cell joined to one the flood was still spreading from at its furthest. Taken
			 * point by point instead, the points beside an open hollow whose own water fell short of the furthest still widened,
			 * and drew part of the hollow as a pane of water standing on the hillside (2026-10-13). */
			var open = new bool[w * d];
			var stack = new Stack<int>();
			foreach (int c in flooded)
			{
				if (open[c] || travelled[c] < MaxMetres - CellMetres)
				{
					continue;
				}
				open[c] = true;
				stack.Push(c);
				while (stack.Count > 0)
				{
					int o = stack.Pop();
					int ox = o % w, oz = o / w;
					for (int k = 0; k < 4; k++)
					{
						int nx = ox + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = oz + (k == 2 ? 1 : k == 3 ? -1 : 0);
						if (nx < 0 || nz < 0 || nx >= w || nz >= d)
						{
							continue;
						}
						int j = nz * w + nx;
						if (!open[j] && travelled[j] > 0f && !float.IsNaN(level[j]))
						{
							open[j] = true;
							stack.Push(j);
						}
					}
				}
			}

			// Each flooded cell widens the side of the point it lies beside, unless it is the shallow fringe or open country.
			var rawLeft = new float[n];
			var rawRight = new float[n];
			foreach (int c in flooded)
			{
				if (open[c])
				{
					continue;
				}
				float px = x0 + (c % w + 0.5f) * CellMetres, pz = z0 + (c / w + 0.5f) * CellMetres;
				int best = -1;
				float bestDistance = float.MaxValue, bestSide = 0f, bestT = 0f;
				for (int v = Math.Max(0, source[c] - 4); v < Math.Min(n - 1, source[c] + 4); v++)
				{
					float ax = river.X[v], az = river.Z[v], ex = river.X[v + 1] - ax, ez = river.Z[v + 1] - az;
					float lengthSq = Math.Max(1e-6f, ex * ex + ez * ez);
					float t = Math.Max(0f, Math.Min(1f, ((px - ax) * ex + (pz - az) * ez) / lengthSq));
					float cx = px - (ax + ex * t), cz = pz - (az + ez * t);
					float distance = (float)Math.Sqrt(cx * cx + cz * cz);
					if (distance < bestDistance)
					{
						bestDistance = distance;
						best = v;
						bestT = t;
						bestSide = ex * (pz - az) - ez * (px - ax);
					}
				}
				if (best < 0)
				{
					continue;
				}
				int p = bestT < 0.5f ? best : best + 1;
				float outside = bestDistance - 0.5f * river.Width[p];
				float depth = level[c] - Ground(c);
				if (outside <= 0f || (outside <= MarginCoversMetres && depth < MarginCoversDepthMetres))
				{
					continue;
				}
				if (bestSide > 0f)
				{
					rawLeft[p] = Math.Max(rawLeft[p], Math.Min(MaxMetres, outside));
				}
				else
				{
					rawRight[p] = Math.Max(rawRight[p], Math.Min(MaxMetres, outside));
				}
			}
			left = Even(rawLeft);
			right = Even(rawRight);
		}

		/// <summary>Each run of fall points: the point before it (its lip) and the point after (its foot).</summary>
		private static List<(int lip, int foot)> Falls(RiverPath river)
		{
			var falls = new List<(int, int)>();
			int n = river.Count;
			if (river.Reach == null || river.Reach.Length != n)
			{
				return falls;
			}
			for (int i = 0; i < n; i++)
			{
				if (river.Reach[i] != RiverReach.Fall)
				{
					continue;
				}
				int last = i;
				while (last + 1 < n && river.Reach[last + 1] == RiverReach.Fall)
				{
					last++;
				}
				falls.Add((Math.Max(0, i - 1), Math.Min(n - 1, last + 1)));
				i = last;
			}
			return falls;
		}

		/// <summary>Marks every cell within <paramref name="radius"/> of a…b as the fall <paramref name="fall"/>'s.</summary>
		private static void MarkZone(int[] zone, int fall, float ax, float az, float bx, float bz, float radius, float x0, float z0, int w, int d)
		{
			int ix0 = Math.Max(0, (int)((Math.Min(ax, bx) - radius - x0) / CellMetres)), ix1 = Math.Min(w - 1, (int)((Math.Max(ax, bx) + radius - x0) / CellMetres));
			int iz0 = Math.Max(0, (int)((Math.Min(az, bz) - radius - z0) / CellMetres)), iz1 = Math.Min(d - 1, (int)((Math.Max(az, bz) + radius - z0) / CellMetres));
			float ex = bx - ax, ez = bz - az, lengthSq = Math.Max(1e-6f, ex * ex + ez * ez);
			for (int z = iz0; z <= iz1; z++)
			{
				for (int x = ix0; x <= ix1; x++)
				{
					float px = x0 + (x + 0.5f) * CellMetres - ax, pz = z0 + (z + 0.5f) * CellMetres - az;
					float t = Math.Max(0f, Math.Min(1f, (px * ex + pz * ez) / lengthSq));
					float cx = px - ex * t, cz = pz - ez * t;
					if (cx * cx + cz * cz <= radius * radius)
					{
						zone[z * w + x] = fall;
					}
				}
			}
		}

		/// <summary>Each point the most of itself and its neighbours, then averaged with them: no notch where one point's water fell short.</summary>
		private static float[] Even(float[] raw)
		{
			int n = raw.Length;
			var widest = new float[n];
			for (int i = 0; i < n; i++)
			{
				float m = raw[i];
				if (i > 0) m = Math.Max(m, raw[i - 1]);
				if (i + 1 < n) m = Math.Max(m, raw[i + 1]);
				widest[i] = m;
			}
			var even = new float[n];
			for (int i = 0; i < n; i++)
			{
				float sum = widest[i], count = 1f;
				if (i > 0) { sum += widest[i - 1]; count++; }
				if (i + 1 < n) { sum += widest[i + 1]; count++; }
				even[i] = widest[i] > 0f ? Math.Max(raw[i], sum / count) : 0f;
			}
			return even;
		}
	}
}
#endif
