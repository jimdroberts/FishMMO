using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Client
{
	/// <summary>
	/// One kind of grass as blades in the scene's type table: a blade prefab with one biome's look for it
	/// (<see cref="BiomeGrassType"/>, from the template of the biome whose rule made the prototype), built from the prototype's
	/// prefab (its mesh's height and vertex colours, its FishMMO/Vegetation material's tints) and, for a rule's prototype, the
	/// rule's healthy and dry tints. Biomes whose looks of a prefab are equal share one; at most
	/// <see cref="GrassBladeRenderer.MaxTypes"/> exist (<see cref="GrassTypeTable"/>).
	/// </summary>
	public sealed class GrassType
	{
		public GameObject Prefab;
		public int Index;
		public string Name;
		/// <summary>Where its look came from, for the log: the biome(s) sharing it, or the fallback.</summary>
		public string Source;
		/// <summary>Typical blade height, metres (the mesh's height × the prototype's mean height scale × the look's).</summary>
		public float Height;
		/// <summary>The look's height scale <see cref="Height"/> was built with (the renderer rescales live edits by it).</summary>
		public float BuiltHeightScale = 1f;
		/// <summary>Linear colours at the root and the tip (the mesh's sRGB vertex colours, linearised, × _BaseColor).</summary>
		public Color Root, Tip;
		/// <summary>Linear healthy and dry tints (the rule's for a rule's prototype, else the material's), the spread between neighbours, the patch size.</summary>
		public Color Healthy, Dry;
		public float TintSpread, PatchMetres, SnowBury;
		/// <summary>
		/// The look: the biome template's own entry (read live by the renderer, so an edit to it in play shows at once), or a
		/// copy of the old look (<see cref="BiomeGrassDefaults"/>) where the biome has none.
		/// </summary>
		public BiomeGrassType Look;
		/// <summary>True when the colours came from the mesh's vertex colours (false: the look's, or the fallback greens).</summary>
		public bool ColoursFromMesh;
		/// <summary>
		/// Linear head colours, up to <see cref="MaxHeadColours"/>: a flower mesh's petal colours (its head vertices,
		/// written with vertex alpha 0.3 by the art generator), most common first; empty when the mesh has none or the look's
		/// own win (<see cref="BiomeGrassType.HeadColoursOverMesh"/>; the look's <see cref="BiomeGrassType.HeadColours"/> are used).
		/// </summary>
		public readonly List<Color> HeadColours = new List<Color>();

		public const int MaxHeadColours = BiomeGrassType.MaxHeadColours;

		/// <summary>Vertex alpha below this marks a head (petal) vertex; stems and leaves are written at 1.</summary>
		public const float HeadVertexAlpha = 0.5f;

		private static readonly Color FallbackRoot = new Color(0.035f, 0.07f, 0.012f);
		private static readonly Color FallbackTip = new Color(0.12f, 0.26f, 0.035f);

		/// <summary>The tints (linear) a type has where nothing gives it its own (no rule, no material), as it always had.</summary>
		public static readonly Color DefaultHealthy = new Color(0.9f, 0.95f, 0.9f), DefaultDry = new Color(0.75f, 0.7f, 0.55f);

		/// <summary>
		/// The healthy and dry tints (linear) a prototype's blades take: its rule's (<paramref name="fromRule"/>: the scatter wrote
		/// the rule's <c>detailHealthyColor</c>/<c>detailDryColor</c> into it; for a generated detail they equal its material's,
		/// both being the catalogue's), else its prefab material's (as every type had before 2026-10-15), else the defaults.
		/// </summary>
		public static void TintsOf(DetailPrototype prototype, bool fromRule, out Color healthy, out Color dry)
		{
			if (fromRule)
			{
				healthy = prototype.healthyColor.linear;
				dry = prototype.dryColor.linear;
				return;
			}
			healthy = DefaultHealthy;
			dry = DefaultDry;
			MeshRenderer renderer = prototype.prototype != null ? prototype.prototype.GetComponent<MeshRenderer>() : null;
			Material material = renderer != null ? renderer.sharedMaterial : null;
			if (material != null)
			{
				if (material.HasProperty("_HealthyColor")) healthy = material.GetColor("_HealthyColor").linear;
				if (material.HasProperty("_DryColor")) dry = material.GetColor("_DryColor").linear;
			}
		}

		/// <summary>
		/// A type of a prototype's prefab with one look, its linear tints given (<see cref="TintsOf"/>). The height is this
		/// prototype's (the first of the prefab to be taken, as it always was); the colours the mesh's unless the look's win.
		/// </summary>
		public static GrassType Build(DetailPrototype prototype, BiomeGrassType look, Color healthy, Color dry)
		{
			GameObject prefab = prototype.prototype;
			MeshFilter filter = prefab != null ? prefab.GetComponent<MeshFilter>() : null;
			MeshRenderer renderer = prefab != null ? prefab.GetComponent<MeshRenderer>() : null;
			Mesh mesh = filter != null ? filter.sharedMesh : null;
			Material material = renderer != null ? renderer.sharedMaterial : null;
			look = look ?? new BiomeGrassType { GroundTint = BiomeGrassType.LegacyGroundTint };

			float meshHeight = mesh != null ? mesh.bounds.max.y : 0.4f;
			if (meshHeight < 0.05f)
			{
				meshHeight = 0.4f;
			}
			float scale = 0.5f * (Mathf.Max(0.1f, prototype.minHeight) + Mathf.Max(0.1f, prototype.maxHeight));
			var type = new GrassType
			{
				Prefab = prefab,
				Name = prefab != null ? prefab.name : "grass",
				// Room for the clump and own height variation (up to about 1.43x) and margin under the packed height's ceiling;
				// a Tall type packs its tallest blades at half resolution (GrassMath.TallShift) and so may stand twice as high.
				Height = Mathf.Clamp(meshHeight * scale * look.HeightScale, 0.05f, GrassMath.MaxPackedHeight * (look.Tall ? GrassMath.TallHeightScale : 1f) / 1.6f),
				BuiltHeightScale = Mathf.Max(0.05f, look.HeightScale),
				Root = FallbackRoot,
				Tip = FallbackTip,
				Healthy = healthy,
				Dry = dry,
				TintSpread = 0.35f,
				PatchMetres = 12f,
				SnowBury = 0.8f,
				Look = look,
			};
			bool ownColours = look.ColoursOverMesh && look.HasBladeColours;
			if (mesh != null && mesh.isReadable && !ownColours)
			{
				type.ColoursFromMesh = ReadColours(mesh, out type.Root, out type.Tip, type.HeadColours);
			}
			else if (mesh != null && mesh.isReadable)
			{
				ReadColours(mesh, out _, out _, type.HeadColours);
			}
			if (look.HeadColoursOverMesh)
			{
				type.HeadColours.Clear();
			}
			// A mesh that gives no colours (none written, not readable), or a look whose colours win: the look's own, where it
			// has them (the new types' design colours), rather than the built-in greens on a golden savanna or a grey dune grass.
			if (!type.ColoursFromMesh && look.HasBladeColours)
			{
				type.Root = look.RootColour.linear;
				type.Tip = look.TipColour.linear;
			}
			if (material != null)
			{
				Color baseColour = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor").linear : Color.white;
				type.Root *= baseColour;
				type.Tip *= baseColour;
				if (material.HasProperty("_TintSpread")) type.TintSpread = material.GetFloat("_TintSpread");
				if (material.HasProperty("_TintPatchMetres")) type.PatchMetres = material.GetFloat("_TintPatchMetres");
				if (material.HasProperty("_SnowBury")) type.SnowBury = material.GetFloat("_SnowBury");
			}
			return type;
		}

		/// <summary>
		/// The mean vertex colour of the bottom and the top fifth of the mesh's stems and blades (the generator darkens
		/// the root), and its head colours: the petal vertices (alpha under <see cref="HeadVertexAlpha"/>) are left
		/// out of the stem's colours and gathered by colour, the most common first.
		/// </summary>
		private static bool ReadColours(Mesh mesh, out Color root, out Color tip, List<Color> heads)
		{
			root = FallbackRoot;
			tip = FallbackTip;
			var colours = new List<Color32>();
			var vertices = new List<Vector3>();
			mesh.GetColors(colours);
			mesh.GetVertices(vertices);
			if (colours.Count != vertices.Count || colours.Count == 0)
			{
				return false;
			}
			var petals = new Dictionary<int, int>();
			float stemTop = float.MinValue, stemBottom = float.MaxValue;
			for (int i = 0; i < vertices.Count; i++)
			{
				Color32 c = colours[i];
				if (c.a / 255f < HeadVertexAlpha)
				{
					int key = c.r << 16 | c.g << 8 | c.b;
					petals[key] = petals.TryGetValue(key, out int n) ? n + 1 : 1;
					continue;
				}
				stemTop = Mathf.Max(stemTop, vertices[i].y);
				stemBottom = Mathf.Min(stemBottom, vertices[i].y);
			}
			var ranked = new List<KeyValuePair<int, int>>(petals);
			ranked.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
			for (int i = 0; i < ranked.Count && heads.Count < MaxHeadColours; i++)
			{
				int key = ranked[i].Key;
				heads.Add(((Color)new Color32((byte)(key >> 16), (byte)(key >> 8), (byte)key, 255)).linear);
			}
			if (stemTop < stemBottom)
			{
				return false;
			}
			float low = stemBottom + 0.2f * (stemTop - stemBottom), high = stemTop - 0.2f * (stemTop - stemBottom);
			Vector4 r = Vector4.zero, t = Vector4.zero;
			int nr = 0, nt = 0;
			for (int i = 0; i < vertices.Count; i++)
			{
				Color c = colours[i];
				if (c.a < HeadVertexAlpha)
				{
					continue;
				}
				if (vertices[i].y <= low)
				{
					r += (Vector4)c;
					nr++;
				}
				else if (vertices[i].y >= high)
				{
					t += (Vector4)c;
					nt++;
				}
			}
			if (nr == 0 || nt == 0)
			{
				return false;
			}
			// The generator writes its hex sRGB picks into the vertex colours; FishMMO/Vegetation multiplied them in
			// raw (as if linear), which is part of why the clumps read pale. Blades take them as the colours they are.
			root = ((Color)(r / nr)).linear;
			tip = ((Color)(t / nt)).linear;
			return true;
		}
	}

	/// <summary>
	/// One terrain as the blade grass reads it: its grass prototypes' detail layers as density maps (RGBA8,
	/// four channels a texture, each layer divided by the terrain's top detail value), its own heightmap
	/// texture for the roots, and a coarse min/max height grid for the CPU's tile bounds. Built once when
	/// the terrain is taken (CPU), released with it.
	/// </summary>
	public sealed class GrassTerrain : IDisposable
	{
		/// <summary>
		/// Blade types one terrain can carry, a density channel each (<see cref="MaxDensityMaps"/> maps of four). Was 8: a
		/// terrain across wetland, forest and meadow (sedge, rush, reeds, Phragmites, wet and woodland flowers, three
		/// grasses…) ran past it, and every type past the eighth stayed meshes on that terrain. 32 on 2026-10-27 (was 16), and
		/// 64 later that day, once the detail layers stopped wrapping at 256 and the tiles' true demand showed (painted blade
		/// types, measured over the saved tiles): Moron 1 at most 20 a tile, Moron 2 46 (3_3), Moron 3 48 (2_1). A terrain past
		/// <see cref="NarrowChannels"/> needs the generate kernel's wide variant (GRASS_WIDE), one past <see cref="WideChannels"/>
		/// its widest (GRASS_WIDEST), chosen for the dispatch by <see cref="GrassTerrainAtlas.Tier"/> (<see cref="TierOf"/>), so
		/// a scene whose terrains all fit sixteen (or thirty-two) runs the kernel it always ran. Past the cap a look of a prefab
		/// already there joins its channel, and a prefab with none is not drawn there (an error).
		/// </summary>
		public const int MaxChannels = 64;

		/// <summary>The channels the narrow generate kernel reads (four density maps), what every terrain had until 2026-10-27.</summary>
		public const int NarrowChannels = 16;

		/// <summary>The channels the wide generate kernel (GRASS_WIDE) reads in registers: eight density maps.</summary>
		public const int WideChannels = 32;

		/// <summary>
		/// The generate kernel a terrain of <paramref name="channels"/> channels needs: 0 the narrow one (to
		/// <see cref="NarrowChannels"/>), 1 GRASS_WIDE (to <see cref="WideChannels"/>), 2 GRASS_WIDEST (to <see cref="MaxChannels"/>,
		/// which walks only the maps the terrain has instead of holding every channel in registers).
		/// </summary>
		public static int TierOf(int channels) => channels > WideChannels ? 2 : channels > NarrowChannels ? 1 : 0;

		/// <summary>Whether a terrain past <see cref="MaxChannels"/> blade looks has been reported (once a session).</summary>
		private static bool warnedChannels;

		/// <summary>Density maps (RGBA8, four channels each) a terrain can have: only as many as its channels need are built.</summary>
		public const int MaxDensityMaps = MaxChannels / 4;

		/// <summary>The min/max grid's cell, metres.</summary>
		public const float BoundsCellMetres = 8f;

		public Terrain Terrain;
		public TerrainData Data;
		public Vector3 Origin;
		public Vector3 Size;
		/// <summary>
		/// Per density channel, the grass type's index (−1 none). A channel is a TYPE, the sum of every prototype
		/// of it: the scatter makes one prototype per biome rule, so a terrain carries several of one prefab
		/// (15 Detail_Grass* prototypes of 4 prefabs on Baoakraal), and a channel per prototype left all past
		/// the eighth as meshes, and a terrain whose first eight grew nowhere as meshes entirely.
		/// </summary>
		public readonly int[] ChannelTypes = new int[MaxChannels];
		public int Channels;
		/// <summary>The prototypes read into the channels (drawn as blades).</summary>
		public int Layers;
		/// <summary>
		/// The density maps, channels 4m..4m+3 in map m: as many as the terrain's channels need (1..<see cref="MaxDensityMaps"/>),
		/// so a terrain of four types or fewer costs one map, as it always did. Null until built.
		/// </summary>
		public Texture2D[] Density;

		/// <summary>How many density maps the terrain has (0 until built).</summary>
		public int DensityMaps => Density != null ? Density.Length : 0;
		/// <summary>The prototypes drawn as blades (by prototype index), what the detail renderer skips.</summary>
		public bool[] Skip;
		public int DensityResolution;
		/// <summary>
		/// Per density texel (<see cref="DensityResolution"/> square, row z first), 1 where any blade type grows; null
		/// until built (then every tile is taken to have grass). Lets the per-camera gather skip a tile with nothing on it
		/// before any of its bounds or frustum work: a beach or a rock face paid the whole gather and drew no blade.
		/// </summary>
		public byte[] Occupied;
		/// <summary>
		/// Per density texel (as <see cref="Occupied"/>), the grass standing there in metres: the tallest type's height
		/// weighted by its density. What hides an animal on the ground; null until built (<see cref="CoverAt"/> reads 0).
		/// </summary>
		public float[] Cover;
		/// <summary>
		/// Per density texel (as <see cref="Occupied"/>), how much of the ground is under blades, 0..1: the blade types'
		/// densities added up. What shades the ground under the sward (<see cref="GrassSwardMap"/>); null until built.
		/// </summary>
		public byte[] Sward;
		public int HeightResolution;
		public float MaxGrassHeight;
		public bool HeightChecked;
		/// <summary>The terrain array set the ground is drawn with (TerrainArrayBinder), for the blades' colour; null until bound.</summary>
		public FishMMO.Shared.TerrainArraySet Arrays;
		/// <summary>
		/// The splat layers under each alphamap texel, packed (<see cref="GrassMath.PackSurfaceLayers"/>: two strongest
		/// layer ids and the first's share), the grass's own copy read by the compute instead of the terrain's 1..8
		/// control maps. Null where the terrain has no layers.
		/// </summary>
		public Texture2D SurfaceLayers;

		/// <summary>
		/// Per heightmap sample (same grid as <see cref="Heightmap"/>), the ground's height over the river or lake water
		/// standing at it: RG8, R 0..1 = −<see cref="FreeboardRangeMetres"/> … +<see cref="FreeboardRangeMetres"/>, 1 where
		/// there is none. Null when no water reaches the terrain and it has no hole. Read bilinearly like the heights, its zero
		/// is the line where the water meets the ground, and the compute grows no blade below it. G is the terrain's painted
		/// holes (<see cref="TerrainHoleMask"/>): texel (x, z) is heightmap cell (x, z), 0 a hole, 1 solid, point-read as the
		/// terrain clips it, and no blade grows over a hole.
		/// </summary>
		/// <remarks>
		/// The density maps cannot cut a creek: they are 2 m texels smoothed over 2 m (the scatter's points made into a
		/// field), so a channel a few metres wide was filled from both banks and its water drawn under a lawn. Nor can they
		/// leave a cave mouth bare: the detail layers under a hole keep what was painted before it was cut.
		/// </remarks>
		public Texture2D Freeboard;

		/// <summary>The freeboard map's range either side of the water line, metres (FishGrassBlades.compute GRASS_FREEBOARD_RANGE).</summary>
		public const float FreeboardRangeMetres = 2f;

		/// <summary>
		/// How far past a river's banks (or round a lake's mask) a heightmap sample takes its water's level: a little over
		/// one 2 m sample, so a creek narrower than the grid still gives the samples either side of it a level to cut against.
		/// Ground there above the water stays positive, so the reach moves no bank.
		/// </summary>
		public const float WaterReachMetres = FishMMO.Shared.Biomes.SceneWaterBodies.MaxReachMetres;

		private float[] minHeights, maxHeights;
		/// <summary>The cover grid being weighed on a worker for a terrain taken from its bake (<see cref="Cover"/> once it lands).</summary>
		private Task<float[]> coverWork;

		/// <summary>Whether <see cref="CoverAt"/> answers from a finished cover map (false while there is none or it is building).</summary>
		public bool CoverSettled => Cover != null || (coverWork != null && coverWork.IsCompleted);
		/// <summary>The water-line map's texels while building, two bytes each (<see cref="Freeboard"/>: R the line, G solid ground).</summary>
		private byte[] freeboardTexels;
		private bool anyWater, anyHole;
		private int gridX, gridZ;

		/* The grass's own copy of the terrain's heights (TerrainData.GetHeights, normalised 0..1 of size.y), filled
		 * from the strips the build reads anyway. TerrainData.heightmapTexture is NOT the same on every graphics
		 * API: its values were right on OpenGL (0.01 m off) and ~1.8 km off on Vulkan, which stood the blades in
		 * columns up into the sky. R16 where the device samples it, else RFloat (WebGPU has no core r16unorm). */
		private Texture2D heightTexture;
		private ushort[] heightTexels16;
		private float[] heightTexels32;

		public Texture Heightmap => heightTexture;

		/// <summary>Metres per unit of <see cref="Heightmap"/> (it holds 0..1 of the terrain's height).</summary>
		public float HeightScale => Size.y;

		/// <summary>
		/// Starts building a terrain's density maps and height grid a strip at a time (<see cref="Builder"/>);
		/// null when there is nothing to build. Reading every grass layer and every height sample at once cost
		/// 25–240 ms per terrain, a frame each, as a scene's terrains were taken.
		/// </summary>
		/// <param name="prototypes">The terrain's blade-grass prototype indices.</param>
		/// <param name="types">Each prototype's grass type (parallel to <paramref name="prototypes"/>).</param>
		public static Builder Begin(Terrain terrain, List<int> prototypes, List<GrassType> types)
		{
			TerrainData data = terrain != null ? terrain.terrainData : null;
			return prototypes.Count == 0 || data == null ? null : new Builder(terrain, data, prototypes, types);
		}

		/// <summary>
		/// A terrain's grass under construction: <see cref="Step"/> reads <see cref="StripRows"/> rows of one grass
		/// layer (or of the heightmap, the alphamaps, the holes) at a time until the frame's budget is spent, then finishes
		/// with the textures. The terrain's mesh grass keeps drawing until it is <see cref="Done"/>.
		/// </summary>
		/// <remarks>
		/// Only the TerrainData reads and the textures are the main thread's. Everything worked out from them (the layers
		/// summed into blocks, the smoothing, the normalising and packing, the height grid, the water line, the holes, the
		/// surface layers) runs on a worker (<see cref="TerrainBuildQueue"/>), as the same managed code in the same order,
		/// so every map is bit for bit what it was; the smoothing and normalising run a channel per worker
		/// (<see cref="BuildDensity"/>), each channel's own operations in their own order. The main thread's share fell
		/// from 25–240 ms a terrain to its reads.
		/// </remarks>
		public sealed class Builder
		{
			public const int StripRows = 64;

			/// <summary>
			/// Metres the detail layers are smoothed over. The scatter writes most grass as points (one cell
			/// per spawned clump, the rest zero): right for the mesh clumps, but read as a density field it grew
			/// blades only in those cells — scattered islands of a few blades. Smoothed, it says how much grass
			/// an area has, and the blades fill it.
			/// </summary>
			public const float SmoothMetres = 2f;

			/// <summary>
			/// A sprinkled species' channel (scattered points) is scaled so this quantile of its non-zero smoothed texels reads
			/// as full: its layer says where it grows, its own chance how thick. A tussock channel is never scaled: it is the
			/// biome rule's carpet coverage, and scaling each terrain's to full grew a desert's dune grass (0.25) and a beach's
			/// as thick as a meadow's.
			/// </summary>
			public const float FullQuantile = 0.95f;

			/// <summary>The density maps' texel size, metres (the detail layer summed into blocks this size).</summary>
			public const float DensityTexelMetres = 2f;

			/// <summary>Strips read ahead of the worker at most: past it the main thread waits a frame (bounded memory).</summary>
			public const int MaxPendingStrips = 8;

			/// <summary>Progress, for the log: which stage and how far.</summary>
			public string Progress => layer < layerPrototypes.Count ? $"reading layer {layer + 1}/{layerPrototypes.Count}" : densityWork == null ? "summing layers" : !textures ? "smoothing" : heightRow < gt.HeightResolution ? "heights" : !holesCut ? "holes" : surfaceRow < surfaceRows ? "surface layers" : "packing";

			public readonly Terrain Terrain;
			public GrassTerrain Result { get; private set; }
			public bool Done { get; private set; }
			/// <summary>True when the build gave up on a worker's exception (logged): the terrain is taken again from the start.</summary>
			public bool Failed { get; private set; }
			/// <summary>Main-thread milliseconds spent (the workers' time is not the frame's).</summary>
			public double Milliseconds { get; private set; }
			public int Frames { get; private set; }

			private readonly GrassTerrain gt;
			/// <summary>The prototypes read, and the channel (type) each one adds into.</summary>
			private readonly List<int> layerPrototypes = new List<int>(), layerChannels = new List<int>();
			private readonly int count, res;
			/// <summary>
			/// The density maps' resolution: the detail layer's summed into <see cref="factor"/>² blocks (about
			/// 2 m texels). Smoothed over 2 m anyway, full detail resolution only cost time — 1056² × 8 layers × 4
			/// blur passes per terrain kept the terrain underfoot on the mesh grass for many seconds — and memory.
			/// </summary>
			private readonly int dres, factor;
			private readonly float top;
			/// <summary>Each channel's density while building, 0..1, at full precision: smoothing a point field in bytes rounded it to nothing.</summary>
			private readonly float[][] density;
			/// <summary>Each channel's blade type height, metres (for <see cref="GrassTerrain.Cover"/>).</summary>
			private readonly float[] channelHeights;
			/// <summary>True for a sprinkled species' channel (its layer is scattered points, normalised); false for coverage.</summary>
			private readonly bool[] channelPoints;
			/// <summary>The worker's queue: the strips' arithmetic, in order.</summary>
			private readonly TerrainBuildQueue queue = new TerrainBuildQueue();
			private int layer, row, heightRow, surfaceRow, surfaceRows = int.MaxValue;
			/// <summary>The terrain's painted holes, read a strip at a time after the heights; null until then.</summary>
			private TerrainHoleMask holes;
			private bool holesCut, waterQueued, heightsBegun;
			/// <summary>The scene's lakes and rivers that reach this terrain; null for none.</summary>
			private List<FishMMO.Shared.Biomes.SceneWaterBodies> waters;
			private Color32[] surfacePixels;
			// A tile without alphamaps (a client build's copy): its stored index map, looked up once (null when it has none).
			private FishMMO.Shared.TerrainDetailBake.IndexMap surfaceIndex;
			private bool surfaceIndexLooked;
			// The scene layer each alphamap channel holds (FishMMO.Shared.TerrainTileLayers), asked once per build.
			private int[] surfaceSceneLayers;
			private bool surfaceSceneLayersLooked;
			/// <summary>Set by the worker when any layer has grass (read on the main thread once the queue is idle).</summary>
			private bool any;
			private bool textures;
			/// <summary>The smoothing, normalising and packing of every channel (a worker's), started once the layers are summed.</summary>
			private Task<DensityResult> densityWork;
			/// <summary>Each channel's prefab (its type's): what a bake records, and what one must match to be taken.</summary>
			private readonly GameObject[] channelPrefabs;

			/// <summary>
			/// Set before the first <see cref="Step"/> (the editor's bake, TerrainDetailBaker): the maps are kept as they are made,
			/// in <see cref="Captured"/> and <see cref="CapturedHeights"/>, once <see cref="Done"/>.
			/// </summary>
			public bool Capture;

			/// <summary>Errors of this terrain's channels: a prefab past <see cref="MaxChannels"/>, not drawn there (the bake reports them).</summary>
			public readonly List<string> Overflow = new List<string>();
			public FishMMO.Shared.TerrainDetailBake.Grass Captured { get; private set; }
			/// <summary>The heights as the build stored them (16 bits, 0..1 of the terrain's height), for the bake.</summary>
			public ushort[] CapturedHeights { get; private set; }

			internal Builder(Terrain terrain, TerrainData data, List<int> prototypes, List<GrassType> types)
			{
				Terrain = terrain;
				// One channel per type, in order of first appearance. Past MaxChannels, a type whose prefab already has a
				// channel here adds into that one (another biome's look of the same plant, GrassTypeTable's merge per terrain);
				// one whose prefab has none is not drawn here (an error: there is no mesh grass).
				var channelTypes = new List<GrassType>();
				for (int i = 0; i < prototypes.Count; i++)
				{
					int c = channelTypes.IndexOf(types[i]);
					if (c < 0)
					{
						if (channelTypes.Count >= MaxChannels)
						{
							c = channelTypes.FindIndex(t => t != null && types[i] != null && t.Prefab == types[i].Prefab);
							if (c < 0)
							{
								string message = $"[Grass] Terrain '{terrain.name}' grows more than {MaxChannels} blade prefabs: '{types[i].Name}' is not drawn there (GPU only: raise GrassTerrain.MaxChannels).";
								if (Overflow.Count == 0)
								{
									Debug.LogError(message);
								}
								Overflow.Add(message);
								continue;
							}
							if (!warnedChannels)
							{
								warnedChannels = true;
								Debug.LogWarning($"[Grass] Terrain '{terrain.name}' grows more than {MaxChannels} blade looks: '{types[i].Name}' of {types[i].Source} is drawn with the look of {channelTypes[c].Source} there (said once a session).");
							}
						}
						else
						{
							c = channelTypes.Count;
							channelTypes.Add(types[i]);
						}
					}
					layerPrototypes.Add(prototypes[i]);
					layerChannels.Add(c);
				}
				count = channelTypes.Count;
				channelPrefabs = new GameObject[count];
				for (int c = 0; c < count; c++)
				{
					channelPrefabs[c] = channelTypes[c] != null ? channelTypes[c].Prefab : null;
				}
				channelHeights = new float[count];
				for (int c = 0; c < count; c++)
				{
					channelHeights[c] = channelTypes[c] != null ? Mathf.Max(0f, channelTypes[c].Height) : 0f;
				}
				channelPoints = new bool[count];
				for (int c = 0; c < count; c++)
				{
					channelPoints[c] = channelTypes[c] != null && channelTypes[c].Look != null && channelTypes[c].Look.Sprinkle > 0f;
				}
				res = Mathf.Max(1, data.detailResolution);
				top = Mathf.Max(1, data.maxDetailScatterPerRes);
				factor = Mathf.Max(1, Mathf.RoundToInt(DensityTexelMetres / Mathf.Max(0.05f, data.size.x / res)));
				dres = Mathf.Max(1, (res + factor - 1) / factor);
				gt = new GrassTerrain
				{
					Terrain = terrain,
					Data = data,
					Origin = terrain.GetPosition(),
					Size = data.size,
					Channels = count,
					Layers = layerPrototypes.Count,
					DensityResolution = dres,
					HeightResolution = data.heightmapResolution,
					Skip = new bool[data.detailPrototypes.Length],
				};
				for (int c = 0; c < MaxChannels; c++)
				{
					gt.ChannelTypes[c] = c < count ? channelTypes[c].Index : -1;
				}
				for (int c = 0; c < count; c++)
				{
					gt.MaxGrassHeight = Mathf.Max(gt.MaxGrassHeight, channelTypes[c].Height);
				}
				foreach (int p in layerPrototypes)
				{
					gt.Skip[p] = true;
				}
				density = new float[count][];
				for (int c = 0; c < count; c++)
				{
					density[c] = new float[dres * dres];
				}
				/* The water is in the terrains' scene, enabled with it on load, so it is there before any terrain is
				 * taken. Its levels are world metres (the scene is cut at the origin). */
				var area = new Rect(gt.Origin.x, gt.Origin.z, gt.Size.x, gt.Size.z);
				foreach (FishMMO.Shared.Biomes.SceneWaterBodies bodies in UnityEngine.Object.FindObjectsByType<FishMMO.Shared.Biomes.SceneWaterBodies>())
				{
					if (Live(bodies) && bodies.Reaches(area))
					{
						(waters ??= new List<FishMMO.Shared.Biomes.SceneWaterBodies>()).Add(bodies);
					}
				}
			}

			/// <summary>
			/// Adds one strip of a detail layer (rows <paramref name="rowBase"/>.. of <paramref name="res"/>) into a channel's
			/// <paramref name="factor"/>² blocks, each cell's share of its layer's top value; true when any cell had grass.
			/// Prototypes of one type add up; the channel is normalised afterwards and clamped to 1.
			/// </summary>
			public static bool AddStrip(int[,] cells, int rows, int rowBase, int res, int factor, int dres, float top, float[] target)
			{
				bool any = false;
				float blockWeight = 1f / (factor * factor);
				for (int z = 0; z < rows; z++)
				{
					for (int x = 0; x < res; x++)
					{
						int v = cells[z, x];
						if (v > 0)
						{
							any = true;
							target[((rowBase + z) / factor) * dres + x / factor] += Mathf.Min(1f, v / top) * blockWeight;
						}
					}
				}
				return any;
			}

			/// <summary>
			/// One channel smoothed in place: (horizontal, vertical) × 2 iterations of a box of <paramref name="radius"/>
			/// texels (≈ a Gaussian), running sums in floats, every line in order. <paramref name="line"/>: scratch, at least
			/// <paramref name="dres"/> long.
			/// </summary>
			public static void Smooth(float[] target, int dres, int radius, float[] line)
			{
				for (int pass = 0; pass < 4; pass++)
				{
					bool horizontal = (pass & 1) == 0;
					for (int l = 0; l < dres; l++)
					{
						for (int i = 0; i < dres; i++)
						{
							line[i] = target[horizontal ? l * dres + i : i * dres + l];
						}
						float sum = 0f;
						int n = 0;
						for (int i = 0; i < Mathf.Min(radius, dres); i++)
						{
							sum += line[i];
							n++;
						}
						for (int i = 0; i < dres; i++)
						{
							int add = i + radius, drop = i - radius - 1;
							if (add < dres)
							{
								sum += line[add];
								n++;
							}
							if (drop >= 0)
							{
								sum -= line[drop];
								n--;
							}
							target[horizontal ? l * dres + i : i * dres + l] = Mathf.Max(0f, sum / n);
						}
					}
				}
			}

			/// <summary>A point channel normalised (<see cref="Normalise"/>); a coverage channel kept as painted, clamped to 1.</summary>
			public static void Scale(float[] values, bool points)
			{
				if (points)
				{
					Normalise(values);
					return;
				}
				for (int i = 0; i < values.Length; i++)
				{
					values[i] = Mathf.Min(1f, values[i]);
				}
			}

			/// <summary>Scales a channel so its <see cref="FullQuantile"/> of non-zero texels reads 1.</summary>
			public static void Normalise(float[] values)
			{
				float max = 0f;
				int nonZero = 0;
				foreach (float v in values)
				{
					if (v > 1e-5f)
					{
						nonZero++;
						if (v > max) max = v;
					}
				}
				if (nonZero == 0 || max <= 0f)
				{
					return;
				}
				const int Bins = 1024;
				var histogram = new int[Bins];
				foreach (float v in values)
				{
					if (v > 1e-5f)
					{
						histogram[Mathf.Min(Bins - 1, (int)(v / max * Bins))]++;
					}
				}
				int want = Mathf.CeilToInt(nonZero * FullQuantile), seen = 0;
				float level = max;
				for (int b = 0; b < Bins; b++)
				{
					seen += histogram[b];
					if (seen >= want)
					{
						level = (b + 1) / (float)Bins * max;
						break;
					}
				}
				float scale = 1f / Mathf.Max(1e-5f, level);
				for (int i = 0; i < values.Length; i++)
				{
					values[i] = Mathf.Min(1f, values[i] * scale);
				}
			}

			/// <summary>Four channels from <paramref name="first"/> packed into a texture's bytes.</summary>
			public static Color32[] Pack(float[][] density, int count, int first, int texels)
			{
				var pixels = new Color32[texels];
				for (int i = 0; i < pixels.Length; i++)
				{
					byte B(int c) => first + c < count ? (byte)Mathf.Clamp(Mathf.RoundToInt(density[first + c][i] * 255f), 0, 255) : (byte)0;
					pixels[i] = new Color32(B(0), B(1), B(2), B(3));
				}
				return pixels;
			}

			/// <summary>What <see cref="BuildDensity"/> gives the textures: the maps' bytes and the per-texel grids.</summary>
			public sealed class DensityResult
			{
				public Color32[][] Maps;
				public byte[] Occupied, Sward;
				public float[] Cover;
			}

			/// <summary>
			/// Every channel smoothed, the point channels (<paramref name="points"/>) normalised and the coverage channels
			/// clamped to 1 (a channel per worker where there are threads, each channel's own operations in order, so the same
			/// floats either way), then the maps packed, and where grass grows, how tall it stands and how much of the ground it
			/// covers, texel by texel with the channels in order.
			/// </summary>
			public static DensityResult BuildDensity(float[][] density, int count, int dres, int radius, float[] channelHeights, bool[] points, bool parallel)
			{
				if (parallel && count > 1)
				{
					Parallel.For(0, count, c =>
					{
						Smooth(density[c], dres, radius, new float[dres]);
						Scale(density[c], points[c]);
					});
				}
				else
				{
					var line = new float[dres];
					for (int c = 0; c < count; c++)
					{
						Smooth(density[c], dres, radius, line);
						Scale(density[c], points[c]);
					}
				}
				int texels = dres * dres;
				// As many maps as the channels need (at least one): map m holds channels 4m..4m+3.
				int maps = Mathf.Clamp((count + 3) / 4, 1, MaxDensityMaps);
				var result = new DensityResult { Maps = new Color32[maps][], Occupied = new byte[texels], Cover = new float[texels], Sward = new byte[texels] };
				for (int m = 0; m < maps; m++)
				{
					Color32[] pixels = Pack(density, count, 4 * m, texels);
					result.Maps[m] = pixels;
					for (int i = 0; i < texels; i++)
					{
						Color32 a = pixels[i];
						if (a.r > 1 || a.g > 1 || a.b > 1 || a.a > 1)
						{
							result.Occupied[i] = 1;
						}
					}
				}
				float[] cover = result.Cover;
				for (int c = 0; c < count; c++)
				{
					float[] d = density[c];
					float h = channelHeights[c];
					for (int i = 0; i < texels; i++)
					{
						cover[i] = Mathf.Max(cover[i], d[i] * h);
					}
				}
				byte[] sward = result.Sward;
				for (int i = 0; i < texels; i++)
				{
					float sum = 0f;
					for (int c = 0; c < count; c++)
					{
						sum += density[c][i];
					}
					sward[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(sum) * 255f);
				}
				return result;
			}

			/// <summary>Packs a strip of alphamaps (rows <paramref name="rowBase"/>.., <paramref name="ares"/> wide) into the surface-layer texels.</summary>
			/// <param name="sceneLayers">The scene layer each channel holds (FishMMO.Shared.TerrainTileLayers.Of), or null where they are the same.</param>
			public static void PackSurfaceStrip(float[,,] maps, int rows, int rowBase, int ares, int alayers, Color32[] pixels, float[] weights, int[] sceneLayers = null)
			{
				for (int r = 0; r < rows; r++)
				{
					for (int x = 0; x < ares; x++)
					{
						for (int l = 0; l < alayers; l++)
						{
							weights[l] = maps[r, x, l];
						}
						uint packed = GrassMath.RenameSurfaceLayers(GrassMath.PackSurfaceLayers(weights, alayers), sceneLayers);
						pixels[(rowBase + r) * ares + x] = new Color32((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24));
					}
				}
			}

			/// <summary>
			/// Packs a strip of a stored index map (rows <paramref name="rowBase"/>.., <paramref name="ares"/> wide, two words a
			/// texel) into the surface-layer texels: a client build's tile, whose alphamaps were left out.
			/// </summary>
			public static void PackSurfaceStrip(uint[] index, int rows, int rowBase, int ares, Color32[] pixels)
			{
				for (int r = 0; r < rows; r++)
				{
					for (int x = 0; x < ares; x++)
					{
						int i = (rowBase + r) * ares + x;
						uint packed = GrassMath.PackSurfaceFromIndex(index[2 * i], index[2 * i + 1]);
						pixels[i] = new Color32((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24));
					}
				}
			}

			/// <summary>Drops an unfinished build, freeing anything it created; its queued work is skipped.</summary>
			public void Abandon()
			{
				queue.Cancel();
				if (!Done || Result == null)
				{
					gt.Dispose();
				}
				Done = true;
				Result = null;
			}

			/// <summary>A bake entry with this build's prototypes, channels and their prefabs; <paramref name="present"/> whether it has blades.</summary>
			private FishMMO.Shared.TerrainDetailBake.Grass Layout(bool present)
			{
				return new FishMMO.Shared.TerrainDetailBake.Grass
				{
					Baked = true,
					Present = present,
					LayerPrototypes = layerPrototypes.ToArray(),
					LayerChannels = layerChannels.ToArray(),
					ChannelPrefabs = (GameObject[])channelPrefabs.Clone(),
				};
			}

			/// <summary>Whether a bake was made from the same prototypes, read into the same channels, of the same prefabs.</summary>
			private bool SameLayout(int[] prototypes, int[] channels, GameObject[] prefabs)
			{
				if (prototypes == null || channels == null || prefabs == null || prototypes.Length != layerPrototypes.Count
					|| channels.Length != layerChannels.Count || prefabs.Length != channelPrefabs.Length)
				{
					return false;
				}
				for (int i = 0; i < prototypes.Length; i++)
				{
					if (prototypes[i] != layerPrototypes[i] || channels[i] != layerChannels[i])
					{
						return false;
					}
				}
				for (int c = 0; c < prefabs.Length; c++)
				{
					if (prefabs[c] != channelPrefabs[c])
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>The heights, the height grid and the water line as the build left them, into the bake (before the textures take them).</summary>
			private void CaptureHeights()
			{
				int hres = gt.heightResolution;
				if (gt.heightTexels16 != null)
				{
					CapturedHeights = (ushort[])gt.heightTexels16.Clone();
				}
				else if (gt.heightTexels32 != null)
				{
					CapturedHeights = new ushort[gt.heightTexels32.Length];
					for (int i = 0; i < CapturedHeights.Length; i++)
					{
						CapturedHeights[i] = (ushort)Mathf.Clamp(Mathf.RoundToInt(gt.heightTexels32[i] * 65535f), 0, 65535);
					}
				}
				Captured.GridX = gt.gridX;
				Captured.GridZ = gt.gridZ;
				Captured.MinHeights = (float[])gt.minHeights.Clone();
				Captured.MaxHeights = (float[])gt.maxHeights.Clone();
				Captured.Freeboard = gt.freeboardTexels != null && (gt.anyWater || gt.anyHole) && gt.freeboardTexels.Length == hres * hres * 2
					? (byte[])gt.freeboardTexels.Clone() : new byte[0];
			}

			/// <summary>RGBA8 maps one after another, as bytes.</summary>
			private static byte[] Flatten(Color32[][] maps, int texels)
			{
				var bytes = new byte[maps.Length * texels * 4];
				int at = 0;
				foreach (Color32[] map in maps)
				{
					for (int i = 0; i < texels; i++)
					{
						Color32 c = map[i];
						bytes[at++] = c.r;
						bytes[at++] = c.g;
						bytes[at++] = c.b;
						bytes[at++] = c.a;
					}
				}
				return bytes;
			}

			/// <summary>
			/// Takes the terrain from its baked maps instead of reading it (<see cref="FishMMO.Shared.TerrainDetailBake"/>): true,
			/// and <see cref="Done"/>, when the bake was made from what this build would read (the same prototypes in the same
			/// channels of the same prefabs, the same resolutions); false, and the builder untouched, when not.
			/// </summary>
			/// <remarks>
			/// What the runtime decides is this builder's own: the channels' types and so their heights, from which the cover
			/// is weighed here off the packed maps (a byte's worth off the floats the full build weighs it from).
			/// </remarks>
			public bool TakeBaked(FishMMO.Shared.TerrainDetailBake.Tile tile)
			{
				FishMMO.Shared.TerrainDetailBake.Grass g = tile?.Grass;
				TerrainData data = gt.Data;
				if (Done || g == null || !g.Baked || data == null || !SameLayout(g.LayerPrototypes, g.LayerChannels, g.ChannelPrefabs))
				{
					return false;
				}
				if (!g.Present)
				{
					gt.Dispose();
					Done = true;
					Result = null;
					return true;
				}
				int hres = data.heightmapResolution, texels = dres * dres;
				int gridX = Mathf.Max(1, Mathf.CeilToInt(gt.Size.x / BoundsCellMetres)), gridZ = Mathf.Max(1, Mathf.CeilToInt(gt.Size.z / BoundsCellMetres));
				int maps = Mathf.Clamp((count + 3) / 4, 1, MaxDensityMaps);
				if (g.DensityResolution != dres || g.DensityMaps != maps || g.Density == null || g.Density.Length != maps * texels * 4
					|| g.Occupied == null || g.Occupied.Length != texels || g.Sward == null || g.Sward.Length != texels
					|| tile.HeightResolution != hres || tile.Heights == null || tile.Heights.Length != hres * hres
					|| g.GridX != gridX || g.GridZ != gridZ || g.MinHeights == null || g.MinHeights.Length != gridX * gridZ
					|| g.MaxHeights == null || g.MaxHeights.Length != gridX * gridZ
					// The surface layers name the terrain's own splat layers per alphamap texel (ids up to 255, the index map's
					// limit): a bake from alphamaps of another size is another terrain's ground.
					|| (g.SurfaceResolution > 0 && data.alphamapLayers > 0 && g.SurfaceResolution != data.alphamapResolution))
				{
					return false;
				}
				gt.Density = new Texture2D[maps];
				for (int m = 0; m < maps; m++)
				{
					gt.Density[m] = MakeTexture(g.Density, m * texels * 4, dres, $"{Terrain.name} grass density {4 * m}-{4 * m + 3}");
				}
				gt.Occupied = g.Occupied;
				gt.Sward = g.Sward;
				// On a worker: ~290k texels × up to 16 channels a terrain was 35–60 ms of the frame each baked terrain was taken
				// in (StartupTimeline, 2026-10-13). Nothing reads it before it lands but CoverAt, which reads 0 until then.
				byte[] packed = g.Density;
				int channels = count;
				float[] heights = (float[])channelHeights.Clone();
				if (TerrainBuildQueue.Inline)
				{
					gt.Cover = CoverOf(packed, maps, channels, texels, heights);
				}
				else
				{
					gt.coverWork = Task.Run(() => CoverOf(packed, maps, channels, texels, heights));
				}
				int ares = g.SurfaceResolution;
				if (ares > 0 && g.Surface != null && g.Surface.Length == ares * ares * 4)
				{
					gt.SurfaceLayers = MakeTexture(g.Surface, 0, ares, $"{Terrain.name} grass surface layers");
				}
				gt.heightResolution = hres;
				gt.gridX = gridX;
				gt.gridZ = gridZ;
				gt.minHeights = g.MinHeights;
				gt.maxHeights = g.MaxHeights;
				if (SystemInfo.SupportsTextureFormat(TextureFormat.R16))
				{
					gt.heightTexels16 = tile.Heights;
				}
				else
				{
					gt.heightTexels32 = new float[tile.Heights.Length];
					for (int i = 0; i < tile.Heights.Length; i++)
					{
						gt.heightTexels32[i] = tile.Heights[i] / 65535f;
					}
				}
				if (g.Freeboard != null && g.Freeboard.Length == hres * hres * 2)
				{
					gt.freeboardTexels = g.Freeboard;
					gt.anyWater = true;
				}
				gt.FinishHeightTexture();
				Result = gt;
				Done = true;
				return true;
			}

			/// <summary>
			/// Per density texel, the grass standing there in metres off packed maps: each channel's density (a byte) times its
			/// type's height, the tallest. <see cref="BuildDensity"/>'s rule, from the bytes.
			/// </summary>
			public static float[] CoverOf(byte[] density, int maps, int channels, int texels, float[] channelHeights)
			{
				var cover = new float[texels];
				for (int c = 0; c < channels && c < 4 * maps; c++)
				{
					float h = channelHeights[c] / 255f;
					int at = (c / 4) * texels * 4 + c % 4;
					for (int i = 0; i < texels; i++)
					{
						cover[i] = Mathf.Max(cover[i], density[at + 4 * i] * h);
					}
				}
				return cover;
			}

			/// <summary>A component in a scene as the build counts it: enabled in play, or (the editor's bake) enabled on an active object.</summary>
			private static bool Live(Behaviour b) => b != null && (b.isActiveAndEnabled || (!Application.isPlaying && b.enabled && b.gameObject.activeInHierarchy));

			/// <summary>Profiler marker of the main thread's share of a step (the reads, the textures).</summary>
			private static readonly Unity.Profiling.ProfilerMarker StepMarker = new Unity.Profiling.ProfilerMarker("GrassTerrain.Builder.Step");

			/// <summary>
			/// Works for at most <paramref name="budgetMs"/> of the main thread's time, or until it waits on the worker; true
			/// once finished (<see cref="Result"/> null: no grass there, or <see cref="Failed"/>).
			/// </summary>
			public bool Step(double budgetMs)
			{
				if (Done)
				{
					return true;
				}
				using (StepMarker.Auto())
				{
					Frames++;
					var watch = System.Diagnostics.Stopwatch.StartNew();
					while (!Done && Advance() && watch.Elapsed.TotalMilliseconds < budgetMs)
					{
					}
					Milliseconds += watch.Elapsed.TotalMilliseconds;
				}
				return Done;
			}

			/// <summary>One step of the main thread's share; false when it is waiting on the worker.</summary>
			private bool Advance()
			{
				TerrainData data = gt.Data;
				if (data == null)
				{
					Done = true;
					return true;
				}
				Exception fault = queue.Fault ?? densityWork?.Exception;
				if (fault != null)
				{
					Debug.LogException(fault);
					Debug.LogWarning($"[Grass] Building '{(Terrain != null ? Terrain.name : "terrain")}' failed on a worker; it is taken again.");
					Abandon();
					Failed = true;
					return true;
				}
				if (layer < layerPrototypes.Count)
				{
					if (queue.Pending >= MaxPendingStrips)
					{
						return false;
					}
					int rows = Mathf.Min(StripRows, res - row);
					int[,] cells = data.GetDetailLayer(0, row, res, rows, layerPrototypes[layer]);
					float[] target = density[layerChannels[layer]];
					int rowBase = row;
					queue.Enqueue(() =>
					{
						if (AddStrip(cells, rows, rowBase, res, factor, dres, top, target))
						{
							any = true;
						}
					});
					row += rows;
					if (row >= res)
					{
						layer++;
						row = 0;
					}
					return true;
				}
				if (densityWork == null)
				{
					if (!queue.Idle)
					{
						return false;
					}
					if (!any)
					{
						if (Capture)
						{
							Captured = Layout(false);
						}
						Done = true;
						return true;
					}
					int radius = Mathf.Max(1, Mathf.RoundToInt(SmoothMetres / Mathf.Max(0.05f, gt.Size.x / dres)));
					float[][] channels = density;
					int n = count, side = dres;
					float[] heights = channelHeights;
					bool[] points = channelPoints;
					densityWork = TerrainBuildQueue.Inline
						? Task.FromResult(BuildDensity(channels, n, side, radius, heights, points, false))
						: Task.Run(() => BuildDensity(channels, n, side, radius, heights, points, true));
					return true;
				}
				int hres = data.heightmapResolution;
				if (!heightsBegun)
				{
					heightsBegun = true;
					gt.BeginHeightGrid();
					return true;
				}
				if (heightRow < hres)
				{
					if (queue.Pending >= MaxPendingStrips)
					{
						return false;
					}
					int rows = Mathf.Min(StripRows, hres - heightRow);
					float[,] strip = data.GetHeights(0, heightRow, hres, rows);
					int zBase = heightRow;
					queue.Enqueue(() => gt.AccumulateHeights(strip, zBase));
					heightRow += rows;
					return true;
				}
				// The water line, against the heights (queued after them, so the worker has them all), a strip at a time.
				if (waters != null && !waterQueued)
				{
					waterQueued = true;
					var bodies = waters.ToArray();
					// Each one's river index built now, on this thread: the worker only reads it (it is built lazily on first use).
					foreach (FishMMO.Shared.Biomes.SceneWaterBodies b in bodies)
					{
						if (b != null)
						{
							b.TryGetSurface(gt.Origin.x, gt.Origin.z, out _);
						}
					}
					for (int z = 0; z < hres; z += StripRows)
					{
						int zBase = z, rows = Mathf.Min(StripRows, hres - z);
						queue.Enqueue(() => gt.AccumulateFreeboard(bodies, zBase, rows));
					}
					return true;
				}
				// The painted holes (a cave mouth, a canyon bed), against the same grid: read here, cut by the worker.
				if (!holesCut)
				{
					holes ??= new TerrainHoleMask(data);
					if (!holes.Complete)
					{
						holes.ReadStrip(data);
						return true;
					}
					if (holes.Any)
					{
						TerrainHoleMask cut = holes;
						// No blade over an opening, nor rooted so near its edge that it leans out over it (2026-10-27).
						queue.Enqueue(() =>
						{
							cut.KeepPlantsClear();
							gt.CutHoles(cut);
						});
					}
					holes = null;
					holesCut = true;
					return true;
				}
				int ares = data.alphamapResolution, alayers = data.alphamapLayers;
				if (alayers <= 0)
				{
					// A client build's tile keeps no alphamaps once its stored index map stands in for them: the same layers,
					// read off that (GrassMath.PackSurfaceFromIndex).
					if (!surfaceIndexLooked)
					{
						surfaceIndexLooked = true;
						surfaceIndex = FishMMO.Shared.SceneTerrainBake.IndexOf(data);
					}
					ares = surfaceIndex != null ? surfaceIndex.Resolution : 0;
				}
				surfaceRows = alayers > 0 || surfaceIndex != null ? ares : 0;
				if (surfaceRow < surfaceRows)
				{
					if (queue.Pending >= MaxPendingStrips)
					{
						return false;
					}
					surfacePixels ??= new Color32[ares * ares];
					int rows = Mathf.Min(StripRows, ares - surfaceRow);
					int rowBase = surfaceRow;
					Color32[] pixels = surfacePixels;
					if (alayers > 0)
					{
						float[,,] maps = data.GetAlphamaps(0, surfaceRow, ares, rows);
						// The ids the compute reads are the set's slices: a tile carrying only the layers it paints is renamed.
						if (!surfaceSceneLayersLooked)
						{
							surfaceSceneLayersLooked = true;
							surfaceSceneLayers = FishMMO.Shared.TerrainTileLayers.Of(Terrain);
						}
						int[] sceneLayers = surfaceSceneLayers;
						queue.Enqueue(() => PackSurfaceStrip(maps, rows, rowBase, ares, alayers, pixels, new float[alayers], sceneLayers));
					}
					else
					{
						uint[] index = surfaceIndex.Map;
						queue.Enqueue(() => PackSurfaceStrip(index, rows, rowBase, ares, pixels));
					}
					surfaceRow += rows;
					return true;
				}
				if (!queue.Idle || !densityWork.IsCompleted)
				{
					return false;
				}
				if (!textures)
				{
					textures = true;
					DensityResult built = densityWork.Result;
					gt.Density = new Texture2D[built.Maps.Length];
					for (int m = 0; m < built.Maps.Length; m++)
					{
						gt.Density[m] = MakeTexture(built.Maps[m], dres, $"{Terrain.name} grass density {4 * m}-{4 * m + 3}");
					}
					gt.Occupied = built.Occupied;
					gt.Cover = built.Cover;
					gt.Sward = built.Sward;
					if (Capture)
					{
						Captured = Layout(true);
						Captured.DensityResolution = dres;
						Captured.DensityMaps = built.Maps.Length;
						Captured.Density = Flatten(built.Maps, dres * dres);
						Captured.Occupied = built.Occupied;
						Captured.Sward = built.Sward;
					}
					return true;
				}
				if (surfacePixels != null)
				{
					gt.SurfaceLayers = MakeTexture(surfacePixels, ares, $"{Terrain.name} grass surface layers");
					if (Capture)
					{
						Captured.SurfaceResolution = ares;
						Captured.Surface = Flatten(new[] { surfacePixels }, ares * ares);
					}
					surfacePixels = null;
					return true;
				}
				if (Capture)
				{
					CaptureHeights();
				}
				gt.FinishHeightTexture();
				Result = gt;
				Done = true;
				return true;
			}
		}

		/// <summary>An RGBA8 map from <paramref name="res"/>² × 4 bytes of <paramref name="bytes"/> from <paramref name="start"/>.</summary>
		private static Texture2D MakeTexture(byte[] bytes, int start, int res, string name)
		{
			var texture = new Texture2D(res, res, TextureFormat.RGBA32, false, true)
			{
				name = name,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			texture.SetPixelData(bytes, 0, start);
			texture.Apply(false, true);
			return texture;
		}

		private static Texture2D MakeTexture(Color32[] pixels, int res, string name)
		{
			var texture = new Texture2D(res, res, TextureFormat.RGBA32, false, true)
			{
				name = name,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			texture.SetPixels32(pixels);
			texture.Apply(false, true);
			return texture;
		}

		/// <summary>The heightmap's resolution, read on the main thread when the height grid begins: the build queue's
		/// worker folds strips in with it, and TerrainData's properties are main-thread only.</summary>
		private int heightResolution;

		private void BeginHeightGrid()
		{
			int res = heightResolution = Data.heightmapResolution;
			if (SystemInfo.SupportsTextureFormat(TextureFormat.R16))
			{
				heightTexels16 = new ushort[res * res];
			}
			else
			{
				heightTexels32 = new float[res * res];
			}
			gridX = Mathf.Max(1, Mathf.CeilToInt(Size.x / BoundsCellMetres));
			gridZ = Mathf.Max(1, Mathf.CeilToInt(Size.z / BoundsCellMetres));
			minHeights = new float[gridX * gridZ];
			maxHeights = new float[gridX * gridZ];
			for (int i = 0; i < minHeights.Length; i++)
			{
				minHeights[i] = float.MaxValue;
				maxHeights[i] = float.MinValue;
			}
		}

		/// <summary>Folds a strip of height samples (rows <paramref name="zBase"/> on) into the min/max grid.</summary>
		private void AccumulateHeights(float[,] heights, int zBase)
		{
			int res = heightResolution;
			float step = (res - 1) / Mathf.Max(1e-3f, Size.x) * BoundsCellMetres;
			int rows = heights.GetLength(0);
			for (int r = 0; r < rows; r++)
			{
				int z = zBase + r;
				for (int x = 0; x < res; x++)
				{
					float v = heights[r, x];
					if (heightTexels16 != null)
					{
						heightTexels16[z * res + x] = (ushort)Mathf.Clamp(Mathf.RoundToInt(v * 65535f), 0, 65535);
					}
					else
					{
						heightTexels32[z * res + x] = v;
					}
					float h = Origin.y + v * Size.y;
					// A sample on a cell border bounds both cells.
					int gx0 = Mathf.Clamp(Mathf.FloorToInt((x - 0.5f) / step), 0, gridX - 1), gx1 = Mathf.Clamp(Mathf.FloorToInt((x + 0.5f) / step), 0, gridX - 1);
					int gz0 = Mathf.Clamp(Mathf.FloorToInt((z - 0.5f) / step), 0, gridZ - 1), gz1 = Mathf.Clamp(Mathf.FloorToInt((z + 0.5f) / step), 0, gridZ - 1);
					for (int gz = gz0; gz <= gz1; gz++)
					{
						for (int gx = gx0; gx <= gx1; gx++)
						{
							int i = gz * gridX + gx;
							if (h < minHeights[i]) minHeights[i] = h;
							if (h > maxHeights[i]) maxHeights[i] = h;
						}
					}
				}
			}
		}

		/// <summary>
		/// The ground's height over the water at heightmap rows <paramref name="zBase"/> … +<paramref name="rows"/>
		/// (<see cref="Freeboard"/>), from the heights already gathered: the highest surface of any lake or river within
		/// <see cref="WaterReachMetres"/> of the sample.
		/// </summary>
		private void AccumulateFreeboard(FishMMO.Shared.Biomes.SceneWaterBodies[] waters, int zBase, int rows)
		{
			int res = heightResolution;
			freeboardTexels ??= NewFreeboardTexels(res);
			float stepX = Size.x / Mathf.Max(1, res - 1), stepZ = Size.z / Mathf.Max(1, res - 1);
			float scale = 0.5f / FreeboardRangeMetres;
			for (int z = zBase; z < zBase + rows; z++)
			{
				float worldZ = Origin.z + z * stepZ;
				for (int x = 0; x < res; x++)
				{
					int i = z * res + x;
					float worldX = Origin.x + x * stepX;
					float level = float.NegativeInfinity;
					foreach (FishMMO.Shared.Biomes.SceneWaterBodies bodies in waters)
					{
						if (bodies != null && bodies.TryGetSurfaceNear(worldX, worldZ, WaterReachMetres, out float here) && here > level)
						{
							level = here;
						}
					}
					if (float.IsNegativeInfinity(level))
					{
						freeboardTexels[2 * i] = 255;
						continue;
					}
					float ground = Origin.y + (heightTexels16 != null ? heightTexels16[i] / 65535f : heightTexels32[i]) * Size.y;
					freeboardTexels[2 * i] = (byte)Mathf.RoundToInt(Mathf.Clamp01((ground - level) * scale + 0.5f) * 255f);
					anyWater |= ground - level < FreeboardRangeMetres;
				}
			}
		}

		/// <summary>A water-line map with no water and no hole: every line at the top of the range, every cell solid.</summary>
		private static byte[] NewFreeboardTexels(int res)
		{
			var texels = new byte[res * res * 2];
			for (int i = 0; i < texels.Length; i++)
			{
				texels[i] = 255;
			}
			return texels;
		}

		/// <summary>Marks every hole cell of <paramref name="holes"/> in the water-line map's G (<see cref="Freeboard"/>).</summary>
		private void CutHoles(TerrainHoleMask holes)
		{
			int res = heightResolution;
			freeboardTexels ??= NewFreeboardTexels(res);
			int cells = Mathf.Min(holes.Resolution, res);
			for (int z = 0; z < cells; z++)
			{
				for (int x = 0; x < cells; x++)
				{
					if (holes.HoleAtCell(x, z))
					{
						freeboardTexels[2 * (z * res + x) + 1] = 0;
						anyHole = true;
					}
				}
			}
		}

		/// <summary>Uploads the heights gathered by <see cref="AccumulateHeights"/> (row z = texture row, as the terrain's own).</summary>
		private void FinishHeightTexture()
		{
			if (freeboardTexels != null && (anyWater || anyHole))
			{
				int fres = Data.heightmapResolution;
				Freeboard = new Texture2D(fres, fres, TextureFormat.RG16, false, true)
				{
					name = $"{(Terrain != null ? Terrain.name : "terrain")} grass water line",
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp,
					hideFlags = HideFlags.DontSave,
				};
				Freeboard.SetPixelData(freeboardTexels, 0);
				Freeboard.Apply(false, true);
			}
			freeboardTexels = null;
			int res = Data.heightmapResolution;
			bool r16 = heightTexels16 != null;
			heightTexture = new Texture2D(res, res, r16 ? TextureFormat.R16 : TextureFormat.RFloat, false, true)
			{
				name = $"{(Terrain != null ? Terrain.name : "terrain")} grass heights",
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave,
			};
			if (r16)
			{
				heightTexture.SetPixelData(heightTexels16, 0);
			}
			else
			{
				heightTexture.SetPixelData(heightTexels32, 0);
			}
			heightTexture.Apply(false, true);
			heightTexels16 = null;
			heightTexels32 = null;
		}

		/// <summary>
		/// Whether any blade type grows under a world xz rectangle, a density texel round it included (the blades
		/// sample the density bilinearly, so a texel's grass reaches half a texel past it). True until built.
		/// </summary>
		public bool HasGrass(float x0, float z0, float x1, float z1)
		{
			byte[] occupied = Occupied;
			int d = DensityResolution;
			if (occupied == null || d <= 0 || occupied.Length < d * d)
			{
				return true;
			}
			float perMetreX = d / Mathf.Max(1e-3f, Size.x), perMetreZ = d / Mathf.Max(1e-3f, Size.z);
			int ax0 = Mathf.Clamp(Mathf.FloorToInt((x0 - Origin.x) * perMetreX) - 1, 0, d - 1);
			int ax1 = Mathf.Clamp(Mathf.FloorToInt((x1 - Origin.x) * perMetreX) + 1, 0, d - 1);
			int az0 = Mathf.Clamp(Mathf.FloorToInt((z0 - Origin.z) * perMetreZ) - 1, 0, d - 1);
			int az1 = Mathf.Clamp(Mathf.FloorToInt((z1 - Origin.z) * perMetreZ) + 1, 0, d - 1);
			for (int z = az0; z <= az1; z++)
			{
				int row = z * d;
				for (int x = ax0; x <= ax1; x++)
				{
					if (occupied[row + x] != 0)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>The ground's height range under a world xz rectangle (clamped to the terrain).</summary>
		public Vector2 HeightRange(float x0, float z0, float x1, float z1)
		{
			int gx0 = Mathf.Clamp(Mathf.FloorToInt((x0 - Origin.x) / BoundsCellMetres), 0, gridX - 1);
			int gx1 = Mathf.Clamp(Mathf.FloorToInt((x1 - Origin.x) / BoundsCellMetres), 0, gridX - 1);
			int gz0 = Mathf.Clamp(Mathf.FloorToInt((z0 - Origin.z) / BoundsCellMetres), 0, gridZ - 1);
			int gz1 = Mathf.Clamp(Mathf.FloorToInt((z1 - Origin.z) / BoundsCellMetres), 0, gridZ - 1);
			float lo = float.MaxValue, hi = float.MinValue;
			for (int gz = gz0; gz <= gz1; gz++)
			{
				for (int gx = gx0; gx <= gx1; gx++)
				{
					int i = gz * gridX + gx;
					lo = Mathf.Min(lo, minHeights[i]);
					hi = Mathf.Max(hi, maxHeights[i]);
				}
			}
			return lo <= hi ? new Vector2(lo, hi) : new Vector2(Origin.y, Origin.y + Size.y);
		}

		/// <summary>How much of the ground at a world point on this terrain is under blades, 0..1 (0 off it, or before it is built).</summary>
		public float SwardAt(float x, float z)
		{
			byte[] sward = Sward;
			int res = DensityResolution;
			if (sward == null || res <= 0 || Size.x <= 0f || Size.z <= 0f)
			{
				return 0f;
			}
			float u = (x - Origin.x) / Size.x, v = (z - Origin.z) / Size.z;
			if (u < 0f || u > 1f || v < 0f || v > 1f)
			{
				return 0f;
			}
			int ix = Mathf.Clamp((int)(u * res), 0, res - 1), iz = Mathf.Clamp((int)(v * res), 0, res - 1);
			return sward[iz * res + ix] / 255f;
		}

		/// <summary>The grass standing at a world point on this terrain, metres (0 off it, or before the map is built).</summary>
		public float CoverAt(float x, float z)
		{
			if (Cover == null && coverWork != null && coverWork.IsCompleted)
			{
				Cover = coverWork.Status == TaskStatus.RanToCompletion ? coverWork.Result : null;
				coverWork = null;
			}
			float[] cover = Cover;
			int res = DensityResolution;
			if (cover == null || res <= 0 || Size.x <= 0f || Size.z <= 0f)
			{
				return 0f;
			}
			float u = (x - Origin.x) / Size.x, v = (z - Origin.z) / Size.z;
			if (u < 0f || u > 1f || v < 0f || v > 1f)
			{
				return 0f;
			}
			int ix = Mathf.Clamp((int)(u * res), 0, res - 1), iz = Mathf.Clamp((int)(v * res), 0, res - 1);
			return cover[iz * res + ix];
		}

		/// <summary>Destroys a texture this made, in play or (the editor's bake) in edit mode.</summary>
		private static void Release(UnityEngine.Object texture)
		{
			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(texture);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(texture);
			}
		}

		public void Dispose()
		{
			if (Density != null)
			{
				foreach (Texture2D map in Density)
				{
					if (map != null)
					{
						Release(map);
					}
				}
			}
			Density = null;
			Occupied = null;
			Cover = null;
			coverWork = null;
			Sward = null;
			if (heightTexture != null)
			{
				Release(heightTexture);
			}
			heightTexture = null;
			if (SurfaceLayers != null)
			{
				Release(SurfaceLayers);
			}
			SurfaceLayers = null;
			if (Freeboard != null)
			{
				Release(Freeboard);
			}
			Freeboard = null;
		}
	}
}
