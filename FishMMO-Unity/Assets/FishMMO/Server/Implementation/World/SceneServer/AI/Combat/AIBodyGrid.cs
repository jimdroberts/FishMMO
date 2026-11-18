using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// Where every NPC body in one physics scene stands, bucketed into a uniform grid so a
	/// separation query reads a handful of cells instead of running a physics overlap.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What it replaced.</b> Each NPC's separation used to run its own <c>OverlapSphere</c> on
	/// the character layer every brain tick, standing or moving. NPCs live on that layer too, so
	/// every query returned the NPC's own collider and paid a rigidbody read and an interface
	/// component lookup to reject it, then the same again for each neighbour: four hundred Active
	/// NPCs was three thousand overlaps and at least three thousand interface lookups a second.
	/// <see cref="AIBrainHost"/> now fills one of these per scene, at most once per network tick
	/// and only for a scene something asked about, and every separation in that scene reads it.
	/// </para>
	/// <para>
	/// <b>Equivalent to the overlap it replaced.</b> <see cref="AISeparation.Resolve"/> weighs
	/// neighbours by horizontal centre distance and ignores anyone at or beyond the radius, so a
	/// collider the sphere merely grazed contributed nothing anyway. The vertical reach stands in
	/// for the sphere's height test, so an NPC on a bridge does not push the one underneath it.
	/// </para>
	/// <para>
	/// Plain C#, no physics and no Unity objects, so it can be pinned in an EditMode test. Each
	/// cell's bodies are a linked list threaded through flat arrays, and the arrays and the cell
	/// table are reused, so a rebuild allocates nothing once warm.
	/// </para>
	/// </remarks>
	public sealed class AIBodyGrid
	{
		/// <summary>
		/// Default cell edge, in metres. About twice a humanoid's separation radius, so the common
		/// query reads a 3 x 3 block.
		/// </summary>
		public const float DefaultCellSize = 2f;

		/// <summary>Cell edge, in metres.</summary>
		public float CellSize { get; }

		/// <summary>Bodies added since the last <see cref="Clear"/>.</summary>
		public int Count { get; private set; }

		/// <summary>Index of the first body in each occupied cell.</summary>
		private readonly Dictionary<long, int> heads = new Dictionary<long, int>();

		private Vector3[] positions = new Vector3[32];
		private uint[] keys = new uint[32];

		/// <summary>Index of the next body in the same cell, or -1.</summary>
		private int[] next = new int[32];

		/// <summary>
		/// Creates an empty grid.
		/// </summary>
		/// <param name="cellSize">Cell edge in metres; a non-positive value uses <see cref="DefaultCellSize"/>.</param>
		public AIBodyGrid(float cellSize = DefaultCellSize)
		{
			CellSize = cellSize > 0f ? cellSize : DefaultCellSize;
		}

		/// <summary>
		/// Empties the grid, keeping its storage.
		/// </summary>
		public void Clear()
		{
			heads.Clear();
			Count = 0;
		}

		/// <summary>
		/// Adds one body.
		/// </summary>
		/// <param name="position">Where it stands.</param>
		/// <param name="key">Its identity key, used to exclude the asker and to break ties.</param>
		public void Add(Vector3 position, uint key)
		{
			if (Count == positions.Length)
			{
				int capacity = positions.Length * 2;
				System.Array.Resize(ref positions, capacity);
				System.Array.Resize(ref keys, capacity);
				System.Array.Resize(ref next, capacity);
			}

			long cell = Pack(CellCoordinate(position.x), CellCoordinate(position.z));
			positions[Count] = position;
			keys[Count] = key;
			next[Count] = heads.TryGetValue(cell, out int head) ? head : -1;
			heads[cell] = Count;
			++Count;
		}

		/// <summary>
		/// Appends every body within <paramref name="radius"/> of <paramref name="center"/>
		/// horizontally and within <paramref name="verticalReach"/> of it vertically, except the
		/// one keyed <paramref name="excludeKey"/>.
		/// </summary>
		/// <param name="center">The asking body's position.</param>
		/// <param name="radius">Horizontal reach, in metres.</param>
		/// <param name="verticalReach">Vertical reach, in metres.</param>
		/// <param name="excludeKey">The asker's own key.</param>
		/// <param name="positionsOut">Receives each neighbour's position.</param>
		/// <param name="keysOut">Receives each neighbour's key, in the same order.</param>
		/// <returns>How many neighbours were appended.</returns>
		public int Query(Vector3 center, float radius, float verticalReach, uint excludeKey, List<Vector3> positionsOut, List<uint> keysOut)
		{
			if (radius <= 0f || Count == 0)
			{
				return 0;
			}

			int reach = Mathf.Max(1, Mathf.CeilToInt(radius / CellSize));
			int cx = CellCoordinate(center.x);
			int cz = CellCoordinate(center.z);
			float sqrRadius = radius * radius;
			int found = 0;

			for (int dx = -reach; dx <= reach; ++dx)
			{
				for (int dz = -reach; dz <= reach; ++dz)
				{
					if (!heads.TryGetValue(Pack(cx + dx, cz + dz), out int i))
					{
						continue;
					}

					for (; i >= 0; i = next[i])
					{
						if (keys[i] == excludeKey)
						{
							continue;
						}

						Vector3 offset = positions[i] - center;
						if (Mathf.Abs(offset.y) > verticalReach)
						{
							continue;
						}
						offset.y = 0f;
						if (offset.sqrMagnitude >= sqrRadius)
						{
							continue;
						}

						positionsOut.Add(positions[i]);
						keysOut.Add(keys[i]);
						++found;
					}
				}
			}

			return found;
		}

		/// <summary>The cell coordinate along one axis.</summary>
		private int CellCoordinate(float value)
		{
			return Mathf.FloorToInt(value / CellSize);
		}

		/// <summary>Packs a cell's two coordinates into one key.</summary>
		private static long Pack(int x, int z)
		{
			return ((long)x << 32) ^ (uint)z;
		}
	}
}
