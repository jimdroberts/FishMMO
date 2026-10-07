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
		[Tooltip("Blades nearer than this cast shadows (0: none).")]
		[Min(0f)] public float ShadowDistance = 20f;
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

		/// <summary>The tuning for a prototype name: the longest matching prefix, or null.</summary>
		public GrassTypeTuning TuningFor(string name)
		{
			GrassTypeTuning best = null;
			if (Types == null || string.IsNullOrEmpty(name))
			{
				return null;
			}
			foreach (GrassTypeTuning t in Types)
			{
				if (t != null && !string.IsNullOrEmpty(t.Prefix) && name.StartsWith(t.Prefix, StringComparison.Ordinal) && (best == null || t.Prefix.Length > best.Prefix.Length))
				{
					best = t;
				}
			}
			return best;
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
