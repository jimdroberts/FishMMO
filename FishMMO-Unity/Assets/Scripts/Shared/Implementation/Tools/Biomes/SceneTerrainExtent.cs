using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The ground one scene's terrains cover, measured as a single landmass rather than tile by
	/// tile.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A scene may hold one Unity terrain or a grid of them stitched into a continent, and the two
	/// have to read the same. Every question anything asks about a scene's ground — how high a
	/// point is, how much area the weather director has, how far the air cools from the shore to
	/// the summit — is a question about the landmass, not about whichever tile happens to be
	/// underfoot.
	/// </para>
	/// <para>
	/// <b>Scoped to one scene.</b> <see cref="Terrain.activeTerrains"/> is global, and a scene
	/// server loads several world scenes additively — each built around its own origin, so their
	/// terrains sit on top of one another in world space. Measuring without filtering by scene
	/// answers with whichever tile the array happened to list first, from whatever scene.
	/// </para>
	/// </remarks>
	public struct SceneTerrainExtent
	{
		/// <summary>True when the scene has at least one terrain with real size.</summary>
		public bool Found;

		/// <summary>How many tiles were measured. One is a lone terrain; more is a stitched landmass.</summary>
		public int TileCount;

		/// <summary>The union of the tiles in world X/Z.</summary>
		public Rect Area;

		/// <summary>World Y of the lowest tile's base: height 0 for the whole landmass.</summary>
		public float BaseY;

		/// <summary>World Y of the highest tile's ceiling: height 1 for the whole landmass.</summary>
		public float TopY;

		/// <summary>
		/// The landmass's full vertical range in metres — what a normalised height of 0 to 1 spans.
		/// </summary>
		/// <remarks>
		/// The range the terrain <em>can</em> use, not the relief it actually has. A heightmap's
		/// values are stored as fractions of its own size, so this is the number that converts one
		/// back into metres, and it does not move when somebody sculpts the ground.
		/// </remarks>
		public float HeightSpanMetres => Mathf.Max(0f, TopY - BaseY);

		/// <summary>The area in square kilometres.</summary>
		public float SquareKm => Area.width * Area.height / 1_000_000f;

		// ── Measuring ─────────────────────────────────────────────────

		private static readonly System.Collections.Generic.Dictionary<int, SceneTerrainExtent> cache =
			new System.Collections.Generic.Dictionary<int, SceneTerrainExtent>();

		/// <summary>The terrain count the cache was built against.</summary>
		private static int cachedTerrainCount = -1;

		/// <summary>
		/// The extent of a scene's terrains, measured once and remembered.
		/// </summary>
		/// <param name="scene">The scene to measure. An invalid scene measures every loaded terrain.</param>
		/// <remarks>
		/// Re-measured whenever the number of active terrains changes, which covers a scene loading
		/// or unloading and a terrain being added or destroyed, without needing to be told. Reading
		/// the count is far cheaper than the union, and a terrain count that is stable while the
		/// tiles underneath it are swapped one-for-one is not a case this needs to survive.
		/// </remarks>
		public static SceneTerrainExtent Of(Scene scene)
		{
			Terrain[] terrains = Terrain.activeTerrains;
			int count = terrains != null ? terrains.Length : 0;
			if (count != cachedTerrainCount)
			{
				cache.Clear();
				cachedTerrainCount = count;
			}

			int key = scene.IsValid() ? scene.handle : 0;
			if (cache.TryGetValue(key, out SceneTerrainExtent cached))
			{
				return cached;
			}

			SceneTerrainExtent measured = Measure(terrains, scene);
			cache[key] = measured;
			return measured;
		}

		/// <summary>Forgets every measurement. For tests, and for tools that move terrain.</summary>
		public static void Invalidate()
		{
			cache.Clear();
			cachedTerrainCount = -1;
		}

		private static SceneTerrainExtent Measure(Terrain[] terrains, Scene scene)
		{
			var extent = new SceneTerrainExtent();
			if (terrains == null)
			{
				return extent;
			}

			bool onlyOne = scene.IsValid();
			float minX = float.MaxValue, minZ = float.MaxValue;
			float maxX = float.MinValue, maxZ = float.MinValue;

			for (int i = 0; i < terrains.Length; i++)
			{
				Terrain terrain = terrains[i];
				if (terrain == null || terrain.terrainData == null
					|| onlyOne && terrain.gameObject.scene.handle != scene.handle)
				{
					continue;
				}
				Vector3 origin = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				if (size.x <= 0f || size.z <= 0f || size.y <= 0f)
				{
					continue;
				}

				minX = Mathf.Min(minX, origin.x);
				maxX = Mathf.Max(maxX, origin.x + size.x);
				minZ = Mathf.Min(minZ, origin.z);
				maxZ = Mathf.Max(maxZ, origin.z + size.z);

				/* Union of the bases and the ceilings, not of one tile's.
				 *
				 * Stitched tiles do not have to share a base or a height: a plateau tile is
				 * commonly raised, and a tile covering flat ground is commonly given a shorter
				 * range so its heightmap keeps its precision. Normalising each tile against its
				 * own range makes the same world altitude read as a different height on either
				 * side of a seam — so the temperature, and with it the biome, steps as you walk
				 * across the join, for no reason that exists in the world. */
				extent.BaseY = extent.Found ? Mathf.Min(extent.BaseY, origin.y) : origin.y;
				extent.TopY = extent.Found ? Mathf.Max(extent.TopY, origin.y + size.y) : origin.y + size.y;
				extent.TileCount++;
				extent.Found = true;
			}

			if (extent.Found)
			{
				extent.Area = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
			}
			return extent;
		}

		/// <summary>
		/// A normalised height for a world altitude on this landmass.
		/// </summary>
		/// <remarks>
		/// Clamped, because a tile's <c>SampleHeight</c> can return a value a hair outside its own
		/// range at the very edge, and because an object may sit above or below the terrain.
		/// </remarks>
		public float Normalize(float worldY)
		{
			float span = HeightSpanMetres;
			return span > 0f ? Mathf.Clamp01((worldY - BaseY) / span) : 0f;
		}

		/// <summary>Metres above the landmass's base for a normalised height.</summary>
		public float ToMetres(float normalizedHeight) => normalizedHeight * HeightSpanMetres;

		public override string ToString()
		{
			return Found
				? $"{TileCount} tile(s), {Area.width:0}x{Area.height:0} m ({SquareKm:0.##} km2), {BaseY:0} to {TopY:0} m ({HeightSpanMetres:0} m span)"
				: "no terrain";
		}
	}
}
