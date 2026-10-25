using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// A biome. The asset is the identity — its cached-object ID is what references, biome maps
	/// and race affinities carry — and everything the biome means is data on it: the elevation
	/// tier and climate envelope it is chosen for during generation, the colour that identifies
	/// it on a biome map, the terrain textures and spawn rules that paint it, the climate variants
	/// it can be experienced under, and the naming data dungeons and points of interest are named
	/// from. Registers with <see cref="BiomeRegistry"/> when loaded.
	///
	/// <para>Nothing here restricts where a biome may be used. A cave biome and a grassland are
	/// the same kind of asset; a biome that should never be picked by climate simply has a
	/// <see cref="SelectionWeight"/> of zero and is placed by hand.</para>
	/// </summary>
	[CreateAssetMenu(fileName = "New Biome", menuName = "FishMMO/Biomes/Biome", order = 1)]
	public class BiomeTemplate : CachedScriptableObject<BiomeTemplate>, ICachedObject
	{
		[Header("Identity")]
		[Tooltip("Shown in tools and generated names, e.g. 'Alpine Meadow'. Defaults to the asset name.")]
		public string DisplayName;
		[TextArea]
		public string Description;
		[Tooltip("Colour that identifies this biome on a biome map and in the terrain painter.")]
		public Color BiomeColorId = Color.clear;
		[Tooltip("Colour used for scene gizmos.")]
		public Color GizmoColor = Color.white;

		[Header("Elevation")]
		[Tooltip("Elevation tier this biome belongs to: 0 deep ocean … 8 nival, 9 for biomes that can appear at any elevation.")]
		[Range(0, 9)] public int ElevationTier = 4;
		[Tooltip("Normalised height band the biome occupies (0 sea floor, 1 highest peak).")]
		[Range(0f, 1f)] public float MinHeight = 0f;
		[Range(0f, 1f)] public float MaxHeight = 1f;

		[Header("Climate envelope — where generation chooses this biome")]
		[Range(-1f, 1f)] public float MinTemperature = -1f;
		[Range(-1f, 1f)] public float MaxTemperature = 1f;
		[Range(-1f, 1f)] public float MinHumidity = -1f;
		[Range(-1f, 1f)] public float MaxHumidity = 1f;
		[Tooltip("The warmest season this biome stands, on the temperature scale (0 freezing, 0.30 is 10 °C). Köppen's lines: anything with trees needs a summer of 10 °C or more, tundra and bog anything above freezing, an ice sheet a summer that never thaws. Read only where the body's seasons are known (a placed scene); -1 to 1 asks nothing.")]
		[Range(-1f, 1f)] public float MinWarmestSeason = -1f;
		[Range(-1f, 1f)] public float MaxWarmestSeason = 1f;
		[Tooltip("Relative chance among biomes whose envelopes fit. 0 = never chosen by climate; placed by hand only.")]
		[Min(0f)] public float SelectionWeight = 1f;

		[Header("Where it can exist at all")]
		/// <remarks>
		/// <para>
		/// The temperature and humidity envelope above says what climate a biome LIKES. These two
		/// say what it physically requires, and they are not the same question: the temperature on
		/// an airless moon at the right distance from its star is perfectly temperate, and a jungle
		/// there is still absurd.
		/// </para>
		/// <para>
		/// Kept as requirements on the biome rather than as a list of allowed biomes on each world.
		/// A world states its own conditions once; every biome states its own needs once; the
		/// resolver matches them. A list per world would be one entry per biome per body to
		/// maintain, and every biome added later would have to be threaded back through every world
		/// that ought to have it.
		/// </para>
		/// </remarks>
		[Tooltip("Air this biome needs. Vacuum-tolerant biomes (regolith, impact basin) leave every box ticked.")]
		public BiomeAtmosphereRequirement Atmosphere = BiomeAtmosphereRequirement.Any;

		[Tooltip("Needs liquid water on the surface. Off for deserts, regolith and anything cryogenic — a methane lake is not water.")]
		public bool RequiresLiquidWater;

		/// <remarks>
		/// <para>
		/// What the biome needs of the WORLD, which neither the climate envelope nor the two fields
		/// above can say: an ice moon's vents and a polar sea read the same temperature and the same
		/// humidity, and only "is this world's water frozen through and heated from below" tells
		/// them apart. Every flag is a predicate on <see cref="BiomeWorldConditions"/> derived from
		/// the body itself — see <see cref="BiomeWorldRequirement"/> for what each one tests and why.
		/// </para>
		/// <para>
		/// None, the default, asks nothing. An Earth biome never needs a flag; an alien one carries
		/// the flags that make it alien, and is then never chosen on a world that is not.
		/// </para>
		/// </remarks>
		[Tooltip("What this biome needs of the world itself, every flag ticked must hold: an ice world, cryovolcanism, tidal heating, a methane sky, no liquid water anywhere... Nothing for an Earth biome.")]
		public BiomeWorldRequirement Requires = BiomeWorldRequirement.None;

		[Header("Climate variants")]
		[Tooltip("How this biome reads under different climates. Empty uses the scene's default variants.")]
		public List<BiomeClimateVariant> ClimateVariants = new List<BiomeClimateVariant>();

		[Header("Naming")]
		public BiomeNamingData Naming = new BiomeNamingData();

		[Header("Ground, as the air meets it")]
		[Tooltip("What the wind can lift off this ground once it blows hard enough: sand, dust, regolith. None for rock, soil, vegetation, ice and water. How hard it has to blow comes from the grain and the world's own gravity and air.")]
		public WeatherSubstance LooseGround;
		[Tooltip("What this ground puts into the air by itself — a volcano's ash, a geyser field's ice. None for nearly everything.")]
		public WeatherSubstance Emits;
		[Tooltip("How hard it does, 0..1: a steady trickle at a tenth, a vent that never stops at one. An eruption multiplies it.")]
		[Range(0f, 1f)] public float EmissionRate;
		[Tooltip("When the ground steams (it emits a vapour such as Steam): how its hot ground divides among geysers, hot springs, fumaroles and mud pots (shares, divided by their sum). Read by the server, every client and the cut alike. Default: a quarter geysers, two fifths springs, the rest fumaroles, no mud pots, as every steaming biome was before 2026-10-18. Written by Author biome art; edits are kept.")]
		[SerializeField] private BiomeGeothermalShares geothermal = BiomeGeothermalShares.Today;

		[Header("How the ground wears")]
		[Tooltip("How erosion, drainage and plateaus shape this biome's ground in a generated scene. Empty wears like temperate soil.")]
		public TerrainProcessProfile TerrainProcess;
		[Tooltip("The landforms sculpted into this biome's ground at the cut (2026-10-18): pingos, tells, craters, dune fields, creek networks, moraines… each a shape family with its density, sizes, conditions and the ground layer it paints, by name from the landform presets. Empty: the ground is carved as before. Written by Author biome art; edits are kept.")]
		[SerializeField] private List<BiomeLandform> landforms = new List<BiomeLandform>();

		/// <remarks>
		/// <para>
		/// What the biome's living things look like (BiomeLooks): its look class is the theme its grass, flowers, small plants,
		/// fungi, stones and critters are coloured from, which the planet it lies on then moves (star, air, warmth); the rare
		/// looks are its two touches of the fantastic, each a patch here and there. Until 2026-10-15 the class was read from
		/// the biome's NAME by keywords (BiomeLooks.ClassOf); the biome art authoring now writes it from the spec table, whose
		/// classes equal what the keywords gave every biome, and a template not authored yet falls back on its name with a
		/// warning.
		/// </para>
		/// <para>
		/// The rare looks are a plain choice where the biome wants one: Auto leaves each to the biome's hash, as it always was
		/// (an enchanted wood may want Faerie and Moonsilver; a cursed fen Witchmoss and Bloodleaf).
		/// </para>
		/// </remarks>
		[Header("Living Looks")]
		[Tooltip("The theme this biome's grass, flowers, small plants, fungi, stones and critters are coloured from (the planet then moves it). Unset: not authored yet (read from the biome's name, with a warning). Written by Author biome art.")]
		[SerializeField] private BiomeLookClass lookClass = BiomeLookClass.Unset;
		[Tooltip("The first of the biome's two rare fantastic looks (about one patch in ten). Auto: chosen by the biome's hash, as before.")]
		[SerializeField] private BiomeRareLook rareLookA = BiomeRareLook.Auto;
		[Tooltip("The second rare fantastic look (about one patch in sixteen). Auto: chosen by the biome's hash, never the same as the first.")]
		[SerializeField] private BiomeRareLook rareLookB = BiomeRareLook.Auto;

		/// <remarks>
		/// How each blade prefab this biome's rules grow (Detail_Grass…, Detail_Reeds…, Detail_Flowers…) looks as procedural
		/// blades HERE: shape, heads, colours and how much of the ground's colour it takes (<see cref="BiomeGrassType"/>). The
		/// blade renderer builds its per-scene type table from the entries of the biomes in the scene, so one prefab can look
		/// different in two biomes. Filled by the biome art authoring with the look every biome's blades had before
		/// 2026-10-15 (BiomeGrassDefaults); entries edited here are kept. A blade prefab with no entry draws with that old
		/// look, with a warning.
		/// </remarks>
		[Header("Blade Grass, Flowers and Reeds")]
		[Tooltip("How each blade prefab this biome grows looks here, one entry per prefab. Written by Author biome art; edits are kept.")]
		[SerializeField] private List<BiomeGrassType> grassTypes = new List<BiomeGrassType>();

		[Header("Main Texture Layer")]
		[Tooltip("Primary base texture that covers the majority of the biome.")]
		[SerializeField] private TerrainTextureLayer mainTextureLayer = new TerrainTextureLayer();

		[Header("Detail Texture Layers")]
		[Tooltip("Additional textures blended with the main texture for variation.")]
		[SerializeField] private List<TerrainTextureLayer> detailTextureLayers = new List<TerrainTextureLayer>();

		/// <remarks>
		/// What the scene's ways are surfaced with through this biome, drawn by the path overlay over whatever ground the
		/// pixel is painted with and never painted into the splat: Road for carts and roads, Small Path for footpaths and
		/// trails, Cobbles for the paved stretches (a town's streets, a bridge's approach). Filled by the biome art authoring
		/// with the generated path grounds the spec table names for the biome (by default what its main ground wears to:
		/// loam through grass, a littered track through forest, packed sand through desert); a slot holding other art is kept.
		/// </remarks>
		[Header("Road and Path Layers")]
		[Tooltip("The ground a road or cart track through this biome wears to.")]
		[SerializeField] private TerrainTextureLayer roadTextureLayer = new TerrainTextureLayer();
		[Tooltip("The ground a footpath or trail through this biome wears to.")]
		[SerializeField] private TerrainTextureLayer smallPathTextureLayer = new TerrainTextureLayer();
		[Tooltip("The paving of a way's cobbled stretches through this biome: a settlement's streets, a bridge's approach.")]
		[SerializeField] private TerrainTextureLayer cobbleTextureLayer = new TerrainTextureLayer();

		[Header("Cliff Texture Layers")]
		[Tooltip("Specialized textures for cliff faces and steep slopes.")]
		[SerializeField] private List<CliffTextureLayer> cliffTextureLayers = new List<CliffTextureLayer>();

		[Header("Riverbed Texture Layer")]
		[Tooltip("Texture for riverbeds and water-adjacent areas.")]
		[SerializeField] private TerrainTextureLayer riverbedTextureLayer = new TerrainTextureLayer();

		/// <remarks>
		/// What a river lays on its bars and beaches where it runs through this biome: sand where its water was slow, gravel
		/// where it was quick. A bar across two biomes takes each one's in proportion to its weight there, as the rest of the
		/// ground does. Filled by the biome art authoring (by default the generated Sand and Gravel every biome's bars once
		/// borrowed from the desert's and the forest's riverbeds); a slot holding other art is kept.
		/// </remarks>
		[Header("River Sediment Layers")]
		[Tooltip("The sand a river lays on its slow bars and beaches in this biome.")]
		[SerializeField] private TerrainTextureLayer barSandTextureLayer = new TerrainTextureLayer();
		[Tooltip("The gravel a river lays on its quick bars in this biome.")]
		[SerializeField] private TerrainTextureLayer barGravelTextureLayer = new TerrainTextureLayer();

		[Header("Lakebed Texture Layer")]
		[Tooltip("Texture for lakebeds and water body floors.")]
		[SerializeField] private TerrainTextureLayer lakebedTextureLayer = new TerrainTextureLayer();

		/// <remarks>
		/// Decision 35 (b), 2026-10-18: up to <see cref="MaximumBedLayers"/> more beds, each painted under the waters it names by its
		/// depth below THAT water's own surface (and in a river by its current): a lake's shelf, slope and deeps, a river's riffles,
		/// pools and reefs (<see cref="BedTextureLayer"/>). What they leave under a river is the Riverbed layer's, under a lake or the
		/// sea the Lakebed's. Empty for every biome until its round adds some (filled by the biome art authoring), so a scene is
		/// painted as before.
		/// </remarks>
		[Header("Bed Layers by Depth")]
		[Tooltip("Up to three more bed grounds, each under the waters it names and within its band of depth below that water's own surface (and of a river's current). What they leave is the Riverbed's under a river, the Lakebed's under a lake or the sea.")]
		[SerializeField] private List<BedTextureLayer> bedTextureLayers = new List<BedTextureLayer>();

		/// <summary>The most bed layers a biome may have (<see cref="BedTextureLayers"/>); the cut paints no more than these.</summary>
		public const int MaximumBedLayers = 3;

		/// <remarks>
		/// <para>
		/// The rock a scene cut stands in this biome: its cliffs (each cliff layer names its own, <see cref="CliffTextureLayer.rockType"/>),
		/// the boulders in its rivers, the ledges of its falls and the stone a point of interest is shaped from. The planet's
		/// geology is the input: where the rock under the scene is one the biome accepts, it is that rock, so a granite
		/// country's grassland stands in granite and a desert's canyon in its sandstone; anywhere else it is the biome's own,
		/// so a bog on a sandstone province is not walled in sandstone (2026-10-08). Ice stays ice.
		/// </para>
		/// <para>
		/// Names are <c>RockTypes</c> names (Granite, Basalt, Sandstone…) or <c>Ice</c>. Filled by the biome art authoring
		/// from the spec table; values typed here are kept.
		/// </para>
		/// </remarks>
		[Header("Rock")]
		[Tooltip("This biome's own rock: what its river boulders, fall ledges and point-of-interest stone are made of where the planet's geology is not one it accepts (Granite, Basalt, Sandstone, Limestone… or Ice). Empty: not authored yet (the cut falls back to the old mapping and warns).")]
		[SerializeField] private string ownRock;
		[Tooltip("The planet-geology rocks this biome's cliffs, river boulders and fall ledges may be made of besides its own: where the rock under the scene is one of these it is that rock, anywhere else the biome's own. Empty accepts only its own.")]
		[SerializeField] private List<string> acceptedRocks = new List<string>();

		/// <remarks>
		/// The rock a cave carved in this biome is cut in, by the cave's form: its walls, roof and floor, and with them its
		/// formations (dripstone, fallen blocks, spoil), which are the shell's own rock (2026-10-16). Until then the cave shaper
		/// decided the shell by form (basalt for a lava tube, glacier ice for an ice cave, the ground's own rock for every other
		/// form) and the dressing gave the formations a rock of their own by form (limestone, basalt, quartzite, water ice),
		/// whatever the shell was. The biome art authoring writes the shaper's rule here for every form
		/// (<see cref="BiomeCaveShell.Ground"/> where it was the ground's rock), so a re-cut cuts the same shells; a biome
		/// round can give a form its own (a desert's lava tubes in obsidian). A form with no entry keeps the old rule.
		/// </remarks>
		[Tooltip("The rock each form of cave carved in this biome is cut in, its formations included: Ground (the rock the ground is made of there, by Own Rock and Accepted Rocks), a rock type name (Basalt, Limestone…), or Ice. A form with no entry keeps the old rule (lava tube Basalt, ice cave Ice, else Ground). Written by Author biome art; edits are kept.")]
		[SerializeField] private List<BiomeCaveShell> caveShells = new List<BiomeCaveShell>();

		/// <remarks>
		/// The crystals this biome's caves grow (<see cref="BiomeCaveCrystal"/>, 2026-10-16, I6c): the clusters in their chambers,
		/// a kind drawn by weight for each, and, where a kind glows by itself (amberlight, wyrdstone, sunstone), the glow lining
		/// their ways and the crystal on a crystal way-light post. Until then every cave and grotto grew selenite wherever it
		/// was; the biome art authoring writes each biome that (its file may name its own). An ice cave's crystals are its ice and
		/// a lava tube grows none, whatever this list says.
		/// </remarks>
		[Tooltip("The crystals this biome's caves and grottos grow in their chambers, each with how often (selenite, amethyst, clear or smoky quartz, green fluorite, frost crystal, obsidian shards, glowing amberlight, wyrdstone, sunstone). The glowing kinds also line the caves' ways and crown its crystal way-light posts. Empty: none grow. Written by Author biome art (selenite); edits are kept.")]
		[SerializeField] private List<BiomeCaveCrystal> caveCrystals = new List<BiomeCaveCrystal>();

		/// <remarks>
		/// The cave biome dressing the inside of this biome's caves of each form (<see cref="BiomeCaveInterior"/>, 2026-10-25, phase
		/// I2n, decisions 37, 46, 51, 57): its formations, crystals, glow, lights, floor grounds, cover and animals; the mouth, the
		/// shell's rock and every mine stay this biome's. A link holds where the cave's shell is one of the interior's
		/// <see cref="CaveInteriorRocks"/>. Until then every cave was dressed from the biome it opened in.
		/// </remarks>
		[Header("Cave Interiors")]
		[Tooltip("The cave biome whose template dresses the inside of this biome's caves of each form (Cave, Ice Cave, Lava Tube…): formations, crystals, glow, lights, floor grounds, cover and animals. The mouth, the shell rock and mines stay this biome's. A link holds only where the shell is one of the interior's Cave Interior Rocks. A form not listed is dressed from this biome. Written by Author biome art (Cave for natural caves, grottos and sea caves, Ice Cave for ice caves, Lava Tube for lava tubes); edits are kept.")]
		[SerializeField] private List<BiomeCaveInterior> caveInteriors = new List<BiomeCaveInterior>();
		[Tooltip("As a cave interior: the shell rocks (rock type names, or Ice) another biome's cave must be cut in for this biome to dress its inside (Cave: the soluble rocks; Ice Cave: ice and the rock ice caves' rocks; Lava Tube: basalt). Empty: any rock. Written by Author biome art; edits are kept.")]
		[SerializeField] private List<string> caveInteriorRocks = new List<string>();
		[Tooltip("The formations this biome's caves of each form take in each place (floor, roof, wall, pools, fallen rock, own crystal): a shape drawn in the cave's own rock, a weight among the place's others and the worlds it forms on. A form not listed takes the dressing's own set. Written by Author biome art (one shape a place, as the dressing chose by form); edits are kept.")]
		[SerializeField] private List<BiomeCaveFormation> caveFormations = new List<BiomeCaveFormation>();
		[Tooltip("What lights this biome's caves of each form by itself: the glow crystals' hue (empty: drawn by chance), how much of the floor's glow is luminous fungus and how often glow-worms hang (1 the dressing's own). Written by Author biome art (ice caves ice-blue, lava tubes amber, neither with fungus or glow-worms; mines fungus only; the rest by chance with both); edits are kept.")]
		[SerializeField] private List<BiomeCaveGlow> caveGlows = new List<BiomeCaveGlow>();

		/// <remarks>
		/// The part this biome plays for a scene's water and ice: the biome painted where a lake, a river, an estuary or a dry
		/// lake bed lies, and the ground of the ice sheet (<see cref="BiomeWaterRole"/>). Until 2026-10-15 the cut found those
		/// biomes by their names; the biome art authoring writes each its role, and any biome given a role is used for it.
		/// </remarks>
		[Header("Water and Ice")]
		[Tooltip("The part this biome plays for the scene's water and ice: Lake, Methane Lake, River, Estuary, Salt Flat (a dry lake bed) or Ice Sheet. None for a biome chosen by climate alone. Written by Author biome art; edits are kept.")]
		[SerializeField] private BiomeWaterRole waterRole = BiomeWaterRole.None;
		/// <remarks>
		/// The colour and clarity of the biome's fresh water (<see cref="BiomeWaterLook"/>, 2026-10-16): the lakes, pools,
		/// rivers, streams and moats whose shores lie in it take it (<see cref="BiomeWaterLooks"/>). Until then every body of
		/// inland water took the inland water material's one look, which is this field's default and what the authoring writes.
		/// </remarks>
		[Tooltip("The colour and clarity of this biome's fresh water: the lakes, pools, rivers, streams and moats whose shores lie in it (peat-black in a bog, milky under a glacier). Colours as the inland water material's; absorption per metre. Written by Author biome art (the inland water's own look); edits are kept.")]
		[SerializeField] private BiomeWaterLook waterLook = BiomeWaterLooks.Today;
		/// <remarks>
		/// The sea as a diver sees it off this biome's coast (<see cref="BiomeSeaLook"/>, 2026-10-18): the underwater pass blends
		/// the looks of the biomes within <see cref="BiomeSeaLooks.ReachMetres"/> of the eye (<see cref="BiomeSeaLooks.Coast"/>).
		/// Until then every sea took its scene's one underwater look, which is this field's default and what the authoring
		/// writes; a biome with that look leaves the sea its scene's own.
		/// </remarks>
		[Tooltip("The sea seen from under its surface off this biome's coast and over its own waters (peat-brown off a bog, milky off a glacier, clear over a reef): colour, visibility and murk as the underwater pass's. Blended along the coast out to 160 m; a biome with the default look leaves the sea its scene's own. Written by Author biome art (the default); edits are kept.")]
		[SerializeField] private BiomeSeaLook seaLook = BiomeSeaLooks.Today;
		/// <remarks>
		/// What the biome paints and grows under the sea (<see cref="BiomeSeaFloor"/>, 2026-10-19, phase I2m): the shore's rule
		/// (its lakebed takes the ground from the water line down, as every biome's did before), its own grounds at every depth (a
		/// sea-floor biome, deep or on the shelf), or its own grounds down to the lowest tide (an estuary). Shore unless the biome's
		/// file says otherwise, so nothing changes until a round sets it.
		/// </remarks>
		[Tooltip("What this biome paints under the sea: Shore (its Lakebed takes the ground from the water line down, as before), Ground (its main and detail layers at every depth, zoned by their depth bands, the Lakebed only under inland water: a sea-floor biome, deep or on the shelf) or To Low Water (its own grounds down to the lowest tide, the Lakebed below: an estuary). Written by Author biome art (Shore); edits are kept.")]
		[SerializeField] private BiomeSeaFloor seaFloor = BiomeSeaFloor.Shore;
		/// <remarks>
		/// How strongly this biome's coast tides (2026-10-19, phase I2m, decision 45): a funnelled estuary's tide is several times the
		/// open coast's. A scene cut with this biome on its coast multiplies its sea's tide by the coast's mean strength
		/// (BiomeSeaFloors.CoastStrength), never past BiomeSeaFloors.MaximumTideMetres. 1 (every biome's) leaves the sea as it was.
		/// </remarks>
		[Tooltip("How strongly this biome's coast tides: the scene sea's tide is multiplied by the coast's mean strength (1 as before; an estuary 4), the tide never moving the sea more than 4 m. Written by Author biome art (1); edits are kept.")]
		[Range(0f, BiomeSeaFloors.MaximumTideStrength)] [SerializeField] private float tideStrength = 1f;
		/// <remarks>
		/// How deep this biome's underwater sites stand (2026-10-25, phase I2m, decision 36 (b)): metres below mean sea level, x the
		/// shallowest and y the deepest, for every underwater kind sited on its floor (a wreck, a drowned shrine, a monster's den) in
		/// place of the kind's own band, which stops at 30–60 m: an abyss's wrecks lie at 6 000 m. (0, 0), every biome's, keeps
		/// each kind's own band, as before.
		/// </remarks>
		[Tooltip("Metres below mean sea level this biome's underwater sites (wrecks, drowned ruins and shrines, dens, crystal formations) stand between, in place of each kind's own band (which stops at 30-60 m). (0, 0): each kind's own, as before. Written by Author biome art ((0, 0)); edits are kept.")]
		[SerializeField] private Vector2 underwaterSiteDepth = Vector2.zero;
		/// <remarks>
		/// What the biome's still fresh water carries on its surface (<see cref="BiomeFloatingPlant"/>, 2026-10-18, I2i): duckweed,
		/// water-lilies, water hyacinth, frogbit, laid by the cut as baked props on the lakes, ponds, moats and slack river pools
		/// whose water lies in the biome, each patch's kind drawn by weight and how thickly by the water's depth, shelter and
		/// distance from the shore (<see cref="BiomeFloatingPlants.Cover"/>). Until then no water carried anything, which is
		/// this field's default (empty) and what the authoring writes until the biome's file names its own.
		/// </remarks>
		[Tooltip("The plants this biome's still fresh water carries on its surface (duckweed carpets, water-lily pads with flowers, water hyacinth mats, frogbit), each with how often: on its lakes, ponds, moats and slack river pools. Empty: none. Written by Author biome art; edits are kept.")]
		[SerializeField] private List<BiomeFloatingPlant> floatingPlants = new List<BiomeFloatingPlant>();

		/// <remarks>
		/// <para>
		/// What lives here besides what grows (2026-10-16): the country the ambient creatures read (<see cref="BiomeHabitat"/>:
		/// forest, wetland, coast, open sea, alien ground…; which birds, bats, small animals and insects belong is the
		/// creatures' own business, from the country, the climate and the hour), and the sea creatures most at home in its
		/// water (by the sea life's kind names: "Reef fish", "Sea turtle", "Crab", "Whale"…), which are that many times as
		/// common here as in other water.
		/// </para>
		/// <para>
		/// Until then the client read the country from keywords in the biome's name, and the sea life named its favourite
		/// biomes itself. The biome art authoring writes both here with exactly what those gave, so nothing changes until a
		/// template is edited; a template never authored still reads its name, with a warning.
		/// </para>
		/// <para>
		/// Alien ground is not stored (2026-10-16): it is the template's air, read live (<see cref="LiveHabitat"/>,
		/// <see cref="BiomeHabitats.Live"/>), so the habitat holds every other bit and an Alien bit set here is dropped.
		/// The sea life is listed with its weight here: a kind listed is that many times as common in this biome's water,
		/// whatever the climate there; one not listed lives as it does in any other water (its own elsewhere weight, by the
		/// climate).
		/// </para>
		/// </remarks>
		[Header("Wildlife")]
		[Tooltip("The country this biome offers ambient creatures (birds, bats, small animals, insects). Alien is not set here: it is read from the biome's air (Atmosphere without Standard, or Requires a methane cycle, cryogenic air or frozen nitrogen) on ground that is not Barren. None, on ground that is not alien: the creatures read the climate instead. Written by Author biome art; edits are kept.")]
		[SerializeField] private BiomeHabitat habitat = BiomeHabitat.None;
		[Tooltip("The sea creatures at home in this biome's water (the sea life's kind names: Reef fish, Sea turtle, Crab, Whale…), each with its weight: groups of it are that many times as common here, whatever the climate. A kind not listed lives here as in any other water. Written by Author biome art (the weight each kind had for its favourite biomes); edits are kept.")]
		[SerializeField] private List<BiomeSeaLife> seaLife = new List<BiomeSeaLife>();

		/// <remarks>
		/// Which vines (ivy, creeper, lianas) grow here and how thickly, by the place they grow on: a cave's mouth, ruined and
		/// old walls, cliff crags and a waterfall's wet rock, and the trees. Until 2026-10-16 a table keyed by the biome's name
		/// (VineBiomes); the biome art authoring writes that table's row here. No species: no vines.
		/// </remarks>
		[Header("Vines")]
		[Tooltip("Which vines grow in this biome and how thickly at each kind of place. No species: none. Written by Author biome art; edits are kept.")]
		[SerializeField] private BiomeVines vines = new BiomeVines();

		/// <remarks>
		/// The resources a gathering site in this biome draws its nodes from (the gathering catalogue's node names: "Iron Vein",
		/// "Silverleaf Patch", "Glowcap Cluster"…); a site draws only those of its families offered here, or any of a family
		/// when none of it is. Until 2026-10-16 every resource named its biomes itself (GatheringResourceCatalogue); the
		/// biome art authoring writes each biome the ones that named it. The catalogue still defines the resources (Generate
		/// Gathering Content makes their items, node templates and prefabs).
		/// </remarks>
		[Header("Gathering")]
		[Tooltip("The gathering resources (node names: Iron Vein, Silverleaf Patch, Glowcap Cluster…) this biome's gathering sites draw from. Written by Author biome art; edits are kept.")]
		[SerializeField] private List<string> gatheringResources = new List<string>();

		/// <remarks>
		/// What a generated point of interest grows from this biome: a herb grove's flowers between its beds, and the bushes,
		/// ferns and creeper crowding a natural cave's mouth with the boulders and deadwood strewn by it. Filled by the biome
		/// art authoring from the spec table (the biome's own flowers, bushes, ferns, boulders and deadwood; a meadow's
		/// flowers and a woodland's ferns and bushes where it has none); lists edited here are kept.
		/// </remarks>
		[Header("Point of Interest Growth")]
		[Tooltip("The flowers a herb grove grows between its beds in this biome.")]
		[SerializeField] private List<GameObject> herbGroveFlowers = new List<GameObject>();
		[Tooltip("The herbs and fungi a herb grove grows its beds of in this biome (by default the wild kin of the herbs and fungi gathered here, else common herbs).")]
		[SerializeField] private List<GameObject> herbGroveHerbs = new List<GameObject>();
		[Tooltip("The bushes crowding a cave mouth's flanks and the bank above it.")]
		[SerializeField] private List<GameObject> caveMouthBushes = new List<GameObject>();
		[Tooltip("The ferns at a cave mouth.")]
		[SerializeField] private List<GameObject> caveMouthFerns = new List<GameObject>();
		[Tooltip("The creeper grown right up to a cave mouth's rock. None for no creeper.")]
		[SerializeField] private GameObject caveMouthCreeper;
		[Tooltip("The boulders heaped at the foot of a cave mouth's walls.")]
		[SerializeField] private List<GameObject> caveMouthBoulders = new List<GameObject>();
		[Tooltip("The fallen trunks and stumps lying by a cave mouth.")]
		[SerializeField] private List<GameObject> caveMouthLogs = new List<GameObject>();

		/// <remarks>
		/// <para>
		/// The points of interest this biome hosts and how they are built here (2026-10-16, phase I5): the kinds sited on its
		/// ground (<see cref="BiomeSiteKind"/>; a kind not listed is never sited here), the weight each kind's template variants
		/// take here (<see cref="BiomeSiteVariant"/>: a stone village in the hills, a longhouse in the cold; a variant not listed
		/// keeps its weight elsewhere), the weight of whole structure styles (<see cref="BiomeSiteStyle"/>; a style not listed
		/// weighs 1), how the weather finishes its buildings (<see cref="BiomeWeathering"/>), what its fields grow
		/// (<see cref="BiomeFieldCrop"/>), what stands in a stronghold's moat (<see cref="BiomeMoatWater"/>, 2026-10-16, I5b) and
		/// how its settlements light their streets (<see cref="BiomeWayLight"/>, I5b).
		/// </para>
		/// <para>
		/// Until then each of these was decided by the biome's name: the point-of-interest catalogue's rows and templates named
		/// their biomes, the props read wet, dry and damp country from keywords, and every field grew wheat. The biome art
		/// authoring writes each biome exactly what those gave it, so nothing changes until a template is edited; a template
		/// never authored with them still cuts by its name, with a warning.
		/// </para>
		/// </remarks>
		[Header("Points of Interest")]
		[Tooltip("The point-of-interest kinds sited on this biome's ground, each with how gladly (1 as anywhere, more to favour it). A kind not listed is never sited here. Written by Author biome art; edits are kept.")]
		[SerializeField] private List<BiomeSiteKind> siteKinds = new List<BiomeSiteKind>();
		[Tooltip("The weight a kind's template variant (Village/Stone, Camp/Hide Tents…) takes in this biome against the kind's other variants, in place of its weight elsewhere. A variant not listed keeps its weight elsewhere. Written by Author biome art; edits are kept.")]
		[SerializeField] private List<BiomeSiteVariant> siteVariants = new List<BiomeSiteVariant>();
		[Tooltip("How many times its variant weight every template of a structure style weighs here (a style not listed: 1). Author biome art writes none; edits are kept.")]
		[SerializeField] private List<BiomeSiteStyle> siteStyles = new List<BiomeSiteStyle>();
		[Tooltip("How the weather finishes this biome's buildings and stones: moss, algae, char or none, and how much (more on worn pieces). Written by Author biome art; edits are kept.")]
		[SerializeField] private BiomeWeathering weathering;
		[Tooltip("The crops this biome's fields grow (detail prefabs, each with its weight). Written by Author biome art (wheat); edits are kept.")]
		[SerializeField] private List<BiomeFieldCrop> fieldCrops = new List<BiomeFieldCrop>();
		[Tooltip("The orchards this biome's fields are planted as (2026-10-18): a tree in a grid, drawn against the crops by its weight, with its spacing, what is sown under it and how its ground is laid. Empty: none. Written by Author biome art; edits are kept.")]
		[SerializeField] private List<BiomeOrchard> orchards = new List<BiomeOrchard>();
		[Tooltip("How its settlements' fields are laid out (2026-10-18): kept near rivers and lakes (bottomland, metres) and set square to a centuriation grid (metres a side). Zero: anywhere, as drawn. Written by Author biome art; edits are kept.")]
		[SerializeField] private BiomeFieldLayout fieldLayout;
		[Tooltip("The flights of old, given-up terraces stepped into the slopes round its settlements (2026-10-18): how many, their risers, the slopes they are cut into and how tumbled. None by default. Written by Author biome art; edits are kept.")]
		[SerializeField] private BiomeAbandonedTerraces abandonedTerraces = BiomeAbandonedTerraces.None;
		[Tooltip("The detail ground layer painted along its water channels (2026-10-18; a wet margin), and how wide; −1 none. Painted by the repaint after the channels are laid. Written by Author biome art; edits are kept.")]
		[SerializeField] private BiomeChannelMargin channelMargin = BiomeChannelMargin.None;
		[Tooltip("What stands in a keep's, castle's or fortress's moat here: Wet (open water), Frozen (ice) or Dry (a dry ditch). Written by Author biome art (what the biome's climate gave the moat before); edits are kept.")]
		[SerializeField] private BiomeMoatWater moatWater = BiomeMoatWater.Wet;
		[Tooltip("How each kind of settlement lights its streets here: the light (iron lantern post, torch post, brazier, cresset, crook lantern, glowjar, crystal post, rune stone: a light that stands on the ground) and the metres between lights. A kind not listed is not lit. Written by Author biome art (lanterns in towns, cities, capitals and ports, torches in villages); edits are kept.")]
		[SerializeField] private List<BiomeWayLight> wayLights = new List<BiomeWayLight>();
		[Tooltip("How each place in this biome's caves and mines is lit (a cave's route marks and camps, a mine's drifts, stopes and lamps): the light and, along a drift, the metres between lights. A place not listed is not lit. Written by Author biome art (lantern stands, wall torches every 17 m, torch posts, hanging lanterns); edits are kept.")]
		[SerializeField] private List<BiomeCaveLight> caveLights = new List<BiomeCaveLight>();
		[Tooltip("Which of this biome's ways are raised boardwalks (plank walks on piles with a pole handrail): its Roads (cart tracks, roads, highways), its Paths (footpaths, trails) across level ground, and the Streets of the settlements built here. None: every way lies on the ground. Written by Author biome art (None); edits are kept.")]
		[SerializeField] private BiomeBoardwalks boardwalks = BiomeBoardwalks.None;

		/// <remarks>
		/// The long, narrow things this biome's country is divided and watered by (2026-10-18, phase I2f, decisions 23 and 29;
		/// <see cref="BiomeLinearStyle"/>): hedges of its own bushes round its fields and along its lanes and wood edges, drystone,
		/// mortared and earth-bank walls, fences of every kind, rows of trees, irrigation channels carried at a gentle fall from the
		/// water to the fields, and the clearance rows of stones picked off them, each with where it goes and how it is built. Laid
		/// by the cut as baked props (PointOfInterestLinearFeatures). Until then a field had the kit's post-and-rail fence and
		/// nothing else was laid, which is this list's default (empty) and what the authoring writes until the biome's file names its
		/// own.
		/// </remarks>
		[Header("Linear Features")]
		[Tooltip("The hedges, walls, fences, tree rows, water channels and clearance rows of this biome: what each is (kind and form), where it is laid (round fields, along lanes, along wood edges, round small sites, as folds, along terraces), how much, how high, its gates and gaps, what it is grown of and the material it is built in. Empty: none (fields keep the kit's fence). Written by Author biome art; edits are kept.")]
		[SerializeField] private List<BiomeLinearStyle> linearFeatures = new List<BiomeLinearStyle>();

		/// <remarks>
		/// What this biome's buildings are made of, per structure style and part (<see cref="BiomeStructurePalette"/>, 2026-10-16).
		/// Written by the biome art authoring with the kit's own materials (<see cref="BiomeStructurePalette.Kit"/>), which are
		/// what every biome's buildings are built of today; the art generator builds a biome's own palette from phase I6.
		/// </remarks>
		[Header("Structures")]
		[Tooltip("What this biome's buildings make each part of (walls, upper walls, thatched and shingled roofs, plinth, trim, door, floor, fences, paving), per structure style, with a tint and a surface variant. Written by Author biome art (the structure kit's own materials); edits are kept. The art generator does not build per-biome palettes yet: a palette differing from the kit's is noted at the cut and built in the kit's materials until it does.")]
		[SerializeField] private BiomeStructurePalette structurePalette = new BiomeStructurePalette();

		/// <summary>
		/// For each value the biome art authoring writes that is not a texture slot or a spawn rule (those carry their own
		/// marks), a fingerprint of what it last wrote: while the value still matches, nobody has changed it and the authoring
		/// may write it again; once it differs it is somebody's edit and is kept. A field with no mark was never authored.
		/// A list carries one mark per entry ("field/identity", <see cref="EntryMarks"/>, 2026-10-16) and its own field mark is
		/// then <see cref="BiomeEntryMarks.PerEntry"/>.
		/// </summary>
		[SerializeField, HideInInspector] private List<AuthoredMark> authoredMarks = new List<AuthoredMark>();

		/// <summary>One authored value's mark: the field's name and the fingerprint of what the authoring wrote.</summary>
		[System.Serializable]
		public struct AuthoredMark
		{
			public string Field;
			public string Fingerprint;
		}

		/// <summary>The names <see cref="AuthoredMark"/>s are kept under: the serialized fields' own names.</summary>
		public static class Authored
		{
			public const string OwnRock = nameof(ownRock);
			public const string AcceptedRocks = nameof(acceptedRocks);
			public const string HerbGroveFlowers = nameof(herbGroveFlowers);
			public const string CaveMouthBushes = nameof(caveMouthBushes);
			public const string CaveMouthFerns = nameof(caveMouthFerns);
			public const string CaveMouthCreeper = nameof(caveMouthCreeper);
			public const string CaveMouthBoulders = nameof(caveMouthBoulders);
			public const string CaveMouthLogs = nameof(caveMouthLogs);
			public const string LookClass = nameof(lookClass);
			public const string CaveShells = nameof(caveShells);
			public const string WaterRole = nameof(waterRole);
			public const string Habitat = nameof(habitat);
			public const string SeaLife = nameof(seaLife);
			public const string Vines = nameof(vines);
			public const string GatheringResources = nameof(gatheringResources);
			public const string HerbGroveHerbs = nameof(herbGroveHerbs);
			public const string SiteKinds = nameof(siteKinds);
			public const string SiteVariants = nameof(siteVariants);
			public const string SiteStyles = nameof(siteStyles);
			public const string Weathering = nameof(weathering);
			public const string FieldCrops = nameof(fieldCrops);
			public const string Orchards = nameof(orchards);
			public const string FieldLayout = nameof(fieldLayout);
			public const string AbandonedTerraces = nameof(abandonedTerraces);
			public const string ChannelMargin = nameof(channelMargin);
			public const string StructurePalette = nameof(structurePalette);
			public const string MoatWater = nameof(moatWater);
			public const string WaterLook = nameof(waterLook);
			public const string SeaLook = nameof(seaLook);
			public const string SeaFloor = nameof(seaFloor);
			public const string TideStrength = nameof(tideStrength);
			public const string UnderwaterSiteDepth = nameof(underwaterSiteDepth);
			public const string WayLights = nameof(wayLights);
			public const string CaveCrystals = nameof(caveCrystals);
			public const string CaveLights = nameof(caveLights);
			public const string CaveInteriors = nameof(caveInteriors);
			public const string CaveInteriorRocks = nameof(caveInteriorRocks);
			public const string CaveFormations = nameof(caveFormations);
			public const string CaveGlows = nameof(caveGlows);
			public const string FloatingPlants = nameof(floatingPlants);
			public const string Boardwalks = nameof(boardwalks);
			public const string LinearFeatures = nameof(linearFeatures);
			public const string Landforms = nameof(landforms);

			/// <summary>
			/// The mark of the vines' four densities (caves, ruins, cliffs, trees), a value of its own since the species carry a
			/// mark each (2026-10-16): not an entry key, so never read as one.
			/// </summary>
			public const string VineDensities = nameof(vines) + ".densities";

			/// <summary>The mark of one entry of a list field, by the entry's identity (<see cref="BiomeEntryMarks"/>).</summary>
			public static string Entry(string field, string id) => field + "/" + id;

			/// <summary>The mark of the <see cref="GrassTypes"/> entry for one blade prefab.</summary>
			public static string GrassType(string prefabName) => nameof(grassTypes) + "/" + prefabName;

			/// <summary>The mark of one texture slot's <see cref="TerrainTextureLayer.GroundMakeup"/> (slot keys as BiomeLocalArt's).</summary>
			public static string GroundMakeup(string slotKey) => "groundMakeup/" + slotKey;

			/// <summary>The mark of one way of a ground layer's <see cref="TerrainTextureLayer.ways"/> (key BiomeLocalArt.WaySlot: <c>detail/2/road</c>).</summary>
			public static string GroundWay(string waySlotKey) => "groundWays/" + waySlotKey;

			/// <summary>
			/// The mark of one ground layer's climate band (<see cref="TerrainTextureLayer.useClimateBand"/> and its ranges, 2026-10-18;
			/// slot keys as BiomeLocalArt's, a climate variant's override included: <c>variant/frozen/main</c>).
			/// </summary>
			public static string GroundClimate(string slotKey) => "groundClimate/" + slotKey;

			/// <summary>The mark of one bed layer's bands (<see cref="BedTextureLayer"/>'s waters, depth and current, 2026-10-18), by its slot key (<c>bed/0</c>).</summary>
			public static string BedBands(string slotKey) => "bedBands/" + slotKey;

			/// <summary>The mark of one climate variant the authoring added or keeps up to date (its envelope), by its key (<see cref="BiomeClimateVariant.Key"/>).</summary>
			public static string ClimateVariant(string variantKey) => nameof(ClimateVariants) + "/" + variantKey;

			/// <summary>The mark of the template's <see cref="BiomeTemplate.Description"/> (a spec column since 2026-10-18).</summary>
			public const string Description = nameof(BiomeTemplate.Description);

			/// <summary>The mark of one part of the naming block (<see cref="BiomeTemplate.Naming"/>'s phonology: <c>naming/Onsets</c>…), a spec column since 2026-10-18.</summary>
			public static string NamingPart(string part) => "naming/" + part;

			/// <summary>The mark of what the ground gives the air (<see cref="BiomeTemplate.Emits"/> and its rate), a spec column since 2026-10-18 (I2o).</summary>
			public const string Emission = nameof(BiomeTemplate.Emits);

			/// <summary>The mark of the hot ground's split among its kinds (<see cref="BiomeTemplate.Geothermal"/>), a spec column since 2026-10-18 (I2q).</summary>
			public const string Geothermal = nameof(geothermal);
		}

		[System.NonSerialized]
		private List<TerrainTextureLayer> cachedTextureLayersInOrder;
		private string key;

		public TerrainTextureLayer MainTextureLayer => mainTextureLayer;
		public List<TerrainTextureLayer> DetailTextureLayers => detailTextureLayers;
		public TerrainTextureLayer RoadTextureLayer => roadTextureLayer;
		public TerrainTextureLayer SmallPathTextureLayer => smallPathTextureLayer;
		public TerrainTextureLayer CobbleTextureLayer => cobbleTextureLayer;
		public List<CliffTextureLayer> CliffTextureLayers => cliffTextureLayers;
		public TerrainTextureLayer RiverbedTextureLayer => riverbedTextureLayer;
		public TerrainTextureLayer BarSandTextureLayer => barSandTextureLayer;
		public TerrainTextureLayer BarGravelTextureLayer => barGravelTextureLayer;
		public TerrainTextureLayer LakebedTextureLayer => lakebedTextureLayer;

		/// <summary>The biome's extra bed layers by depth (and current), at most <see cref="MaximumBedLayers"/> painted (decision 35, 2026-10-18).</summary>
		public List<BedTextureLayer> BedTextureLayers => bedTextureLayers;

		/// <summary>The biome's own rock (a rock type name, or Ice); null or empty when not authored.</summary>
		public string OwnRock
		{
			get => ownRock;
			set => ownRock = value;
		}
		/// <summary>The planet-geology rocks the biome accepts besides its own.</summary>
		public List<string> AcceptedRocks => acceptedRocks;

		/// <summary>The rock each form of cave is cut in here, its formations included (<see cref="BiomeCaveShell"/>).</summary>
		public List<BiomeCaveShell> CaveShells => caveShells;

		/// <summary>The crystals this biome's caves grow, with their weights (<see cref="BiomeCaveCrystal"/>); read through BiomeTemplateArt.CaveCrystals.</summary>
		public List<BiomeCaveCrystal> CaveCrystals => caveCrystals;

		/// <summary>The cave biome dressing the inside of this biome's caves of each form (<see cref="BiomeCaveInterior"/>); read through CaveInteriorArt.</summary>
		public List<BiomeCaveInterior> CaveInteriors => caveInteriors;

		/// <summary>As a cave interior, the shell rocks it dresses the inside of (empty: any).</summary>
		public List<string> CaveInteriorRocks => caveInteriorRocks;

		/// <summary>The formations this biome's caves take by form and place (<see cref="BiomeCaveFormation"/>).</summary>
		public List<BiomeCaveFormation> CaveFormations => caveFormations;

		/// <summary>What lights this biome's caves by form by itself (<see cref="BiomeCaveGlow"/>).</summary>
		public List<BiomeCaveGlow> CaveGlows => caveGlows;

		/// <summary>
		/// The rock a cave of this form is cut in here (a rock type name, Ice, or <see cref="BiomeCaveShell.Ground"/>): the first
		/// entry for the form that names one, or null.
		/// </summary>
		public string CaveShellFor(BiomeCaveForm form)
		{
			if (caveShells != null)
			{
				foreach (BiomeCaveShell entry in caveShells)
				{
					if (entry.Form == form && !string.IsNullOrEmpty(entry.Rock))
					{
						return entry.Rock;
					}
				}
			}
			return null;
		}

		/// <summary>The part this biome plays for a scene's water and ice (<see cref="BiomeWaterRole"/>).</summary>
		public BiomeWaterRole WaterRole
		{
			get => waterRole;
			set => waterRole = value;
		}
		/// <summary>
		/// The country this biome offers ambient creatures as stored (<see cref="BiomeHabitat"/>), never with the Alien bit,
		/// which is read live (<see cref="LiveHabitat"/>): one stored before 2026-10-16 is masked here, and setting it does
		/// nothing. None when not authored, or authored as none.
		/// </summary>
		public BiomeHabitat Habitat
		{
			get => habitat & BiomeHabitats.Stored;
			set => habitat = value & BiomeHabitats.Stored;
		}

		/// <summary>
		/// The country this biome offers as the creatures read it: <see cref="Habitat"/> and, on ground that forms only under
		/// another world's air, Alien (<see cref="BiomeHabitats.Live"/>, from <see cref="Atmosphere"/> and <see cref="Requires"/>).
		/// </summary>
		public BiomeHabitat LiveHabitat => BiomeHabitats.Live(habitat, Atmosphere, Requires);

		/// <summary>
		/// Drops an Alien bit the authoring stored before it was read live (2026-10-16); true when there was one. Every read
		/// masks it anyway; this only cleans the asset.
		/// </summary>
		public bool DropStoredAlien()
		{
			if ((habitat & BiomeHabitat.Alien) == 0)
			{
				return false;
			}
			habitat &= BiomeHabitats.Stored;
			return true;
		}

		/// <summary>The sea life's kinds at home in this biome's water, each with its weight (<see cref="BiomeSeaLife"/>).</summary>
		public List<BiomeSeaLife> SeaLife => seaLife;

		/// <summary>
		/// The weight this biome gives a sea life kind (by name, ignoring case) as one of its own: the first entry naming it;
		/// false when none does (the kind lives here as in any other water).
		/// </summary>
		public bool TrySeaLifeWeight(string kind, out float weight) => BiomeSeaLife.TryWeight(seaLife, kind, out weight);

		/// <summary>Which vines grow here and how thickly (<see cref="BiomeVines"/>).</summary>
		public BiomeVines Vines
		{
			get => vines;
			set => vines = value ?? new BiomeVines();
		}

		/// <summary>The gathering resources (node names) this biome's gathering sites draw from.</summary>
		public List<string> GatheringResources => gatheringResources;

		public List<GameObject> HerbGroveFlowers => herbGroveFlowers;
		public List<GameObject> HerbGroveHerbs => herbGroveHerbs;
		public List<GameObject> CaveMouthBushes => caveMouthBushes;
		public List<GameObject> CaveMouthFerns => caveMouthFerns;
		public GameObject CaveMouthCreeper
		{
			get => caveMouthCreeper;
			set => caveMouthCreeper = value;
		}
		public List<GameObject> CaveMouthBoulders => caveMouthBoulders;
		public List<GameObject> CaveMouthLogs => caveMouthLogs;

		/// <summary>The point-of-interest kinds sited on this biome's ground, with how gladly (<see cref="BiomeSiteKind"/>).</summary>
		public List<BiomeSiteKind> SiteKinds => siteKinds;

		/// <summary>The weight each listed template variant takes here (<see cref="BiomeSiteVariant"/>).</summary>
		public List<BiomeSiteVariant> SiteVariants => siteVariants;

		/// <summary>The weight of whole structure styles' templates here (<see cref="BiomeSiteStyle"/>).</summary>
		public List<BiomeSiteStyle> SiteStyles => siteStyles;

		/// <summary>How the weather finishes this biome's buildings and stones (<see cref="BiomeWeathering"/>).</summary>
		public BiomeWeathering Weathering
		{
			get => weathering;
			set => weathering = value;
		}

		/// <summary>The crops this biome's fields grow (<see cref="BiomeFieldCrop"/>).</summary>
		public List<BiomeFieldCrop> FieldCrops => fieldCrops;

		/// <summary>The orchards this biome's fields are planted as (<see cref="BiomeOrchard"/>); read through BiomeTemplateArt.Orchards.</summary>
		public List<BiomeOrchard> Orchards => orchards ??= new List<BiomeOrchard>();

		/// <summary>How this biome's fields are laid out (<see cref="BiomeFieldLayout"/>); read through BiomeTemplateArt.FieldLayout.</summary>
		public BiomeFieldLayout FieldLayout
		{
			get => fieldLayout;
			set => fieldLayout = value;
		}

		/// <summary>The old terraces round this biome's settlements (<see cref="BiomeAbandonedTerraces"/>); read through BiomeTemplateArt.AbandonedTerraces.</summary>
		public BiomeAbandonedTerraces AbandonedTerraces
		{
			get => abandonedTerraces;
			set => abandonedTerraces = value;
		}

		/// <summary>The ground this biome paints along its water channels (<see cref="BiomeChannelMargin"/>); read through BiomeTemplateArt.ChannelMargin.</summary>
		public BiomeChannelMargin ChannelMargin
		{
			get => channelMargin;
			set => channelMargin = value;
		}

		/// <summary>What stands in a stronghold's moat here (<see cref="BiomeMoatWater"/>); read through BiomeTemplateArt.MoatWater.</summary>
		public BiomeMoatWater MoatWater
		{
			get => moatWater;
			set => moatWater = value;
		}

		/// <summary>The colour and clarity of this biome's fresh water (<see cref="BiomeWaterLook"/>); read through <see cref="BiomeWaterLooks.Of"/>.</summary>
		/// <summary>
		/// How a steaming biome's hot ground divides among geysers, hot springs, fumaroles and mud pots (2026-10-18, phase I2q,
		/// decision 47; GeothermalVents.KindOf). <see cref="BiomeGeothermalShares.Today"/> unless the biome's file says otherwise.
		/// </summary>
		public BiomeGeothermalShares Geothermal
		{
			get => geothermal;
			set => geothermal = value;
		}

		public BiomeWaterLook WaterLook
		{
			get => waterLook;
			set => waterLook = value;
		}

		/// <summary>The sea seen from under it off this biome's coast (<see cref="BiomeSeaLook"/>); read through <see cref="BiomeSeaLooks.Of"/>.</summary>
		public BiomeSeaLook SeaLook
		{
			get => seaLook;
			set => seaLook = value;
		}

		/// <summary>What the biome paints and grows under the sea (<see cref="BiomeSeaFloor"/>, 2026-10-19, phase I2m): Shore unless its file says otherwise.</summary>
		public BiomeSeaFloor SeaFloor
		{
			get => seaFloor;
			set => seaFloor = value;
		}

		/// <summary>How strongly this biome's coast tides (decision 45), 0 … <see cref="BiomeSeaFloors.MaximumTideStrength"/>; read through <see cref="BiomeSeaFloors.Strength"/>.</summary>
		public float TideStrength
		{
			get => tideStrength;
			set => tideStrength = BiomeSeaFloors.Strength(value);
		}

		/// <summary>How deep this biome's underwater sites stand, metres below mean sea level ((0, 0): each kind's own band); read through <see cref="BiomeSeaFloors.SiteDepth"/>.</summary>
		public Vector2 UnderwaterSiteDepth
		{
			get => underwaterSiteDepth;
			set => underwaterSiteDepth = value;
		}

		/// <summary>How each kind of settlement lights its streets here (<see cref="BiomeWayLight"/>); a kind not listed is not lit.</summary>
		public List<BiomeWayLight> WayLights => wayLights;

		/// <summary>How each place in this biome's caves and mines is lit (<see cref="BiomeCaveLight"/>); a place not listed is not lit.</summary>
		public List<BiomeCaveLight> CaveLights => caveLights;

		/// <summary>The plants this biome's still fresh water carries, with their weights (<see cref="BiomeFloatingPlant"/>); read through BiomeTemplateArt.FloatingPlants.</summary>
		public List<BiomeFloatingPlant> FloatingPlants => floatingPlants;

		/// <summary>The landforms sculpted into this biome's ground at the cut (<see cref="BiomeLandform"/>); read through BiomeTemplateArt.Landforms.</summary>
		public List<BiomeLandform> Landforms => landforms ??= new List<BiomeLandform>();

		/// <summary>Which of this biome's ways are raised boardwalks (<see cref="BiomeBoardwalks"/>); read through BiomeTemplateArt.Boardwalks.</summary>
		public BiomeBoardwalks Boardwalks
		{
			get => boardwalks;
			set => boardwalks = value;
		}

		/// <summary>This biome's hedges, walls, fences, tree rows, channels and clearance rows (<see cref="BiomeLinearStyle"/>); read through BiomeTemplateArt.LinearFeatures.</summary>
		public List<BiomeLinearStyle> LinearFeatures => linearFeatures ??= new List<BiomeLinearStyle>();

		/// <summary>What this biome's buildings are made of, per style and part (<see cref="BiomeStructurePalette"/>).</summary>
		public BiomeStructurePalette StructurePalette
		{
			get => structurePalette;
			set => structurePalette = value ?? new BiomeStructurePalette();
		}

		/// <summary>The biome's look class (<see cref="BiomeLookClass.Unset"/> when not authored); read through <see cref="BiomeLooks.ClassOf(BiomeTemplate)"/>.</summary>
		public BiomeLookClass LookClass
		{
			get => lookClass;
			set => lookClass = value;
		}

		public BiomeRareLook RareLookA
		{
			get => rareLookA;
			set => rareLookA = value;
		}

		public BiomeRareLook RareLookB
		{
			get => rareLookB;
			set => rareLookB = value;
		}

		/// <summary>How each blade prefab looks in this biome (<see cref="BiomeGrassType"/>).</summary>
		public List<BiomeGrassType> GrassTypes => grassTypes;

		/// <summary>This biome's look for a blade prefab, or null when it has none (the renderer then draws the old look and warns).</summary>
		public BiomeGrassType GrassTypeFor(GameObject prefab)
		{
			if (prefab == null || grassTypes == null)
			{
				return null;
			}
			foreach (BiomeGrassType look in grassTypes)
			{
				if (look != null && look.Prefab == prefab)
				{
					return look;
				}
			}
			return null;
		}

		/// <summary>As <see cref="GrassTypeFor(GameObject)"/>, by the prefab's name (generated prefabs have fixed, unique names).</summary>
		public BiomeGrassType GrassTypeFor(string prefabName)
		{
			if (string.IsNullOrEmpty(prefabName) || grassTypes == null)
			{
				return null;
			}
			foreach (BiomeGrassType look in grassTypes)
			{
				if (look != null && look.Prefab != null && look.Prefab.name == prefabName)
				{
					return look;
				}
			}
			return null;
		}

		/// <summary>The fingerprint the authoring last wrote a field with (<see cref="Authored"/>), or null when it never has.</summary>
		public string AuthoredFingerprint(string field)
		{
			if (authoredMarks != null)
			{
				foreach (AuthoredMark mark in authoredMarks)
				{
					if (mark.Field == field)
					{
						return mark.Fingerprint;
					}
				}
			}
			return null;
		}

		/// <summary>Records what the authoring wrote a field with (<see cref="Authored"/>).</summary>
		public void SetAuthoredFingerprint(string field, string fingerprint)
		{
			authoredMarks ??= new List<AuthoredMark>();
			for (int i = 0; i < authoredMarks.Count; i++)
			{
				if (authoredMarks[i].Field == field)
				{
					authoredMarks[i] = new AuthoredMark { Field = field, Fingerprint = fingerprint };
					return;
				}
			}
			authoredMarks.Add(new AuthoredMark { Field = field, Fingerprint = fingerprint });
		}

		/// <summary>Forgets that the authoring wrote a field (its value is gone, as a pruned grass entry's): it is a gap again.</summary>
		public void ClearAuthoredFingerprint(string field)
		{
			authoredMarks?.RemoveAll(mark => mark.Field == field);
		}

		/// <summary>True when the authoring has written the field at least once, so an empty value is a decision, not a gap.</summary>
		public bool IsAuthored(string field) => AuthoredFingerprint(field) != null;

		/// <summary>
		/// The marks of a list field's entries (<see cref="BiomeEntryMarks"/>): entry identity → the print the authoring wrote, or
		/// <see cref="BiomeEntryMarks.Removed"/> for an entry it wrote that was deleted by hand. A fresh dictionary.
		/// </summary>
		public Dictionary<string, string> EntryMarks(string field)
		{
			var marks = new Dictionary<string, string>(System.StringComparer.Ordinal);
			if (authoredMarks != null)
			{
				string prefix = Authored.Entry(field, string.Empty);
				foreach (AuthoredMark mark in authoredMarks)
				{
					if (mark.Field != null && mark.Field.Length > prefix.Length && mark.Field.StartsWith(prefix, System.StringComparison.Ordinal))
					{
						marks[mark.Field.Substring(prefix.Length)] = mark.Fingerprint;
					}
				}
			}
			return marks;
		}

		/// <summary>Replaces every mark of a list field's entries with these (identity → mark), in identity order.</summary>
		public void SetEntryMarks(string field, IReadOnlyDictionary<string, string> marks)
		{
			authoredMarks ??= new List<AuthoredMark>();
			string prefix = Authored.Entry(field, string.Empty);
			authoredMarks.RemoveAll(mark => mark.Field != null && mark.Field.Length > prefix.Length && mark.Field.StartsWith(prefix, System.StringComparison.Ordinal));
			if (marks == null)
			{
				return;
			}
			var ids = new List<string>(marks.Keys);
			ids.Sort(System.StringComparer.Ordinal);
			foreach (string id in ids)
			{
				authoredMarks.Add(new AuthoredMark { Field = prefix + id, Fingerprint = marks[id] });
			}
		}

		/// <summary>Normalised registry key: the asset name, lowercase letters only ("Alpine Meadow" → "alpinemeadow").</summary>
		public string Key
		{
			get
			{
				if (key == null)
				{
					key = BiomeClimateVariant.Normalize(name);
				}
				return key;
			}
		}

		/// <summary>Display name, falling back to the asset name.</summary>
		/// <summary>How this biome's ground wears: its profile's values, or temperate soil's when it has none.</summary>
		public TerrainProcess ResolvedTerrainProcess => TerrainProcess != null ? TerrainProcess.Values : Biomes.TerrainProcess.Temperate;

		public string ResolvedDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName;

		/// <summary>True when climate-driven generation may pick this biome.</summary>
		public bool IsSelectable => SelectionWeight > 0f;

		private static readonly string[] tierNames =
		{
			"Deep Ocean", "Ocean", "Coastal Water", "Beach", "Lowland", "Highland", "Mountain", "Alpine", "Nival", "Nival",
		};

		/// <summary>Human name of an elevation tier, for tools.</summary>
		public static string TierName(int tier)
		{
			return tier >= 0 && tier < tierNames.Length ? tierNames[tier] : tier.ToString();
		}

		// ── Climate ───────────────────────────────────────────────────

		public bool ContainsHeight(float height) => height >= MinHeight && height <= MaxHeight;

		public bool ContainsClimate(float temperature, float humidity)
		{
			return temperature >= MinTemperature && temperature <= MaxTemperature
				&& humidity >= MinHumidity && humidity <= MaxHumidity;
		}

		/// <summary>
		/// The envelope with the summer test: <see cref="ContainsClimate(float, float)"/> and, when the
		/// sample's seasons are known, a warmest season inside <see cref="MinWarmestSeason"/>…<see cref="MaxWarmestSeason"/>.
		/// </summary>
		public bool ContainsClimate(in ClimateSample sample)
		{
			return ContainsClimate(sample.Temperature, sample.Humidity) && WarmestSeasonDistance(sample) <= 0f;
		}

		/// <summary>How far the sample's warmest season lies outside this biome's range, 0 inside or when the seasons are unknown.</summary>
		public float WarmestSeasonDistance(in ClimateSample sample)
		{
			if (!sample.SeasonKnown)
			{
				return 0f;
			}
			float w = sample.WarmestSeason;
			return w < MinWarmestSeason ? MinWarmestSeason - w : w > MaxWarmestSeason ? w - MaxWarmestSeason : 0f;
		}

		/// <summary>
		/// <see cref="ClimateDistance(float, float)"/> with the summer's distance in it too, in the same units.
		/// </summary>
		public float ClimateDistance(in ClimateSample sample)
		{
			float d = ClimateDistance(sample.Temperature, sample.Humidity);
			float w = WarmestSeasonDistance(sample);
			return Mathf.Sqrt(d * d + w * w);
		}

		/// <summary>
		/// How far a climate reading is from this biome's envelope, 0 when inside. Temperature and
		/// humidity each span 2 units, so the distance is in the same units as the ranges.
		/// </summary>
		public float ClimateDistance(float temperature, float humidity)
		{
			float dt = temperature < MinTemperature ? MinTemperature - temperature : temperature > MaxTemperature ? temperature - MaxTemperature : 0f;
			float dh = humidity < MinHumidity ? MinHumidity - humidity : humidity > MaxHumidity ? humidity - MaxHumidity : 0f;
			return Mathf.Sqrt(dt * dt + dh * dh);
		}

		/// <summary>
		/// How central a reading is within the envelope, 1 at the centre falling to 0 at its edge;
		/// used to break ties between biomes that all contain the reading.
		/// </summary>
		public float ClimateCentrality(float temperature, float humidity)
		{
			float halfT = Mathf.Max(0.0001f, (MaxTemperature - MinTemperature) * 0.5f);
			float halfH = Mathf.Max(0.0001f, (MaxHumidity - MinHumidity) * 0.5f);
			float t = Mathf.Abs(temperature - (MinTemperature + halfT)) / halfT;
			float h = Mathf.Abs(humidity - (MinHumidity + halfH)) / halfH;
			return Mathf.Clamp01(1f - Mathf.Max(t, h));
		}

		/// <summary>The first of this template's own variants that fits the reading, or null.</summary>
		public BiomeClimateVariant ResolveOwnVariant(float temperature, float humidity)
		{
			for (int i = 0; i < ClimateVariants.Count; i++)
			{
				BiomeClimateVariant variant = ClimateVariants[i];
				if (variant != null && variant.Matches(temperature, humidity))
				{
					return variant;
				}
			}
			return null;
		}

		/// <summary>The template's own variant with this key, or null.</summary>
		public BiomeClimateVariant FindOwnVariant(string variantKey)
		{
			string normalized = BiomeClimateVariant.Normalize(variantKey);
			if (normalized.Length == 0)
			{
				return null;
			}
			for (int i = 0; i < ClimateVariants.Count; i++)
			{
				if (ClimateVariants[i] != null && ClimateVariants[i].Key == normalized)
				{
					return ClimateVariants[i];
				}
			}
			return null;
		}

		// ── Terrain layers ────────────────────────────────────────────

		/// <summary>All texture layers in painting order: main, details, roads, cliffs, riverbed, river sediment, lakebed. Cached.</summary>
		public List<TerrainTextureLayer> GetAllTextureLayersInOrder()
		{
			if (cachedTextureLayersInOrder != null)
			{
				return cachedTextureLayersInOrder;
			}

			var layers = new List<TerrainTextureLayer>();
			if (mainTextureLayer != null && mainTextureLayer.HasAlbedo) layers.Add(mainTextureLayer);
			AddWithAlbedo(layers, detailTextureLayers);
			if (roadTextureLayer != null && roadTextureLayer.HasAlbedo) layers.Add(roadTextureLayer);
			if (smallPathTextureLayer != null && smallPathTextureLayer.HasAlbedo) layers.Add(smallPathTextureLayer);
			if (cobbleTextureLayer != null && cobbleTextureLayer.HasAlbedo) layers.Add(cobbleTextureLayer);
			AddWithAlbedo(layers, cliffTextureLayers);
			if (riverbedTextureLayer != null && riverbedTextureLayer.HasAlbedo) layers.Add(riverbedTextureLayer);
			if (barSandTextureLayer != null && barSandTextureLayer.HasAlbedo) layers.Add(barSandTextureLayer);
			if (barGravelTextureLayer != null && barGravelTextureLayer.HasAlbedo) layers.Add(barGravelTextureLayer);
			if (lakebedTextureLayer != null && lakebedTextureLayer.HasAlbedo) layers.Add(lakebedTextureLayer);
			AddWithAlbedo(layers, bedTextureLayers);

			cachedTextureLayersInOrder = layers;
			return layers;
		}

		private static void AddWithAlbedo<T>(List<TerrainTextureLayer> target, List<T> source) where T : TerrainTextureLayer
		{
			if (source == null)
			{
				return;
			}
			for (int i = 0; i < source.Count; i++)
			{
				if (source[i] != null && source[i].HasAlbedo)
				{
					target.Add(source[i]);
				}
			}
		}

		/// <summary>Call after editing layers at runtime.</summary>
		public void InvalidateTextureLayerCache()
		{
			cachedTextureLayersInOrder = null;
		}

		/// <summary>True when at least one layer has a texture to paint with.</summary>
		public bool HasValidTextureLayers()
		{
			InvalidateTextureLayerCache();
			return GetAllTextureLayersInOrder().Count > 0;
		}

		/// <summary>Gives every layer a fresh blend-noise offset so tiled biomes do not repeat.</summary>
		public void RandomizeNoiseOffsets(DeterministicRNG rng = null)
		{
			rng ??= new DeterministicRNG();
			foreach (TerrainTextureLayer layer in EveryLayer())
			{
				layer.blendNoiseOffsetX = rng.Range(-1000f, 1000f);
				layer.blendNoiseOffsetY = rng.Range(-1000f, 1000f);
			}
		}

		private IEnumerable<TerrainTextureLayer> EveryLayer()
		{
			if (mainTextureLayer != null) yield return mainTextureLayer;
			if (detailTextureLayers != null) foreach (TerrainTextureLayer l in detailTextureLayers) if (l != null) yield return l;
			if (roadTextureLayer != null) yield return roadTextureLayer;
			if (smallPathTextureLayer != null) yield return smallPathTextureLayer;
			if (cobbleTextureLayer != null) yield return cobbleTextureLayer;
			if (cliffTextureLayers != null) foreach (CliffTextureLayer l in cliffTextureLayers) if (l != null) yield return l;
			if (riverbedTextureLayer != null) yield return riverbedTextureLayer;
			if (barSandTextureLayer != null) yield return barSandTextureLayer;
			if (barGravelTextureLayer != null) yield return barGravelTextureLayer;
			if (lakebedTextureLayer != null) yield return lakebedTextureLayer;
			if (bedTextureLayers != null) foreach (BedTextureLayer l in bedTextureLayers) if (l != null) yield return l;
		}

		private void OnValidate()
		{
			mainTextureLayer ??= new TerrainTextureLayer();
			detailTextureLayers ??= new List<TerrainTextureLayer>();
			roadTextureLayer ??= new TerrainTextureLayer();
			smallPathTextureLayer ??= new TerrainTextureLayer();
			cobbleTextureLayer ??= new TerrainTextureLayer();
			cliffTextureLayers ??= new List<CliffTextureLayer>();
			riverbedTextureLayer ??= new TerrainTextureLayer();
			barSandTextureLayer ??= new TerrainTextureLayer();
			barGravelTextureLayer ??= new TerrainTextureLayer();
			lakebedTextureLayer ??= new TerrainTextureLayer();
			bedTextureLayers ??= new List<BedTextureLayer>();
			acceptedRocks ??= new List<string>();
			caveShells ??= new List<BiomeCaveShell>();
			caveCrystals ??= new List<BiomeCaveCrystal>();
			caveInteriors ??= new List<BiomeCaveInterior>();
			caveInteriorRocks ??= new List<string>();
			caveFormations ??= new List<BiomeCaveFormation>();
			caveGlows ??= new List<BiomeCaveGlow>();
			seaLife ??=new List<BiomeSeaLife>();
			habitat &= BiomeHabitats.Stored;
			vines ??= new BiomeVines();
			vines.Species ??= new List<BiomeVineSpecies>();
			gatheringResources ??= new List<string>();
			herbGroveFlowers ??= new List<GameObject>();
			herbGroveHerbs ??= new List<GameObject>();
			caveMouthBushes ??= new List<GameObject>();
			caveMouthFerns ??= new List<GameObject>();
			caveMouthBoulders ??= new List<GameObject>();
			caveMouthLogs ??= new List<GameObject>();
			siteKinds ??= new List<BiomeSiteKind>();
			siteVariants ??= new List<BiomeSiteVariant>();
			siteStyles ??= new List<BiomeSiteStyle>();
			fieldCrops ??= new List<BiomeFieldCrop>();
			orchards ??= new List<BiomeOrchard>();
			wayLights ??= new List<BiomeWayLight>();
			caveLights ??= new List<BiomeCaveLight>();
			floatingPlants ??= new List<BiomeFloatingPlant>();
			landforms ??= new List<BiomeLandform>();
			structurePalette ??= new BiomeStructurePalette();
			structurePalette.Surfaces ??= new List<BiomeStructureSurface>();
			grassTypes ??= new List<BiomeGrassType>();
			authoredMarks ??= new List<AuthoredMark>();
			if (MaxHeight < MinHeight) MaxHeight = MinHeight;
			if (MaxTemperature < MinTemperature) MaxTemperature = MinTemperature;
			if (MaxHumidity < MinHumidity) MaxHumidity = MinHumidity;
			key = null;
			InvalidateTextureLayerCache();
		}

		// ── Registration ──────────────────────────────────────────────

		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);
			key = null;
			BiomeRegistry.Register(this);
		}

		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			BiomeRegistry.Unregister(this);
			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}
