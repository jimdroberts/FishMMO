using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The client's terrain look: the GPU-driven terrain objects, the ground under and around them, rock seams, the heightmap draw, moss, grass, detail scatter, cave cover and trails.
	/// </summary>
	/// <remarks>
	/// Client-only; loaded with the client's static assets. The terrain's own profile (Phase 4 of the core and modules plan, 2026-11-17): these settings were part of <see cref="WeatherRenderProfile"/>, and the asset was split field for field.
	/// </remarks>
	[CreateAssetMenu(fileName = "Terrain Render Profile", menuName = "FishMMO/World/Terrain Render Profile", order = 21)]
	public class TerrainRenderProfile : CachedScriptableObject<TerrainRenderProfile>, ICachedObject
	{
		[Header("Terrain instancing")]
		[Tooltip("The GPU-driven terrain tree and detail culling (FishTerrainInstancing.compute). Referenced here, not from Resources, so it ships to clients only.")]
		public ComputeShader TerrainInstancingCompute;
		[Tooltip("FishMMO/Vegetation Indirect: the procedural-instancing twin the GPU-driven terrain path draws vegetation with. Referenced so a client build includes it.")]
		public Shader VegetationIndirectShader;
		[Tooltip("FishMMO/Weather Lit Indirect: the twin for rocks, formations and ice.")]
		public Shader WeatherLitIndirectShader;
		[Tooltip("FishMMO/Vegetation Prime: the depth the GPU-driven path lays ahead of each alpha-tested vegetation draw, so the lit pass shades only the leaf in front (TerrainGpuRenderer). Referenced so a client build includes it.")]
		public Shader VegetationPrimeShader;
		[Tooltip("Every keyword set the vegetation and rock materials use, mapped onto the indirect twins (the art generator writes it). Nothing in a build uses those shaders directly, so without this only their keyword-free variants ship: grass loses its alpha test and draws as solid quads.")]
		public ShaderVariantCollection IndirectShaderVariants;

		[Header("Ground colour under vegetation")]
		[Tooltip("How much of the ground's colour a grass blade or leaf takes at its root (GroundColourMap). Live: changes apply while playing.")]
		[Range(0f, 1f)] public float GroundRootBlend = 1f;
		[Tooltip("How much of the ground's colour the rest of a low plant takes, above Ground Blend Height. Lit grass reads lighter than the soil at the same colour (normals up, translucency, no ground occlusion), so this wants to be high.")]
		[Range(0f, 1f)] public float GroundPlantBlend = 0.9f;
		[Tooltip("Metres above the root over which the blend eases from the root's to the plant's.")]
		[Min(0.05f)] public float GroundBlendHeight = 0.8f;
		[Tooltip("How much of a blade's own light and dark (texture, darker root) survives the blend, as darkening only: 0 = flat ground colour, 0.3 = up to 30% darker in the blade's dark parts.")]
		[Range(0f, 0.6f)] public float GroundBladeDetail = 0.25f;
		[Tooltip("Metres above the root where the blend starts fading out, and where it is gone: tree crowns keep their own colour.")]
		[Min(0f)] public float GroundCrownStart = 1.5f;
		[Min(0.1f)] public float GroundCrownEnd = 3f;

		[Header("Distant terrain objects (trees, plants, terrain rocks)")]
		[Tooltip("Metres from the camera where distant trees, plants and terrain rocks start settling into the landscape: lighting normal flattening toward up (no black shadowed sides against a flat-lit hillside) and colour drifting toward the local ground's. Live.")]
		[Min(0f)] public float DistanceBlendStart = 80f;
		[Tooltip("Metres where that is complete.")]
		[Min(1f)] public float DistanceBlendEnd = 600f;
		[Tooltip("How far their lighting normal flattens to up at full distance.")]
		[Range(0f, 1f)] public float DistanceNormalFlatten = 0.6f;
		[Tooltip("How much of the ground's colour they take at full distance (0 keeps their own).")]
		[Range(0f, 1f)] public float DistanceGroundPull = 0.35f;

		[Header("Contact with the ground: trees and terrain rocks")]
		[Tooltip("The GPU-drawn terrain trees (trunk bases), boulders and rock formations render the TERRAIN itself over a band at their base — its own layers, normal, weather and lighting, exactly as the terrain draws that point — fading into their own look above, so they sit in the ground instead of on it. No geometry is added or moved: collision is unchanged. Small scattered details (pebbles, shells, litter) take none. Live.")]
		public bool ContactBlendLarge = true;
		[Tooltip("Metres above the ground (measured across the slope) over which the blend fades out.")]
		[Range(0.01f, 3f)] public float ContactBlendHeightLarge = 0.5f;
		[Tooltip("How much of the terrain the band shows above its solid foot (the lowest third of the band is always wholly the terrain, so the object fades out of the ground).")]
		[Range(0f, 1f)] public float ContactColourLarge = 0.75f;
		[Header("Rock crevices (rock against rock)")]
		[Tooltip("Where one rock meets another, the seam darkens toward its bottom and fills with the surrounding ground's own material, as soil and grit settle between real stones. Distance fields per rock shape (RockContactField), so the seam is the same from every angle. Rocks only, never trees. Live.")]
		public bool RockCrevices = true;
		[Tooltip("Metres from the other rock over which the crevice fades out.")]
		[Range(0.02f, 1.5f)] public float RockCreviceWidth = 0.35f;
		[Tooltip("How dark the bottom of a crevice is (0 none, 1 black).")]
		[Range(0f, 1f)] public float RockCreviceShadow = 0.45f;
		[Tooltip("How much of the surrounding ground's material fills a crevice on the parts that face up.")]
		[Range(0f, 1f)] public float RockCreviceSoil = 0.85f;
		[Tooltip("FishRockField.compute: lists the rocks around the camera into the crevice grid every frame. Referenced here so it ships to clients only.")]
		public ComputeShader RockFieldCompute;
		[Tooltip("Hidden/FishMMO/RockSdf: draws each rock shape's distance field. Referenced here so it ships to clients only.")]
		public Shader RockSdfShader;

		[Header("Rock seam fillets (rocks joined into walls)")]
		[Tooltip("Near the camera, the seams where rocks meet are filled with a smooth surface of rock (RockFillets: the smooth union of their distance fields, built on the GPU and kept until the camera moves on), so stacked boulders, cliff rocks and sections read as one wall. Every rock keeps its own mesh and its own collider: the fillet adds a little in front of a concave seam only, never more than Width/4 − Inset outside the rocks. Lit, shadowed and weathered as the rocks are, wearing the nearer rock's material, with the crevice's soil in its bottom. Needs compute shaders. Live.")]
		public bool RockFillets = true;
		[Tooltip("How wide a seam the fillet fills, metres (the smooth minimum's width k): it meets each face about this far from the other rock and stands at most k/4 out of the corner.")]
		[Range(0.2f, 1.5f)] public float RockFilletWidth = 1f;
		[Tooltip("A pair of rocks blends no wider than this share of the smaller one's size, so a stone against a wall is not buried in a mound.")]
		[Range(0.1f, 1f)] public float RockFilletSizeShare = 0.5f;
		[Tooltip("Metres the fillet's surface lies under the rocks' own faces away from the seam (hidden there), so it comes out of them only where it fills the seam and never fights a face for depth. Its distance-field cut follows the fields' accuracy: larger hides more of a big rock's field error at the cost of a thinner fillet.")]
		[Range(0.01f, 0.3f)] public float RockFilletInset = 0.06f;
		[Tooltip("Metres from the camera where the fillets are gone (dissolved over the last quarter). They are built round the camera out to this plus the rebuild step, so they never pop in view.")]
		[Range(12f, 48f)] public float RockFilletDistance = 36f;
		[Tooltip("The lowest quality level (0 Performant, 1 Balanced, 2 High Fidelity) that draws them.")]
		[Range(0, 2)] public int RockFilletMinQuality = 1;
		[Tooltip("Seam cells meshed per frame while a build runs (each 2 m cell is 1331 field samples): the build's cost per frame against how many frames it takes.")]
		[Range(16, 1024)] public int RockFilletCellsPerFrame = 192;
		[Tooltip("The most fillet triangles held (64 bytes each, twice: the build and the drawn set). A build with more keeps the first this many and warns.")]
		[Range(8192, 262144)] public int RockFilletTriangles = 131072;
		[Tooltip("FishRockFillets.compute: finds the seam cells and meshes their fillets. Referenced here so it ships to clients only.")]
		public ComputeShader RockFilletsCompute;
		[Tooltip("FishMMO/Rock Fillet: draws them. Referenced here so it ships to clients only.")]
		public Shader RockFilletShader;

		[Header("Terrain heightmap draw")]
		[Tooltip("FishTerrainHeightmap.compute: chooses the ground's geomorphed patches on the GPU for every camera (TerrainHeightmapSystem). Referenced here so a client build includes it; empty, the editor loads it by path.")]
		public ComputeShader TerrainHeightmapCompute;
		[Tooltip("Hidden/FishMMO/Weather Terrain Array GPU: draws them with the ground's own shading. Referenced here so a client build includes it; empty, the editor loads it by path.")]
		public Shader TerrainHeightmapShader;
		public TerrainHeightmapSettings TerrainHeightmap = new TerrainHeightmapSettings();

		[Header("Moss (rock, cliffs, caves, stone, timber and deadwood)")]
		[Tooltip("How much moss the world grows, times each scene's dampness (MossClimate: the biome under the camera — swamp and forest a lot, desert, volcanic and ice next to none) and each material's own Moss amount (_FishMoss, set by the art generator). A waterfall's spray grows it at this strength whatever the climate, on that rock and on the ground round the fall. 0 turns it off everywhere. Live (2026-10-12).")]
		[Range(0f, 2f)] public float MossStrength = 1f;
		[Tooltip("Metres one tile of the moss texture covers (projected in world space, so the same on every rock and wall).")]
		[Range(0.25f, 8f)] public float MossTileMetres = 2f;
		[Tooltip("The dampness the editor previews with out of play mode, where no biome is being sampled (0..1).")]
		[Range(0f, 1f)] public float MossEditorDampness = 0.6f;
		[Tooltip("The generated moss surface (Generate biome art writes Ground_Moss_Albedo; alpha holds its smoothness). Assigned by Create weather render profile; empty, the editor loads it by path and a build draws a plain green instead.")]
		public Texture2D MossAlbedo;
		[Tooltip("The generated moss surface's normal map (Ground_Moss_Normal).")]
		public Texture2D MossNormal;

		[Header("Procedural grass")]
		[Tooltip("FishGrassBlades.compute: generates the visible blades around each camera every frame. Referenced here, not from Resources, so it ships to clients only.")]
		public ComputeShader GrassBladesCompute;
		[Tooltip("FishMMO/Grass Blades: the blade strips drawn from the compute's output.")]
		public Shader GrassBladesShader;
		public GrassBladeSettings Grass = new GrassBladeSettings();

		[Header("GPU detail scatter")]
		[Tooltip("FishDetailScatter.compute: generates the visible mesh details named below (pebbles, small rocks, shells, litter) around each camera every frame instead of the CPU's detail chunks. Referenced here, not from Resources, so it ships to clients only.")]
		public ComputeShader DetailScatterCompute;
		public DetailScatterSettings DetailScatter = new DetailScatterSettings();

		[Header("Cave cover")]
		[Tooltip("FishCaveCover.compute: scatters the small cover (moss, glow moss, grit, mushrooms, crusts, soda straws, glow-worm threads; a mine's timber, iron and candles) on carved caves' rock around each camera every frame (CaveCoverSystem). Referenced here so a client build includes it; empty, the editor loads it by path.")]
		public ComputeShader CaveCoverCompute;
		[Tooltip("FishMMO/Cave Cover: draws the cave cover. Referenced here so a client build includes it; empty, the editor loads it by path.")]
		public Shader CaveCoverShader;
		[Tooltip("CaveCover_Set (Generate Biome Art writes it with the structures): every cave cover type at every level, a submesh each. Referenced here so a client build includes it; empty, the editor loads it by path.")]
		public Mesh CaveCoverMesh;
		public CaveCoverSettings CaveCover = new CaveCoverSettings();
		[Tooltip("FishMMO/World/Cave Skylight Shafts: the daylight's shafts and the dust motes in them under the openings in caves' roofs near the camera (CaveSkylights). Lighting, not ambient life: moved from the ambient life profile (Phase 4, 2026-11-17). Referenced here so a client build includes it; empty, it is found by name, which only works in the editor.")]
		public Shader CaveSkylightShaftsShader;

		[Header("Trails")]
		[Tooltip("Footprints and trodden grass round the camera (GroundTrailMap): every character in view leaves stride prints in snow, ash, sand and wet ground, and grass lies down where a body pushes through it and rises again.")]
		public bool GroundTrails = true;
		[Tooltip("FishMMO/Ground Trail: stamps and fades the trail map. Referenced here so a client build includes it; empty, it is found by name, which only works in the editor.")]
		public Shader GroundTrailShader;

		/// <summary>
		/// The loaded profile, if any. In the editor, a world scene played straight from its own file never runs the
		/// client boot that loads the profile into the cache; there the project's profile asset is loaded once instead.
		/// Builds always have it cached.
		/// </summary>
		public static TerrainRenderProfile Active
		{
			get
			{
				TerrainRenderProfile cached = GetFirst<TerrainRenderProfile>();
#if UNITY_EDITOR
				if (cached == null)
				{
					if (editorFallback == null && !editorFallbackTried)
					{
						editorFallbackTried = true;
						editorFallback = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainRenderProfile>(EditorProfilePath);
					}
					return editorFallback;
				}
#endif
				return cached;
			}
		}

#if UNITY_EDITOR
		/// <summary>The project's profile asset, for <see cref="Active"/>'s editor fallback.</summary>
		public const string EditorProfilePath = "Assets/Prefabs/Client/Weather/Terrain Render Profile.asset";
		private static TerrainRenderProfile editorFallback;
		private static bool editorFallbackTried;

		[UnityEditor.InitializeOnEnterPlayMode]
		private static void ResetEditorFallback()
		{
			editorFallback = null;
			editorFallbackTried = false;
		}
#endif
	}
}
