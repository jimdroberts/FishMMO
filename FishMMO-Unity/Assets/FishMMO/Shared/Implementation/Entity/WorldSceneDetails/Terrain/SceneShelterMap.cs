using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared
{
	/// <summary>
	/// Where a scene keeps the rain off, baked by the cut: the height of the first surface the rain meets coming straight
	/// down, wherever that is something over the ground rather than the open ground itself — a roof, a rock shelter's slab,
	/// a bridge, a cliff's overhang, or the hillside over a cave or a mine. Anything below that height is under cover.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Of the scene, not of a fire.</b> Shelter is a property of the place, so it is baked once for the whole scene and
	/// read for whatever asks: a site's campfire, a fire a player lights, and later a character's weather exposure. Every
	/// peer reads the same asset, so a fire under an overhang is lit for everyone, whoever made it.
	/// </para>
	/// <para>
	/// <b>From the scene's own collision</b> (<see cref="ScenePropCollisionSet"/>: the sites' buildings and shelters, the
	/// cliffs, the boulders), rasterised from above at <see cref="CellMetres"/> by the cut, never from the physics or the
	/// camera: the client's sky occlusion map rays the colliders round the camera at the quality tier's resolution, which no
	/// two players share. The terrain counts as a roof only over the ground the collision reaches below it — a cave's,
	/// a mine's — so open hillside is open.
	/// </para>
	/// <para>
	/// <b>Sparse.</b> Only <see cref="ChunkCells"/>² cells round something that shelters are kept, as half floats; a scene of
	/// a village and a few camps is a few hundred kilobytes.
	/// </para>
	/// </remarks>
	[PreferBinarySerialization]
	public sealed class SceneShelterMap : ScriptableObject
	{
		/// <summary>Metres per cell.</summary>
		public const float CellMetres = 1f;

		/// <summary>Cells along a chunk's side.</summary>
		public const int ChunkCells = 32;

		/// <summary>A cell with nothing over its ground: a half-float NaN.</summary>
		public const ushort Open = 0x7E00;

		/// <summary>How far over a point a surface must be to keep the rain off it, metres: not the ground it stands on.</summary>
		public const float Clearance = 0.5f;

		[Tooltip("Each kept chunk's grid coordinates, x in the low 16 bits and z in the high, as signed shorts.")]
		[HideInInspector] public int[] ChunkKeys = Array.Empty<int>();

		[Tooltip("The roof's height (world y) at each cell of each kept chunk, as half floats, chunk after chunk, rows from the south-west; NaN where the sky is open.")]
		[HideInInspector] public ushort[] Heights = Array.Empty<ushort>();

		[NonSerialized] private Dictionary<int, int> index;

		/// <summary>The key of the chunk at chunk coordinates.</summary>
		public static int KeyOf(int chunkX, int chunkZ) => (chunkX & 0xFFFF) | (chunkZ << 16);

		/// <summary>The chunk holding a world x/z, and the cell within it.</summary>
		public static void Locate(float x, float z, out int chunkX, out int chunkZ, out int cell)
		{
			int cx = Mathf.FloorToInt(x / CellMetres), cz = Mathf.FloorToInt(z / CellMetres);
			chunkX = cx >= 0 ? cx / ChunkCells : (cx + 1) / ChunkCells - 1;
			chunkZ = cz >= 0 ? cz / ChunkCells : (cz + 1) / ChunkCells - 1;
			cell = (cz - chunkZ * ChunkCells) * ChunkCells + (cx - chunkX * ChunkCells);
		}

		/// <summary>Replaces the whole map.</summary>
		public void Set(int[] chunkKeys, ushort[] heights)
		{
			ChunkKeys = chunkKeys ?? Array.Empty<int>();
			Heights = heights != null && heights.Length == ChunkKeys.Length * ChunkCells * ChunkCells ? heights : new ushort[ChunkKeys.Length * ChunkCells * ChunkCells];
			index = null;
		}

		/// <summary>The height of the surface over a world x/z (world y), where something is over the ground there; false where the sky is open.</summary>
		public bool TryRoof(float x, float z, out float roofY)
		{
			roofY = float.NaN;
			Dictionary<int, int> chunks = Index();
			Locate(x, z, out int chunkX, out int chunkZ, out int cell);
			if (!chunks.TryGetValue(KeyOf(chunkX, chunkZ), out int chunk))
			{
				return false;
			}
			ushort half = Heights[chunk * ChunkCells * ChunkCells + cell];
			if (half == Open)
			{
				return false;
			}
			roofY = PlanetClimateTableData.FromHalf(half);
			return true;
		}

		/// <summary>True when something keeps the rain off a point: a surface at least <see cref="Clearance"/> over it.</summary>
		public bool IsCovered(Vector3 point)
		{
			return TryRoof(point.x, point.z, out float roof) && roof > point.y + Clearance;
		}

		/// <summary>
		/// How open to the weather a point is, 0 under cover … 1 under open sky: the point itself and the ground a metre round
		/// it, the point counting twice, so exposure blends across a slab's edge and its middle is wholly sheltered.
		/// </summary>
		public float Exposure(Vector3 point)
		{
			float covered = (IsCovered(point) ? 2f : 0f)
				+ (IsCovered(point + new Vector3(CellMetres, 0f, 0f)) ? 1f : 0f)
				+ (IsCovered(point - new Vector3(CellMetres, 0f, 0f)) ? 1f : 0f)
				+ (IsCovered(point + new Vector3(0f, 0f, CellMetres)) ? 1f : 0f)
				+ (IsCovered(point - new Vector3(0f, 0f, CellMetres)) ? 1f : 0f);
			return 1f - covered / 6f;
		}

		private Dictionary<int, int> Index()
		{
			if (index == null)
			{
				var built = new Dictionary<int, int>(ChunkKeys.Length);
				for (int i = 0; i < ChunkKeys.Length; i++)
				{
					built[ChunkKeys[i]] = i;
				}
				index = built;
			}
			return index;
		}
	}
}
