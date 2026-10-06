#if UNITY_EDITOR
using System;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The positions one scatter rule has placed so far anywhere in a scene, and whether a new one
	/// keeps its distance from all of them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Scene-wide, so spacing holds across a seam.</b> One of these lives per rule for the whole
	/// scatter, not per tile; a tree at the east edge of one tile keeps the first tree of the next
	/// tile at bay exactly as a tree inside the same tile would.
	/// </para>
	/// <para>
	/// <b>Distance on the ground plane, not in 3D.</b> On a heightfield the vertical offset between
	/// two neighbours is the slope times their distance, so a 3D test would let a steep hillside
	/// pack trees closer, in plan, than a flat meadow — the opposite of how anything grows.
	/// </para>
	/// <para>
	/// <b>A hash of cells, not a dense grid.</b> Cells are the spacing wide, so a candidate only
	/// ever compares against the 3×3 cells around it; storing only occupied cells makes the memory
	/// proportional to what was placed rather than to the scene's area over the spacing squared,
	/// which for half-metre grass on a 20 km scene would be over a billion cells. Open addressing
	/// and index-linked chains keep the hot path free of allocation; storage doubles when full.
	/// </para>
	/// <para>
	/// <b>Per-point spacing, for a canopy.</b> Built with <c>perPoint</c>, each position carries its own
	/// spacing and two stand at least the mean of theirs apart: a 30 m oak keeps a birch further off
	/// than a birch keeps another birch, and trees of different forest rules share one of these, so
	/// species do not stand inside one another. The cells are then the widest spacing any point may
	/// carry (a point's own is clamped to it), so the 3×3 search still finds every neighbour that
	/// could be too close.
	/// </para>
	/// </remarks>
	internal sealed class ScatterSpacing
	{
		private readonly float inverseCell;
		private readonly float minimumSquared;

		// Hash slots: the cell key and the head of that cell's chain, -1 when the slot is empty.
		private long[] keys;
		private int[] heads;
		private int mask;
		private int occupied;

		// Points, chained per cell through next.
		private float[] xs;
		private float[] zs;
		private int[] next;
		private int count;

		// Each point's own spacing, in per-point mode only; null otherwise.
		private float[] spans;
		private readonly float widest;

		/// <summary>Number of positions recorded.</summary>
		public int Count => count;

		/// <param name="minimumSpacing">Closest two positions may stand, in metres. Must be positive.</param>
		/// <param name="expected">Positions expected, to size the storage once.</param>
		public ScatterSpacing(float minimumSpacing, int expected) : this(minimumSpacing, expected, false)
		{
		}

		/// <param name="minimumSpacing">Closest two positions may stand, in metres; with <paramref name="perPoint"/>, the widest spacing any one point may carry. Must be positive.</param>
		/// <param name="expected">Positions expected, to size the storage once.</param>
		/// <param name="perPoint">True when every position carries its own spacing (<see cref="IsClear(float, float, float)"/>).</param>
		public ScatterSpacing(float minimumSpacing, int expected, bool perPoint)
		{
			if (!(minimumSpacing > 0f))
			{
				throw new ArgumentOutOfRangeException(nameof(minimumSpacing), "Spacing must be positive; skip the test instead.");
			}
			inverseCell = 1f / minimumSpacing;
			minimumSquared = minimumSpacing * minimumSpacing;
			widest = minimumSpacing;

			int points = Math.Max(16, expected);
			xs = new float[points];
			zs = new float[points];
			next = new int[points];
			spans = perPoint ? new float[points] : null;

			int slots = 32;
			while (slots < points * 2)
			{
				slots <<= 1;
			}
			Allocate(slots);
		}

		/// <summary>True when (x, z) is at least the spacing from every recorded position.</summary>
		public bool IsClear(float x, float z)
		{
			int cx = Floor(x * inverseCell);
			int cz = Floor(z * inverseCell);
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					int slot = Find(Key(cx + dx, cz + dz));
					if (slot < 0)
					{
						continue;
					}
					for (int p = heads[slot]; p >= 0; p = next[p])
					{
						float ox = xs[p] - x;
						float oz = zs[p] - z;
						if (ox * ox + oz * oz < minimumSquared)
						{
							return false;
						}
					}
				}
			}
			return true;
		}

		/// <summary>
		/// Per-point mode: true when (x, z), carrying <paramref name="spacing"/>, stands at least the mean of
		/// its own and each recorded position's spacing from every one of them.
		/// </summary>
		public bool IsClear(float x, float z, float spacing)
		{
			if (spans == null)
			{
				return IsClear(x, z);
			}
			float own = Math.Min(Math.Max(0f, spacing), widest);
			int cx = Floor(x * inverseCell);
			int cz = Floor(z * inverseCell);
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					int slot = Find(Key(cx + dx, cz + dz));
					if (slot < 0)
					{
						continue;
					}
					for (int p = heads[slot]; p >= 0; p = next[p])
					{
						float ox = xs[p] - x;
						float oz = zs[p] - z;
						float need = (own + spans[p]) * 0.5f;
						if (ox * ox + oz * oz < need * need)
						{
							return false;
						}
					}
				}
			}
			return true;
		}

		/// <summary>Per-point mode: records a position with its own spacing. Does not test it; call <see cref="IsClear(float, float, float)"/> first.</summary>
		public void Add(float x, float z, float spacing)
		{
			Add(x, z);
			if (spans != null)
			{
				spans[count - 1] = Math.Min(Math.Max(0f, spacing), widest);
			}
		}

		/// <summary>Records a position. Does not test it; call <see cref="IsClear(float, float)"/> first.</summary>
		/// <remarks>In per-point mode it carries the widest spacing; use <see cref="Add(float, float, float)"/> instead.</remarks>
		public void Add(float x, float z)
		{
			if (count == xs.Length)
			{
				int grown = xs.Length * 2;
				Array.Resize(ref xs, grown);
				Array.Resize(ref zs, grown);
				Array.Resize(ref next, grown);
				if (spans != null)
				{
					Array.Resize(ref spans, grown);
				}
			}
			if ((occupied + 1) * 2 > heads.Length)
			{
				Rehash(heads.Length * 2);
			}

			long key = Key(Floor(x * inverseCell), Floor(z * inverseCell));
			int slot = Slot(key);
			while (heads[slot] >= 0 && keys[slot] != key)
			{
				slot = (slot + 1) & mask;
			}
			if (heads[slot] < 0)
			{
				keys[slot] = key;
				occupied++;
			}
			xs[count] = x;
			zs[count] = z;
			if (spans != null)
			{
				spans[count] = widest;
			}
			next[count] = heads[slot];
			heads[slot] = count;
			count++;
		}

		private int Find(long key)
		{
			int slot = Slot(key);
			while (heads[slot] >= 0)
			{
				if (keys[slot] == key)
				{
					return slot;
				}
				slot = (slot + 1) & mask;
			}
			return -1;
		}

		private void Allocate(int slots)
		{
			keys = new long[slots];
			heads = new int[slots];
			for (int i = 0; i < slots; i++)
			{
				heads[i] = -1;
			}
			mask = slots - 1;
			occupied = 0;
		}

		private void Rehash(int slots)
		{
			long[] oldKeys = keys;
			int[] oldHeads = heads;
			Allocate(slots);
			for (int i = 0; i < oldHeads.Length; i++)
			{
				if (oldHeads[i] < 0)
				{
					continue;
				}
				int slot = Slot(oldKeys[i]);
				while (heads[slot] >= 0)
				{
					slot = (slot + 1) & mask;
				}
				keys[slot] = oldKeys[i];
				heads[slot] = oldHeads[i];
				occupied++;
			}
		}

		private int Slot(long key)
		{
			ulong h = (ulong)key * 0x9E3779B97F4A7C15UL;
			return (int)(h >> 40) & mask;
		}

		private static long Key(int cx, int cz)
		{
			return ((long)cx << 32) | (uint)cz;
		}

		private static int Floor(float value)
		{
			int i = (int)value;
			return value < i ? i - 1 : i;
		}
	}
}
#endif
