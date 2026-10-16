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

		[Header("How the ground wears")]
		[Tooltip("How erosion, drainage and plateaus shape this biome's ground in a generated scene. Empty wears like temperate soil.")]
		public TerrainProcessProfile TerrainProcess;

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
		/// The part this biome plays for a scene's water and ice: the biome painted where a lake, a river, an estuary or a dry
		/// lake bed lies, and the ground of the ice sheet (<see cref="BiomeWaterRole"/>). Until 2026-10-15 the cut found those
		/// biomes by their names; the biome art authoring writes each its role, and any biome given a role is used for it.
		/// </remarks>
		[Header("Water and Ice")]
		[Tooltip("The part this biome plays for the scene's water and ice: Lake, Methane Lake, River, Estuary, Salt Flat (a dry lake bed) or Ice Sheet. None for a biome chosen by climate alone. Written by Author biome art; edits are kept.")]
		[SerializeField] private BiomeWaterRole waterRole = BiomeWaterRole.None;

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
		/// </remarks>
		[Header("Wildlife")]
		[Tooltip("The country this biome offers ambient creatures (birds, bats, small animals, insects). None: the creatures read the climate instead. Written by Author biome art; edits are kept.")]
		[SerializeField] private BiomeHabitat habitat = BiomeHabitat.None;
		[Tooltip("The sea creatures (the sea life's kind names: Reef fish, Sea turtle, Crab, Whale…) most at home in this biome's water: groups of them are their kind's preferred weight times as common here. Written by Author biome art; edits are kept.")]
		[SerializeField] private List<string> seaLifeHomes = new List<string>();

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

		/// <summary>
		/// For each value the biome art authoring writes that is not a texture slot or a spawn rule (those carry their own
		/// marks), a fingerprint of what it last wrote: while the value still matches, nobody has changed it and the authoring
		/// may write it again; once it differs it is somebody's edit and is kept. A field with no mark was never authored.
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
			public const string SeaLifeHomes = nameof(seaLifeHomes);
			public const string Vines = nameof(vines);
			public const string GatheringResources = nameof(gatheringResources);
			public const string HerbGroveHerbs = nameof(herbGroveHerbs);

			/// <summary>The mark of the <see cref="GrassTypes"/> entry for one blade prefab.</summary>
			public static string GrassType(string prefabName) => nameof(grassTypes) + "/" + prefabName;

			/// <summary>The mark of one texture slot's <see cref="TerrainTextureLayer.GroundMakeup"/> (slot keys as BiomeLocalArt's).</summary>
			public static string GroundMakeup(string slotKey) => "groundMakeup/" + slotKey;
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
		/// <summary>The country this biome offers ambient creatures (<see cref="BiomeHabitat"/>); None when not authored, or authored as none.</summary>
		public BiomeHabitat Habitat
		{
			get => habitat;
			set => habitat = value;
		}

		/// <summary>The sea life's kinds most at home in this biome's water, by kind name.</summary>
		public List<string> SeaLifeHomes => seaLifeHomes;

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
			acceptedRocks ??= new List<string>();
			caveShells ??= new List<BiomeCaveShell>();
			seaLifeHomes ??= new List<string>();
			vines ??= new BiomeVines();
			vines.Species ??= new List<BiomeVineSpecies>();
			gatheringResources ??= new List<string>();
			herbGroveFlowers ??= new List<GameObject>();
			herbGroveHerbs ??= new List<GameObject>();
			caveMouthBushes ??= new List<GameObject>();
			caveMouthFerns ??= new List<GameObject>();
			caveMouthBoulders ??= new List<GameObject>();
			caveMouthLogs ??= new List<GameObject>();
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
