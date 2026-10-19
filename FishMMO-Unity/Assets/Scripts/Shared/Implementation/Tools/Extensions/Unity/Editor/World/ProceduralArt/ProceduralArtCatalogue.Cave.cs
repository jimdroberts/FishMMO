#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What glows, or shines, on a cave's rock (the cave kit's glow art, <see cref="CaveGlowMeshes"/>).</summary>
	/// <remarks>Append-only: names are in generated prefab paths.</remarks>
	public enum CaveGlowKind
	{
		/// <summary>A cluster of glowing crystal points on a lump of host rock, 0.6–1.4 m.</summary>
		Crystal,
		/// <summary>Giant glowing crystal columns, 3–5.5 m: a cavern's centrepiece.</summary>
		GiantCrystal,
		/// <summary>A tuft of small luminous bonnet caps on slender stalks (Mycena), 0.1–0.5 m.</summary>
		Fungus,
		/// <summary>A giant glowcap, 1–2.7 m: a fantasy cavern's mushroom, gills glowing under a broad cap.</summary>
		GiantFungus,
		/// <summary>Tiers of luminous bracket fungi on a wall (Panellus).</summary>
		ShelfFungus,
		/// <summary>A patch of glow-worm snares hung from a roof (Arachnocampa): silk threads beaded with droplets, the larvae glowing at their tops.</summary>
		GlowWorms,
		/// <summary>An ore seam in a wall: copper, iron, silver or gold in a band of quartz.</summary>
		OreVein,
		/// <summary>A seam of a glowing fantasy ore (starmetal, emberstone) in quartz.</summary>
		GlowOreVein,
	}

	/// <summary>How a piece of glow art is set against the cave's rock.</summary>
	public enum CaveMount
	{
		/// <summary>On a floor or a ledge: origin at its foot, standing up +y; its base reaches a little below y = 0 to seat on uneven rock.</summary>
		Floor,
		/// <summary>On a wall: origin on the wall's surface, its back toward +z (into the rock), standing out along −z, up +y.</summary>
		Wall,
		/// <summary>From a roof: origin on the roof's surface, hanging down −y; it reaches up into the rock to seat on an uneven roof.</summary>
		Roof,
	}

	/// <summary>
	/// How a cave crystal grows (<see cref="CaveMineral"/>, 2026-10-16, I6c): the shape its meshes are built in. Append-only:
	/// names are in generated mesh paths.
	/// </summary>
	public enum CaveMineralHabit
	{
		/// <summary>A druse: a crust of many small slender points carpeting a lump of rock, leaning outward (clear quartz).</summary>
		Druse,
		/// <summary>A few stout six-sided prisms with pyramid tips out of a pocket in the rock (smoky quartz).</summary>
		Prisms,
		/// <summary>A geode broken open: a rind of rock round a hollow lined with points growing inward (amethyst, wyrdstone).</summary>
		Geode,
		/// <summary>Interlocking cubes, some stood on an edge or a corner, on a lump of rock (fluorite).</summary>
		Cubes,
		/// <summary>Thin glassy blades and splinters jutting every way out of a mass of rock (obsidian).</summary>
		Shards,
		/// <summary>Rounded nodules and drops sunk in a bed of rock (amber).</summary>
		Nodules,
		/// <summary>Broad tabular blades with chisel tips bursting from a lump of scoria (sunstone).</summary>
		Blades,
	}

	/// <summary>
	/// One crystal of the cave-crystal catalogue (<see cref="BiomeCaveCrystalKind"/>, 2026-10-16, I6c): the rock formation it
	/// already is, or the habit and material it is built in; whether and how it lights the cave.
	/// </summary>
	public struct CaveMineral
	{
		public BiomeCaveCrystalKind Kind;
		/// <summary>
		/// Whether it is a rock formation's crystal the rock art already makes (<see cref="FormationRock"/>'s
		/// <see cref="FormationShape"/>): selenite is gypsum's, frost crystal water ice's. No mesh, material or prefab of its own.
		/// </summary>
		public bool Formation;
		public string FormationRock, FormationShape;
		/// <summary>The habit its own meshes are built in (when not a formation).</summary>
		public CaveMineralHabit Habit;
		/// <summary>Its material's colours (its own when not a formation); <see cref="CaveGlowHue.Emission"/> above black for one that glows.</summary>
		public CaveGlowHue Hue;
		/// <summary>
		/// Whether a cave light goes beside its clusters in a chamber (as beside selenite's always did): the glowing kinds, selenite
		/// and frost crystal; the plain minerals only glitter under a lamp.
		/// </summary>
		public bool Lit;
		/// <summary>The hue bits of its light (<see cref="CaveArt.EmitterSeed"/>: 0 violet, 1 blue, 2 green, 3 amber), or −1 for one by chance.</summary>
		public int LightHue;
		/// <summary>
		/// Whether its glow FLICKERS instead of breathing (2026-10-18, decision 33: stormglass's threads of lightning): its light
		/// a cold glow with the flicker (SceneEmitterKind.Glow, SceneGlow.FlickerBit) wherever it lights a cave or stands in a
		/// crystal post, its material's emission crackling with it (_FishStructure −1). No kind flickers before its biome's round.
		/// </summary>
		public bool Flickers;
		/// <summary>One line for designers.</summary>
		public string Description;

		/// <summary>Whether it glows by its own material (its emission).</summary>
		public bool Glows => !Formation && Hue.Emission.maxColorComponent > 0f;
	}

	/// <summary>One colour (or ore) a kind of glow art is made in.</summary>
	public struct CaveGlowHue
	{
		public string Name;
		/// <summary>The surface's colour under a lamp (<c>_BaseColor</c>).</summary>
		public Color Albedo;
		/// <summary>Its glow, HDR (<c>_EmissionColor</c>, over the glow map): black for an ore that only shines.</summary>
		public Color Emission;
		public float Smoothness, Metallic;
		/// <summary>The colour of the light it casts, for the placer's emitters (black for none).</summary>
		public Color Light;
		/// <summary>
		/// The hue bits (8–9 of the seed) a <see cref="SceneEmitterKind.Crystal"/> emitter carries for this light: 0 violet,
		/// 1 blue, 2 green, 3 amber (SiteLightPool); −1 for no light.
		/// </summary>
		public int EmitterHue;
	}

	/// <summary>One kind of glow art: how it is mounted, how many shapes it has, the hues it comes in and what it costs.</summary>
	public struct CaveGlowFamily
	{
		public CaveGlowKind Kind;
		public CaveMount Mount;
		/// <summary>The kind whose meshes it wears (a glowing ore vein wears the ore veins').</summary>
		public CaveGlowKind Shape;
		/// <summary>Distinct shapes; each in every hue.</summary>
		public int Variants;
		public CaveGlowHue[] Hues;
		/// <summary>The materials' group: <c>Cave_{MaterialSet}_{Hue}</c>.</summary>
		public string MaterialSet;
		/// <summary>A mesh collider from the last level (what a player would walk into); none for what is brushed past.</summary>
		public bool Collides;
		public bool CastsShadows;
		/// <summary>How far a light placed with it should reach, metres (the placer adds the light, for the brightest few); 0 for none.</summary>
		public float Reach;
		/// <summary>One line for designers.</summary>
		public string Description;
	}

	/// <summary>
	/// The cave kit's glow art (2026-10-13: "Crystals, Luminous fungi, Glow-worm ceilings, Ore veins in mines"): its kinds,
	/// hues and shapes, names and paths. Built in the structure kit's steps (BiomeArtGenerator.CaveGlow.cs), so a
	/// structures-only generation makes it too and the Structures family's fingerprint covers it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why it glows in the dark.</b> Every glowing material is FishMMO/Weather Lit with <c>_EMISSION</c> on: URP adds the
	/// emission after the lighting, so the cave darkness (which takes the occlusion, the ambient and the sun away) leaves it
	/// whole. Its brightness is not flat paint: the mesh's first texture coordinate is a glow level (0 dark .. 1 brightest,
	/// u) and a mottle coordinate (v), read through two shared ramps (<c>Structure_CaveGlow_Albedo</c>,
	/// <c>Structure_CaveGlow_Emission</c>), so a crystal brightens to its tip, a cap's gills outshine its top and a
	/// glow-worm's larva outshines its threads; the material only colours it.
	/// </para>
	/// <para>
	/// <b>Ore</b> is textured in metres instead (<c>Structure_CaveOre_Albedo</c>: crystalline grains), tinted and made
	/// metallic per ore; a glowing ore uses the same grains as its glow map, so its crystals shine unevenly.
	/// </para>
	/// </remarks>
	public static partial class ProceduralArtCatalogue
	{
		/// <summary>Every cave glow mesh, material and prefab starts with this.</summary>
		public const string CavePrefix = "Cave_";

		/// <summary>The sub-folder of the generated prefabs the cave glow prefabs are written to.</summary>
		public const string CavePrefabFolderName = "Cave";

		public const string CavePrefabsFolder = PrefabsFolder + "/" + CavePrefabFolderName;

		/// <summary>The glow ramps and the ore grains: one small tileable set shared by every cave glow material.</summary>
		public const int CaveGlowTextureSize = 256;

		/// <summary>Screen heights at which glow art steps to its next level; the last culls (later than a structure's: a light is seen from afar in the dark).</summary>
		public static readonly float[] CaveGlowLodHeights = { 0.12f, 0.04f, 0.004f };

		/// <summary>The crystals' hues: what cave crystals glow in (quartz-ice, beryl green, amethyst violet, citrine amber).</summary>
		public static readonly CaveGlowHue[] CrystalHues =
		{
			new CaveGlowHue { Name = "IceBlue", Albedo = new Color(0.62f, 0.8f, 0.95f), Emission = new Color(0.32f, 0.68f, 1.6f), Smoothness = 0.88f, Light = new Color(0.45f, 0.75f, 1f), EmitterHue = 1 },
			new CaveGlowHue { Name = "Green", Albedo = new Color(0.55f, 0.88f, 0.62f), Emission = new Color(0.3f, 1.35f, 0.55f), Smoothness = 0.88f, Light = new Color(0.45f, 1f, 0.6f), EmitterHue = 2 },
			new CaveGlowHue { Name = "Violet", Albedo = new Color(0.72f, 0.58f, 0.92f), Emission = new Color(0.95f, 0.45f, 1.6f), Smoothness = 0.88f, Light = new Color(0.68f, 0.45f, 1f), EmitterHue = 0 },
			new CaveGlowHue { Name = "Amber", Albedo = new Color(0.95f, 0.75f, 0.42f), Emission = new Color(1.6f, 0.85f, 0.28f), Smoothness = 0.88f, Light = new Color(1f, 0.7f, 0.35f), EmitterHue = 3 },
		};

		/// <summary>
		/// The fungi's hues: the green of real luminous fungi (Mycena, Panellus, Omphalotus glow 520–530 nm), a fantasy blue,
		/// and a pale gold.
		/// </summary>
		public static readonly CaveGlowHue[] FungusHues =
		{
			new CaveGlowHue { Name = "Teal", Albedo = new Color(0.78f, 0.9f, 0.82f), Emission = new Color(0.32f, 1.25f, 0.82f), Smoothness = 0.4f, Light = new Color(0.4f, 1f, 0.75f), EmitterHue = 2 },
			new CaveGlowHue { Name = "Blue", Albedo = new Color(0.78f, 0.85f, 0.95f), Emission = new Color(0.3f, 0.7f, 1.45f), Smoothness = 0.4f, Light = new Color(0.4f, 0.65f, 1f), EmitterHue = 1 },
			new CaveGlowHue { Name = "Gold", Albedo = new Color(0.95f, 0.88f, 0.7f), Emission = new Color(1.35f, 1.0f, 0.42f), Smoothness = 0.4f, Light = new Color(1f, 0.85f, 0.5f), EmitterHue = 3 },
		};

		/// <summary>Glow-worm light: the larvae's blue-green (Arachnocampa, about 487 nm).</summary>
		public static readonly CaveGlowHue[] GlowWormHues =
		{
			new CaveGlowHue { Name = "Blue", Albedo = new Color(0.8f, 0.88f, 0.9f), Emission = new Color(0.25f, 0.95f, 1.5f), Smoothness = 0.85f, Light = new Color(0.35f, 0.85f, 1f), EmitterHue = 1 },
		};

		/// <summary>
		/// The ores, as their seams show in a wall: copper as malachite green over its native metal, iron as rusty hematite,
		/// silver and gold native in their quartz. They shine under a lamp (metallic) and do not glow.
		/// </summary>
		public static readonly CaveGlowHue[] OreHues =
		{
			new CaveGlowHue { Name = "Copper", Albedo = new Color(0.2f, 0.52f, 0.36f), Smoothness = 0.55f, Metallic = 0.25f, EmitterHue = -1 },
			new CaveGlowHue { Name = "Iron", Albedo = new Color(0.42f, 0.17f, 0.1f), Smoothness = 0.4f, Metallic = 0.35f, EmitterHue = -1 },
			new CaveGlowHue { Name = "Silver", Albedo = new Color(0.78f, 0.79f, 0.8f), Smoothness = 0.7f, Metallic = 0.95f, EmitterHue = -1 },
			new CaveGlowHue { Name = "Gold", Albedo = new Color(1f, 0.78f, 0.34f), Smoothness = 0.75f, Metallic = 1f, EmitterHue = -1 },
		};

		/// <summary>The fantasy ores that glow: starmetal's cold blue-white, emberstone's banked-fire red.</summary>
		public static readonly CaveGlowHue[] GlowOreHues =
		{
			new CaveGlowHue { Name = "Starmetal", Albedo = new Color(0.75f, 0.82f, 0.95f), Emission = new Color(0.55f, 0.75f, 1.7f), Smoothness = 0.75f, Metallic = 0.7f, Light = new Color(0.6f, 0.75f, 1f), EmitterHue = 1 },
			new CaveGlowHue { Name = "Emberstone", Albedo = new Color(0.5f, 0.16f, 0.08f), Emission = new Color(1.7f, 0.5f, 0.14f), Smoothness = 0.5f, Metallic = 0.3f, Light = new Color(1f, 0.5f, 0.22f), EmitterHue = 3 },
		};

		/// <summary>The milky quartz an ore seam runs in (<c>Cave_Gangue</c>, the seams' first submesh).</summary>
		public static readonly CaveGlowHue Gangue = new CaveGlowHue { Name = "Gangue", Albedo = new Color(0.82f, 0.8f, 0.74f), Smoothness = 0.55f, EmitterHue = -1 };

		/// <summary>Every kind of glow art, in <see cref="CaveGlowKind"/> order.</summary>
		public static readonly CaveGlowFamily[] CaveGlowFamilies =
		{
			new CaveGlowFamily { Kind = CaveGlowKind.Crystal, Mount = CaveMount.Floor, Shape = CaveGlowKind.Crystal, Variants = 3, Hues = CrystalHues, MaterialSet = "Crystal",
				Collides = true, CastsShadows = true, Reach = 6f,
				Description = "Glowing crystal points on a lump of host rock: v0 a druse of eleven, v1 five stout points, v2 a spray of slender needles; about 1 m across, 0.6–1.3 m tall." },
			new CaveGlowFamily { Kind = CaveGlowKind.GiantCrystal, Mount = CaveMount.Floor, Shape = CaveGlowKind.GiantCrystal, Variants = 2, Hues = CrystalHues, MaterialSet = "Crystal",
				Collides = true, CastsShadows = true, Reach = 12f,
				Description = "Giant crystal columns: v0 one leaning 4.6 m column with lesser points at its foot, v1 three diverging columns up to 5.2 m." },
			new CaveGlowFamily { Kind = CaveGlowKind.Fungus, Mount = CaveMount.Floor, Shape = CaveGlowKind.Fungus, Variants = 3, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = false, CastsShadows = false, Reach = 3.5f,
				Description = "Bonnet caps on slender stalks (Mycena): v0 a tight tuft 0.1–0.25 m, v1 a spread patch 0.9 m across, v2 a tall clump to 0.5 m." },
			new CaveGlowFamily { Kind = CaveGlowKind.GiantFungus, Mount = CaveMount.Floor, Shape = CaveGlowKind.GiantFungus, Variants = 2, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = true, CastsShadows = true, Reach = 9f,
				Description = "Giant glowcaps with a skirt on the stalk, glowing gills and spots on the cap: v0 one 2.7 m tall, v1 a group of three, 2.2, 1.6 and 1 m." },
			new CaveGlowFamily { Kind = CaveGlowKind.ShelfFungus, Mount = CaveMount.Wall, Shape = CaveGlowKind.ShelfFungus, Variants = 3, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = false, CastsShadows = false, Reach = 3f,
				Description = "Overlapping bracket fungi (Panellus) standing out of a wall, pores glowing underneath: v0 a cluster 0.4 m, v1 a column of tiers 0.8 m tall, v2 a patch 1 m across." },
			new CaveGlowFamily { Kind = CaveGlowKind.GlowWorms, Mount = CaveMount.Roof, Shape = CaveGlowKind.GlowWorms, Variants = 3, Hues = GlowWormHues, MaterialSet = "GlowWorm",
				Collides = false, CastsShadows = false, Reach = 0f,
				Description = "A glow-worm colony hung from a roof: silk threads 0.1–0.6 m beaded with droplets, a glowing larva at the top of each; v0 2 m across, v1 3 m, v2 4 m." },
			new CaveGlowFamily { Kind = CaveGlowKind.OreVein, Mount = CaveMount.Wall, Shape = CaveGlowKind.OreVein, Variants = 4, Hues = OreHues, MaterialSet = "Ore",
				Collides = false, CastsShadows = false, Reach = 0f,
				Description = "An ore seam along x in a band of quartz, standing 2–6 cm out of the wall: v0 1.5 m, v1 2.5 m, v2 4 m, v3 3 m with a branch." },
			new CaveGlowFamily { Kind = CaveGlowKind.GlowOreVein, Mount = CaveMount.Wall, Shape = CaveGlowKind.OreVein, Variants = 4, Hues = GlowOreHues, MaterialSet = "GlowOre",
				Collides = false, CastsShadows = false, Reach = 4f,
				Description = "A seam of glowing ore in quartz, the ore veins' shapes: v0 1.5 m, v1 2.5 m, v2 4 m, v3 3 m with a branch." },
		};

		/// <summary>The family of a kind.</summary>
		public static CaveGlowFamily CaveGlow(CaveGlowKind kind) => CaveGlowFamilies[(int)kind];

		// ── Cave crystals (2026-10-16, I6c) ───────────────────────────

		/// <summary>
		/// Distinct shapes of every cave crystal built in its own habit: as many as a rock formation has
		/// (<see cref="RockTypes.VariantCount"/>), so a chamber's cluster draws its variant as a formation's always did.
		/// </summary>
		public const int CaveMineralVariants = RockTypes.VariantCount;

		/// <summary>How far the light beside a glowing crystal reaches, metres (a cluster's, as the glow art's crystals).</summary>
		public const float CaveMineralReach = 6f;

		/// <summary>
		/// The cave-crystal catalogue in <see cref="BiomeCaveCrystalKind"/> order: selenite and frost crystal are the gypsum and
		/// water-ice formations the rock art already makes; every other kind its own habit and material, built with the cave
		/// glow art (one mesh set a habit, a material and three prefabs a kind). Colours from the approved biome drafts where they
		/// name one (smoky quartz #4a4038–#8a7a68, green fluorite #4aa070–#9ad8a8, amberlight #d88a20 glowing #ffb040 low,
		/// wyrdstone glowing #a8f070 low).
		/// </summary>
		public static readonly CaveMineral[] CaveMinerals =
		{
			new CaveMineral { Kind = BiomeCaveCrystalKind.Selenite, Formation = true, FormationRock = RockTypes.Gypsum.Name, FormationShape = "Crystal", Lit = true, LightHue = -1,
				Description = "Selenite: milky gypsum blades with chisel tips (the gypsum Crystal formation), a cave light of a hue by chance beside it: every cave's crystal before 2026-10-16." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Amethyst, Habit = CaveMineralHabit.Geode, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Amethyst", Albedo = new Color(0.56f, 0.36f, 0.8f), Smoothness = 0.86f, EmitterHue = -1 },
				Description = "Amethyst: a geode broken open, its grey rind round a hollow lined with violet points." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.ClearQuartz, Habit = CaveMineralHabit.Druse, LightHue = -1,
				Hue = new CaveGlowHue { Name = "ClearQuartz", Albedo = new Color(0.86f, 0.9f, 0.93f), Smoothness = 0.93f, EmitterHue = -1 },
				Description = "Clear quartz: a druse of small water-clear points carpeting a lump of rock." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.SmokyQuartz, Habit = CaveMineralHabit.Prisms, LightHue = -1,
				Hue = new CaveGlowHue { Name = "SmokyQuartz", Albedo = new Color(0.47f, 0.4f, 0.34f), Smoothness = 0.9f, EmitterHue = -1 },
				Description = "Smoky quartz: a few stout brown-grey prisms with pyramid tips out of a pegmatite pocket." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.GreenFluorite, Habit = CaveMineralHabit.Cubes, LightHue = -1,
				Hue = new CaveGlowHue { Name = "GreenFluorite", Albedo = new Color(0.42f, 0.74f, 0.54f), Smoothness = 0.82f, EmitterHue = -1 },
				Description = "Green fluorite: interlocking translucent green cubes, some stood on an edge or a corner." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.FrostCrystal, Formation = true, FormationRock = RockTypes.WaterIce.Name, FormationShape = "Crystal", Lit = true, LightHue = 1,
				Description = "Frost crystal: six-sided columns of clear ice (the water-ice Crystal formation, an ice cave's own), a blue cave light beside it." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Obsidian, Habit = CaveMineralHabit.Shards, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Obsidian", Albedo = new Color(0.07f, 0.065f, 0.08f), Smoothness = 0.96f, EmitterHue = -1 },
				Description = "Obsidian shards: glassy black blades and splinters jutting every way out of the rock." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Amberlight, Habit = CaveMineralHabit.Nodules, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Amberlight", Albedo = new Color(0.85f, 0.54f, 0.13f), Emission = new Color(0.95f, 0.6f, 0.2f), Smoothness = 0.8f, Light = new Color(1f, 0.7f, 0.35f), EmitterHue = 3 },
				Description = "Amberlight (fantasy): rounded nodules and drops of resin-amber sunk in a rock bed, glowing a low warm gold." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Wyrdstone, Habit = CaveMineralHabit.Geode, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Wyrdstone", Albedo = new Color(0.6f, 0.74f, 0.56f), Emission = new Color(0.62f, 1.05f, 0.44f), Smoothness = 0.84f, Light = new Color(0.66f, 0.94f, 0.44f), EmitterHue = 2 },
				Description = "Wyrdstone (fantasy): a mossy geode whose green-violet points glow from within." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Sunstone, Habit = CaveMineralHabit.Blades, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Sunstone", Albedo = new Color(0.95f, 0.56f, 0.24f), Emission = new Color(1.6f, 0.62f, 0.16f), Smoothness = 0.78f, Light = new Color(1f, 0.6f, 0.28f), EmitterHue = 3 },
				Description = "Sunstone (fantasy, volcanic): broad tabular blades of orange-gold crystal alight like embers, bursting from scoria." },
		};

		/// <summary>The catalogue's entry for a kind (a kind past the table is selenite's).</summary>
		public static CaveMineral CaveMineralOf(BiomeCaveCrystalKind kind) => (int)kind < CaveMinerals.Length ? CaveMinerals[(int)kind] : CaveMinerals[0];

		/// <summary>The habits the catalogue's own-built kinds use, each once, in habit order.</summary>
		public static IEnumerable<CaveMineralHabit> CaveMineralHabits()
		{
			var used = new HashSet<CaveMineralHabit>();
			foreach (CaveMineral m in CaveMinerals)
			{
				if (!m.Formation)
				{
					used.Add(m.Habit);
				}
			}
			foreach (CaveMineralHabit habit in (CaveMineralHabit[])System.Enum.GetValues(typeof(CaveMineralHabit)))
			{
				if (used.Contains(habit))
				{
					yield return habit;
				}
			}
		}

		/// <summary>A cave crystal's level mesh: <c>Cave_MineralGeode_0_LOD0</c> (shared by every kind of the habit).</summary>
		public static string CaveMineralMesh(CaveMineralHabit habit, int variant, int lod) => $"{CavePrefix}Mineral{habit}_{variant}_LOD{lod}";

		/// <summary>A cave crystal's prefab under the generated prefabs folder, with its sub-folder: <c>Cave/Cave_Mineral_Amethyst_1</c>.</summary>
		public static string CaveMineralPrefab(BiomeCaveCrystalKind kind, int variant) => $"{CavePrefabFolderName}/{CavePrefix}Mineral_{kind}_{variant}";

		/// <summary>A cave crystal's material: <c>Cave_Mineral_Amethyst</c>.</summary>
		public static string CaveMineralMaterial(BiomeCaveCrystalKind kind) => $"{CavePrefix}Mineral_{kind}";

		// ── Names ─────────────────────────────────────────────────────

		/// <summary>A cave glow texture: <c>Structure_CaveGlow_Albedo</c>, <c>Structure_CaveGlow_Emission</c>, <c>Structure_CaveOre_Albedo</c>.</summary>
		public static string CaveGlowTexture(string surface, string map) => StructureTexture(surface, map);

		public static string CaveGlowAlbedoPath => CaveGlowTexture("CaveGlow", "Albedo");
		public static string CaveGlowEmissionPath => CaveGlowTexture("CaveGlow", "Emission");
		public static string CaveOreAlbedoPath => CaveGlowTexture("CaveOre", "Albedo");

		/// <summary>A level's payload mesh: <c>Cave_Crystal_0_LOD0</c> (shared by every hue).</summary>
		public static string CaveGlowMesh(CaveGlowKind shape, int variant, int lod) => $"{CavePrefix}{shape}_{variant}_LOD{lod}";

		/// <summary>A prefab's name under the generated prefabs folder, with its sub-folder: <c>Cave/Cave_Crystal_Violet_2</c>.</summary>
		public static string CaveGlowPrefab(CaveGlowKind kind, string hue, int variant) => $"{CavePrefabFolderName}/{CavePrefix}{kind}_{hue}_{variant}";

		/// <summary>A hue's material: <c>Cave_Crystal_Violet</c>, <c>Cave_Ore_Gold</c>.</summary>
		public static string CaveGlowMaterial(string set, string hue) => $"{CavePrefix}{set}_{hue}";

		/// <summary>The quartz an ore seam runs in.</summary>
		public static string CaveGangueMaterial => CavePrefix + Gangue.Name;

		/// <summary>Whether a kind's meshes are its own (a glowing ore vein wears the ore veins').</summary>
		public static bool OwnsShape(in CaveGlowFamily family) => family.Shape == family.Kind;

		/// <summary>Every cave glow payload file: the three maps, and three meshes a variant of each shape.</summary>
		public static IEnumerable<string> CaveGlowPayloadPaths()
		{
			yield return CaveGlowAlbedoPath;
			yield return CaveGlowEmissionPath;
			yield return CaveOreAlbedoPath;
			foreach (CaveGlowFamily f in CaveGlowFamilies)
			{
				if (!OwnsShape(in f))
				{
					continue;
				}
				for (int v = 0; v < f.Variants; v++)
				{
					for (int lod = 0; lod < CaveGlowMeshes.Levels; lod++)
					{
						yield return MeshPath(CaveGlowMesh(f.Shape, v, lod));
					}
				}
			}
			foreach (CaveMineralHabit habit in CaveMineralHabits())
			{
				for (int v = 0; v < CaveMineralVariants; v++)
				{
					for (int lod = 0; lod < CaveGlowMeshes.Levels; lod++)
					{
						yield return MeshPath(CaveMineralMesh(habit, v, lod));
					}
				}
			}
		}

		/// <summary>Every cave glow material: one a hue of each material set, and the gangue.</summary>
		public static IEnumerable<string> CaveGlowWrapperPaths()
		{
			var seen = new HashSet<string>();
			foreach (CaveGlowFamily f in CaveGlowFamilies)
			{
				foreach (CaveGlowHue hue in f.Hues)
				{
					string name = CaveGlowMaterial(f.MaterialSet, hue.Name);
					if (seen.Add(name))
					{
						yield return MaterialPath(name);
					}
				}
			}
			yield return MaterialPath(CaveGangueMaterial);
			foreach (CaveMineral m in CaveMinerals)
			{
				if (!m.Formation)
				{
					yield return MaterialPath(CaveMineralMaterial(m.Kind));
				}
			}
		}

		/// <summary>Every cave glow prefab name (with its sub-folder): each variant of each kind in each of its hues.</summary>
		public static IEnumerable<string> CaveGlowPrefabNames()
		{
			foreach (CaveGlowFamily f in CaveGlowFamilies)
			{
				foreach (CaveGlowHue hue in f.Hues)
				{
					for (int v = 0; v < f.Variants; v++)
					{
						yield return CaveGlowPrefab(f.Kind, hue.Name, v);
					}
				}
			}
			foreach (CaveMineral m in CaveMinerals)
			{
				if (!m.Formation)
				{
					for (int v = 0; v < CaveMineralVariants; v++)
					{
						yield return CaveMineralPrefab(m.Kind, v);
					}
				}
			}
		}
	}
}
#endif
