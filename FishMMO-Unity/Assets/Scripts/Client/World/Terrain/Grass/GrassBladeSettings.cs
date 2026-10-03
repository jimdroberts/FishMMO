using System;
using UnityEngine;

namespace FishMMO.Client
{
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

		[Tooltip("Detail prototype prefab names starting with any of these are drawn as blades (and skipped by the detail renderer). Reeds stay meshes: a reed is a stiff stem with a seed head, which a tapering blade loses.")]
		public string[] PrototypePrefixes = { "Detail_Grass" };

		[Tooltip("Per-type differences, matched by prefix (longest first).")]
		public GrassTypeTuning[] Types =
		{
			new GrassTypeTuning("Detail_Grass", 1f, 1f, 1f, 0.12f, 0.4f),
			new GrassTypeTuning("Detail_GrassLush", 1f, 0.9f, 1f, 0.1f, 0.45f),
			new GrassTypeTuning("Detail_GrassDry", 0.8f, 1.3f, 0.85f, 0.15f, 0.25f),
			new GrassTypeTuning("Detail_GrassTall", 0.75f, 0.7f, 1.15f, 0.12f, 0.55f),
			new GrassTypeTuning("Detail_GrassTuft", 0.85f, 1.6f, 0.9f, 0.3f, 0.3f),
		};

		[Header("Density rings (distance m, blades per square metre)")]
		[Tooltip("Blades per square metre out to each distance, falling log-log between them; the first is the near density, which also sets the candidate lattice (cell = 1/sqrt(density)). Thinning is nested: a far blade is always one of the near ones.")]
		public Vector2[] Rings =
		{
			new Vector2(8f, 1600f),
			new Vector2(20f, 480f),
			new Vector2(50f, 110f),
			new Vector2(120f, 22f),
			new Vector2(300f, 4.5f),
		};

		[Tooltip("The detail-layer density (as a share of its maximum) at and above which grass grows at the FULL ring density. The scatter paints biome grass at roughly 20–40% of the maximum; read as a straight spawn chance that thinned the near field to a few dozen blades per square metre, which looked like the old clumps. Below this the field thins in proportion, so painted edges still fade.")]
		[Range(0.05f, 1f)] public float Fullness = 0.35f;
		[Tooltip("Blades widen as the field thins: width × share^-exponent (0.5 keeps the covered area per blade count).")]
		[Range(0f, 1f)] public float WidenExponent = 0.5f;
		[Range(1f, 12f)] public float MaxWiden = 6f;

		[Header("Levels of detail")]
		[Tooltip("Blades nearer than this draw the 15-vertex strip; to Lod1 Distance the 7-vertex one; beyond, a triangle.")]
		[Min(1f)] public float Lod0Distance = 12f;
		[Min(2f)] public float Lod1Distance = 45f;
		[Tooltip("Blades nearer than this cast shadows (0: none).")]
		[Min(0f)] public float ShadowDistance = 20f;
		[Tooltip("The most blades the camera view can draw in a frame (all levels); the shadow views get 0.3 of it each. Appends past it are dropped.")]
		[Min(1024)] public int BladeCap = 2500000;
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
