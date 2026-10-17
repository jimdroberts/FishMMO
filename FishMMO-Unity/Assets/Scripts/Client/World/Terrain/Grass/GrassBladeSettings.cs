using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The procedural blade grass (<see cref="GrassBladeSystem"/>): which detail prototypes become blades,
	/// the density rings, the blade's shape and its light. On the Weather Render Profile ("Procedural
	/// grass"); read every frame, so changes apply while playing.
	/// </summary>
	/// <remarks>
	/// <b>Engine-wide settings only (2026-10-15).</b> How each blade TYPE looks — its shape, heads, colours and how much of the
	/// ground's colour it takes — is the biome's: <see cref="FishMMO.Shared.Biomes.BiomeTemplate.GrassTypes"/>, one entry per
	/// blade prefab per biome, so one prefab can look different in two biomes. The profile's old per-prefix Types list and its
	/// one Terrain Texture Tint for every type were removed from here (their values are BiomeGrassDefaults, which the
	/// authoring writes onto every template, and the fallback for a prototype whose biome has no entry). The profile asset
	/// still holds the two keys until it is next saved; nothing reads them.
	/// </remarks>
	[Serializable]
	public class GrassBladeSettings
	{
		[Tooltip("Off: the grass detail prototypes stay meshes (as before) everywhere. The dashboard's A/B button overrides it per play session.")]
		public bool Enabled = true;

		[Tooltip("Detail prototype prefab names starting with any of these are drawn as blades (and skipped by the detail renderer): a rendering-path rule, not a look. Reeds and meadow flowers are blades too: sprinkled stems with a seed head or a flower at the tip (their biome's Grass Types entries). Sea plants never are (a blade lawn on the sea floor read as land grass).")]
		public string[] PrototypePrefixes = { "Detail_Grass", "Detail_Reeds", "Detail_Flowers" };

		[Header("Density rings (distance m, blades per square metre)")]
		[Tooltip("Blades per square metre out to each distance, falling log-log between them; the first is the near density, which also sets the candidate lattice (cell = 1/sqrt(density)). Thinning is nested: a far blade is always one of the near ones.")]
		public Vector2[] Rings =
		{
			// To 50 m the count falls exactly as a blade's width on screen grows (density × distance constant, the
			// blades widened by the same factor), so every blade stays about two pixels wide and the ground is wholly
			// covered: the field looks as full at 50 m as at 8. Beyond, it thins faster than the pixel cap
			// (MaxBladePixels) lets blades widen, and coverage eases off into the far field's ground colour.
			new Vector2(7.5f, 1600f),
			new Vector2(50f, 240f),
			new Vector2(100f, 60f),
			new Vector2(200f, 15f),
			new Vector2(400f, 4f),
			new Vector2(600f, 2f),
		};

		[Tooltip("The detail-layer density (as a share of its maximum) at and above which grass grows at the FULL ring density. The scatter paints biome grass at roughly 20–40% of the maximum; read as a straight spawn chance that thinned the near field to a few dozen blades per square metre, which looked like the old clumps. Below this the field thins in proportion, so painted edges still fade.")]
		[Range(0.05f, 1f)] public float Fullness = 0.35f;
		[Tooltip("The coarsest candidate lattice level sprinkled species (flowers, reeds) stand on. Each level holds a quarter of the cells, so their chance per candidate is four times higher and the distance thinning keeps all of them four times further down the share. 0: they thin with the lawn from the first ring, and a flower grew out of the ground a few metres ahead of the camera. 2 (with the default rings): full count to about 75 m, where a flower head is under a pixel. A type never goes so coarse that its chance passes 1 (reeds stop at 1).")]
		[Range(0, 3)] public int SprinkleLevel = 2;

		[Header("River and lake banks")]
		[Tooltip("How far above a river's or lake's water a blade must root, metres (each terrain's water line, cut per blade). The density maps are 2 m texels smoothed over 2 m and filled narrow creeks from both banks; this cuts the grass where the water meets the ground.")]
		[Range(0f, 0.5f)] public float BankClearance = 0.03f;
		[Tooltip("Over how much more height above that the bank grass thins in to full, metres: a fringe instead of a mown edge.")]
		[Range(0.01f, 1f)] public float BankThinning = 0.25f;
		[Tooltip("Blades widen as the field thins: width × share^-exponent. 1 keeps the ground covered as fully as near the camera (fewer blades, each standing for the ones thinned out); 0.5 let coverage fall to two thirds by 15 m and a quarter by 50 m, which read as the grass fading out just past the camera. Flower and seed heads keep their area instead (share^-0.5 whatever this is).")]
		[Range(0f, 1f)] public float WidenExponent = 1f;
		[Tooltip("The most a blade widens (and a head grows), as a multiple of its near width. Past the share where it caps, coverage falls with the share: at 12 that is about 50 m with the default rings.")]
		[Range(1f, 96f)] public float MaxWiden = 64f;
		[Tooltip("The widest a widened blade gets ON SCREEN, pixels (at the base width, Blade Width). Blades are what a pixel shows: widening only in metres left them one or two pixels wide past 8 m and under a pixel past 100 m, which read as the grass getting smaller and sparser with distance. Where the field thins faster than this allows blades to widen, coverage falls off instead of the blades turning into cards.")]
		[Range(0.5f, 8f)] public float MaxBladePixels = 2.5f;

		[Header("Levels of detail")]
		[Tooltip("Blades nearer than this draw the 15-vertex strip; to Lod1 Distance the 7-vertex one; beyond, a triangle.")]
		[Min(1f)] public float Lod0Distance = 12f;
		[Min(2f)] public float Lod1Distance = 45f;
		[Tooltip("Blades nearer than this cast shadows (0: none, the default). A blade is narrower than a texel of the nearest shadow cascade and the wind moves it every frame, so cast blade shadows flicker; grass still receives the shadows of trees, rocks and clouds, and Root Occlusion shades its base.")]
		[Min(0f)] public float ShadowDistance = 0f;
		[Tooltip("The most blades the camera view can draw in a frame (all levels); the shadow views get 0.3 of it each. Appends past it are dropped.")]
		[Min(1024)] public int BladeCap = 5000000;
		[Tooltip("The share of the grass distance over which the field dissolves at its edge.")]
		[Range(0.02f, 0.5f)] public float DistanceFadeBand = 0.12f;

		[Header("Blades and clumps")]
		[Tooltip("A blade's base width near the camera, metres.")]
		[Range(0.002f, 0.05f)] public float BladeWidth = 0.012f;
		[Tooltip("Clump (tussock) size: the Voronoi cell whose blades share facing, height and colour.")]
		[Range(0.15f, 4f)] public float ClumpMetres = 0.7f;
		[Tooltip("How much a clump's blades share one facing (0: each faces out from the clump's centre).")]
		[Range(0f, 1f)] public float ClumpFacing = 0.45f;
		[Tooltip("Clump height variation: ± this share of the type's height.")]
		[Range(0f, 0.8f)] public float ClumpHeightVariation = 0.3f;
		[Tooltip("The most a blade's tip leans over (share of its height), at a clump's edge.")]
		[Range(0f, 0.95f)] public float MaxLean = 0.5f;

		[Header("Seen from above and edge-on")]
		[Tooltip("How far blades lay over, degrees from upright, when the view looks straight down on them (none at eye level, easing in from a view about 20° down). Along a smooth direction field a few metres across, turned toward the wind as it rises (never toward or away from the camera, so the meadow does not swivel as the camera moves): from above a lawn is combed in patches. From above an upright blade is a dot: without this a lawn seen from a height was pins in a cushion.")]
		[Range(0f, 80f)] public float TopDownLayDegrees = 60f;
		[Tooltip("How much of a blade's width it keeps when seen edge-on (0 none: an edge-on blade is a sliver; 1 as wide as face-on). The extra width is added across the view, so a blade turned edge-on to the camera still covers what it would face-on.")]
		[Range(0f, 1f)] public float EdgeOnThicken = 0.6f;

		[Header("Light")]
		[Tooltip("How far the normal turns toward each edge, degrees: a blade reads as a cylinder, not a card.")]
		[Range(0f, 70f)] public float NormalRounding = 38f;
		[Tooltip("The light kept at the root (dense grass shades its own base): 1 none, 0.3 strong.")]
		[Range(0f, 1f)] public float RootOcclusion = 0.35f;
		[Tooltip("How much of the roots' shade the ground under the grass takes (GrassSwardMap): 0 none, the ground lit as open ground and glowing between the blades; 1 the roots' own.")]
		[Range(0f, 1f)] public float GroundShade = 0.8f;
		[Tooltip("Light through a blade when the sun is behind it.")]
		[Range(0f, 2f)] public float Translucency = 0.6f;
		[Range(0f, 0.8f)] public float Smoothness = 0.3f;
		[Range(0f, 2f)] public float Specular = 0.6f;

		[Header("Ground colour")]
		[Tooltip("The ground's colour at the very root (GroundColourMap), easing off up the blade.")]
		[Range(0f, 1f)] public float RootGroundBlend = 0.85f;
		[Tooltip("The share of the blade, from the root, over which the ground's colour eases off.")]
		[Range(0.02f, 1f)] public float RootGroundHeight = 0.3f;
		[Tooltip("How much the whole blade leans to the ground's hue (keeps its own brightness). Only where no terrain arrays are bound; otherwise the terrain texture below decides.")]
		[Range(0f, 1f)] public float GroundHueTint = 0.25f;

		[Header("Terrain texture colour")]
		// How much each blade takes of the terrain texture's colour under its root is its type's, per biome
		// (BiomeGrassType.GroundTint; every type took 0.85 here before 2026-10-15).
		[Tooltip("The terrain colour's brightness on the blades: below 1 because lit, translucent blades read lighter than the soil they grow from.")]
		[Range(0.3f, 1.5f)] public float TerrainTextureBrightness = 0.85f;
		[Tooltip("The ground's colour outright at the very root, easing off over the root ground height.")]
		[Range(0f, 1f)] public float TerrainTextureRoot = 0.9f;
		[Tooltip("The least patch of ground one blade's colour stands for, metres (the albedo mip read): small picks up the texture's grain near the camera, large its average. Read trilinearly; far blades stand for the field's blade spacing at their distance (larger), shared by their neighbours, so the colour is continuous and does not flicker.")]
		[Range(0.02f, 2f)] public float TerrainTextureFootprint = 0.25f;

		[Header("Wind")]
		[Range(0f, 3f)] public float WindStrength = 1.6f;
		[Tooltip("Gust waves' length, metres: the bands of bent grass that roll across a meadow.")]
		[Range(2f, 80f)] public float GustWaveMetres = 14f;
		[Range(0f, 0.3f)] public float Flutter = 0.04f;
		[Tooltip("Wind the grass always feels, as a share of a full gale, so a calm meadow is not frozen.")]
		[Range(0f, 0.5f)] public float IdleBreeze = 0.06f;
		[Tooltip("How far wind lays a blade over, degrees from vertical, at full strength in a gust crest (a blade lies down in a gale rather than only leaning).")]
		[Range(10f, 85f)] public float MaxWindBendDegrees = 75f;
		[Tooltip("How sharp the gust crests are: 1 a smooth swell, 3–5 distinct bands rolling across the field.")]
		[Range(1f, 8f)] public float GustSharpness = 3f;
		[Tooltip("How fast the gust bands travel downwind, as a multiple of the wind's own pace.")]
		[Range(0f, 3f)] public float GustSpeed = 1f;
		[Tooltip("How much blades laid over in a gust crest brighten (their flat side catching the sky): what makes the waves visible.")]
		[Range(0f, 1f)] public float GustSheen = 0.45f;
		[Tooltip("From this distance (m) blades bend less in the wind: a blade thinner than a pixel swinging through a gust flickers, and laid-over crests bare the soil as dark streaks. The gust sheen keeps the waves visible.")]
		[Min(0f)] public float WindCalmStart = 15f;
		[Tooltip("By this distance (m) blades keep only Far Wind of their bend.")]
		[Min(1f)] public float WindCalmEnd = 80f;
		[Tooltip("The share of the wind's bend far blades keep (beyond Wind Calm End).")]
		[Range(0f, 1f)] public float FarWind = 0.2f;

		[Header("Far field (blending into the terrain)")]
		[Tooltip("Metres from the camera where blades start converging to the ground's colour and lighting.")]
		[Min(0f)] public float FarBlendStart = 30f;
		[Tooltip("Where the convergence is complete, as a share of the grass distance.")]
		[Range(0.1f, 1f)] public float FarBlendEnd = 0.75f;
		[Tooltip("How much of the ground's colour far blades take (0 own colour, 1 the ground's).")]
		[Range(0f, 1f)] public float FarGroundPull = 0.8f;
		[Tooltip("How far far blades' lighting normal flattens to the terrain's up (removes the dark shadowed sides that made distant grass read as specks).")]
		[Range(0f, 1f)] public float FarNormalFlatten = 0.9f;

		/// <summary>The candidate lattice's cell, metres: one candidate per cell at the near density.</summary>
		public float CellMetres => 1f / Mathf.Sqrt(Mathf.Max(1f, NearDensity));

		public float NearDensity => Rings != null && Rings.Length > 0 ? Mathf.Max(1f, Rings[0].y) : 200f;

		/// <summary>True when a prototype of this name is drawn as blades.</summary>
		public bool IsBladePrototype(string name)
		{
			if (PrototypePrefixes == null || string.IsNullOrEmpty(name))
			{
				return false;
			}
			foreach (string prefix in PrototypePrefixes)
			{
				if (!string.IsNullOrEmpty(prefix) && name.StartsWith(prefix, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}
	}
}
