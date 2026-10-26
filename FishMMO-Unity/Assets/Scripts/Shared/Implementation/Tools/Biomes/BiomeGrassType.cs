using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>What a stem carries at its tip, drawn as two crossed cards there (FishGrassBlades.hlsl GrassHeadVertex).</summary>
	public enum GrassBladeHead : byte
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

	/// <summary>
	/// How one blade prefab (a <c>Detail_Grass…</c>, <c>Detail_Reeds…</c> or <c>Detail_Flowers…</c> detail) looks and grows as
	/// procedural blades in ONE biome: its shape, its stems' heads, its colours and how much of the ground's colour it takes.
	/// A list of these is on every <see cref="BiomeTemplate"/> (<see cref="BiomeTemplate.GrassTypes"/>), one per blade prefab
	/// its rules grow, so the same meadow grass can be lush and green in one biome and short and straw-coloured in the next.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where it was.</b> Until 2026-10-15 every biome's blades of a prefab looked the same: one global list on the Weather
	/// Render Profile, matched by prefab-name prefix, and one global ground tint (0.85) for every type. Those values are now
	/// <see cref="BiomeGrassDefaults"/>, which the biome art authoring writes onto every template for every blade prefab its
	/// rules grow, so the first authoring changes no scene; the per-biome rounds then tune each biome's own.
	/// </para>
	/// <para>
	/// <b>Colours.</b> A blade's root and tip colours come from its prototype mesh's vertex colours (the art generator's picks)
	/// unless <see cref="ColoursOverMesh"/> is ticked; <see cref="RootColour"/> and <see cref="TipColour"/> are used where the
	/// mesh gives none, or always when ticked. The same holds for the head colours and the mesh's petals. The healthy and dry
	/// tints are the spawn rule's (<see cref="PrefabSpawnRule.detailHealthyColor"/>, <see cref="PrefabSpawnRule.detailDryColor"/>),
	/// carried by the rule's detail prototype: a biome's rule says how its patches brown.
	/// </para>
	/// <para>
	/// <b>Ground tint</b> (<see cref="GroundTint"/>): how much of the terrain texture's colour under its root a blade takes
	/// (1: the ground's colour, shaded root to tip by the type; 0: the type's own colours). 0.85 for every type before
	/// 2026-10-15 (what the authoring writes, so nothing changed); an entry made by hand starts at
	/// <see cref="DefaultGroundTint"/>, where a type's own colour reads through.
	/// </para>
	/// </remarks>
	[Serializable]
	public class BiomeGrassType
	{
		/// <summary>The ground tint a hand-made entry starts with: the type's own colour reads through the ground's.</summary>
		public const float DefaultGroundTint = 0.4f;

		/// <summary>The one global ground tint every type took before 2026-10-15 (the profile's TerrainTextureTint).</summary>
		public const float LegacyGroundTint = 0.85f;

		/// <summary>Head colours one type can have (the blade renderer's table holds four).</summary>
		public const int MaxHeadColours = 4;

		/// <summary>The head colour every type had where neither its mesh nor its tuning gave one.</summary>
		public static readonly Color DefaultHeadColour = new Color(0.36f, 0.25f, 0.14f);

		[Tooltip("The blade prefab (Detail_Grass…, Detail_Reeds…, Detail_Flowers…) whose stems in this biome look like this.")]
		public GameObject Prefab;

		[Header("Shape")]
		[Tooltip("Chance a fully covered lattice cell holds a blade of this type (0..1): its density relative to the near density.")]
		[Range(0.05f, 1f)] public float Density = 1f;
		[Tooltip("Resistance to the wind: the push divides by it.")]
		[Range(0.2f, 4f)] public float Stiffness = 1f;
		[Tooltip("Blade base width, as a multiple of the renderer's base width.")]
		[Range(0.3f, 3f)] public float Width = 1f;
		[Tooltip("How far blades are pulled toward their clump's centre (0 none: an even lawn; 0.7 tussocks with bare ground between).")]
		[Range(0f, 0.9f)] public float ClumpPull = 0.3f;
		[Tooltip("How much a blade curves over (0 straight .. 1 rises then arches).")]
		[Range(0f, 1f)] public float Bend = 0.4f;
		[Tooltip("Multiplies the height read from the prototype mesh and its rule's height scale.")]
		[Range(0.3f, 2f)] public float HeightScale = 1f;
		[Tooltip("May stand taller than 1.56 m (to 3.1 m: Phragmites, elephant grass). Off, the type's height is capped at 1.56 m.")]
		public bool Tall;
		[Tooltip("How deep into a river's or lake's water this type may stand, metres (reeds and rushes in the shallows). 0: none of it grows below the bank.")]
		[Range(0f, 1.5f)] public float Wade;

		[Header("Sprinkled species and stem tips")]
		[Tooltip("0: a tussock species — a clump is wholly one type, picked among the others by their painted share (grass). Above 0: stems of this type are sprinkled one at a time through whatever else grows, this many per square metre near the camera where its layer is fully painted (flowers, reeds).")]
		[Min(0f)] public float Sprinkle;
		[Tooltip("What the stem carries at its tip.")]
		public GrassBladeHead Head;
		[Tooltip("The head's size, metres: a flower's radius, a seed head's length.")]
		[Range(0.005f, 0.4f)] public float HeadSize = 0.035f;
		[Tooltip("The share of this type's stems that carry its head (0..1).")]
		[Range(0f, 1f)] public float HeadShare = 1f;
		[Tooltip("A strap that keeps its width to a rounded end (a reed's leaf) instead of tapering to a point.")]
		public bool BluntTip;
		/// <remarks>
		/// Decision 41 (2026-10-18, the Cryovolcanic Plain round): mineral blades (hoar-frost needles, salt quills) stand as they grew
		/// and never sway, whatever the wind and the world's air (the blade shader reads it per type, GrassBladeRenderer's MORE row).
		/// </remarks>
		[Tooltip("Never sways: a mineral blade (ice needles, salt quills) stands as it grew whatever the wind. Off: sways with the wind and the world's air, as every blade did.")]
		public bool Rigid;
		/// <remarks>
		/// Decision 75 (2026-10-25, phase I2t, the Savanna round): an after-burn flower (fire lilies) grows ONLY on ground the burn mosaic
		/// burnt one to four weeks before (BurnMosaic.BurnStage.Lily; GrassBladeRenderer's MORE row w), and nowhere else.
		/// </remarks>
		[Tooltip("Grows only where the burn mosaic burnt the ground one to four weeks before (fire lilies after a veld fire). Off: grows wherever it is painted, as every type did.")]
		public bool AfterBurn;

		[Header("Colours (sRGB)")]
		[Tooltip("The blade's colour at the root. Used where the prototype mesh gives no colours, or always when Colours Over Mesh is ticked. Alpha 0: none (the built-in greens where the mesh gives none).")]
		public Color RootColour = Color.clear;
		[Tooltip("The blade's colour at the tip, as Root Colour.")]
		public Color TipColour = Color.clear;
		[Tooltip("Use Root and Tip Colour even where the prototype mesh has colours of its own (this biome's grass is not the mesh's green).")]
		public bool ColoursOverMesh;
		[Tooltip("Up to four head colours (a clump's heads share one of them). Used where the prototype mesh has no petal colours, or always when Head Colours Over Mesh is ticked.")]
		public Color[] HeadColours = { DefaultHeadColour };
		[Tooltip("Use Head Colours even where the prototype mesh has petal colours of its own.")]
		public bool HeadColoursOverMesh;

		[Header("Ground")]
		[Tooltip("How much of the terrain texture's colour under its root the blade takes: 1 the ground's colour (shaded root to tip by the type), 0 the type's own colours. Every type took 0.85 before 2026-10-15.")]
		[Range(0f, 1f)] public float GroundTint = DefaultGroundTint;

		public BiomeGrassType() { }

		public BiomeGrassType(float density, float stiffness, float width, float clumpPull, float bend)
		{
			Density = density;
			Stiffness = stiffness;
			Width = width;
			ClumpPull = clumpPull;
			Bend = bend;
		}

		/// <summary>A sprinkled species with a head on every stem (flowers, reeds).</summary>
		public BiomeGrassType Sprinkled(float perSquareMetre, GrassBladeHead head, float headSize, bool bluntTip = false)
		{
			Sprinkle = perSquareMetre;
			Head = head;
			HeadSize = headSize;
			BluntTip = bluntTip;
			return this;
		}

		/// <summary>A head on <paramref name="share"/> of its stems (a tussock species' seed heads or plumes), of one colour (sRGB hex).</summary>
		public BiomeGrassType Headed(GrassBladeHead head, float headSize, float share, string colour = null)
		{
			Head = head;
			HeadSize = headSize;
			HeadShare = share;
			if (colour != null)
			{
				HeadColours = new[] { Hex(colour) };
			}
			return this;
		}

		/// <summary>Its head colours (sRGB hex, up to four).</summary>
		public BiomeGrassType Palette(params string[] colours)
		{
			HeadColours = new Color[Math.Min(colours.Length, MaxHeadColours)];
			for (int i = 0; i < HeadColours.Length; i++)
			{
				HeadColours[i] = Hex(colours[i]);
			}
			return this;
		}

		/// <summary>Its blade colours where the mesh has none (sRGB hex).</summary>
		public BiomeGrassType Colours(string root, string tip)
		{
			RootColour = Hex(root);
			TipColour = Hex(tip);
			return this;
		}

		/// <summary>Standing <paramref name="metres"/> into a river's or lake's water.</summary>
		public BiomeGrassType Wading(float metres)
		{
			Wade = metres;
			return this;
		}

		/// <summary>Allowed past 1.56 m (<see cref="Tall"/>).</summary>
		public BiomeGrassType Taller()
		{
			Tall = true;
			return this;
		}

		/// <summary>With this ground tint.</summary>
		public BiomeGrassType Tinted(float groundTint)
		{
			GroundTint = groundTint;
			return this;
		}

		/// <summary>Never swaying (<see cref="Rigid"/>): a mineral blade.</summary>
		public BiomeGrassType Rigidly()
		{
			Rigid = true;
			return this;
		}

		/// <summary>Grows only after a burn (<see cref="AfterBurn"/>): a fire lily.</summary>
		public BiomeGrassType AfterBurning()
		{
			AfterBurn = true;
			return this;
		}

		/// <summary>True when <see cref="RootColour"/> and <see cref="TipColour"/> are colours (alpha above 0), not "none".</summary>
		public bool HasBladeColours => RootColour.a > 0f && TipColour.a > 0f;

		/// <summary>A deep copy.</summary>
		public BiomeGrassType Clone()
		{
			var copy = (BiomeGrassType)MemberwiseClone();
			copy.HeadColours = HeadColours != null ? (Color[])HeadColours.Clone() : new Color[0];
			return copy;
		}

		/// <summary>
		/// Every value of the look but the prefab, as text: equal looks give equal signatures. Floats are rounded to 1/10 000
		/// first, so a value that survives a trip through an asset's YAML as the nearest float still reads the same. What the
		/// blade renderer's per-scene table merges two biomes' entries by, and (with the prefab) what the authoring marks a
		/// written entry with.
		/// </summary>
		public string Signature()
		{
			var text = new StringBuilder(256);
			text.Append(Q(Density)).Append(',').Append(Q(Stiffness)).Append(',').Append(Q(Width)).Append(',').Append(Q(ClumpPull))
				.Append(',').Append(Q(Bend)).Append(',').Append(Q(HeightScale)).Append(',').Append(Tall ? 'T' : 't').Append(',').Append(Q(Wade))
				.Append("|s").Append(Q(Sprinkle)).Append(',').Append((int)Head).Append(',').Append(Q(HeadSize)).Append(',').Append(Q(HeadShare))
				.Append(',').Append(BluntTip ? 'B' : 'b')
				.Append("|c").Append(Q(RootColour)).Append(Q(TipColour)).Append(ColoursOverMesh ? 'M' : 'm').Append("|h");
			if (HeadColours != null)
			{
				foreach (Color c in HeadColours)
				{
					text.Append(Q(c));
				}
			}
			text.Append(HeadColoursOverMesh ? 'M' : 'm').Append("|g").Append(Q(GroundTint));
			// Only a rigid look says so (2026-10-18), so every signature and mark made before reads the same.
			if (Rigid)
			{
				text.Append("|R");
			}
			// Likewise only an after-burn look (2026-10-25, I2t).
			if (AfterBurn)
			{
				text.Append("|F");
			}
			return text.ToString();
		}

		private static string Q(float value) => Math.Round(value * 10000.0).ToString(CultureInfo.InvariantCulture);

		private static string Q(Color c) => $"({Q(c.r)},{Q(c.g)},{Q(c.b)},{Q(c.a)})";

		/// <summary>"#rrggbb" as an opaque colour, parsed here rather than by ColorUtility (a native call) so the table builds anywhere, tests included.</summary>
		public static Color Hex(string hex)
		{
			string h = hex.TrimStart('#');
			if (h.Length != 6 || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
			{
				return Color.magenta;
			}
			return new Color((v >> 16 & 255u) / 255f, (v >> 8 & 255u) / 255f, (v & 255u) / 255f, 1f);
		}
	}

	/// <summary>
	/// Every blade prefab's look as it was before looks were per biome (2026-10-15), by prefab-name prefix, the longest
	/// matching prefix winning: the Weather Render Profile's seven (the generic <c>Detail_Grass</c>, the four legacy grasses,
	/// reeds and meadow flowers) and the vegetation expansion's thirteen (sedge, cotton, dune, feather, savanna, Phragmites,
	/// rush, wheat, five flower sets), every one at the old global ground tint of 0.85.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What it is for.</b> What the biome art authoring writes onto a template for each blade prefab its rules grow
	/// (<see cref="BiomeTemplate.GrassTypes"/>), so authoring changes nothing a scene shows; and the blade renderer's fallback
	/// for a prototype whose biome has no entry for it (with a warning). Never a biome's decision.
	/// </para>
	/// <para>
	/// <b>Heights</b> are the meshes' (the art generator builds each at its design height): every height scale here is 1. The
	/// two taller than 1.56 m (ReedsPlume 2.5 m, GrassSavanna 1.8 m) are <see cref="BiomeGrassType.Tall"/>.
	/// </para>
	/// </remarks>
	public static class BiomeGrassDefaults
	{
		/// <summary>The prefab-name prefixes drawn as blades by default (the profile's PrototypePrefixes: a rendering-path rule).</summary>
		public static readonly string[] BladePrefixes = { "Detail_Grass", "Detail_Reeds", "Detail_Flowers" };

		private static readonly (string Prefix, BiomeGrassType Look)[] Table =
		{
			// The profile's own, as its asset held them (= GrassBladeSettings' initialiser before 2026-10-15).
			("Detail_Grass", new BiomeGrassType(1f, 1f, 1f, 0.12f, 0.4f)),
			("Detail_GrassLush", new BiomeGrassType(1f, 0.9f, 1f, 0.1f, 0.45f)),
			("Detail_GrassDry", new BiomeGrassType(0.8f, 1.3f, 0.85f, 0.15f, 0.25f)),
			("Detail_GrassTall", new BiomeGrassType(0.75f, 0.7f, 1.15f, 0.12f, 0.55f)),
			("Detail_GrassTuft", new BiomeGrassType(0.85f, 1.6f, 0.9f, 0.3f, 0.3f)),
			// Stiff straps standing in loose beds, each with a brown spindle at its top.
			("Detail_Reeds", new BiomeGrassType(1f, 2.6f, 2f, 0.35f, 0.15f).Sprinkled(110f, GrassBladeHead.SeedHead, 0.16f, bluntTip: true)),
			// Thin straight stems, a flower each, sprinkled through the meadow grass.
			("Detail_Flowers", new BiomeGrassType(1f, 1.3f, 0.8f, 0.1f, 0.12f).Sprinkled(14f, GrassBladeHead.Flower, 0.032f)),

			// The vegetation expansion's (2026-10-10, design §2c/§2d; GrassTypeDefaults before 2026-10-15).
			// Carex: tussocks of arching, keeled blue-green blades (GrassTuft, less stiff and more bent, pulled into tussocks).
			("Detail_GrassSedge", new BiomeGrassType(0.85f, 1.3f, 1.1f, 0.45f, 0.6f).Colours("#5e7a4a", "#8a9a62").Wading(0.1f)),
			// Cottongrass: thin stems sprinkled through the bog, each with a white cotton tuft (a large white flower head).
			("Detail_GrassCotton", new BiomeGrassType(1f, 1.2f, 0.7f, 0.1f, 0.1f).Sprinkled(30f, GrassBladeHead.Flower, 0.045f)
				.Palette("#f6f6f0", "#ecebe0").Colours("#5a6a3a", "#8a9450").Wading(0.05f)),
			// Marram, cordgrass, Stipagrostis: stiff, narrow rolled blades in tussocks with sand between.
			("Detail_GrassDune", new BiomeGrassType(0.7f, 1.8f, 0.7f, 0.55f, 0.3f).Colours("#8a9a6e", "#a8b088")),
			// Stipa: fine bunch grass whose silky awns stream in the wind; a quarter of the stems carry a silver-straw plume.
			("Detail_GrassFeather", new BiomeGrassType(0.8f, 0.8f, 0.6f, 0.4f, 0.55f).Headed(GrassBladeHead.Plume, 0.22f, 0.25f, "#dcd8c0")
				.Colours("#8a8a5a", "#c8c49a")),
			// Red-oat / elephant grass: GrassTall's broad swathes at twice its height, golden.
			("Detail_GrassSavanna", new BiomeGrassType(0.8f, 0.8f, 1.3f, 0.15f, 0.5f).Colours("#b8963c", "#d8bc6a").Taller()),
			// Phragmites: the Reeds' stiff blunt straps, taller, standing in the shallows, a purple-brown plume on each stem.
			("Detail_ReedsPlume", new BiomeGrassType(1f, 2.4f, 2f, 0.35f, 0.2f).Sprinkled(110f, GrassBladeHead.Plume, 0.3f, bluntTip: true)
				.Palette("#6a4a5a").Colours("#4e6430", "#8a9050").Wading(0.4f).Taller()),
			// Juncus: tussocks of stiff, thin, dark cylindrical stems, some with a small brown tuft near the top.
			("Detail_GrassRush", new BiomeGrassType(0.9f, 2.2f, 0.55f, 0.5f, 0.08f).Headed(GrassBladeHead.SeedHead, 0.04f, 0.3f, "#6a4a2a")
				.Colours("#2e4a1e", "#3e5a2a").Wading(0.25f)),
			// Wheat: an even golden stand (fields are hand-masked, so no tussocks), most stems with an ear.
			("Detail_GrassWheat", new BiomeGrassType(0.9f, 1.2f, 1f, 0.05f, 0.35f).Headed(GrassBladeHead.SeedHead, 0.08f, 0.6f, "#c8a050")
				.Colours("#9a8a40", "#d8c070")),
			// Flower sets: the meadow flowers' stems, sized to each set; a clump's flowers share one of four colours.
			// Gentian, saxifrage, moss campion, alpine buttercup: tiny heads on short stiff stems.
			("Detail_FlowersAlpine", new BiomeGrassType(1f, 1.5f, 0.7f, 0.1f, 0.08f).Sprinkled(20f, GrassBladeHead.Flower, 0.018f)
				.Palette("#3050c0", "#f4f4f0", "#c0408a", "#f0c830").Colours("#4a6a2a", "#66843a")),
			// Purple loosestrife, flag iris, meadowsweet, marsh marigold: tall wet-ground flowers at the water's edge.
			("Detail_FlowersWet", new BiomeGrassType(1f, 1.1f, 0.9f, 0.1f, 0.15f).Sprinkled(10f, GrassBladeHead.Flower, 0.04f)
				.Palette("#b0408a", "#f0d030", "#f0ead0", "#f2b81e").Colours("#3e6a2a", "#5e8a3a").Wading(0.1f)),
			// Bluebell, wood anemone, wild garlic, primrose: low shade flowers.
			("Detail_FlowersWoodland", new BiomeGrassType(1f, 1.3f, 0.8f, 0.1f, 0.2f).Sprinkled(12f, GrassBladeHead.Flower, 0.025f)
				.Palette("#5a5ac8", "#f6f6f2", "#eef0e6", "#f0e890").Colours("#3a6224", "#58803a")),
			// Fireweed, goldenrod, lupin, umbellifers: tall, softer stems with big heads, fewer of them.
			("Detail_FlowersTall", new BiomeGrassType(1f, 1f, 0.9f, 0.1f, 0.2f).Sprinkled(8f, GrassBladeHead.Flower, 0.05f)
				.Palette("#c84890", "#e0b020", "#7a5ac8", "#f2f0e6").Colours("#4a7a2c", "#6a9040")),
			// A desert superbloom: poppy, lupine, desert marigold, verbena.
			("Detail_FlowersDesert", new BiomeGrassType(1f, 1.4f, 0.8f, 0.1f, 0.1f).Sprinkled(18f, GrassBladeHead.Flower, 0.03f)
				.Palette("#f08020", "#4a60c8", "#f0c020", "#d0609a").Colours("#6a7a3a", "#8a9450")),
		};

		/// <summary>The prefixes the table matches by, longest last within each family (for tests and tools).</summary>
		public static string[] Prefixes
		{
			get
			{
				var prefixes = new string[Table.Length];
				for (int i = 0; i < Table.Length; i++)
				{
					prefixes[i] = Table[i].Prefix;
				}
				return prefixes;
			}
		}

		/// <summary>True for a prefab drawn as blades by default (<see cref="BladePrefixes"/>).</summary>
		public static bool IsBlade(string prefabName)
		{
			if (string.IsNullOrEmpty(prefabName))
			{
				return false;
			}
			foreach (string prefix in BladePrefixes)
			{
				if (prefabName.StartsWith(prefix, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// A fresh copy of the old look for a blade prefab by name (the longest matching prefix), at the old ground tint of
		/// 0.85, with no prefab set; null when no prefix matches (not a blade).
		/// </summary>
		public static BiomeGrassType For(string prefabName)
		{
			if (string.IsNullOrEmpty(prefabName))
			{
				return null;
			}
			int best = -1;
			for (int i = 0; i < Table.Length; i++)
			{
				if (prefabName.StartsWith(Table[i].Prefix, StringComparison.Ordinal) && (best < 0 || Table[i].Prefix.Length > Table[best].Prefix.Length))
				{
					best = i;
				}
			}
			if (best < 0)
			{
				return null;
			}
			BiomeGrassType look = Table[best].Look.Clone();
			look.GroundTint = BiomeGrassType.LegacyGroundTint;
			return look;
		}
	}
}
