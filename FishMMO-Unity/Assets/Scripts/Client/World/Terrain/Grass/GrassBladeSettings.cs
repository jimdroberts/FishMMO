using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>What a stem carries at its tip, drawn as two crossed cards there (FishGrassBlades.hlsl GrassHeadVertex).</summary>
	public enum GrassHead
	{
		/// <summary>A plain blade.</summary>
		None = 0,
		/// <summary>A spindle along the stem's top (a reed's or a rush's seed head): brown, upright, sways with the stem.</summary>
		SeedHead = 1,
		/// <summary>A disc tilted toward the sky (a meadow flower), coloured from the prototype mesh's petal colours.</summary>
		Flower = 2,
		/// <summary>
		/// A soft panicle nodding over at the stem's top (feather grass's silky awns, a Phragmites reed's plume): longer and
		/// much wider than a seed head, its far end drooping toward the stem's facing, lit through like a thin leaf.
		/// </summary>
		Plume = 3,
	}

	/// <summary>How one kind of grass prototype differs from the others as blades (matched by name prefix, longest first).</summary>
	[Serializable]
	public class GrassTypeTuning
	{
		[Tooltip("Detail prototype prefab names starting with this take these values (the longest matching prefix wins).")]
		public string Prefix = "Detail_Grass";
		[Tooltip("Chance a fully covered lattice cell holds a blade of this type (0..1): its density relative to the near density.")]
		[Range(0.05f, 1f)] public float Density = 1f;
		[Tooltip("Resistance to the wind: the push divides by it.")]
		[Range(0.2f, 4f)] public float Stiffness = 1f;
		[Tooltip("Blade base width, as a multiple of the base width.")]
		[Range(0.3f, 3f)] public float Width = 1f;
		[Tooltip("How far blades are pulled toward their clump's centre (0 none: an even lawn; 0.7 tussocks with bare ground between).")]
		[Range(0f, 0.9f)] public float ClumpPull = 0.3f;
		[Tooltip("How much a blade curves over (0 straight .. 1 rises then arches).")]
		[Range(0f, 1f)] public float Bend = 0.4f;
		[Tooltip("Multiplies the height read from the prototype mesh and its height scale.")]
		[Range(0.3f, 2f)] public float HeightScale = 1f;

		[Header("Sprinkled species and stem tips")]
		[Tooltip("0: a tussock species — a clump is wholly one type, picked among the others by their painted share (grass). Above 0: stems of this type are sprinkled one at a time through whatever else grows, this many per square metre near the camera where its layer is fully painted (flowers, reeds), and thin with distance as the grass does.")]
		[Min(0f)] public float Sprinkle = 0f;
		[Tooltip("What the stem carries at its tip.")]
		public GrassHead Head = GrassHead.None;
		[Tooltip("The head's size, metres: a flower's radius, a seed head's length.")]
		[Range(0.005f, 0.4f)] public float HeadSize = 0.035f;
		[Tooltip("The head's colour where the prototype mesh has no petal colours of its own (a seed head; a flower mesh's petals win).")]
		public Color HeadColour = new Color(0.36f, 0.25f, 0.14f);
		[Tooltip("A strap that keeps its width to a rounded end (a reed's leaf) instead of tapering to a point.")]
		public bool BluntTip;

		[Tooltip("How deep into a river's or lake's water this type may stand, metres (reeds and rushes in the shallows). 0: none of it grows below the bank (Bank Clearance).")]
		[Range(0f, 1.5f)] public float Wade = 0f;

		[Header("Added with the 32-type renderer (2026-10-10)")]
		[Tooltip("The share of this type's stems that carry its head (0..1; 0 reads as 1, every stem, as before this field existed). A tussock species with a head (feather grass, rushes, wheat) would otherwise put three records on every blade of a dense lawn; a sprinkled species keeps 1.")]
		[Range(0f, 1f)] public float HeadShare = 1f;
		[Tooltip("Up to four head colours (sRGB), used where the prototype mesh has no petal colours of its own (its petal colours win; with neither, Head Colour). A clump's heads share one of them.")]
		public Color[] HeadPalette = new Color[0];
		[Tooltip("The blade colours at the root and the tip (sRGB) where the prototype mesh gives none (no vertex colours, or not readable). Alpha 0: the built-in greens, as before this field existed.")]
		public Color BladeRoot = Color.clear;
		public Color BladeTip = Color.clear;
		[Tooltip("May stand taller than 1.56 m (to 3.1 m: Phragmites, elephant grass). A blade over 2.5 m packs its height at half the resolution; off, the type's height is capped at 1.56 m as it always was.")]
		public bool Tall;

		public GrassTypeTuning() { }

		public GrassTypeTuning(string prefix, float density, float stiffness, float width, float clumpPull, float bend, float heightScale = 1f)
		{
			Prefix = prefix;
			Density = density;
			Stiffness = stiffness;
			Width = width;
			ClumpPull = clumpPull;
			Bend = bend;
			HeightScale = heightScale;
		}

		/// <summary>The same tuning as a sprinkled species with a head (flowers, reeds).</summary>
		public GrassTypeTuning Sprinkled(float perSquareMetre, GrassHead head, float headSize, bool bluntTip = false)
		{
			Sprinkle = perSquareMetre;
			Head = head;
			HeadSize = headSize;
			BluntTip = bluntTip;
			return this;
		}

		/// <summary>The same tuning with a head on <paramref name="share"/> of its stems (a tussock species' seed heads or plumes).</summary>
		public GrassTypeTuning Headed(GrassHead head, float headSize, float share, string colour = null)
		{
			Head = head;
			HeadSize = headSize;
			HeadShare = share;
			if (colour != null)
			{
				HeadColour = Hex(colour);
			}
			return this;
		}

		/// <summary>The same tuning with its head colours where the mesh has none (sRGB hex, up to four).</summary>
		public GrassTypeTuning Palette(params string[] colours)
		{
			HeadPalette = new Color[colours.Length];
			for (int i = 0; i < colours.Length; i++)
			{
				HeadPalette[i] = Hex(colours[i]);
			}
			HeadColour = colours.Length > 0 ? HeadPalette[0] : HeadColour;
			return this;
		}

		/// <summary>The same tuning with blade colours where the mesh has none (sRGB hex).</summary>
		public GrassTypeTuning Colours(string root, string tip)
		{
			BladeRoot = Hex(root);
			BladeTip = Hex(tip);
			return this;
		}

		/// <summary>The same tuning standing <paramref name="metres"/> into a river's or lake's water.</summary>
		public GrassTypeTuning Wading(float metres)
		{
			Wade = metres;
			return this;
		}

		/// <summary>The same tuning allowed past 1.56 m (<see cref="Tall"/>).</summary>
		public GrassTypeTuning Taller()
		{
			Tall = true;
			return this;
		}

		/// <summary>A deep copy (the defaults table's entries are shared; whoever writes one into a profile gets its own).</summary>
		public GrassTypeTuning Clone()
		{
			var copy = (GrassTypeTuning)MemberwiseClone();
			copy.HeadPalette = HeadPalette != null ? (Color[])HeadPalette.Clone() : new Color[0];
			return copy;
		}

		/// <summary>The share of stems with a head: <see cref="HeadShare"/>, with 0 (an entry saved before the field) read as every stem.</summary>
		public float EffectiveHeadShare => HeadShare > 0f ? Mathf.Min(1f, HeadShare) : 1f;

		/// <summary>"#rrggbb" as an opaque colour, parsed here rather than by ColorUtility (a native call) so the defaults table builds anywhere, tests included.</summary>
		private static Color Hex(string hex)
		{
			string h = hex.TrimStart('#');
			if (h.Length != 6 || !uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint v))
			{
				return Color.magenta;
			}
			return new Color((v >> 16 & 255u) / 255f, (v >> 8 & 255u) / 255f, (v & 255u) / 255f, 1f);
		}
	}

	/// <summary>
	/// The blade types added by the vegetation expansion (2026-10-10, design §2c/§2d), as tunings the code supplies when the
	/// profile has none of its own: <see cref="GrassBladeSettings.TuningFor"/> takes an entry here only where its prefix
	/// matches a prototype more closely than any of the profile's (so an entry Jim adds to the profile always wins, and a
	/// prototype the profile already matches as closely is untouched). Each is derived from the nearest of the profile's
	/// seven (GrassTuft, GrassTall, GrassDry, Reeds, Detail_Flowers) so the new ones read as the same family.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why in code.</b> The tunings live in the Weather Render Profile asset, which Jim's editor holds loaded: an edit on
	/// disk is overwritten by the stale in-memory copy on the next save. Without these a new <c>Detail_GrassSedge</c> took
	/// the bare <c>Detail_Grass</c> tuning (an even lawn), a <c>Detail_ReedsPlume</c> the cattail's brown spindle, and every
	/// new flower set the meadow flowers' size. <see cref="GrassBladeSettings.AddMissingDefaultTypes"/> writes them into a
	/// profile for an editor tool (with Undo) when they should become editable.
	/// </para>
	/// <para>
	/// <b>Colours.</b> A blade's colours come from its prototype mesh's vertex colours (the art generator's hex picks, so the
	/// flora package's meshes decide them), its heads from the mesh's petal vertices. The <see cref="GrassTypeTuning.BladeRoot"/>,
	/// <see cref="GrassTypeTuning.BladeTip"/> and <see cref="GrassTypeTuning.HeadPalette"/> here are the design's colours,
	/// used only where a mesh gives none, so a missing or colourless mesh still draws the right plant. A prototype whose
	/// prefab does not exist is never a type at all.
	/// </para>
	/// <para>
	/// <b>Heights</b> are the meshes' (the art generator builds each at its design height): every <see cref="GrassTypeTuning.HeightScale"/>
	/// here is 1. The two taller than 1.56 m (ReedsPlume 2.5 m, GrassSavanna 1.8 m) are <see cref="GrassTypeTuning.Tall"/>.
	/// </para>
	/// </remarks>
	public static class GrassTypeDefaults
	{
		/// <summary>The new types' tunings, by prefix (prefab names are <c>Detail_&lt;Name&gt;</c>).</summary>
		public static readonly GrassTypeTuning[] Types =
		{
			// Carex: tussocks of arching, keeled blue-green blades (GrassTuft, less stiff and more bent, pulled into tussocks).
			new GrassTypeTuning("Detail_GrassSedge", 0.85f, 1.3f, 1.1f, 0.45f, 0.6f).Colours("#5e7a4a", "#8a9a62").Wading(0.1f),
			// Cottongrass: thin stems sprinkled through the bog, each with a white cotton tuft (a large white flower head).
			new GrassTypeTuning("Detail_GrassCotton", 1f, 1.2f, 0.7f, 0.1f, 0.1f).Sprinkled(30f, GrassHead.Flower, 0.045f)
				.Palette("#f6f6f0", "#ecebe0").Colours("#5a6a3a", "#8a9450").Wading(0.05f),
			// Marram, cordgrass, Stipagrostis: stiff, narrow rolled blades in tussocks with sand between (GrassDry stiffer, GrassTuft's pull).
			new GrassTypeTuning("Detail_GrassDune", 0.7f, 1.8f, 0.7f, 0.55f, 0.3f).Colours("#8a9a6e", "#a8b088"),
			// Stipa: fine bunch grass whose silky awns stream in the wind; a quarter of the stems carry a silver-straw plume.
			new GrassTypeTuning("Detail_GrassFeather", 0.8f, 0.8f, 0.6f, 0.4f, 0.55f).Headed(GrassHead.Plume, 0.22f, 0.25f, "#dcd8c0")
				.Colours("#8a8a5a", "#c8c49a"),
			// Red-oat / elephant grass: GrassTall's broad swathes at twice its height, golden.
			new GrassTypeTuning("Detail_GrassSavanna", 0.8f, 0.8f, 1.3f, 0.15f, 0.5f).Colours("#b8963c", "#d8bc6a").Taller(),
			// Phragmites: the Reeds' stiff blunt straps, taller, standing in the shallows, a purple-brown plume on each stem.
			new GrassTypeTuning("Detail_ReedsPlume", 1f, 2.4f, 2f, 0.35f, 0.2f).Sprinkled(110f, GrassHead.Plume, 0.3f, bluntTip: true)
				.Palette("#6a4a5a").Colours("#4e6430", "#8a9050").Wading(0.4f).Taller(),
			// Juncus: tussocks of stiff, thin, dark cylindrical stems, some with a small brown tuft near the top.
			new GrassTypeTuning("Detail_GrassRush", 0.9f, 2.2f, 0.55f, 0.5f, 0.08f).Headed(GrassHead.SeedHead, 0.04f, 0.3f, "#6a4a2a")
				.Colours("#2e4a1e", "#3e5a2a").Wading(0.25f),
			// Wheat: an even golden stand (fields are hand-masked, so no tussocks), most stems with an ear.
			new GrassTypeTuning("Detail_GrassWheat", 0.9f, 1.2f, 1f, 0.05f, 0.35f).Headed(GrassHead.SeedHead, 0.08f, 0.6f, "#c8a050")
				.Colours("#9a8a40", "#d8c070"),

			// Flower sets: the meadow flowers' stems (Detail_Flowers), sized to each set; a clump's flowers share one of four colours.
			// Gentian, saxifrage, moss campion, alpine buttercup: tiny heads on short stiff stems.
			new GrassTypeTuning("Detail_FlowersAlpine", 1f, 1.5f, 0.7f, 0.1f, 0.08f).Sprinkled(20f, GrassHead.Flower, 0.018f)
				.Palette("#3050c0", "#f4f4f0", "#c0408a", "#f0c830").Colours("#4a6a2a", "#66843a"),
			// Purple loosestrife, flag iris, meadowsweet, marsh marigold: tall wet-ground flowers at the water's edge.
			new GrassTypeTuning("Detail_FlowersWet", 1f, 1.1f, 0.9f, 0.1f, 0.15f).Sprinkled(10f, GrassHead.Flower, 0.04f)
				.Palette("#b0408a", "#f0d030", "#f0ead0", "#f2b81e").Colours("#3e6a2a", "#5e8a3a").Wading(0.1f),
			// Bluebell, wood anemone, wild garlic, primrose: low shade flowers.
			new GrassTypeTuning("Detail_FlowersWoodland", 1f, 1.3f, 0.8f, 0.1f, 0.2f).Sprinkled(12f, GrassHead.Flower, 0.025f)
				.Palette("#5a5ac8", "#f6f6f2", "#eef0e6", "#f0e890").Colours("#3a6224", "#58803a"),
			// Fireweed, goldenrod, lupin, umbellifers: tall, softer stems with big heads, fewer of them.
			new GrassTypeTuning("Detail_FlowersTall", 1f, 1f, 0.9f, 0.1f, 0.2f).Sprinkled(8f, GrassHead.Flower, 0.05f)
				.Palette("#c84890", "#e0b020", "#7a5ac8", "#f2f0e6").Colours("#4a7a2c", "#6a9040"),
			// A desert superbloom: poppy, lupine, desert marigold, verbena.
			new GrassTypeTuning("Detail_FlowersDesert", 1f, 1.4f, 0.8f, 0.1f, 0.1f).Sprinkled(18f, GrassHead.Flower, 0.03f)
				.Palette("#f08020", "#4a60c8", "#f0c020", "#d0609a").Colours("#6a7a3a", "#8a9450"),
		};

		/// <summary>The longest-prefix default for a prototype name, or null.</summary>
		public static GrassTypeTuning For(string name) => GrassBladeSettings.LongestPrefix(Types, name);
	}

	/// <summary>
	/// The procedural blade grass (<see cref="GrassBladeSystem"/>): which detail prototypes become blades,
	/// the density rings, the blade's shape and its light. On the Weather Render Profile ("Procedural
	/// grass"); read every frame, so changes apply while playing.
	/// </summary>
	[Serializable]
	public class GrassBladeSettings
	{
		[Tooltip("Off: the grass detail prototypes stay meshes (as before) everywhere. The dashboard's A/B button overrides it per play session.")]
		public bool Enabled = true;

		[Tooltip("Detail prototype prefab names starting with any of these are drawn as blades (and skipped by the detail renderer). Reeds and meadow flowers are blades too: sprinkled stems with a seed head or a flower at the tip (their Types entries). Sea plants never are (a blade lawn on the sea floor read as land grass).")]
		public string[] PrototypePrefixes = { "Detail_Grass", "Detail_Reeds", "Detail_Flowers" };

		[Tooltip("Per-type differences, matched by prefix (longest first).")]
		public GrassTypeTuning[] Types =
		{
			new GrassTypeTuning("Detail_Grass", 1f, 1f, 1f, 0.12f, 0.4f),
			new GrassTypeTuning("Detail_GrassLush", 1f, 0.9f, 1f, 0.1f, 0.45f),
			new GrassTypeTuning("Detail_GrassDry", 0.8f, 1.3f, 0.85f, 0.15f, 0.25f),
			new GrassTypeTuning("Detail_GrassTall", 0.75f, 0.7f, 1.15f, 0.12f, 0.55f),
			new GrassTypeTuning("Detail_GrassTuft", 0.85f, 1.6f, 0.9f, 0.3f, 0.3f),
			// Stiff straps standing in loose beds, each with a brown spindle at its top.
			new GrassTypeTuning("Detail_Reeds", 1f, 2.6f, 2f, 0.35f, 0.15f).Sprinkled(110f, GrassHead.SeedHead, 0.16f, bluntTip: true),
			// Thin straight stems, a flower each, sprinkled through the meadow grass.
			new GrassTypeTuning("Detail_Flowers", 1f, 1.3f, 0.8f, 0.1f, 0.12f).Sprinkled(14f, GrassHead.Flower, 0.032f),
		};

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
		[Tooltip("How much each blade takes the colour of the terrain TEXTURE under its root (the same arrays, tiling and tints the ground is drawn with). 1: the blade is the ground's colour, shaded root to tip by its type; 0: the type's own colours.")]
		[Range(0f, 1f)] public float TerrainTextureTint = 0.85f;
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

		/// <summary>
		/// The tuning for a prototype name: the longest matching prefix among the profile's <see cref="Types"/> and the code's
		/// <see cref="GrassTypeDefaults.Types"/>, the profile's on a tie; null when neither matches. A default is taken only
		/// for a prototype it names more closely than any profile entry (a new type the profile has never heard of: e.g.
		/// <c>Detail_GrassSedge</c> over the profile's bare <c>Detail_Grass</c>), so every prototype the profile already
		/// tunes resolves exactly as before.
		/// </summary>
		public GrassTypeTuning TuningFor(string name)
		{
			GrassTypeTuning own = LongestPrefix(Types, name);
			GrassTypeTuning fallback = GrassTypeDefaults.For(name);
			return fallback != null && (own == null || fallback.Prefix.Length > own.Prefix.Length) ? fallback : own;
		}

		/// <summary>The entry of <paramref name="tunings"/> whose prefix starts <paramref name="name"/> and is the longest, or null.</summary>
		public static GrassTypeTuning LongestPrefix(GrassTypeTuning[] tunings, string name)
		{
			GrassTypeTuning best = null;
			if (tunings == null || string.IsNullOrEmpty(name))
			{
				return null;
			}
			foreach (GrassTypeTuning t in tunings)
			{
				if (t != null && !string.IsNullOrEmpty(t.Prefix) && name.StartsWith(t.Prefix, StringComparison.Ordinal) && (best == null || t.Prefix.Length > best.Prefix.Length))
				{
					best = t;
				}
			}
			return best;
		}

		/// <summary>
		/// Appends a copy of every <see cref="GrassTypeDefaults.Types"/> entry whose prefix <see cref="Types"/> lacks, and
		/// returns how many. For an editor tool that makes the defaults editable in the profile: record the profile for Undo
		/// and mark it dirty around the call (through the loaded object, never by editing the asset's file). Rendering is the
		/// same before and after: <see cref="TuningFor"/> already resolved these names to the same values.
		/// </summary>
		public int AddMissingDefaultTypes()
		{
			var added = new System.Collections.Generic.List<GrassTypeTuning>();
			foreach (GrassTypeTuning d in GrassTypeDefaults.Types)
			{
				bool present = false;
				if (Types != null)
				{
					foreach (GrassTypeTuning t in Types)
					{
						present |= t != null && string.Equals(t.Prefix, d.Prefix, StringComparison.Ordinal);
					}
				}
				if (!present)
				{
					added.Add(d.Clone());
				}
			}
			if (added.Count > 0)
			{
				var merged = new System.Collections.Generic.List<GrassTypeTuning>(Types ?? new GrassTypeTuning[0]);
				merged.AddRange(added);
				Types = merged.ToArray();
			}
			return added.Count;
		}

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
