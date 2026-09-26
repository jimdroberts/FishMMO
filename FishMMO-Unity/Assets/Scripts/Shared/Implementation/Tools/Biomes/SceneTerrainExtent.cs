using System.Collections.Generic;
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

		/// <summary>
		/// One terrain tile of a scene, with the bounds the samplers test read once rather than per
		/// sample.
		/// </summary>
		public readonly struct Tile
		{
			/// <summary>The terrain. Unity-null once it has been destroyed.</summary>
			public readonly Terrain Terrain;

			/// <summary>World position of the tile's corner, from <see cref="Terrain.GetPosition"/>.</summary>
			public readonly Vector3 Origin;

			/// <summary>The tile's size, from its terrain data.</summary>
			public readonly Vector3 Size;

			public Tile(Terrain terrain, Vector3 origin, Vector3 size)
			{
				Terrain = terrain;
				Origin = origin;
				Size = size;
			}

			/// <summary>True when the tile lies under a world position, edges included.</summary>
			public bool Covers(Vector3 worldPosition)
			{
				return worldPosition.x >= Origin.x && worldPosition.x <= Origin.x + Size.x
					&& worldPosition.z >= Origin.z && worldPosition.z <= Origin.z + Size.z;
			}
		}

		/// <summary>One scene's measurement: the landmass and the tiles it was measured from.</summary>
		private sealed class Entry
		{
			public SceneTerrainExtent Extent;
			public Tile[] Tiles;
			/// <summary>False while a terrain of the scene has no data yet, so the entry is not kept.</summary>
			public bool Complete;
		}

		private static readonly Tile[] NoTiles = new Tile[0];

		/// <summary>Measurements by scene handle; 0 is the unscoped measurement of every terrain.</summary>
		private static readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();

		/// <summary>The active terrains the cache was measured against, in Unity's order.</summary>
		private static readonly List<Terrain> snapshot = new List<Terrain>();

		/// <summary>Reused for the comparison, so checking the snapshot does not allocate.</summary>
		private static readonly List<Terrain> probe = new List<Terrain>();

		/// <summary><see cref="Time.frameCount"/> at which <see cref="snapshot"/> was last compared.</summary>
		private static int validatedFrame = -1;

		/// <summary>
		/// The extent of a scene's terrains, measured once and remembered.
		/// </summary>
		/// <param name="scene">The scene to measure. An invalid scene measures every loaded terrain.</param>
		/// <remarks>
		/// See <see cref="TilesOf"/> for when a measurement is taken again.
		/// </remarks>
		public static SceneTerrainExtent Of(Scene scene)
		{
			return EntryFor(scene).Extent;
		}

		/// <summary>
		/// The tiles a scene's ground is made of, in the order Unity lists its active terrains, and
		/// the landmass they make up.
		/// </summary>
		/// <param name="scene">The scene. An invalid scene answers with every loaded terrain.</param>
		/// <param name="extent">Receives the scene's landmass, as <see cref="Of"/> would.</param>
		/// <returns>The tiles, including any with no height range; never null. Do not modify it.</returns>
		/// <remarks>
		/// <para>
		/// <b>Why a cache at all.</b> <see cref="Terrain.activeTerrains"/> builds a new array on
		/// every read, and a lookup that walked it asked every tile of every loaded scene for its
		/// scene, data, position and size. The biome sampler did that on every weather sample,
		/// and the weather is sampled per character per second for exposure — with fifty terrains
		/// and three hundred players, some six hundred arrays and sixty thousand engine calls a
		/// second, most of them about scenes the character was not in.
		/// </para>
		/// <para>
		/// <b>When it is measured again.</b> In play mode the active terrains are compared, by
		/// reference and in order, against the list the cache was built from: once per frame, and
		/// again whenever a scene is asked about that has no measurement yet, so a scene that
		/// loaded this frame is measured against this frame's terrains. Any difference — a scene
		/// loading or unloading, a terrain enabled, disabled, added or destroyed — drops every
		/// measurement. A measurement that found a terrain with no data yet (added a moment before
		/// its data is assigned) is not kept. Outside play mode nothing is kept at all: tools add,
		/// move and resize terrain between calls within one editor frame, and nothing there is
		/// sampled often enough to be worth the risk.
		/// </para>
		/// <para>
		/// A terrain moved or resized in place keeps its identity, so a tool that does that in play
		/// mode calls <see cref="Invalidate"/>, as it always had to for the landmass.
		/// </para>
		/// </remarks>
		public static Tile[] TilesOf(Scene scene, out SceneTerrainExtent extent)
		{
			Entry entry = EntryFor(scene);
			extent = entry.Extent;
			return entry.Tiles;
		}

		/// <summary>Forgets every measurement. For tests, and for tools that move terrain.</summary>
		public static void Invalidate()
		{
			cache.Clear();
			snapshot.Clear();
			validatedFrame = -1;
		}

		/// <summary>
		/// Whether the active terrains must be compared with the snapshot before answering.
		/// </summary>
		/// <param name="frame">The current frame.</param>
		/// <param name="validatedFrame">The frame the snapshot was last compared on.</param>
		/// <param name="cacheMiss">True when the scene asked about has no measurement.</param>
		/// <remarks>
		/// Once per frame, and on a miss. The miss is what makes a scene that finished loading
		/// after this frame's comparison exact rather than a frame late: its terrains are in the
		/// active list now, and the comparison is what puts them in the snapshot it is measured
		/// from.
		/// </remarks>
		internal static bool MustCompareSnapshot(int frame, int validatedFrame, bool cacheMiss)
		{
			return cacheMiss || frame != validatedFrame;
		}

		private static Entry EntryFor(Scene scene)
		{
			if (!Application.isPlaying)
			{
				Terrain.GetActiveTerrains(probe);
				return Measure(probe, scene);
			}

			int key = scene.IsValid() ? scene.handle : 0;
			bool miss = !cache.TryGetValue(key, out Entry entry);
			int frame = Time.frameCount;
			if (MustCompareSnapshot(frame, validatedFrame, miss))
			{
				validatedFrame = frame;
				Terrain.GetActiveTerrains(probe);
				if (!SameTerrains(probe, snapshot))
				{
					snapshot.Clear();
					snapshot.AddRange(probe);
					cache.Clear();
					miss = true;
				}
			}

			if (miss)
			{
				entry = Measure(snapshot, scene);
				if (entry.Complete)
				{
					cache[key] = entry;
				}
			}
			return entry;
		}

		private static bool SameTerrains(List<Terrain> a, List<Terrain> b)
		{
			if (a.Count != b.Count)
			{
				return false;
			}
			for (int i = 0; i < a.Count; i++)
			{
				if (!ReferenceEquals(a[i], b[i]))
				{
					return false;
				}
			}
			return true;
		}

		private static Entry Measure(List<Terrain> terrains, Scene scene)
		{
			var entry = new Entry { Complete = true };
			var extent = new SceneTerrainExtent();
			List<Tile> tiles = null;

			bool onlyOne = scene.IsValid();
			float minX = float.MaxValue, minZ = float.MaxValue;
			float maxX = float.MinValue, maxZ = float.MinValue;

			for (int i = 0; i < terrains.Count; i++)
			{
				Terrain terrain = terrains[i];
				if (terrain == null || onlyOne && terrain.gameObject.scene.handle != scene.handle)
				{
					continue;
				}
				TerrainData data = terrain.terrainData;
				if (data == null)
				{
					// Added and not yet given its data, most likely; measure again next time.
					entry.Complete = false;
					continue;
				}
				Vector3 origin = terrain.GetPosition();
				Vector3 size = data.size;

				// Every tile with data is sampled, whatever its size — the height lookup always
				// accepted a flat one — but only tiles with real size make up the landmass.
				(tiles ??= new List<Tile>()).Add(new Tile(terrain, origin, size));

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
			entry.Extent = extent;
			entry.Tiles = tiles != null ? tiles.ToArray() : NoTiles;
			return entry;
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
