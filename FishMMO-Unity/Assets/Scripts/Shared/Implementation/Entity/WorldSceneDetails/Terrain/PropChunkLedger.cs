using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Which of one scene's prop collision chunks are wanted, kept by reference count: every focus point holds the chunks
	/// round the 32 m cell it stands in, and a chunk is wanted while any focus holds it. The bookkeeping half of
	/// <see cref="PropColliderStreamer"/>, with no Unity objects in it, so it is testable on its own.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>By cell, not by point.</b> A focus is placed again only when it crosses into another cell (or its radius
	/// changes), and then holds every chunk within its radius of ANY point of that cell (<see cref="CellGap"/>). That is a
	/// superset of the chunks within its radius of where it actually stands (<see cref="PointGap"/>), for every point in
	/// the cell, so moving inside a cell never needs a chunk the focus does not already hold.
	/// </para>
	/// <para>
	/// <b>Released late.</b> A held chunk is let go only once it is more than the radius and the release margin from the
	/// focus's cell, so a focus pacing across a cell edge does not empty and rebuild the chunks along the far rim each
	/// time. It only ever keeps more collision, never less.
	/// </para>
	/// </remarks>
	public sealed class PropChunkLedger
	{
		/// <summary>One focus's hold on this ledger's scene: the chunks it keeps wanted, and the cell it was placed in.</summary>
		public sealed class Hold
		{
			/// <summary>Chunks (with props) this focus keeps wanted.</summary>
			public readonly HashSet<long> Cells = new HashSet<long>();
			public int X, Z;
			public float Radius;
			public bool Placed;
		}

		/// <summary>Chunk side, metres.</summary>
		public readonly float CellMetres;

		/// <summary>The chunks that have collidable props; no other chunk is ever wanted.</summary>
		public readonly HashSet<long> PropCells;

		/// <summary>Chunks a hold newly took since the lists were last cleared, each with its distance from the focus then.</summary>
		public readonly List<(long cell, float distance)> Acquired = new List<(long, float)>();

		/// <summary>Chunks whose last hold let go since the lists were last cleared. One may be wanted again by now: check <see cref="IsWanted"/>.</summary>
		public readonly List<long> Released = new List<long>();

		private readonly Dictionary<long, int> references = new Dictionary<long, int>();
		private readonly List<long> scratch = new List<long>();

		public PropChunkLedger(float cellMetres, HashSet<long> propCells)
		{
			CellMetres = cellMetres;
			PropCells = propCells ?? new HashSet<long>();
		}

		/// <summary>Chunks wanted by at least one hold.</summary>
		public int WantedCount => references.Count;

		/// <summary>Every wanted chunk.</summary>
		public Dictionary<long, int>.KeyCollection Wanted => references.Keys;

		public bool IsWanted(long cell) => references.ContainsKey(cell);

		/// <summary>How many holds keep <paramref name="cell"/> wanted.</summary>
		public int References(long cell) => references.TryGetValue(cell, out int count) ? count : 0;

		/// <summary>
		/// Places a hold at <paramref name="at"/>: when that is a new cell (or radius) for it, it lets go of the chunks now
		/// beyond <paramref name="radius"/> + <paramref name="releaseMargin"/> of the cell and takes every chunk within
		/// <paramref name="radius"/> of it. Returns whether anything was recomputed.
		/// </summary>
		public bool Place(Hold hold, Vector3 at, float radius, float releaseMargin)
		{
			int x = Mathf.FloorToInt(at.x / CellMetres), z = Mathf.FloorToInt(at.z / CellMetres);
			if (hold.Placed && hold.X == x && hold.Z == z && hold.Radius == radius)
			{
				return false;
			}
			hold.Placed = true;
			hold.X = x;
			hold.Z = z;
			hold.Radius = radius;

			float keep = radius + Mathf.Max(0f, releaseMargin);
			scratch.Clear();
			foreach (long cell in hold.Cells)
			{
				Unkey(cell, out int kx, out int kz);
				if (CellGap(kx - x, kz - z, CellMetres) > keep)
				{
					scratch.Add(cell);
				}
			}
			for (int i = 0; i < scratch.Count; i++)
			{
				hold.Cells.Remove(scratch[i]);
				Unreference(scratch[i]);
			}

			int span = Mathf.CeilToInt(radius / CellMetres) + 1;
			for (int dz = -span; dz <= span; dz++)
			{
				for (int dx = -span; dx <= span; dx++)
				{
					if (CellGap(dx, dz, CellMetres) > radius)
					{
						continue;
					}
					long cell = Key(x + dx, z + dz);
					if (!PropCells.Contains(cell) || !hold.Cells.Add(cell))
					{
						continue;
					}
					references.TryGetValue(cell, out int count);
					references[cell] = count + 1;
					Acquired.Add((cell, PointGap(at, x + dx, z + dz, CellMetres)));
				}
			}
			return true;
		}

		/// <summary>Lets go of everything a hold keeps; it is placed afresh next time.</summary>
		public void Drop(Hold hold)
		{
			foreach (long cell in hold.Cells)
			{
				Unreference(cell);
			}
			hold.Cells.Clear();
			hold.Placed = false;
		}

		/// <summary>The chunks with props within <paramref name="radius"/> of any point of cell (<paramref name="x"/>, <paramref name="z"/>).</summary>
		public void CellsNear(int x, int z, float radius, List<long> into)
		{
			into.Clear();
			int span = Mathf.CeilToInt(radius / CellMetres) + 1;
			for (int dz = -span; dz <= span; dz++)
			{
				for (int dx = -span; dx <= span; dx++)
				{
					long cell = Key(x + dx, z + dz);
					if (CellGap(dx, dz, CellMetres) <= radius && PropCells.Contains(cell))
					{
						into.Add(cell);
					}
				}
			}
		}

		private void Unreference(long cell)
		{
			if (!references.TryGetValue(cell, out int count))
			{
				return;
			}
			if (count > 1)
			{
				references[cell] = count - 1;
				return;
			}
			references.Remove(cell);
			Released.Add(cell);
		}

		/// <summary>
		/// The chunks within <paramref name="radius"/> of the point <paramref name="at"/> (whether they have props or not),
		/// with their distances: the per-point rule, as the streamer measured it before chunks were held by cell.
		/// </summary>
		public static void PointCells(Vector3 at, float radius, float cellMetres, List<(long cell, float distance)> into)
		{
			into.Clear();
			int x0 = Mathf.FloorToInt((at.x - radius) / cellMetres), x1 = Mathf.FloorToInt((at.x + radius) / cellMetres);
			int z0 = Mathf.FloorToInt((at.z - radius) / cellMetres), z1 = Mathf.FloorToInt((at.z + radius) / cellMetres);
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					float distance = PointGap(at, x, z, cellMetres);
					if (distance <= radius)
					{
						into.Add((Key(x, z), distance));
					}
				}
			}
		}

		/// <summary>Distance from a point to chunk (<paramref name="x"/>, <paramref name="z"/>)'s square, horizontally.</summary>
		public static float PointGap(Vector3 at, int x, int z, float cellMetres)
		{
			float dx = Mathf.Max(0f, Mathf.Max(x * cellMetres - at.x, at.x - (x + 1) * cellMetres));
			float dz = Mathf.Max(0f, Mathf.Max(z * cellMetres - at.z, at.z - (z + 1) * cellMetres));
			return Mathf.Sqrt(dx * dx + dz * dz);
		}

		/// <summary>
		/// Least distance between two chunks' squares <paramref name="dx"/>, <paramref name="dz"/> chunks apart: never more
		/// than <see cref="PointGap"/> from any point of the one to the other, in float arithmetic as well (each step is
		/// monotonic and the whole-chunk gaps are exact), which is what makes holding by cell a superset.
		/// </summary>
		public static float CellGap(int dx, int dz, float cellMetres)
		{
			float gx = Mathf.Max(0, System.Math.Abs(dx) - 1) * cellMetres;
			float gz = Mathf.Max(0, System.Math.Abs(dz) - 1) * cellMetres;
			return Mathf.Sqrt(gx * gx + gz * gz);
		}

		public static long Key(int x, int z) => ((long)x << 32) | (uint)z;

		public static void Unkey(long key, out int x, out int z)
		{
			x = (int)(key >> 32);
			z = (int)(uint)key;
		}
	}
}
