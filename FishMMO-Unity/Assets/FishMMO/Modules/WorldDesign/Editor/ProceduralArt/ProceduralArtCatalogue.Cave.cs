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
		/// <summary>A towering glowcap, a mushroom tree of the deep, 5–15 m as the dressing scales it (2026-10-20).</summary>
		TowerFungus,
		/// <summary>A glowcap of the middle size, 3–5 m (2026-10-20).</summary>
		MidFungus,
		/// <summary>A cluster of glowing puffballs or morels on a floor, 0.3–1 m (2026-10-20).</summary>
		Puffballs,
		/// <summary>Tall tiers of great bracket fungi on a wall, 1.6–2.8 m (2026-10-20).</summary>
		ShelfTiers,
		/// <summary>A curtain of glowing roots hung from a roof (fantasy, 2026-10-20).</summary>
		GlowRoots,
		/// <summary>Tree roots hanging through a thin roof near a mouth (2026-10-20); they do not glow.</summary>
		HangingRoots,
		/// <summary>A glowing creeper on a wet wall (fantasy, 2026-10-20).</summary>
		GlowVine,
		/// <summary>A fissure in a crust of cooling lava, glowing molten in its depths (2026-10-20, cave lava): on a floor, or on a wall turned out of it.</summary>
		MagmaCrack,
		/// <summary>Magma seeping out of a wall in glowing runnels down to its foot (2026-10-20, cave lava).</summary>
		MagmaSeep,
		/// <summary>Lava stalactites (lavacicles) hung from a roof over molten lava, their dripping tips still glowing (2026-10-20, cave lava).</summary>
		Lavacicle,
		/// <summary>A low skin of ropy pahoehoe crust cooled black at molten lava's edge, its toes still glowing (2026-10-20, cave lava).</summary>
		LavaCrust,
		/// <summary>Glow-worm silk hung along a damp roof joint, long beaded threads lit by their larvae (2026-10-20, caves livened).</summary>
		GlowSilk,
		/// <summary>Tree roots come down through a thin roof all the way to the floor and spreading over it (2026-10-20, caves livened); they do not glow.</summary>
		TapRoots,
		/// <summary>A heap of bat guano on a floor under a roost (2026-10-20, caves livened); it does not glow.</summary>
		GuanoHeap,
		/// <summary>A frozen waterfall down a wall or a drop: fused, bulging columns of ice and a cone at the foot (2026-10-20, caves livened: ice).</summary>
		IceFall,
		/// <summary>A fringe of rippled icicles along a roof's edge (2026-10-20, caves livened: ice).</summary>
		IcicleCurtain,
		/// <summary>A frozen pool: a dead-level sheet of glassy ice on a floor, walked on (2026-10-20, caves livened: ice).</summary>
		FrozenPool,
		/// <summary>Hoarfrost: feathery hoar crystals on a wall near the ice (2026-10-20, caves livened: ice).</summary>
		Hoarfrost,
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
		// 2026-10-26 (NEW-CRYSTALS, decision 34): the habits the biome drafts name, each its own builder in CaveGlowMeshes.Habits.cs.
		/// <summary>Grape clusters: bunches of overlapping domes crusting a lump of rock (malachite, smithsonite, hyalite).</summary>
		Botryoidal,
		/// <summary>Twins: pairs of flattened prisms joined in a V (Japan-law quartz), and v3 crossed through each other (staurolite).</summary>
		Twinned,
		/// <summary>Helictites: curling wire-thin filaments sprouting every way, against gravity.</summary>
		Helix,
		/// <summary>Massive: squat irregular many-sided columns with low tops fused in a lump (chalcopyrite).</summary>
		Massive,
		/// <summary>Coralloid: knobbly branching sprays (flos-ferri aragonite).</summary>
		Coralloid,
		/// <summary>Foliated: books of thin hexagonal plates stacked and fanned (orpiment, specular hematite).</summary>
		Foliated,
		/// <summary>Fibrous: sheaves of near-parallel needles fanning from a waist (satin spar, stilbite's bow-ties).</summary>
		Fibrous,
		/// <summary>Rosettes: thin rounded petals set about a centre every way, as a desert rose's.</summary>
		Rosette,
		/// <summary>Octahedra on a lump of rock, some on a face, some on a point (spinel, alum, pink fluorite).</summary>
		Octahedral,
		/// <summary>Bipyramids: double-ended pointed crystals with a short waist (zircon, native sulphur), v3 long four-sided spindles.</summary>
		Bipyramid,
		/// <summary>Dendrites: fern-branched six-armed plates fanned off the rock (sublimation hoar), v3 single feathered fronds.</summary>
		Dendrite,
		/// <summary>Pearls: polished spheres nested in a shallow rimstone cup (cave pearls).</summary>
		Pearls,
		/// <summary>Floss: soft hair-thin tufts and curls over a crust (cave cotton, thenardite).</summary>
		Floss,
		/// <summary>Cups: hollow hexagonal cups, open ends up and out (depth hoar), v3 in nested chains.</summary>
		Cupped,
		/// <summary>Barrels: hexagonal crystals swelling at the middle and tapering to flat ends (sapphire).</summary>
		Barrel,
		/// <summary>Stars: rays radiating every way from a centre (sixling stars, ikaite), v3 flat seven-rayed stars.</summary>
		Stellate,
		/// <summary>Droplets: rounded teardrops with pointed tails, standing and lying (tektite splash-forms, tear-glass).</summary>
		Droplet,
		/// <summary>Rhombohedra: skewed six-faced crystals (Iceland spar, cinnabar, siderite).</summary>
		Rhomb,
		/// <summary>Tabular: thin flat plates stood on edge and intergrown (barite, ilmenite, hydrohalite).</summary>
		Tabular,
		/// <summary>Radiating: sprays of fine needles from a point (wollastonite, phillipsite), v2 a flat fan.</summary>
		Radiating,
		/// <summary>Leaves: thin curved translucent leaf-shaped plates fanned from a root.</summary>
		Leaf,
		/// <summary>Rhombic dodecahedra half sunk in rock (garnet, hauyne).</summary>
		Dodecahedral,
		/// <summary>Trapezohedra: twenty-four-faced near-round crystals (leucite, analcime).</summary>
		Trapezohedral,
		/// <summary>Trapiche: hexagonal prisms whose sections show six dark spokes round a dark core (trapiche emerald).</summary>
		Trapiche,
		/// <summary>Matrix: nodules sunk in a big host lump and veins crossing it (turquoise, lapis), v3 a seam through it.</summary>
		Matrix,
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

	/// <summary>
	/// A biome's own look of a cave crystal kind other biomes grow too (2026-10-26, NEW-CRYSTALS; decisions 12, 97): one kind,
	/// each biome its own habit and colour, chosen by <see cref="BiomeCaveCrystal.Look"/> — not a second kind. Its light, glow
	/// and flicker are the kind's; it is always built in its own habit (a formation kind's look too), its own material and prefabs.
	/// </summary>
	public struct CaveMineralLook
	{
		public BiomeCaveCrystalKind Kind;
		/// <summary>Its number among the kind's looks (<see cref="BiomeCaveCrystal.Look"/>), 1 on; append-only within a kind.</summary>
		public byte Look;
		/// <summary>Its name in generated paths: <c>Cave_Mineral_Obsidian_Rossa_1</c>.</summary>
		public string Name;
		/// <summary>The biome (its asset name, "Permanent Ice") whose spec takes this look when it names the kind (BiomeArtSpec.Entry.CaveCrystals); null: chosen by hand only.</summary>
		public string Biome;
		public CaveMineralHabit Habit;
		public Color Albedo;
		public float Smoothness, Metallic;
		public string Description;
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

		/// <summary>
		/// Glow-worm light: the larvae's blue-green (Arachnocampa, about 487 nm); and (2026-10-26, I2n-c, decision 86 (e)) Rootlight, the
		/// deeper electric blue of the dismal-swamp fungus-gnat larvae (Orfelia) threading the roots of drowned galleries. Appended
		/// only: a hue's index is what the dressing draws.
		/// </summary>
		public static readonly CaveGlowHue[] GlowWormHues =
		{
			new CaveGlowHue { Name = "Blue", Albedo = new Color(0.8f, 0.88f, 0.9f), Emission = new Color(0.25f, 0.95f, 1.5f), Smoothness = 0.85f, Light = new Color(0.35f, 0.85f, 1f), EmitterHue = 1 },
			new CaveGlowHue { Name = "Rootlight", Albedo = new Color(0.74f, 0.8f, 0.95f), Emission = new Color(0.1f, 0.38f, 1.65f), Smoothness = 0.85f, Light = new Color(0.22f, 0.42f, 1f), EmitterHue = 1 },
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

		/// <summary>
		/// Tree roots as they hang through a cave's thin roof (2026-10-20): the dark earth-brown of living roots and the pale
		/// buff of roots that have hung long in still air; and (2026-10-26, I2n-c, decision 86 (d)) WaterRoots, the wet dark red-brown
		/// of alder and willow roots that hang into a drowned gallery's water (#3a2a1e, reddened by their root-hair tassels). They do
		/// not glow. Appended only: a hue's index is what the dressing draws.
		/// </summary>
		public static readonly CaveGlowHue[] RootHues =
		{
			new CaveGlowHue { Name = "Earth", Albedo = new Color(0.4f, 0.29f, 0.2f), Smoothness = 0.22f, EmitterHue = -1 },
			new CaveGlowHue { Name = "Pale", Albedo = new Color(0.66f, 0.58f, 0.46f), Smoothness = 0.2f, EmitterHue = -1 },
			new CaveGlowHue { Name = "WaterRoots", Albedo = new Color(0.3f, 0.17f, 0.12f), Smoothness = 0.45f, EmitterHue = -1 },
		};

		/// <summary>
		/// Molten rock as cave lava shows it (2026-10-20): a glassy black basalt crust over a glow that is its own blackbody light —
		/// Molten a fresh yellow-orange (~1150 °C, its brightest clipped to yellow as hot lava looks), Banked a deep red-orange
		/// (~1000 °C, cooling). Their light is amber (hue 3), as the placer's lava lights are (SceneMagma).
		/// </summary>
		public static readonly CaveGlowHue[] MagmaHues =
		{
			new CaveGlowHue { Name = "Molten", Albedo = new Color(0.085f, 0.07f, 0.065f), Emission = new Color(2.1f, 0.78f, 0.16f), Smoothness = 0.62f, Light = new Color(1f, 0.54f, 0.2f), EmitterHue = 3 },
			new CaveGlowHue { Name = "Banked", Albedo = new Color(0.075f, 0.065f, 0.06f), Emission = new Color(1.5f, 0.38f, 0.07f), Smoothness = 0.55f, Light = new Color(1f, 0.42f, 0.14f), EmitterHue = 3 },
		};

		/// <summary>
		/// Bat guano as it piles under a roost (2026-10-20, caves livened): the dark moist brown of a heap still fed, and the pale
		/// buff crust of one long dry. It does not glow.
		/// </summary>
		public static readonly CaveGlowHue[] GuanoHues =
		{
			new CaveGlowHue { Name = "Fresh", Albedo = new Color(0.4f, 0.31f, 0.22f), Smoothness = 0.35f, EmitterHue = -1 },
			new CaveGlowHue { Name = "Dry", Albedo = new Color(0.82f, 0.74f, 0.6f), Smoothness = 0.12f, EmitterHue = -1 },
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
			// 2026-10-20 (cave realism: "Largest mushrooms should get up to 15m tall", "More glow is good inside the caves", "More
			// vines and stuff inside caves"). Appended, so no older kind's variants or hues move.
			new CaveGlowFamily { Kind = CaveGlowKind.TowerFungus, Mount = CaveMount.Floor, Shape = CaveGlowKind.TowerFungus, Variants = 4, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = true, CastsShadows = true, Reach = 14f,
				Description = "Mushroom trees: fibrous stalks on buttress-rooted feet, brackets up them, gilled glowing caps, veils of glowing strands: v0 one 12 m dome, v1 a stand of 11, 7.5 and 5 m, v2 a leaning 10 m parasol, v3 a 14 m bell and a 4.5 m one. Scaled 5–15 m." },
			new CaveGlowFamily { Kind = CaveGlowKind.MidFungus, Mount = CaveMount.Floor, Shape = CaveGlowKind.MidFungus, Variants = 3, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = true, CastsShadows = true, Reach = 9f,
				Description = "Glowcaps of 3–5 m, made as the mushroom trees: v0 a 4.4 m dome, v1 two parasols (4.2, 3 m), v2 three bells (4, 3.3, 2.6 m)." },
			new CaveGlowFamily { Kind = CaveGlowKind.Puffballs, Mount = CaveMount.Floor, Shape = CaveGlowKind.Puffballs, Variants = 3, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = false, CastsShadows = false, Reach = 4f,
				Description = "v0 seven puffballs to 0.6 m tall with glowing warts, v1 six morels with glowing pits, v2 a burst giant puffball, its spores drifting glowing over it." },
			new CaveGlowFamily { Kind = CaveGlowKind.ShelfTiers, Mount = CaveMount.Wall, Shape = CaveGlowKind.ShelfTiers, Variants = 3, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = false, CastsShadows = false, Reach = 5f,
				Description = "Tiers of great brackets on a wall, pores glowing under them: v0 a column 1.8 m tall, v1 a staircase climbing 2.8 m, v2 a fan 2.4 by 1.6 m." },
			new CaveGlowFamily { Kind = CaveGlowKind.GlowRoots, Mount = CaveMount.Roof, Shape = CaveGlowKind.GlowRoots, Variants = 3, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = false, CastsShadows = false, Reach = 5f,
				Description = "Glowroots (fantasy) hung from a roof, brightening down to a glowing bulb: v0 2 m across to 2.2 m long, v1 3 m across to 3.5 m, v2 a fringe 2.6 m along to 1.4 m." },
			new CaveGlowFamily { Kind = CaveGlowKind.HangingRoots, Mount = CaveMount.Roof, Shape = CaveGlowKind.HangingRoots, Variants = 3, Hues = RootHues, MaterialSet = "Root",
				Collides = false, CastsShadows = false, Reach = 0f,
				Description = "Tree roots hanging through a thin roof: v0 a few thick roots to 1.6 m, v1 a hairy curtain 2.5 m across to 2.4 m, v2 a root knot and its rootlets. Not lit." },
			new CaveGlowFamily { Kind = CaveGlowKind.GlowVine, Mount = CaveMount.Wall, Shape = CaveGlowKind.GlowVine, Variants = 3, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = false, CastsShadows = false, Reach = 4f,
				Description = "A glowing creeper (fantasy) on a wet wall: v0 a patch 1.5 m, v1 a sheet 2.5 by 2 m, v2 a cascade hanging 2.4 m from its top." },
			// 2026-10-20 (cave lava: "glowing floor/wall cracks, magma seeping from walls, glowing lavacicles"). Appended, so no older
			// kind's variants or hues move. Lit by the placer's lava lights (SceneEmitterKind.Magma), not by a light of their own.
			new CaveGlowFamily { Kind = CaveGlowKind.MagmaCrack, Mount = CaveMount.Floor, Shape = CaveGlowKind.MagmaCrack, Variants = 4, Hues = MagmaHues, MaterialSet = "Magma",
				Collides = false, CastsShadows = false, Reach = 4f,
				Description = "A fissure in black crust, its raised lips parting on a glowing molten floor, flat on the rock (4 cm proud): v0 1.6 m with a short branch, v1 a fork 2.4 m, v2 a star of three 1.2 m, v3 a long crack 3.2 m. Along x." },
			new CaveGlowFamily { Kind = CaveGlowKind.MagmaSeep, Mount = CaveMount.Wall, Shape = CaveGlowKind.MagmaSeep, Variants = 3, Hues = MagmaHues, MaterialSet = "Magma",
				Collides = false, CastsShadows = false, Reach = 4f,
				Description = "Magma seeping from a crack in a wall in glowing runnels down to its foot, spreading in a small lobe there: v0 one runnel 1.2 m, v1 one 2.2 m splitting in two, v2 three from a crack 1.6 m across, 1.6 m tall." },
			new CaveGlowFamily { Kind = CaveGlowKind.Lavacicle, Mount = CaveMount.Roof, Shape = CaveGlowKind.Lavacicle, Variants = 3, Hues = MagmaHues, MaterialSet = "Magma",
				Collides = false, CastsShadows = false, Reach = 4f,
				Description = "Lava stalactites on a crusted roof patch, dark where they leave the rock and glowing to their dripping tips: v0 seven to 0.6 m, v1 a fringe of twelve along 1.6 m to 0.9 m, v2 five stout shark's teeth to 0.4 m." },
			new CaveGlowFamily { Kind = CaveGlowKind.LavaCrust, Mount = CaveMount.Floor, Shape = CaveGlowKind.LavaCrust, Variants = 3, Hues = MagmaHues, MaterialSet = "Magma",
				Collides = false, CastsShadows = false, Reach = 4f,
				Description = "A low lobe of ropy pahoehoe crust, its rope folds arcing toward its front, its toes still glowing in their creases (≤ 12 cm tall): v0 1.6 by 1 m, v1 a broad sheet 2.4 by 1.6 m, v2 a long tongue 3 by 0.9 m. Flowing along +x." },
			// 2026-10-20 (caves livened: what hangs from the roof). Appended, so no older kind's variants or hues move. Silk in the
			// fungi's hues, so it glows as the cave's cover threads do (CaveGlowPalette); the roots wear the hanging roots' material.
			new CaveGlowFamily { Kind = CaveGlowKind.GlowSilk, Mount = CaveMount.Roof, Shape = CaveGlowKind.GlowSilk, Variants = 3, Hues = FungusHues, MaterialSet = "Fungus",
				Collides = false, CastsShadows = false, Reach = 0f,
				Description = "Glow-worm silk along a damp roof joint: long threads 0.25–1.6 m beaded with glowing droplets, a larva at each top; v0 a fringe along 2.4 m, v1 a band along 3.6 m, v2 a cluster 1.2 m across under a drip point. Along x." },
			new CaveGlowFamily { Kind = CaveGlowKind.TapRoots, Mount = CaveMount.Roof, Shape = CaveGlowKind.TapRoots, Variants = 3, Hues = RootHues, MaterialSet = "Root",
				Collides = false, CastsShadows = false, Reach = 0f,
				Description = "Tree roots hanging 4 m from a thin roof to the floor (scaled in y to it), spreading in rootlets over it: v0 three thick forking roots, v1 a curtain of twelve thin ones (half reach the floor), v2 a column of five twisted together. Not lit." },
			new CaveGlowFamily { Kind = CaveGlowKind.GuanoHeap, Mount = CaveMount.Floor, Shape = CaveGlowKind.GuanoHeap, Variants = 3, Hues = GuanoHues, MaterialSet = "Guano",
				Collides = false, CastsShadows = false, Reach = 0f,
				Description = "Bat guano under a roost, dark on top and pale crust at its foot: v0 a peaked heap 0.7 m round, 0.3 m high; v1 a low spread 1.3 m round with four cones; v2 an old terraced heap 1 m round, 0.4 m high. Not lit." },
			// 2026-10-20 (caves livened: an ice cave's ice, CaveGlowMeshes.Ice.cs). Appended, so no older kind's variants or hues move.
			// The "Ice" set's glassy, faintly glowing materials; a blue light only where the placer gives a tall fall one.
			new CaveGlowFamily { Kind = CaveGlowKind.IceFall, Mount = CaveMount.Wall, Shape = CaveGlowKind.IceFall, Variants = 3, Hues = IceHues, MaterialSet = "Ice",
				Collides = true, CastsShadows = true, Reach = 9f,
				Description = "A frozen waterfall 4 m tall (scaled in y to its face): fused columns bulging every 0.6-0.9 m, standing further out toward a cone of ice at the foot, a rolled lip at the top: v0 four stout columns 1.4 m wide, v1 a curtain of nine 3 m wide (a third end short in points), v2 two streams meeting in one great cone." },
			new CaveGlowFamily { Kind = CaveGlowKind.IcicleCurtain, Mount = CaveMount.Roof, Shape = CaveGlowKind.IcicleCurtain, Variants = 3, Hues = IceHues, MaterialSet = "Ice",
				Collides = false, CastsShadows = false, Reach = 0f,
				Description = "Rippled icicles hung from a ridge of ice along a roof's edge, longest toward the middle, a drop at each tip: v0 fourteen along 2 m to 1 m, v1 twenty-two along 2.4 m to 1.6 m with a frozen drapery between them, v2 six great ones along 1.4 m to 2.2 m. Along x." },
			new CaveGlowFamily { Kind = CaveGlowKind.FrozenPool, Mount = CaveMount.Floor, Shape = CaveGlowKind.FrozenPool, Variants = 3, Hues = IceHues, MaterialSet = "Ice",
				Collides = true, CastsShadows = false, Reach = 0f,
				Description = "A frozen pool, its surface at the origin and widest 1 m (scaled in x and z to the pool, never in y): clear dark ice with pale rings of trapped bubbles, frosted at the rim, bevelled 0.1 m down and skirted 0.6 m into the rock: v0 lobed round, v1 long (0.6 as wide), v2 two pools frozen into one." },
			new CaveGlowFamily { Kind = CaveGlowKind.Hoarfrost, Mount = CaveMount.Wall, Shape = CaveGlowKind.Hoarfrost, Variants = 3, Hues = RimeHues, MaterialSet = "Ice",
				Collides = false, CastsShadows = false, Reach = 0f,
				Description = "Hoarfrost: feathery six-sided hoar plates 2-8 cm out of a wall, thickest in the middle: v0 a patch 1 by 0.8 m, v1 a streak 0.7 by 1.5 m, v2 a band 1.9 by 0.7 m." },
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
			// 2026-10-26 (NEW-CRYSTALS, decision 34): every kind the biome drafts name, colours from the drafts, in kind order. The
			// fantasy kinds glow (a cold glow but where a draft says warm; the warm tint itself is the biome's glow mode, I6i);
			// Flickers where the draft says flicker, twinkle, sparkle or dart. Sulphur needles are the sulphur Crystals formation.
			new CaveMineral { Kind = BiomeCaveCrystalKind.Phillipsite, Habit = CaveMineralHabit.Radiating, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Phillipsite", Albedo = new Color(0.941f, 0.925f, 0.894f), Smoothness = 0.86f, EmitterHue = -1 },
				Description = "Phillipsite (AbyssalPlain): colourless-white zeolite prism sprays and cruciform twins on red clay." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Lodeheart, Habit = CaveMineralHabit.Geode, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Lodeheart", Albedo = new Color(0.173f, 0.165f, 0.18f), Smoothness = 0.8f, Emission = new Color(0.55f, 1.082f, 1.01f), Light = new Color(0.478f, 0.941f, 0.878f), EmitterHue = 1 },
				Description = "Lodeheart (fantasy, AbyssalPlain): a split manganese nodule, concentric black rings round a pale aquamarine core that glows." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.FireIce, Habit = CaveMineralHabit.Botryoidal, LightHue = -1,
				Hue = new CaveGlowHue { Name = "FireIce", Albedo = new Color(0.925f, 0.894f, 0.769f), Smoothness = 0.55f, EmitterHue = -1 },
				Description = "FireIce (Abyss): methane hydrate: translucent yellow-white waxy cauliflower masses." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Hadalite, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 0,
				Hue = new CaveGlowHue { Name = "Hadalite", Albedo = new Color(0.102f, 0.078f, 0.188f), Smoothness = 0.9f, Emission = new Color(0.478f, 0.406f, 1.15f), Light = new Color(0.416f, 0.353f, 1f), EmitterHue = 0 },
				Description = "Hadalite (fantasy, Abyss): violet-black prisms with a slow cold heartbeat glow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Gwindel, Habit = CaveMineralHabit.Prisms, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Gwindel", Albedo = new Color(0.91f, 0.933f, 0.941f), Smoothness = 0.93f, EmitterHue = -1 },
				Description = "Gwindel (Alpine): water-clear twisted rock-crystal prism fans with smoky cores." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.PinkFluorite, Habit = CaveMineralHabit.Octahedral, LightHue = -1,
				Hue = new CaveGlowHue { Name = "PinkFluorite", Albedo = new Color(0.91f, 0.627f, 0.722f), Smoothness = 0.82f, EmitterHue = -1 },
				Description = "PinkFluorite (Alpine): rose octahedra on milky quartz." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Starfrost, Habit = CaveMineralHabit.Stellate, Lit = true, LightHue = 0, Flickers = true,
				Hue = new CaveGlowHue { Name = "Starfrost", Albedo = new Color(0.925f, 0.918f, 1f), Smoothness = 0.9f, Emission = new Color(0.902f, 0.884f, 1.15f), Light = new Color(0.784f, 0.769f, 1f), EmitterHue = 0 },
				Description = "Starfrost (fantasy, Alpine): frost-white six-rayed star clusters that twinkle cold silver-violet." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Stibnite, Habit = CaveMineralHabit.Radiating, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Stibnite", Albedo = new Color(0.416f, 0.431f, 0.455f), Smoothness = 0.85f, Metallic = 0.8f, EmitterHue = -1 },
				Description = "Stibnite (AlpineMeadow): steel-grey metallic needle sprays with a blue tarnish." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Rhodochrosite, Habit = CaveMineralHabit.Rhomb, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Rhodochrosite", Albedo = new Color(0.753f, 0.188f, 0.353f), Smoothness = 0.8f, EmitterHue = -1 },
				Description = "Rhodochrosite (AlpineMeadow): raspberry-red rhombs banded with white on quartz." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Stormglass, Habit = CaveMineralHabit.Pearls, Lit = true, LightHue = 1, Flickers = true,
				Hue = new CaveGlowHue { Name = "Stormglass", Albedo = new Color(0.541f, 0.541f, 0.659f), Smoothness = 0.88f, Emission = new Color(0.902f, 0.992f, 1.15f), Light = new Color(0.784f, 0.863f, 1f), EmitterHue = 1 },
				Description = "Stormglass (fantasy, AlpineMeadow): smoky violet-grey spheres full of cloud, threads of lightning flickering blue-white in them." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.FireOpal, Habit = CaveMineralHabit.Nodules, LightHue = -1,
				Hue = new CaveGlowHue { Name = "FireOpal", Albedo = new Color(0.878f, 0.353f, 0.102f), Smoothness = 0.9f, EmitterHue = -1 },
				Description = "FireOpal (Badlands): orange-red jelly nodules in vesicles, flashing a play of colour." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Perilight, Habit = CaveMineralHabit.Botryoidal, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Perilight", Albedo = new Color(0.91f, 0.941f, 0.878f), Smoothness = 0.86f, Emission = new Color(0.866f, 1.082f, 0.577f), Light = new Color(0.753f, 0.941f, 0.502f), EmitterHue = 2 },
				Description = "Perilight (fantasy, Badlands): water-clear botryoidal hyalite crusts glowing cold green-gold." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Twinquartz, Habit = CaveMineralHabit.Twinned, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Twinquartz", Albedo = new Color(0.933f, 0.949f, 0.949f), Smoothness = 0.93f, EmitterHue = -1 },
				Description = "Twinquartz (BambooForest): Japan-law twins: flat heart-shaped V-paired quartz plates, water-clear." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Jadeite, Habit = CaveMineralHabit.Nodules, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Jadeite", Albedo = new Color(0.541f, 0.753f, 0.541f), Smoothness = 0.7f, EmitterHue = -1 },
				Description = "Jadeite (BambooForest): white to apple-green waxy jade nodules." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Hotarubi, Habit = CaveMineralHabit.Druse, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Hotarubi", Albedo = new Color(0.902f, 0.941f, 0.847f), Smoothness = 0.88f, Emission = new Color(0.938f, 1.082f, 0.433f), Light = new Color(0.816f, 0.941f, 0.376f), EmitterHue = 2 },
				Description = "Hotarubi (fantasy, BambooForest): firefly-glass: a clear druse full of drifting yellow-green pinpoints." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Aragonite, Habit = CaveMineralHabit.Radiating, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Aragonite", Albedo = new Color(0.949f, 0.925f, 0.878f), Smoothness = 0.7f, EmitterHue = -1 },
				Description = "Aragonite (Beach, Farmland): sea-cave needle sprays, cream-white and satin (Farmland's own look: flos-ferri coralloid sprays)." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Pearlspar, Habit = CaveMineralHabit.Tabular, Lit = true, LightHue = 0,
				Hue = new CaveGlowHue { Name = "Pearlspar", Albedo = new Color(0.941f, 0.91f, 0.941f), Smoothness = 0.92f, Emission = new Color(1.046f, 0.974f, 1.15f), Light = new Color(0.91f, 0.847f, 1f), EmitterHue = 0 },
				Description = "Pearlspar (fantasy, Beach): nacreous iridescent plates with a pastel shimmer." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Galena, Habit = CaveMineralHabit.Cubes, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Galena", Albedo = new Color(0.416f, 0.431f, 0.455f), Smoothness = 0.85f, Metallic = 0.9f, EmitterHue = -1 },
				Description = "Galena (Castle): lead-silver metallic cubes on white calcite." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Chalcopyrite, Habit = CaveMineralHabit.Massive, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Chalcopyrite", Albedo = new Color(0.722f, 0.604f, 0.251f), Smoothness = 0.8f, Metallic = 0.85f, EmitterHue = -1 },
				Description = "Chalcopyrite (Castle): brassy masses with a peacock tarnish." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Wyrmglass, Habit = CaveMineralHabit.Bipyramid, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Wyrmglass", Albedo = new Color(0.925f, 0.922f, 0.894f), Smoothness = 0.88f, Emission = new Color(1.15f, 1.082f, 0.866f), Light = new Color(1f, 0.941f, 0.753f), EmitterHue = 3 },
				Description = "Wyrmglass (fantasy, Castle): milk-white translucent spindles each with a coiled serpent-shaped inclusion, glowing white-gold." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.BlueJohn, Habit = CaveMineralHabit.Nodules, LightHue = -1,
				Hue = new CaveGlowHue { Name = "BlueJohn", Albedo = new Color(0.29f, 0.165f, 0.416f), Smoothness = 0.7f, EmitterHue = -1 },
				Description = "BlueJohn (Cave): Castleton banded fluorite: nodules banded purple-blue and honey-yellow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Helictite, Habit = CaveMineralHabit.Helix, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Helictite", Albedo = new Color(0.957f, 0.941f, 0.91f), Smoothness = 0.6f, EmitterHue = -1 },
				Description = "Helictite (Cave): calcite that grows against gravity: curling wire filaments, white-translucent with clear tips." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Echospar, Habit = CaveMineralHabit.Rhomb, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Echospar", Albedo = new Color(0.933f, 0.957f, 1f), Smoothness = 0.95f, Emission = new Color(0.974f, 1.046f, 1.15f), Light = new Color(0.847f, 0.91f, 1f), EmitterHue = 1 },
				Description = "Echospar (fantasy, Cave): optical calcite rhombs, water-clear with a doubled image and a faint cold blue-white glow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Cassiterite, Habit = CaveMineralHabit.Twinned, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Cassiterite", Albedo = new Color(0.165f, 0.118f, 0.086f), Smoothness = 0.9f, Metallic = 0.3f, EmitterHue = -1 },
				Description = "Cassiterite (CoastalWater): Cornish tinstone: black-brown adamantine stubby prisms and knee twins." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Bellspar, Habit = CaveMineralHabit.Cupped, Lit = true, LightHue = 0,
				Hue = new CaveGlowHue { Name = "Bellspar", Albedo = new Color(0.29f, 0.227f, 0.659f), Smoothness = 0.88f, Emission = new Color(0.622f, 0.55f, 1.15f), Light = new Color(0.541f, 0.478f, 1f), EmitterHue = 0 },
				Description = "Bellspar (fantasy, CoastalWater): violet-blue hollow prisms that ring like drowned bells." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Apatite, Habit = CaveMineralHabit.Prisms, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Apatite", Albedo = new Color(0.227f, 0.604f, 0.541f), Smoothness = 0.86f, EmitterHue = -1 },
				Description = "Apatite (CoralReef): sea-green glassy hexagonal prisms on a cream phosphorite crust." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Lagoonglass, Habit = CaveMineralHabit.Blades, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Lagoonglass", Albedo = new Color(0.227f, 0.784f, 0.753f), Smoothness = 0.88f, Emission = new Color(0.289f, 1.01f, 0.938f), Light = new Color(0.251f, 0.878f, 0.816f), EmitterHue = 1 },
				Description = "Lagoonglass (fantasy, CoralReef): turquoise blades that hold the lagoon's light." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Tektite, Habit = CaveMineralHabit.Droplet, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Tektite", Albedo = new Color(0.118f, 0.165f, 0.086f), Smoothness = 0.9f, EmitterHue = -1 },
				Description = "Tektite (Crater): bottle-green to black glass splash-forms with sculpted pitted skins." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Starglass, Habit = CaveMineralHabit.Shards, Lit = true, LightHue = 0,
				Hue = new CaveGlowHue { Name = "Starglass", Albedo = new Color(0.086f, 0.078f, 0.11f), Smoothness = 0.95f, Emission = new Color(0.902f, 0.83f, 1.15f), Light = new Color(0.784f, 0.722f, 1f), EmitterHue = 0 },
				Description = "Starglass (fantasy, Crater): black glass blades shot with violet-white star-points." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.SalAmmoniac, Habit = CaveMineralHabit.Cubes, LightHue = -1,
				Hue = new CaveGlowHue { Name = "SalAmmoniac", Albedo = new Color(0.973f, 0.949f, 0.847f), Smoothness = 0.6f, EmitterHue = -1 },
				Description = "SalAmmoniac (CryovolcanicPlain, SulphurFlats): skeletal hopper cubes and feathers, white to cream (Sulphur Flats' own look: feathery fronds)." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Eitrglass, Habit = CaveMineralHabit.Blades, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Eitrglass", Albedo = new Color(0.165f, 0.816f, 0.627f), Smoothness = 0.86f, Emission = new Color(0.55f, 1.15f, 0.938f), Light = new Color(0.478f, 1f, 0.816f), EmitterHue = 2 },
				Description = "Eitrglass (fantasy, CryovolcanicPlain): translucent teal blades with a sloshing liquid-venom core." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Dogtooth, Habit = CaveMineralHabit.Bipyramid, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Dogtooth", Albedo = new Color(0.847f, 0.69f, 0.439f), Smoothness = 0.9f, EmitterHue = -1 },
				Description = "Dogtooth (DeepOcean, Grassland): calcite scalenohedra in vugs: honey to water-clear spearheads (Grassland's own look: in loess-doll geodes)." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Stellamaris, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 1, Flickers = true,
				Hue = new CaveGlowHue { Name = "Stellamaris", Albedo = new Color(0.878f, 0.925f, 1f), Smoothness = 0.92f, Emission = new Color(0.758f, 0.902f, 1.15f), Light = new Color(0.659f, 0.784f, 1f), EmitterHue = 1 },
				Description = "Stellamaris (fantasy, DeepOcean): blue-white clear prisms each holding one twinkling pinpoint of light." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Malachite, Habit = CaveMineralHabit.Botryoidal, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Malachite", Albedo = new Color(0.165f, 0.478f, 0.29f), Smoothness = 0.6f, EmitterHue = -1 },
				Description = "Malachite (Desert): banded green botryoidal crusts with azurite-blue spots." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Sunglass, Habit = CaveMineralHabit.Nodules, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Sunglass", Albedo = new Color(0.91f, 0.878f, 0.627f), Smoothness = 0.75f, Emission = new Color(1.082f, 1.01f, 0.722f), Light = new Color(0.941f, 0.878f, 0.627f), EmitterHue = 3 },
				Description = "Sunglass (fantasy, Desert): pale lemon-green translucent desert-glass lumps with frosted skins, glowing warm." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Ilmenite, Habit = CaveMineralHabit.Tabular, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Ilmenite", Albedo = new Color(0.165f, 0.165f, 0.188f), Smoothness = 0.8f, Metallic = 0.6f, EmitterHue = -1 },
				Description = "Ilmenite (DustSea): black tabular plates with a bronze-steel sheen." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Phaseglass, Habit = CaveMineralHabit.Blades, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Phaseglass", Albedo = new Color(0.863f, 0.902f, 0.957f), Smoothness = 0.88f, Emission = new Color(0.902f, 0.974f, 1.15f), Light = new Color(0.784f, 0.847f, 1f), EmitterHue = 1 },
				Description = "Phaseglass (fantasy, DustSea): milky blue-white blades whose glow follows the moon." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Vivianite, Habit = CaveMineralHabit.Radiating, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Vivianite", Albedo = new Color(0.165f, 0.416f, 0.541f), Smoothness = 0.85f, EmitterHue = -1 },
				Description = "Vivianite (Estuary, PeatBog): marsh-clay phosphate: radiating blue-green blades darkening to indigo (Peat Bog's own look: deep blue-green blades round buried bone)." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Ebbspar, Habit = CaveMineralHabit.Rhomb, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Ebbspar", Albedo = new Color(0.875f, 0.91f, 0.878f), Smoothness = 0.92f, Emission = new Color(0.622f, 1.01f, 0.902f), Light = new Color(0.541f, 0.878f, 0.784f), EmitterHue = 2 },
				Description = "Ebbspar (fantasy, Estuary): water-clear rhombs with a grey-green heart whose glow follows the tide." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Celestine, Habit = CaveMineralHabit.Tabular, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Celestine", Albedo = new Color(0.722f, 0.816f, 0.91f), Smoothness = 0.85f, EmitterHue = -1 },
				Description = "Celestine (Farmland): pale sky-blue tabular blades with a pearly lustre." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Fogpearl, Habit = CaveMineralHabit.Pearls, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Fogpearl", Albedo = new Color(0.941f, 0.941f, 0.925f), Smoothness = 0.8f, Emission = new Color(1.01f, 1.064f, 1.15f), Light = new Color(0.878f, 0.925f, 1f), EmitterHue = 1 },
				Description = "Fogpearl (fantasy, Farmland): nests of cave pearls in gour pools with a pearl-white breathing glow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Cinnabar, Habit = CaveMineralHabit.Rhomb, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Cinnabar", Albedo = new Color(0.69f, 0.125f, 0.102f), Smoothness = 0.88f, EmitterHue = -1 },
				Description = "Cinnabar (ForestRuins): vermilion rhombohedral crystals on white dolomite, adamantine." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Farrstone, Habit = CaveMineralHabit.Bipyramid, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Farrstone", Albedo = new Color(0.941f, 0.753f, 0.251f), Smoothness = 0.88f, Emission = new Color(1.15f, 1.082f, 0.722f), Light = new Color(1f, 0.941f, 0.627f), EmitterHue = 3 },
				Description = "Farrstone (fantasy, ForestRuins): the kings' radiance: honey-gold bipyramids glowing with a slow warm pulse." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Realgar, Habit = CaveMineralHabit.Prisms, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Realgar", Albedo = new Color(0.784f, 0.188f, 0.102f), Smoothness = 0.75f, EmitterHue = -1 },
				Description = "Realgar (Fortress): scarlet-orange resinous prisms on white calcite." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Orpiment, Habit = CaveMineralHabit.Foliated, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Orpiment", Albedo = new Color(0.91f, 0.753f, 0.251f), Smoothness = 0.7f, EmitterHue = -1 },
				Description = "Orpiment (Fortress): lemon-gold foliated masses, pearly." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Dalistone, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Dalistone", Albedo = new Color(0.941f, 0.91f, 0.816f), Smoothness = 0.85f, Emission = new Color(1.082f, 0.974f, 0.649f), Light = new Color(0.941f, 0.847f, 0.565f), EmitterHue = 3 },
				Description = "Dalistone (fantasy, Fortress): pale honey-white prisms curved like a tur's horn, breathing a warm glow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.IcelandSpar, Habit = CaveMineralHabit.Rhomb, LightHue = -1,
				Hue = new CaveGlowHue { Name = "IcelandSpar", Albedo = new Color(0.957f, 0.925f, 0.847f), Smoothness = 0.95f, EmitterHue = -1 },
				Description = "IcelandSpar (GeyserBasin): clear colourless rhombohedra with a faint honey tint and a doubled image." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Stilbite, Habit = CaveMineralHabit.Fibrous, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Stilbite", Albedo = new Color(0.91f, 0.69f, 0.596f), Smoothness = 0.7f, EmitterHue = -1 },
				Description = "Stilbite (GeyserBasin): peach-salmon bow-tie sheaves with white sprays in basalt vugs." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Solarsteinn, Habit = CaveMineralHabit.Rhomb, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Solarsteinn", Albedo = new Color(0.957f, 0.941f, 0.878f), Smoothness = 0.93f, Emission = new Color(1.15f, 1.082f, 0.902f), Light = new Color(1f, 0.941f, 0.784f), EmitterHue = 3 },
				Description = "Solarsteinn (fantasy, GeyserBasin): sunstone spar rhombs whose white-gold glow brightens on the faces turned toward the sun." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.CupHoar, Habit = CaveMineralHabit.Cupped, LightHue = -1,
				Hue = new CaveGlowHue { Name = "CupHoar", Albedo = new Color(0.933f, 0.957f, 0.98f), Smoothness = 0.8f, EmitterHue = -1 },
				Description = "CupHoar (Glacier): hollow hexagonal depth-hoar cups, glittering." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Nagamani, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Nagamani", Albedo = new Color(0.878f, 0.957f, 0.941f), Smoothness = 0.92f, Emission = new Color(0.478f, 1.01f, 0.866f), Light = new Color(0.416f, 0.878f, 0.753f), EmitterHue = 2 },
				Description = "Nagamani (fantasy, Glacier): water-clear prisms with a sea-green core that glows." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Skyglass, Habit = CaveMineralHabit.Shards, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Skyglass", Albedo = new Color(0.659f, 0.816f, 0.941f), Smoothness = 0.9f, Emission = new Color(0.649f, 0.902f, 1.15f), Light = new Color(0.565f, 0.784f, 1f), EmitterHue = 1 },
				Description = "Skyglass (fantasy, Grassland): the sky's tears: pale-blue clear shards with a soft glow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Turquoise, Habit = CaveMineralHabit.Matrix, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Turquoise", Albedo = new Color(0.29f, 0.69f, 0.659f), Smoothness = 0.5f, EmitterHue = -1 },
				Description = "Turquoise (HighDesert): sky-blue to green nuggets and spider-web veins in brown limonite." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Gueststar, Habit = CaveMineralHabit.Shards, Lit = true, LightHue = 1, Flickers = true,
				Hue = new CaveGlowHue { Name = "Gueststar", Albedo = new Color(0.933f, 0.949f, 1f), Smoothness = 0.9f, Emission = new Color(0.902f, 0.992f, 1.15f), Light = new Color(0.784f, 0.863f, 1f), EmitterHue = 1 },
				Description = "Gueststar (fantasy, HighDesert): frost-white shard sprays each with one burning blue-white point that twinkles." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Garnet, Habit = CaveMineralHabit.Dodecahedral, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Garnet", Albedo = new Color(0.478f, 0.102f, 0.133f), Smoothness = 0.85f, EmitterHue = -1 },
				Description = "Garnet (Hills): wine-red almandine dodecahedra in schist." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Staurolite, Habit = CaveMineralHabit.Twinned, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Staurolite", Albedo = new Color(0.416f, 0.29f, 0.196f), Smoothness = 0.5f, EmitterHue = -1 },
				Description = "Staurolite (Hills): fairy crosses: brown cross-twinned prisms." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Heartgarnet, Habit = CaveMineralHabit.Dodecahedral, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Heartgarnet", Albedo = new Color(0.541f, 0.063f, 0.125f), Smoothness = 0.88f, Emission = new Color(1.15f, 0.216f, 0.289f), Light = new Color(1f, 0.188f, 0.251f), EmitterHue = 3 },
				Description = "Heartgarnet (fantasy, Hills): the moura's heart: a deep-red garnet glowing from within with a slow heartbeat." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Hoarfeather, Habit = CaveMineralHabit.Dendrite, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Hoarfeather", Albedo = new Color(0.957f, 0.973f, 0.988f), Smoothness = 0.8f, EmitterHue = -1 },
				Description = "Hoarfeather (IceCave): giant sublimation hoar: fern-branched six-armed plates, white-translucent." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Cryocalcite, Habit = CaveMineralHabit.Nodules, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Cryocalcite", Albedo = new Color(0.902f, 0.839f, 0.706f), Smoothness = 0.55f, EmitterHue = -1 },
				Description = "Cryocalcite (IceCave): pearly cream-to-amber spherule clusters on melted-out ice." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Chudfire, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Chudfire", Albedo = new Color(0.91f, 0.941f, 0.973f), Smoothness = 0.92f, Emission = new Color(1.15f, 0.794f, 0.361f), Light = new Color(1f, 0.69f, 0.314f), EmitterHue = 3 },
				Description = "Chudfire (fantasy, IceCave): a clear ice prism with a frozen gold-orange flame inside." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Hydrohalite, Habit = CaveMineralHabit.Tabular, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Hydrohalite", Albedo = new Color(0.933f, 0.973f, 1f), Smoothness = 0.9f, EmitterHue = -1 },
				Description = "Hydrohalite (IceGeyserField): water-clear to pale-blue platy prisms of a salt that forms only below freezing." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Hyalite, Habit = CaveMineralHabit.Botryoidal, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Hyalite", Albedo = new Color(0.91f, 0.957f, 0.933f), Smoothness = 0.9f, Emission = new Color(0.713f, 1.15f, 0.902f), Light = new Color(0.62f, 1f, 0.784f), EmitterHue = 2 },
				Description = "Hyalite (fantasy, IceGeyserField): water-clear beaded opal crusts glowing cold green." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Nightpearl, Habit = CaveMineralHabit.Pearls, Lit = true, LightHue = 0,
				Hue = new CaveGlowHue { Name = "Nightpearl", Albedo = new Color(0.965f, 0.933f, 0.949f), Smoothness = 0.9f, Emission = new Color(1.082f, 0.974f, 1.15f), Light = new Color(0.941f, 0.847f, 1f), EmitterHue = 0 },
				Description = "Nightpearl (fantasy, IcePalace): luminous pearls nested in nacre-lined ice cups, a pink-lilac sheen." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.ScythianEmerald, Habit = CaveMineralHabit.Prisms, LightHue = -1,
				Hue = new CaveGlowHue { Name = "ScythianEmerald", Albedo = new Color(0.118f, 0.478f, 0.29f), Smoothness = 0.9f, EmitterHue = -1 },
				Description = "ScythianEmerald (IceRuin): deep green beryl hexagonal prisms in white quartz." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Hornice, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Hornice", Albedo = new Color(0.659f, 0.784f, 0.91f), Smoothness = 0.9f, Emission = new Color(0.866f, 0.992f, 1.15f), Light = new Color(0.753f, 0.863f, 1f), EmitterHue = 1 },
				Description = "Hornice (fantasy, IceSheet): blue ice prisms with horn-like growth rings, glowing with the wind." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Jadeice, Habit = CaveMineralHabit.Prisms, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Jadeice", Albedo = new Color(0.165f, 0.416f, 0.29f), Smoothness = 0.85f, EmitterHue = -1 },
				Description = "Jadeice (IceShelf): bottle-green banded marine-ice prisms." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Swellglass, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Swellglass", Albedo = new Color(0.541f, 0.816f, 0.722f), Smoothness = 0.9f, Emission = new Color(0.695f, 1.082f, 0.938f), Light = new Color(0.604f, 0.941f, 0.816f), EmitterHue = 2 },
				Description = "Swellglass (fantasy, IceShelf): sea-green glass prisms that brighten and dim with the swell." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Chrysolite, Habit = CaveMineralHabit.Nodules, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Chrysolite", Albedo = new Color(0.29f, 0.416f, 0.141f), Smoothness = 0.85f, EmitterHue = -1 },
				Description = "Chrysolite (ImpactBasin): olivine, the medieval chrysolite: bottle-green granular nodules." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.MansionPearl, Habit = CaveMineralHabit.Nodules, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "MansionPearl", Albedo = new Color(0.933f, 0.941f, 0.918f), Smoothness = 0.85f, Emission = new Color(1.006f, 1.046f, 1.15f), Light = new Color(0.875f, 0.91f, 1f), EmitterHue = 1 },
				Description = "MansionPearl (fantasy, ImpactBasin): milky nacre-white nodules each lit on its own mansion night." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.WatermelonTourmaline, Habit = CaveMineralHabit.Prisms, LightHue = -1,
				Hue = new CaveGlowHue { Name = "WatermelonTourmaline", Albedo = new Color(0.878f, 0.353f, 0.541f), Smoothness = 0.88f, EmitterHue = -1 },
				Description = "WatermelonTourmaline (Jungle): striated prisms, a pink core in a green rind, in white pegmatite." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Leafglass, Habit = CaveMineralHabit.Leaf, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Leafglass", Albedo = new Color(0.416f, 0.816f, 0.416f), Smoothness = 0.86f, Emission = new Color(0.622f, 1.082f, 0.55f), Light = new Color(0.541f, 0.941f, 0.478f), EmitterHue = 2 },
				Description = "Leafglass (fantasy, Jungle): translucent leaf-green glassy plates veined like leaf skeletons." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Specularite, Habit = CaveMineralHabit.Foliated, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Specularite", Albedo = new Color(0.165f, 0.165f, 0.188f), Smoothness = 0.9f, Metallic = 0.8f, EmitterHue = -1 },
				Description = "Specularite (JungleRuins): specular hematite: mirror-black glittering plates with a steel sheen." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Glyphjade, Habit = CaveMineralHabit.Massive, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Glyphjade", Albedo = new Color(0.541f, 0.784f, 0.627f), Smoothness = 0.8f, Emission = new Color(0.794f, 1.082f, 0.866f), Light = new Color(0.69f, 0.941f, 0.753f), EmitterHue = 2 },
				Description = "Glyphjade (fantasy, JungleRuins): translucent green-white crystal with dark glyph-shaped inclusions." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Zircon, Habit = CaveMineralHabit.Bipyramid, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Zircon", Albedo = new Color(0.659f, 0.376f, 0.165f), Smoothness = 0.92f, EmitterHue = -1 },
				Description = "Zircon (JungleTemple): stubby honey-brown bipyramids, adamantine." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Monsoonglass, Habit = CaveMineralHabit.Blades, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Monsoonglass", Albedo = new Color(0.604f, 0.69f, 0.753f), Smoothness = 0.86f, Emission = new Color(0.866f, 0.974f, 1.082f), Light = new Color(0.753f, 0.847f, 0.941f), EmitterHue = 1 },
				Description = "Monsoonglass (fantasy, JungleTemple): rain-grey translucent blades with silver streaks, bright in the wet season." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Smithsonite, Habit = CaveMineralHabit.Botryoidal, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Smithsonite", Albedo = new Color(0.541f, 0.784f, 0.753f), Smoothness = 0.6f, EmitterHue = -1 },
				Description = "Smithsonite (Karst): zinc grape-crusts in pastel teal, waxy." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Mistjade, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Mistjade", Albedo = new Color(0.604f, 0.784f, 0.69f), Smoothness = 0.82f, Emission = new Color(0.902f, 1.082f, 1.01f), Light = new Color(0.784f, 0.941f, 0.878f), EmitterHue = 2 },
				Description = "Mistjade (fantasy, Karst): celadon prisms holding a slow white mist." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.CavePearl, Habit = CaveMineralHabit.Pearls, LightHue = -1,
				Hue = new CaveGlowHue { Name = "CavePearl", Albedo = new Color(0.925f, 0.902f, 0.847f), Smoothness = 0.7f, EmitterHue = -1 },
				Description = "CavePearl (Lake): polished cream-white calcite spheres nested in rimstone cups." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Nixglass, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Nixglass", Albedo = new Color(0.416f, 0.816f, 0.784f), Smoothness = 0.95f, Emission = new Color(0.478f, 0.938f, 0.902f), Light = new Color(0.416f, 0.816f, 0.784f), EmitterHue = 1 },
				Description = "Nixglass (fantasy, Lake): water-clear blue-green prisms that look wet, a slow ripple glow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Thenardite, Habit = CaveMineralHabit.Floss, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Thenardite", Albedo = new Color(0.957f, 0.949f, 0.925f), Smoothness = 0.4f, EmitterHue = -1 },
				Description = "Thenardite (LavaTube, Volcanic): cave cotton: white sulphate efflorescence crusts and hair tufts (Volcanic's own look: hair-needles on tube walls)." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.OlivineBomb, Habit = CaveMineralHabit.Nodules, LightHue = -1,
				Hue = new CaveGlowHue { Name = "OlivineBomb", Albedo = new Color(0.353f, 0.478f, 0.102f), Smoothness = 0.88f, EmitterHue = -1 },
				Description = "OlivineBomb (LavaTube): bottle-green glassy olivine nodules bursting from broken basalt bombs." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Magecstone, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Magecstone", Albedo = new Color(1f, 0.941f, 0.784f), Smoothness = 0.9f, Emission = new Color(1.082f, 0.794f, 0.289f), Light = new Color(0.941f, 0.69f, 0.251f), EmitterHue = 3 },
				Description = "Magecstone (fantasy, LavaTube): shards of the captive sun: white-gold prisms with a warm glow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Marcasite, Habit = CaveMineralHabit.Blades, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Marcasite", Albedo = new Color(0.722f, 0.627f, 0.314f), Smoothness = 0.8f, Metallic = 0.8f, EmitterHue = -1 },
				Description = "Marcasite (Mangrove): brassy cockscomb spear crystals with a blue-violet tarnish." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Nuru, Habit = CaveMineralHabit.Blades, Lit = true, LightHue = 1, Flickers = true,
				Hue = new CaveGlowHue { Name = "Nuru", Albedo = new Color(0.878f, 0.933f, 0.973f), Smoothness = 0.9f, Emission = new Color(0.406f, 1.01f, 1.082f), Light = new Color(0.353f, 0.878f, 0.941f), EmitterHue = 1 },
				Description = "Nuru (fantasy, Mangrove): milky blue-white blades that sparkle like disturbed seafire." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Simetite, Habit = CaveMineralHabit.Nodules, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Simetite", Albedo = new Color(0.541f, 0.165f, 0.102f), Smoothness = 0.85f, EmitterHue = -1 },
				Description = "Simetite (MarblePalace): Sicilian red amber: translucent wine-red to orange nodules." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Fatastone, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 0,
				Hue = new CaveGlowHue { Name = "Fatastone", Albedo = new Color(0.933f, 0.941f, 0.957f), Smoothness = 0.93f, Emission = new Color(1.01f, 0.974f, 1.15f), Light = new Color(0.878f, 0.847f, 1f), EmitterHue = 0 },
				Description = "Fatastone (fantasy, MarblePalace): mirage quartz: clear prisms whose cores hold a faint moving image of towers." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Waxspar, Habit = CaveMineralHabit.Rhomb, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Waxspar", Albedo = new Color(0.957f, 0.933f, 0.863f), Smoothness = 0.5f, EmitterHue = -1 },
				Description = "Waxspar (MethaneLake): soft waxy colourless-cream rhombs." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Adamant, Habit = CaveMineralHabit.Octahedral, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Adamant", Albedo = new Color(0.165f, 0.173f, 0.188f), Smoothness = 0.85f, Metallic = 0.7f, EmitterHue = -1 },
				Description = "Adamant (MethaneLake): lodestone octahedra, black steel with a violet temper sheen." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Orphanstone, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Orphanstone", Albedo = new Color(0.627f, 0.094f, 0.157f), Smoothness = 0.9f, Emission = new Color(1.15f, 0.478f, 0.262f), Light = new Color(1f, 0.416f, 0.227f), EmitterHue = 3 },
				Description = "Orphanstone (fantasy, MethaneLake): a lone wine-red carbuncle with a slow warm breath." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Cristobalite, Habit = CaveMineralHabit.Botryoidal, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Cristobalite", Albedo = new Color(0.957f, 0.957f, 0.941f), Smoothness = 0.5f, EmitterHue = -1 },
				Description = "Cristobalite (MoltenSurface): snow-white spherule clusters in vesicles and glass." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.BrimstoneAmber, Habit = CaveMineralHabit.Nodules, LightHue = -1,
				Hue = new CaveGlowHue { Name = "BrimstoneAmber", Albedo = new Color(0.659f, 0.125f, 0.055f), Smoothness = 0.8f, EmitterHue = -1 },
				Description = "BrimstoneAmber (MoltenSurface): glassy blood-red quenched-sulphur beads and crusts." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Dracontite, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Dracontite", Albedo = new Color(0.784f, 0.251f, 0.102f), Smoothness = 0.88f, Emission = new Color(1.15f, 0.505f, 0.216f), Light = new Color(1f, 0.439f, 0.188f), EmitterHue = 3 },
				Description = "Dracontite (fantasy, MoltenSurface): a dragon's-brain stone: ember-red prisms with a star core." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Hydrophane, Habit = CaveMineralHabit.Nodules, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Hydrophane", Albedo = new Color(0.925f, 0.91f, 0.878f), Smoothness = 0.8f, EmitterHue = -1 },
				Description = "Hydrophane (MountainSlope): milky-white to honey opal nodules with rainbow fire flecks." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Moonstone, Habit = CaveMineralHabit.Tabular, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Moonstone", Albedo = new Color(0.91f, 0.933f, 0.973f), Smoothness = 0.9f, Emission = new Color(0.695f, 0.83f, 1.15f), Light = new Color(0.604f, 0.722f, 1f), EmitterHue = 1 },
				Description = "Moonstone (fantasy, MountainSlope): tabular sanidine with a drifting blue sheen." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.NitrogenSpar, Habit = CaveMineralHabit.Prisms, LightHue = -1,
				Hue = new CaveGlowHue { Name = "NitrogenSpar", Albedo = new Color(0.949f, 0.91f, 0.863f), Smoothness = 0.6f, EmitterHue = -1 },
				Description = "NitrogenSpar (NitrogenIceField): hexagonal nitrogen ice, colourless-to-peach and waxy." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Tearglass, Habit = CaveMineralHabit.Droplet, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Tearglass", Albedo = new Color(0.91f, 0.949f, 1f), Smoothness = 0.92f, Emission = new Color(0.934f, 1.037f, 1.15f), Light = new Color(0.812f, 0.902f, 1f), EmitterHue = 1 },
				Description = "Tearglass (fantasy, NitrogenIceField): clusters of clear teardrops with a faint breathing glow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.DesertRose, Habit = CaveMineralHabit.Rosette, LightHue = -1,
				Hue = new CaveGlowHue { Name = "DesertRose", Albedo = new Color(0.784f, 0.659f, 0.533f), Smoothness = 0.3f, EmitterHue = -1 },
				Description = "DesertRose (Oasis): sand-included gypsum rosettes, buff-pink." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Alum, Habit = CaveMineralHabit.Octahedral, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Alum", Albedo = new Color(0.949f, 0.949f, 0.933f), Smoothness = 0.8f, EmitterHue = -1 },
				Description = "Alum (Oasis): water-clear octahedra with a white efflorescent bloom." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Oraclestone, Habit = CaveMineralHabit.Rhomb, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Oraclestone", Albedo = new Color(0.91f, 0.753f, 0.439f), Smoothness = 0.88f, Emission = new Color(1.082f, 0.866f, 0.505f), Light = new Color(0.941f, 0.753f, 0.439f), EmitterHue = 3 },
				Description = "Oraclestone (fantasy, Oasis): honey-gold translucent calcite holding a ram-horn spiral, glowing warm at dusk." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Siderite, Habit = CaveMineralHabit.Rhomb, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Siderite", Albedo = new Color(0.659f, 0.471f, 0.227f), Smoothness = 0.75f, EmitterHue = -1 },
				Description = "Siderite (Ocean): iron spar: honey-brown saddle rhombs on cream ankerite." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.GalernaStone, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "GalernaStone", Albedo = new Color(0.353f, 0.29f, 0.416f), Smoothness = 0.88f, Emission = new Color(1.046f, 0.577f, 0.262f), Light = new Color(0.91f, 0.502f, 0.227f), EmitterHue = 3 },
				Description = "GalernaStone (fantasy, Ocean): smoky storm-violet quartz prisms with an ember core that brightens as the wind rises." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Wispglass, Habit = CaveMineralHabit.Nodules, Lit = true, LightHue = 2, Flickers = true,
				Hue = new CaveGlowHue { Name = "Wispglass", Albedo = new Color(0.847f, 0.91f, 0.69f), Smoothness = 0.85f, Emission = new Color(0.902f, 1.082f, 0.505f), Light = new Color(0.784f, 0.941f, 0.439f), EmitterHue = 2 },
				Description = "Wispglass (fantasy, PeatBog): pale green-gold nodules with a slow flickering glow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Stillglass, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Stillglass", Albedo = new Color(0.933f, 0.957f, 0.973f), Smoothness = 0.94f, Emission = new Color(0.974f, 1.064f, 1.15f), Light = new Color(0.847f, 0.925f, 1f), EmitterHue = 1 },
				Description = "Stillglass (fantasy, PermanentIce): clear prisms each holding a water drop or snowflake stopped mid-fall, a steady glow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.BariteRose, Habit = CaveMineralHabit.Rosette, LightHue = -1,
				Hue = new CaveGlowHue { Name = "BariteRose", Albedo = new Color(0.541f, 0.29f, 0.188f), Smoothness = 0.3f, EmitterHue = -1 },
				Description = "BariteRose (Plains): rose-rock rosettes, reddish-brown with sand-dusted petals." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.SatinSpar, Habit = CaveMineralHabit.Fibrous, LightHue = -1,
				Hue = new CaveGlowHue { Name = "SatinSpar", Albedo = new Color(0.957f, 0.941f, 0.91f), Smoothness = 0.75f, EmitterHue = -1 },
				Description = "SatinSpar (Plains): silky white fibrous gypsum veins with a chatoyant sheen." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Sunspoke, Habit = CaveMineralHabit.Radiating, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Sunspoke", Albedo = new Color(0.957f, 0.925f, 0.847f), Smoothness = 0.9f, Emission = new Color(1.082f, 0.866f, 0.289f), Light = new Color(0.941f, 0.753f, 0.251f), EmitterHue = 3 },
				Description = "Sunspoke (fantasy, Plains): clear quartz shot through with golden needles radiating like wheel spokes, glowing warm gold." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Antozonite, Habit = CaveMineralHabit.Cubes, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Antozonite", Albedo = new Color(0.165f, 0.102f, 0.227f), Smoothness = 0.8f, EmitterHue = -1 },
				Description = "Antozonite (RadiationPlain): the fetid spar: fluorite darkened to violet-black with violet edges." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Septentrion, Habit = CaveMineralHabit.Stellate, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Septentrion", Albedo = new Color(0.933f, 0.949f, 1f), Smoothness = 0.9f, Emission = new Color(0.974f, 1.028f, 1.15f), Light = new Color(0.847f, 0.894f, 1f), EmitterHue = 1 },
				Description = "Septentrion (fantasy, RadiationPlain): seven-rayed star clusters of clear white crystal." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Balas, Habit = CaveMineralHabit.Octahedral, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Balas", Albedo = new Color(0.769f, 0.416f, 0.478f), Smoothness = 0.9f, EmitterHue = -1 },
				Description = "Balas (RegolithPlain): the medieval balas ruby: pink spinel octahedra." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Levanite, Habit = CaveMineralHabit.Blades, Lit = true, LightHue = 0,
				Hue = new CaveGlowHue { Name = "Levanite", Albedo = new Color(0.353f, 0.329f, 0.408f), Smoothness = 0.86f, Emission = new Color(0.902f, 0.866f, 0.992f), Light = new Color(0.784f, 0.753f, 0.863f), EmitterHue = 0 },
				Description = "Levanite (fantasy, RegolithPlain): smoky violet-grey bladed prisms bright at sunrise and sunset." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Sapphire, Habit = CaveMineralHabit.Barrel, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Sapphire", Albedo = new Color(0.165f, 0.227f, 0.541f), Smoothness = 0.92f, EmitterHue = -1 },
				Description = "Sapphire (Rille): deep cornflower corundum barrels tapering both ends." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Shamirspar, Habit = CaveMineralHabit.Blades, Lit = true, LightHue = 0,
				Hue = new CaveGlowHue { Name = "Shamirspar", Albedo = new Color(0.91f, 0.925f, 0.957f), Smoothness = 0.94f, Emission = new Color(0.974f, 0.902f, 1.15f), Light = new Color(0.847f, 0.784f, 1f), EmitterHue = 0 },
				Description = "Shamirspar (fantasy, Rille): clear blades split clean as glass, a violet-white band sliding along each." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Agate, Habit = CaveMineralHabit.Geode, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Agate", Albedo = new Color(0.541f, 0.596f, 0.659f), Smoothness = 0.7f, EmitterHue = -1 },
				Description = "Agate (River): banded chalcedony geodes, grey-blue, rust and milk round a rock-crystal core." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.RhinegoldSeam, Habit = CaveMineralHabit.Matrix, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "RhinegoldSeam", Albedo = new Color(0.847f, 0.659f, 0.22f), Smoothness = 0.85f, Emission = new Color(1.082f, 0.866f, 0.361f), Light = new Color(0.941f, 0.753f, 0.314f), EmitterHue = 3 },
				Description = "RhinegoldSeam (fantasy, River): old-gold threads in milky quartz, a warm shimmer like light through moving water." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Kyanite, Habit = CaveMineralHabit.Blades, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Kyanite", Albedo = new Color(0.353f, 0.541f, 0.784f), Smoothness = 0.8f, EmitterHue = -1 },
				Description = "Kyanite (RockyCoast): sky-blue bladed sprays in white quartz pods." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Haarglass, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Haarglass", Albedo = new Color(0.541f, 0.784f, 0.69f), Smoothness = 0.75f, Emission = new Color(0.722f, 1.046f, 0.902f), Light = new Color(0.627f, 0.91f, 0.784f), EmitterHue = 2 },
				Description = "Haarglass (fantasy, RockyCoast): finfolk glass: frosted sea-green prisms holding a drowned light." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Lajvard, Habit = CaveMineralHabit.Matrix, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Lajvard", Albedo = new Color(0.118f, 0.227f, 0.541f), Smoothness = 0.6f, EmitterHue = -1 },
				Description = "Lajvard (RockyTerrain): lapis lazuli: deep ultramarine veins and nodules flecked with gold pyrite." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Shabchiragh, Habit = CaveMineralHabit.Octahedral, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Shabchiragh", Albedo = new Color(0.784f, 0.188f, 0.29f), Smoothness = 0.9f, Emission = new Color(1.15f, 0.478f, 0.55f), Light = new Color(1f, 0.416f, 0.478f), EmitterHue = 3 },
				Description = "Shabchiragh (fantasy, RockyTerrain): the night-lamp gem: rose-red spinel octahedra glowing warm after sunset." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Wollastonite, Habit = CaveMineralHabit.Radiating, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Wollastonite", Albedo = new Color(0.941f, 0.933f, 0.91f), Smoothness = 0.6f, EmitterHue = -1 },
				Description = "Wollastonite (RunawayGreenhousePlain): white silky radiating sprays." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Pyrrhotite, Habit = CaveMineralHabit.Tabular, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Pyrrhotite", Albedo = new Color(0.541f, 0.416f, 0.227f), Smoothness = 0.8f, Metallic = 0.8f, EmitterHue = -1 },
				Description = "Pyrrhotite (RunawayGreenhousePlain): bronze-brown metallic hexagonal plates." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Sibylleaf, Habit = CaveMineralHabit.Leaf, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Sibylleaf", Albedo = new Color(0.816f, 0.251f, 0.353f), Smoothness = 0.86f, Emission = new Color(1.046f, 0.722f, 0.361f), Light = new Color(0.91f, 0.627f, 0.314f), EmitterHue = 3 },
				Description = "Sibylleaf (fantasy, RunawayGreenhousePlain): thin curved translucent crimson-amber leaves." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Halite, Habit = CaveMineralHabit.Cubes, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Halite", Albedo = new Color(0.957f, 0.925f, 0.925f), Smoothness = 0.7f, EmitterHue = -1 },
				Description = "Halite (SaltFlat): water-clear to pale pink hopper cubes." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Sylvite, Habit = CaveMineralHabit.Cubes, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Sylvite", Albedo = new Color(0.784f, 0.376f, 0.227f), Smoothness = 0.6f, EmitterHue = -1 },
				Description = "Sylvite (SaltFlat): translucent brick-red cubes." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Embersalt, Habit = CaveMineralHabit.Cubes, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Embersalt", Albedo = new Color(0.91f, 0.627f, 0.478f), Smoothness = 0.7f, Emission = new Color(1.15f, 0.794f, 0.577f), Light = new Color(1f, 0.69f, 0.502f), EmitterHue = 3 },
				Description = "Embersalt (fantasy, SaltFlat): amber-rose cubes with a warm glow from dusk." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Reefgold, Habit = CaveMineralHabit.Prisms, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Reefgold", Albedo = new Color(0.91f, 0.878f, 0.784f), Smoothness = 0.85f, Metallic = 0.2f, EmitterHue = -1 },
				Description = "Reefgold (Savanna): milky quartz prisms with leafy native gold on their faces." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Emerald, Habit = CaveMineralHabit.Prisms, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Emerald", Albedo = new Color(0.102f, 0.541f, 0.29f), Smoothness = 0.9f, EmitterHue = -1 },
				Description = "Emerald (Savanna): deep green hexagonal prisms in black schist." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Rainglass, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Rainglass", Albedo = new Color(0.416f, 0.816f, 0.69f), Smoothness = 0.9f, Emission = new Color(0.622f, 1.082f, 0.938f), Light = new Color(0.541f, 0.941f, 0.816f), EmitterHue = 2 },
				Description = "Rainglass (fantasy, Savanna): blue-green prisms that brighten with the rain." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Goblinfire, Habit = CaveMineralHabit.Druse, Lit = true, LightHue = 2, Flickers = true,
				Hue = new CaveGlowHue { Name = "Goblinfire", Albedo = new Color(0.541f, 0.816f, 0.753f), Smoothness = 0.88f, Emission = new Color(0.478f, 1.082f, 0.866f), Light = new Color(0.416f, 0.941f, 0.753f), EmitterHue = 2 },
				Description = "Goblinfire (fantasy, Scree): blue-green cold crystals whose glow hops from point to point." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Peridot, Habit = CaveMineralHabit.Nodules, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Peridot", Albedo = new Color(0.416f, 0.541f, 0.165f), Smoothness = 0.85f, EmitterHue = -1 },
				Description = "Peridot (Scrubland): olive-green glassy grains in black basalt bombs." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Sunshell, Habit = CaveMineralHabit.Rosette, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Sunshell", Albedo = new Color(0.91f, 0.847f, 0.722f), Smoothness = 0.88f, Emission = new Color(1.082f, 0.866f, 0.577f), Light = new Color(0.941f, 0.753f, 0.502f), EmitterHue = 3 },
				Description = "Sunshell (fantasy, Scrubland): an iridescent spiral shell-crystal glowing warm after sunset." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Lapastone, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 1, Flickers = true,
				Hue = new CaveGlowHue { Name = "Lapastone", Albedo = new Color(0.878f, 0.925f, 0.973f), Smoothness = 0.93f, Emission = new Color(0.861f, 1.01f, 1.15f), Light = new Color(0.749f, 0.878f, 1f), EmitterHue = 1 },
				Description = "Lapastone (fantasy, Seamount): water-clear blue-white prisms through which pale threads of light dart." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Analcime, Habit = CaveMineralHabit.Trapezohedral, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Analcime", Albedo = new Color(0.925f, 0.918f, 0.902f), Smoothness = 0.85f, EmitterHue = -1 },
				Description = "Analcime (Seamount): water-clear to milky zeolite trapezohedra from pillow vesicles." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Rutilquartz, Habit = CaveMineralHabit.Prisms, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Rutilquartz", Albedo = new Color(0.941f, 0.933f, 0.902f), Smoothness = 0.93f, EmitterHue = -1 },
				Description = "Rutilquartz (Snow): Venus hair: water-clear prisms pierced by golden needles." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.MidwinterSpar, Habit = CaveMineralHabit.Blades, Lit = true, LightHue = 0,
				Hue = new CaveGlowHue { Name = "MidwinterSpar", Albedo = new Color(0.541f, 0.478f, 0.659f), Smoothness = 0.88f, Emission = new Color(1.01f, 0.974f, 1.15f), Light = new Color(0.878f, 0.847f, 1f), EmitterHue = 0 },
				Description = "MidwinterSpar (fantasy, Snow): smoke-violet bladed spar with a white core, brightest in the polar night." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Dioptase, Habit = CaveMineralHabit.Rhomb, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Dioptase", Albedo = new Color(0.039f, 0.541f, 0.416f), Smoothness = 0.9f, EmitterHue = -1 },
				Description = "Dioptase (Steppe): vivid emerald-green rhombohedral prisms on white quartz." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.WolfsEye, Habit = CaveMineralHabit.Nodules, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "WolfsEye", Albedo = new Color(0.353f, 0.227f, 0.125f), Smoothness = 0.85f, Emission = new Color(0.974f, 0.722f, 0.289f), Light = new Color(0.847f, 0.627f, 0.251f), EmitterHue = 3 },
				Description = "WolfsEye (fantasy, Steppe): smoky-amber chatoyant chalcedony nodules with a moving bright eye." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Covellite, Habit = CaveMineralHabit.Tabular, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Covellite", Albedo = new Color(0.165f, 0.165f, 0.478f), Smoothness = 0.85f, Metallic = 0.6f, EmitterHue = -1 },
				Description = "Covellite (SubsurfaceOceanVent): indigo-blue iridescent hexagonal plates." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Anhydrite, Habit = CaveMineralHabit.Blades, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Anhydrite", Albedo = new Color(0.91f, 0.878f, 0.925f), Smoothness = 0.6f, EmitterHue = -1 },
				Description = "Anhydrite (SubsurfaceOceanVent): white-lilac bladed prisms." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Gledstone, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Gledstone", Albedo = new Color(0.227f, 0.039f, 0.024f), Smoothness = 0.88f, Emission = new Color(1.15f, 0.406f, 0.117f), Light = new Color(1f, 0.353f, 0.102f), EmitterHue = 3 },
				Description = "Gledstone (fantasy, SubsurfaceOceanVent): ember-red sulphide prisms glowing at night." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.NativeSulphur, Habit = CaveMineralHabit.Bipyramid, LightHue = -1,
				Hue = new CaveGlowHue { Name = "NativeSulphur", Albedo = new Color(0.91f, 0.753f, 0.188f), Smoothness = 0.7f, EmitterHue = -1 },
				Description = "NativeSulphur (SulphurFlats): lemon-amber translucent bipyramids, resinous." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Bluebrim, Habit = CaveMineralHabit.Bipyramid, Lit = true, LightHue = 1, Flickers = true,
				Hue = new CaveGlowHue { Name = "Bluebrim", Albedo = new Color(0.91f, 0.816f, 0.314f), Smoothness = 0.75f, Emission = new Color(0.334f, 0.541f, 1.15f), Light = new Color(0.29f, 0.471f, 1f), EmitterHue = 1 },
				Description = "Bluebrim (fantasy, SulphurFlats): sulphur bipyramids holding a flickering cold blue flame." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Leadfeather, Habit = CaveMineralHabit.Dendrite, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Leadfeather", Albedo = new Color(0.541f, 0.549f, 0.573f), Smoothness = 0.8f, Metallic = 0.8f, EmitterHue = -1 },
				Description = "Leadfeather (SulphuricCloudDeck): dendritic metal-frost feathers, lead-silver." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Pyrite, Habit = CaveMineralHabit.Cubes, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Pyrite", Albedo = new Color(0.784f, 0.659f, 0.251f), Smoothness = 0.85f, Metallic = 0.9f, EmitterHue = -1 },
				Description = "Pyrite (SulphuricCloudDeck, Swamp): brass striated cube druses (Swamp's own look: pyrite suns in black shale)." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Cloudheart, Habit = CaveMineralHabit.Pearls, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Cloudheart", Albedo = new Color(0.91f, 0.847f, 0.627f), Smoothness = 0.8f, Emission = new Color(1.082f, 1.019f, 0.758f), Light = new Color(0.941f, 0.886f, 0.659f), EmitterHue = 3 },
				Description = "Cloudheart (fantasy, SulphuricCloudDeck): milk-saffron translucent spheroids, brighter as the deck sinks." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Mireopal, Habit = CaveMineralHabit.Nodules, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Mireopal", Albedo = new Color(0.604f, 0.753f, 0.722f), Smoothness = 0.85f, Emission = new Color(0.55f, 0.866f, 0.794f), Light = new Color(0.478f, 0.753f, 0.69f), EmitterHue = 2 },
				Description = "Mireopal (fantasy, Swamp): milky green-indigo opal nodules with a slow drifting inner glow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Aetite, Habit = CaveMineralHabit.Geode, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Aetite", Albedo = new Color(0.416f, 0.227f, 0.102f), Smoothness = 0.3f, EmitterHue = -1 },
				Description = "Aetite (SwampCave): eagle-stone: hollow limonite geodes, a powdery ochre hollow round a loose core." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.BollasEye, Habit = CaveMineralHabit.Nodules, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "BollasEye", Albedo = new Color(0.541f, 0.604f, 0.227f), Smoothness = 0.85f, Emission = new Color(1.082f, 1.118f, 0.794f), Light = new Color(0.941f, 0.973f, 0.69f), EmitterHue = 2 },
				Description = "BollasEye (fantasy, SwampCave): chatoyant honey-green ovoids with one slit of light across." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Hauyne, Habit = CaveMineralHabit.Dodecahedral, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Hauyne", Albedo = new Color(0.118f, 0.29f, 0.753f), Smoothness = 0.85f, EmitterHue = -1 },
				Description = "Hauyne (SwampRuins): sapphire-blue small dodecahedra in pale pumice." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Leucite, Habit = CaveMineralHabit.Trapezohedral, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Leucite", Albedo = new Color(0.925f, 0.91f, 0.878f), Smoothness = 0.7f, EmitterHue = -1 },
				Description = "Leucite (SwampRuins): white glassy trapezohedra in grey lava." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Saeculite, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Saeculite", Albedo = new Color(0.541f, 0.416f, 0.227f), Smoothness = 0.85f, Emission = new Color(1.046f, 0.902f, 0.577f), Light = new Color(0.91f, 0.784f, 0.502f), EmitterHue = 3 },
				Description = "Saeculite (fantasy, SwampRuins): the ages: a smoky-honey prism of ten bands, one lit a day." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.TrapicheEmerald, Habit = CaveMineralHabit.Trapiche, LightHue = -1,
				Hue = new CaveGlowHue { Name = "TrapicheEmerald", Albedo = new Color(0.118f, 0.478f, 0.227f), Smoothness = 0.85f, EmitterHue = -1 },
				Description = "TrapicheEmerald (SwampTemple): green beryl prisms whose sections show a six-spoked black star." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Mistberyl, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Mistberyl", Albedo = new Color(0.816f, 0.91f, 0.847f), Smoothness = 0.85f, Emission = new Color(0.974f, 1.118f, 1.01f), Light = new Color(0.847f, 0.973f, 0.878f), EmitterHue = 2 },
				Description = "Mistberyl (fantasy, SwampTemple): pale green-white translucent columns, brighter as the mist thickens." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Quicksilver, Habit = CaveMineralHabit.Rhomb, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Quicksilver", Albedo = new Color(0.784f, 0.8f, 0.824f), Smoothness = 0.95f, Metallic = 1f, EmitterHue = -1 },
				Description = "Quicksilver (TholinPlain): mercury frozen solid: mirror-bright rhombohedra." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Hazeamber, Habit = CaveMineralHabit.Botryoidal, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Hazeamber", Albedo = new Color(0.722f, 0.353f, 0.094f), Smoothness = 0.85f, Emission = new Color(1.082f, 0.722f, 0.325f), Light = new Color(0.941f, 0.627f, 0.282f), EmitterHue = 3 },
				Description = "Hazeamber (fantasy, TholinPlain): polymerised haze: translucent orange botryoids glowing warm at night." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Epsomite, Habit = CaveMineralHabit.Radiating, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Epsomite", Albedo = new Color(0.957f, 0.965f, 0.949f), Smoothness = 0.6f, EmitterHue = -1 },
				Description = "Epsomite (TidalFracture): clear needles with rust-stained roots." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Sakhrastone, Habit = CaveMineralHabit.Tabular, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Sakhrastone", Albedo = new Color(0.416f, 0.055f, 0.11f), Smoothness = 0.88f, Emission = new Color(0.902f, 0.18f, 0.271f), Light = new Color(0.784f, 0.157f, 0.235f), EmitterHue = 3 },
				Description = "Sakhrastone (fantasy, TidalFracture): dust of the Rock: deep ruby tabular plates glowing cold ruby." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Labradorite, Habit = CaveMineralHabit.Blades, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Labradorite", Albedo = new Color(0.165f, 0.227f, 0.29f), Smoothness = 0.85f, EmitterHue = -1 },
				Description = "Labradorite (Tundra): dark grey blades with a blue-green-gold schiller flash." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Aurorite, Habit = CaveMineralHabit.Shards, Lit = true, LightHue = 2,
				Hue = new CaveGlowHue { Name = "Aurorite", Albedo = new Color(0.627f, 0.91f, 0.753f), Smoothness = 0.9f, Emission = new Color(0.361f, 1.082f, 0.722f), Light = new Color(0.314f, 0.941f, 0.627f), EmitterHue = 2 },
				Description = "Aurorite (fantasy, Tundra): glassy shards glowing a slow green-to-violet shift." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Ikaite, Habit = CaveMineralHabit.Stellate, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Ikaite", Albedo = new Color(0.941f, 0.925f, 0.878f), Smoothness = 0.5f, EmitterHue = -1 },
				Description = "Ikaite (UnderwaterCanyon): cold-seafloor carbonate: chalky-white spiky stellate clusters (its own look: brown-grey glendonite pseudomorphs)." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Tremorspar, Habit = CaveMineralHabit.Blades, Lit = true, LightHue = 0,
				Hue = new CaveGlowHue { Name = "Tremorspar", Albedo = new Color(0.541f, 0.478f, 0.604f), Smoothness = 0.8f, Emission = new Color(0.902f, 0.83f, 1.01f), Light = new Color(0.784f, 0.722f, 0.878f), EmitterHue = 0 },
				Description = "Tremorspar (fantasy, UnderwaterCanyon): smoky-lavender porcelain blades flaring with the canyon's tremors." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Demantoid, Habit = CaveMineralHabit.Dodecahedral, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Demantoid", Albedo = new Color(0.227f, 0.627f, 0.251f), Smoothness = 0.92f, EmitterHue = -1 },
				Description = "Demantoid (Valley): vivid emerald-green garnet dodecahedra in serpentine, with rainbow fire." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Sphene, Habit = CaveMineralHabit.Twinned, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Sphene", Albedo = new Color(0.659f, 0.627f, 0.251f), Smoothness = 0.9f, EmitterHue = -1 },
				Description = "Sphene (Valley): titanite: flat wedge envelope twins, honey-green, adamantine." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Laurinite, Habit = CaveMineralHabit.Rosette, Lit = true, LightHue = 0,
				Hue = new CaveGlowHue { Name = "Laurinite", Albedo = new Color(0.91f, 0.541f, 0.659f), Smoothness = 0.86f, Emission = new Color(1.15f, 0.722f, 0.866f), Light = new Color(1f, 0.627f, 0.753f), EmitterHue = 0 },
				Description = "Laurinite (fantasy, Valley): the rose garden's roses: rose-pink crystal rosettes of curved petals with a soft rose glow." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.SulphurNeedles, Formation = true, FormationRock = RockTypes.Sulphur.Name, FormationShape = "Crystals", LightHue = -1,
				Description = "SulphurNeedles (Volcanic Cave): lemon sulphur needles in vugs, the sulphur Crystals formation the rock art already makes (no art of its own, no light)." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Smokestone, Habit = CaveMineralHabit.Shards, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Smokestone", Albedo = new Color(0.078f, 0.071f, 0.086f), Smoothness = 0.97f, EmitterHue = -1 },
				Description = "Smokestone (VolcanicCave): obsidian mirror-shards under whose gloss a slow smoke curls." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Hearthglass, Habit = CaveMineralHabit.Shards, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Hearthglass", Albedo = new Color(0.227f, 0.063f, 0.031f), Smoothness = 0.92f, Emission = new Color(1.15f, 0.433f, 0.144f), Light = new Color(1f, 0.376f, 0.125f), EmitterHue = 3 },
				Description = "Hearthglass (fantasy, Volcanic): ember-red glass shards with a molten core." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Hieratite, Habit = CaveMineralHabit.Octahedral, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Hieratite", Albedo = new Color(0.925f, 0.918f, 0.894f), Smoothness = 0.7f, EmitterHue = -1 },
				Description = "Hieratite (VolcanicTemple): white-to-grey fluosilicate crusts and tiny octahedra with a faint lilac cast." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Forgeheart, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Forgeheart", Albedo = new Color(0.784f, 0.416f, 0.102f), Smoothness = 0.86f, Emission = new Color(1.15f, 0.622f, 0.216f), Light = new Color(1f, 0.541f, 0.188f), EmitterHue = 3 },
				Description = "Forgeheart (fantasy, VolcanicTemple): ember-orange prisms with an anvil-dark core." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Goethite, Habit = CaveMineralHabit.Botryoidal, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Goethite", Albedo = new Color(0.102f, 0.086f, 0.071f), Smoothness = 0.85f, Metallic = 0.4f, EmitterHue = -1 },
				Description = "Goethite (Wasteland): black botryoidal iron crust with an iridescent skin." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Weepglass, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 3,
				Hue = new CaveGlowHue { Name = "Weepglass", Albedo = new Color(0.29f, 0.251f, 0.22f), Smoothness = 0.9f, Emission = new Color(1.15f, 0.289f, 0.216f), Light = new Color(1f, 0.251f, 0.188f), EmitterHue = 3 },
				Description = "Weepglass (fantasy, Wasteland): smoky quartz points with a red drop of light at the tip after sunset." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Carnelian, Habit = CaveMineralHabit.Nodules, LightHue = -1,
				Hue = new CaveGlowHue { Name = "Carnelian", Albedo = new Color(0.784f, 0.314f, 0.118f), Smoothness = 0.75f, EmitterHue = -1 },
				Description = "Carnelian (Wetlands): translucent orange-red chalcedony nodules and banded geode halves." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.Abzulite, Habit = CaveMineralHabit.Prisms, Lit = true, LightHue = 1,
				Hue = new CaveGlowHue { Name = "Abzulite", Albedo = new Color(0.165f, 0.541f, 0.784f), Smoothness = 0.9f, Emission = new Color(0.478f, 0.938f, 1.01f), Light = new Color(0.416f, 0.816f, 0.878f), EmitterHue = 1 },
				Description = "Abzulite (fantasy, Wetlands): deep sweet-water-blue prisms with a slow inner tide." },
			new CaveMineral { Kind = BiomeCaveCrystalKind.FlintGeode, Habit = CaveMineralHabit.Geode, LightHue = -1,
				Hue = new CaveGlowHue { Name = "FlintGeode", Albedo = new Color(0.784f, 0.816f, 0.878f), Smoothness = 0.8f, EmitterHue = -1 },
				Description = "FlintGeode (Woodland): hollow flint lined with chalcedony and drusy quartz." },
		};

		/// <summary>The catalogue's entry for a kind (a kind past the table is selenite's).</summary>
		public static CaveMineral CaveMineralOf(BiomeCaveCrystalKind kind) => (int)kind < CaveMinerals.Length ? CaveMinerals[(int)kind] : CaveMinerals[0];

		/// <summary>The habits the catalogue's own-built kinds and looks use, each once, in habit order.</summary>
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
			foreach (CaveMineralLook look in CaveMineralLooks)
			{
				used.Add(look.Habit);
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

		/// <summary>
		/// The biomes' own looks of the kinds several drafts share (2026-10-26, NEW-CRYSTALS; decisions 12, 97), numbered within their
		/// kind in append order. Twenty looks over twelve kinds: kept to the drafts that name one, as each costs a material and
		/// <see cref="CaveMineralVariants"/> prefabs (meshes are the habit's, shared).
		/// </summary>
		public static readonly CaveMineralLook[] CaveMineralLooks =
		{
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Obsidian, Look = 1, Name = "Badlands", Biome = "Badlands", Habit = CaveMineralHabit.Shards, Albedo = new Color(0.165f, 0.078f, 0.063f), Smoothness = 0.95f,
				Description = "Obsidian Badlands: black with mahogany bands." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Obsidian, Look = 2, Name = "IrisGlass", Biome = "Permanent Ice", Habit = CaveMineralHabit.Nodules, Albedo = new Color(0.078f, 0.071f, 0.094f), Smoothness = 0.96f,
				Description = "Obsidian IrisGlass: iris glass: black nodules whose sheen bands flash green, gold and violet." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Obsidian, Look = 3, Name = "Itzsheen", Biome = "Volcanic Cave", Habit = CaveMineralHabit.Blades, Albedo = new Color(0.118f, 0.165f, 0.102f), Smoothness = 0.96f,
				Description = "Obsidian Itzsheen: translucent bottle-green blades with a gold sheen at grazing light." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Obsidian, Look = 4, Name = "Rossa", Biome = "Volcanic Temple", Habit = CaveMineralHabit.Shards, Albedo = new Color(0.353f, 0.102f, 0.063f), Smoothness = 0.95f,
				Description = "Obsidian Rossa: red-brown obsidian shards." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Aragonite, Look = 1, Name = "FlosFerri", Biome = "Farmland", Habit = CaveMineralHabit.Coralloid, Albedo = new Color(0.941f, 0.91f, 0.847f), Smoothness = 0.6f,
				Description = "Aragonite FlosFerri: flos-ferri: white-honey coralloid sprays." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.SalAmmoniac, Look = 1, Name = "SulphurFlats", Biome = "Sulphur Flats", Habit = CaveMineralHabit.Dendrite, Albedo = new Color(0.957f, 0.941f, 0.894f), Smoothness = 0.6f,
				Description = "SalAmmoniac SulphurFlats: white skeletal feathery fronds." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Dogtooth, Look = 1, Name = "Grassland", Biome = "Grassland", Habit = CaveMineralHabit.Geode, Albedo = new Color(0.91f, 0.863f, 0.753f), Smoothness = 0.88f,
				Description = "Dogtooth Grassland: pale honey scalenohedra in loess-doll geodes." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Vivianite, Look = 1, Name = "PeatBog", Biome = "Peat Bog", Habit = CaveMineralHabit.Blades, Albedo = new Color(0.118f, 0.29f, 0.353f), Smoothness = 0.86f,
				Description = "Vivianite PeatBog: deep blue-green blade crystals round buried bone and iron." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.ClearQuartz, Look = 1, Name = "Hills", Biome = "Hills", Habit = CaveMineralHabit.Druse, Albedo = new Color(0.878f, 0.863f, 0.816f), Smoothness = 0.86f, Metallic = 0.1f,
				Description = "ClearQuartz Hills: milky vein quartz with gold flecks." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.ClearQuartz, Look = 2, Name = "Crystallus", Biome = "Ice Ruin", Habit = CaveMineralHabit.Prisms, Albedo = new Color(0.894f, 0.918f, 0.941f), Smoothness = 0.9f,
				Description = "ClearQuartz Crystallus: crystallus: prisms frosted milky at the base, clear above, bubble trails up their length." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.ClearQuartz, Look = 3, Name = "BlueQuartz", Biome = "Ice Sheet", Habit = CaveMineralHabit.Prisms, Albedo = new Color(0.541f, 0.604f, 0.69f), Smoothness = 0.9f,
				Description = "ClearQuartz BlueQuartz: opalescent blue-grey prisms in charnockite." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.FrostCrystal, Look = 1, Name = "HoarChandelier", Biome = "Ice Shelf", Habit = CaveMineralHabit.Cupped, Albedo = new Color(0.91f, 0.941f, 0.973f), Smoothness = 0.85f,
				Description = "FrostCrystal HoarChandelier: depth-hoar cups in clusters." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Thenardite, Look = 1, Name = "Volcanic", Biome = "Volcanic", Habit = CaveMineralHabit.Fibrous, Albedo = new Color(0.941f, 0.933f, 0.902f), Smoothness = 0.45f,
				Description = "Thenardite Volcanic: white hair-needles and crusts on tube walls." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Selenite, Look = 1, Name = "Swallowtail", Biome = "Marble Palace", Habit = CaveMineralHabit.Twinned, Albedo = new Color(0.957f, 0.949f, 0.918f), Smoothness = 0.9f,
				Description = "Selenite Swallowtail: Messinian twinned selenite: water-clear fishtail blades." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Selenite, Look = 2, Name = "Swordblade", Biome = "Volcanic Cave", Habit = CaveMineralHabit.Blades, Albedo = new Color(0.933f, 0.941f, 0.925f), Smoothness = 0.9f,
				Description = "Selenite Swordblade: knife-thin clear blades in fans." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Selenite, Look = 3, Name = "Wetlands", Biome = "Wetlands", Habit = CaveMineralHabit.Twinned, Albedo = new Color(0.933f, 0.949f, 0.933f), Smoothness = 0.88f,
				Description = "Selenite Wetlands: water-clear swallowtail twins in gypsum walls." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Amethyst, Look = 1, Name = "MountainSlope", Biome = "Mountain Slope", Habit = CaveMineralHabit.Geode, Albedo = new Color(0.416f, 0.29f, 0.604f), Smoothness = 0.86f,
				Description = "Amethyst MountainSlope: deep violet points in basalt geodes." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Amethyst, Look = 2, Name = "Eonyang", Biome = "Scree", Habit = CaveMineralHabit.Druse, Albedo = new Color(0.784f, 0.69f, 0.878f), Smoothness = 0.86f,
				Description = "Amethyst Eonyang: pale lilac points in granodiorite pockets." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Pyrite, Look = 1, Name = "Swamp", Biome = "Swamp", Habit = CaveMineralHabit.Radiating, Albedo = new Color(0.722f, 0.596f, 0.22f), Smoothness = 0.82f, Metallic = 0.85f,
				Description = "Pyrite Swamp: pyrite suns: brassy radiating discs in black shale." },
			new CaveMineralLook { Kind = BiomeCaveCrystalKind.Ikaite, Look = 1, Name = "Glendonite", Biome = null, Habit = CaveMineralHabit.Stellate, Albedo = new Color(0.416f, 0.369f, 0.314f), Smoothness = 0.3f,
				Description = "Ikaite Glendonite: glendonite: brown-grey opaque pseudomorphs after ikaite." },
		};

		/// <summary>A kind's look by its number; false for 0 (the kind's own) or a number the kind has not.</summary>
		public static bool TryCaveMineralLook(BiomeCaveCrystalKind kind, byte look, out CaveMineralLook found)
		{
			if (look != 0)
			{
				foreach (CaveMineralLook l in CaveMineralLooks)
				{
					if (l.Kind == kind && l.Look == look)
					{
						found = l;
						return true;
					}
				}
			}
			found = default;
			return false;
		}

		/// <summary>The look a biome's spec grows a kind in: the look named for that biome, else 0 (the kind's own).</summary>
		public static byte CaveMineralLookFor(BiomeCaveCrystalKind kind, string biome)
		{
			foreach (CaveMineralLook l in CaveMineralLooks)
			{
				if (l.Kind == kind && l.Biome != null && l.Biome == biome)
				{
					return l.Look;
				}
			}
			return 0;
		}

		/// <summary>A look's colours: its kind's hue (glow, light) in the look's albedo, smoothness and metal, named after the look.</summary>
		public static CaveGlowHue CaveMineralLookHue(in CaveMineralLook look)
		{
			CaveGlowHue hue = CaveMineralOf(look.Kind).Hue;
			hue.Name = look.Kind + "_" + look.Name;
			hue.Albedo = look.Albedo;
			hue.Smoothness = look.Smoothness;
			hue.Metallic = look.Metallic;
			if (CaveMineralOf(look.Kind).Formation)
			{
				hue.EmitterHue = -1;
			}
			return hue;
		}

		/// <summary>A look's prefab, with its sub-folder: <c>Cave/Cave_Mineral_Obsidian_Rossa_1</c>.</summary>
		public static string CaveMineralLookPrefab(in CaveMineralLook look, int variant) => $"{CavePrefabFolderName}/{CavePrefix}Mineral_{look.Kind}_{look.Name}_{variant}";

		/// <summary>A look's material: <c>Cave_Mineral_Obsidian_Rossa</c>.</summary>
		public static string CaveMineralLookMaterial(in CaveMineralLook look) => $"{CavePrefix}Mineral_{look.Kind}_{look.Name}";

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

		/// <summary>Every cave glow payload file: the look array holding its three maps (ProceduralArtLookArrays), and three meshes a variant of each shape.</summary>
		public static IEnumerable<string> CaveGlowPayloadPaths()
		{
			foreach (ProceduralArtLookArrays.Layout layout in ProceduralArtLookArrays.LayoutsOfSet(ProceduralArtLookArrays.CaveGlowSetName))
			{
				yield return layout.Path;
			}
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
			foreach (CaveMineralLook look in CaveMineralLooks)
			{
				yield return MaterialPath(CaveMineralLookMaterial(in look));
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
			foreach (CaveMineralLook look in CaveMineralLooks)
			{
				for (int v = 0; v < CaveMineralVariants; v++)
				{
					yield return CaveMineralLookPrefab(in look, v);
				}
			}
		}
	}
}
#endif
