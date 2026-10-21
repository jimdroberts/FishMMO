#if !UNITY_SERVER || UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// How much of its deep-snow lift each heightmap vertex of a terrain tile keeps near a hole in the terrain: none at a
	/// hole's edge, all of it <see cref="FadeMetres"/> away, eased between. One byte a vertex in an R8 map the array terrain
	/// shader loads at the vertex (FishTerrainSnowHoles.hlsl), handed to each tile by <see cref="TerrainArrayBinder"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> A hole is cut where rock is the ground (a cave's mouth, a canyon wall), and that rock meets the hole's
	/// edge flush. The terrain's vertices rise under deep snow and the rock does not, so lifted to the snow's full depth the
	/// terrain's edge stood that far over the rock with nothing under it. The rock stays where it is; the snow comes down
	/// onto it over the last metres.
	/// </para>
	/// <para>
	/// <b>Why a map.</b> Scanning the holes from the vertex (2026-10-19) reached three cells and was read only at the
	/// vertices drawn: a far patch's vertices stand several cells apart, so its fade collapsed into a flat square plate that
	/// moved with the level of detail. Here every vertex's own value is the exact distance to the nearest hole quad, its own
	/// tile's or a neighbour's, so a vertex shared by two patches or two tiles is lifted alike whichever draws it.
	/// </para>
	/// </remarks>
	public static class TerrainSnowHoleFade
	{
		/// <summary>How far from a hole's edge the snow is back to its full depth, metres.</summary>
		public const float FadeMetres = 8f;

		private static Texture2D blank;

		/// <summary>A 1×1 map of full lift: what a tile with no hole near it is given.</summary>
		public static Texture2D Blank
		{
			get
			{
				if (blank == null)
				{
					blank = new Texture2D(1, 1, TextureFormat.R8, false, true) { name = "Snow hole fade (none)", hideFlags = HideFlags.HideAndDontSave };
					blank.SetPixelData(new byte[] { 255 }, 0);
					blank.Apply(false, true);
				}
				return blank;
			}
		}

		/// <summary>
		/// Each tile's map, for the tiles that have a hole within <see cref="FadeMetres"/> (their own or a neighbour's); the
		/// others are not in it and take <see cref="Blank"/>.
		/// </summary>
		public static Dictionary<Terrain, Texture2D> Build(IReadOnlyList<Terrain> tiles)
		{
			var maps = new Dictionary<Terrain, Texture2D>();
			var holes = new List<(Terrain Tile, List<Rect> Quads)>();
			foreach (Terrain tile in tiles)
			{
				List<Rect> quads = HoleQuads(tile);
				if (quads != null)
				{
					holes.Add((tile, quads));
				}
			}
			if (holes.Count == 0)
			{
				return maps;
			}
			foreach (Terrain tile in tiles)
			{
				Texture2D map = MapFor(tile, holes);
				if (map != null)
				{
					maps[tile] = map;
				}
			}
			return maps;
		}

		/// <summary>Frees maps made by <see cref="Build"/>.</summary>
		public static void Release(Dictionary<Terrain, Texture2D> maps)
		{
			if (maps == null)
			{
				return;
			}
			foreach (Texture2D map in maps.Values)
			{
				if (map == null)
				{
					continue;
				}
				if (Application.isPlaying)
				{
					Object.Destroy(map);
				}
				else
				{
					Object.DestroyImmediate(map);
				}
			}
			maps.Clear();
		}

		/// <summary>The world xz rectangles of a tile's hole quads, or null when it has none.</summary>
		private static List<Rect> HoleQuads(Terrain tile)
		{
			if (tile == null || tile.terrainData == null)
			{
				return null;
			}
			TerrainData data = tile.terrainData;
			int res = data.holesResolution;
			if (res <= 0)
			{
				return null;
			}
			Vector3 origin = tile.GetPosition();
			float cellX = data.size.x / res, cellZ = data.size.z / res;
			List<Rect> quads = null;
			// In strips, so a tile's whole mask is never held at once.
			const int Strip = 64;
			for (int z0 = 0; z0 < res; z0 += Strip)
			{
				int rows = Mathf.Min(Strip, res - z0);
				bool[,] solid = data.GetHoles(0, z0, res, rows);
				for (int z = 0; z < rows; z++)
				{
					for (int x = 0; x < res; x++)
					{
						if (!solid[z, x])
						{
							quads ??= new List<Rect>();
							quads.Add(new Rect(origin.x + x * cellX, origin.z + (z0 + z) * cellZ, cellX, cellZ));
						}
					}
				}
			}
			return quads;
		}

		/// <summary>A tile's map from every tile's hole quads, or null when none lies within <see cref="FadeMetres"/> of it.</summary>
		private static Texture2D MapFor(Terrain tile, List<(Terrain Tile, List<Rect> Quads)> holes)
		{
			if (tile == null || tile.terrainData == null)
			{
				return null;
			}
			TerrainData data = tile.terrainData;
			int n = data.heightmapResolution;
			Vector3 origin = tile.GetPosition();
			float stepX = data.size.x / (n - 1), stepZ = data.size.z / (n - 1);
			var reach = new Rect(origin.x - FadeMetres, origin.z - FadeMetres, data.size.x + 2f * FadeMetres, data.size.z + 2f * FadeMetres);
			float[] nearest2 = null;
			float far2 = FadeMetres * FadeMetres;
			foreach ((Terrain _, List<Rect> quads) in holes)
			{
				foreach (Rect quad in quads)
				{
					if (!quad.Overlaps(reach))
					{
						continue;
					}
					if (nearest2 == null)
					{
						nearest2 = new float[n * n];
						for (int i = 0; i < nearest2.Length; i++)
						{
							nearest2[i] = far2;
						}
					}
					// The vertices within reach of this quad, each held to its distance from the quad's nearest point.
					int x0 = Mathf.Max(0, Mathf.FloorToInt((quad.xMin - FadeMetres - origin.x) / stepX));
					int x1 = Mathf.Min(n - 1, Mathf.CeilToInt((quad.xMax + FadeMetres - origin.x) / stepX));
					int z0 = Mathf.Max(0, Mathf.FloorToInt((quad.yMin - FadeMetres - origin.z) / stepZ));
					int z1 = Mathf.Min(n - 1, Mathf.CeilToInt((quad.yMax + FadeMetres - origin.z) / stepZ));
					for (int z = z0; z <= z1; z++)
					{
						float wz = origin.z + z * stepZ;
						float dz = Mathf.Max(0f, Mathf.Max(quad.yMin - wz, wz - quad.yMax));
						int row = z * n;
						for (int x = x0; x <= x1; x++)
						{
							float wx = origin.x + x * stepX;
							float dx = Mathf.Max(0f, Mathf.Max(quad.xMin - wx, wx - quad.xMax));
							float d2 = dx * dx + dz * dz;
							if (d2 < nearest2[row + x])
							{
								nearest2[row + x] = d2;
							}
						}
					}
				}
			}
			if (nearest2 == null)
			{
				return null;
			}
			var bytes = new byte[n * n];
			for (int i = 0; i < bytes.Length; i++)
			{
				bytes[i] = (byte)Mathf.RoundToInt(255f * Fade(Mathf.Sqrt(nearest2[i])));
			}
			var map = new Texture2D(n, n, TextureFormat.R8, false, true)
			{
				name = $"Snow hole fade ({tile.name})",
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
			};
			map.SetPixelData(bytes, 0);
			map.Apply(false, true);
			return map;
		}

		/// <summary>The share of the lift kept <paramref name="metres"/> from the nearest hole: 0 at its edge, 1 from <see cref="FadeMetres"/>.</summary>
		public static float Fade(float metres)
		{
			float t = Mathf.Clamp01(metres / FadeMetres);
			return t * t * (3f - 2f * t);
		}
	}
}
#endif
