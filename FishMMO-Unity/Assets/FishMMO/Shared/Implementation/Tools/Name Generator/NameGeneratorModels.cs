using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Category of title to generate. <see cref="None"/> asks for no title at all;
	/// it is last so authored <c>TitleTemplate.Category</c> values keep their ints.
	/// </summary>
	public enum TitleType { Any, Honorific, Epithet, Rank, Legend, Occupation, None }

	/// <summary>
	/// The social register a title belongs to. A banker wants a Civil title, a
	/// warchief a Martial one, a legendary hero a Mythic one; Any lets the
	/// name's meaning decide.
	/// </summary>
	public enum TitleRegister { Any = 0, Civil = 1, Martial = 2, Mythic = 3 }

	/// <summary>A generated character entry: given name, optional family name and title, and the derived meaning.</summary>
	public class CharacterEntry
	{
		public string Name { get; set; }
		/// <summary>Optional second name drawn from the same phonology; empty when not requested.</summary>
		public string FamilyName { get; set; }
		public string Title { get; set; }
		public string Meaning { get; set; }
		public string Race { get; set; }
		public string TitleCategory { get; set; }
		public List<string> NameFragments { get; set; } = new();

		/// <summary>Given name plus family name when there is one.</summary>
		public string FullName =>
			string.IsNullOrEmpty(FamilyName) ? Name : $"{Name} {FamilyName}";

		/// <summary>Full name plus title when there is one.</summary>
		public string FullTitle =>
			string.IsNullOrEmpty(Title) ? FullName : $"{FullName}, {Title}";

		public string FragmentBreakdown =>
			NameFragments.Count > 0 ? string.Join(" + ", NameFragments) + " → " + Name : Name;

		public override string ToString() => FullTitle;
	}

	/// <summary>Phonology for a single race or cultural variant.</summary>
	public class RacePhonology
	{
		public string[] Onsets { get; set; }
		public string[] Nuclei { get; set; }
		public string[] Codas { get; set; }
		public string[] Middles { get; set; }
		public int SyllMin { get; set; }
		public int SyllMax { get; set; }
		public string[] FeminineSuffixes { get; set; }
		public string[] MasculineSuffixes { get; set; }
		public string Description { get; set; }
		public string[] Tags { get; set; }
		/// <summary>Weighted codas — when set, replaces Codas with weighted selection.</summary>
		public (string item, int weight)[] WeightedCodas { get; set; }
	}

	/// <summary>A generated city entry.</summary>
	public class CityNameEntry
	{
		public string Name { get; set; }
		public string Meaning { get; set; }
		public string Race { get; set; }
		public string CityType { get; set; }
		public List<string> NameFragments { get; set; } = new();

		public string FragmentBreakdown =>
			NameFragments.Count > 0 ? string.Join(" + ", NameFragments) + " → " + Name : Name;

		public override string ToString() => Name;
	}

	/// <summary>City type categories for generation.</summary>
	public enum CityType { Any, Capital, Fortress, Village, Port, Sacred, Ruin }

	/// <summary>Title arrays organized by category for a single race.</summary>
	public class RaceTitles
	{
		/// <summary>Honorifics that fit any gender ("Captain", "Elder").</summary>
		public string[] Honorific { get; set; }
		/// <summary>Honorifics for masculine names ("Sir", "Lord").</summary>
		public string[] HonorificMasculine { get; set; }
		/// <summary>Honorifics for feminine names ("Dame", "Lady").</summary>
		public string[] HonorificFeminine { get; set; }
		public string[] Epithet { get; set; }
		public string[] Rank { get; set; }
		public string[] Legend { get; set; }
		/// <summary>Trades and callings this race practises ("Runesmith", "Brewer"); the grammar's generic list backs it.</summary>
		public string[] Occupational { get; set; }
	}

	/// <summary>City suffix sets for a single race.</summary>
	public class RaceCitySuffixes
	{
		public string[] Capital { get; set; }
		public string[] Fortress { get; set; }
		public string[] Village { get; set; }
		public string[] Port { get; set; }
		public string[] Sacred { get; set; }
		public string[] Ruin { get; set; }
	}

	/// <summary>A generated dungeon entry.</summary>
	public class DungeonNameEntry
	{
		public string Name { get; set; }
		public string Meaning { get; set; }
		public string Biome { get; set; }
		public List<string> NameFragments { get; set; } = new();

		public string FragmentBreakdown =>
			NameFragments.Count > 0 ? string.Join(" + ", NameFragments) + " → " + Name : Name;

		public override string ToString() => Name;
	}

	/// <summary>A generated point-of-interest entry.</summary>
	public class POINameEntry
	{
		public string Name { get; set; }
		public string Meaning { get; set; }
		public string Biome { get; set; }
		public string POIType { get; set; }
		public List<string> NameFragments { get; set; } = new();

		public string FragmentBreakdown =>
			NameFragments.Count > 0 ? string.Join(" + ", NameFragments) + " → " + Name : Name;

		public override string ToString() => Name;
	}

	/// <summary>POI category types for generation.</summary>
	/// <remarks>
	/// Also the KIND of a generated point of interest (<see cref="FishMMO.Shared.PointOfInterestKinds"/>), one map marker
	/// type each. Append only: the ordinal is stored in scene namer settings and POI assets, so inserting renumbers them.
	/// The first eleven predate the generator; the rest were appended with it (2026-10-10).
	/// </remarks>
	public enum POIType
	{
		Any, Landmark, Camp, Shrine, Tower, Bridge,
		Clearing, Spring, Cave, Monument, Wreck,
		// Water
		Waterfall, Rapids, River, Lake, HotSpring, RiverMouth, Delta,
		// Landforms
		Peak, Pass, Gorge, Mesa, Butte, Valley, Island, Bay, Headland, NaturalArch, Sinkhole, Crater, DuneSea, SaltFlat, Glacier,
		// Volcanic
		Volcano, LavaLake, FumaroleField, ObsidianField,
		// Swamp and wetland
		SunkenTemple, DrownedVillage, WitchHut, StiltVillage, BogShrine, MangroveMaze, WispHollow,
		// Terrain-shaped
		Grotto, SeaCave, IceCave, LavaTube, Overhang,
		// Wild sites
		BanditCamp, HuntingLodge, LumberCamp, FishingCamp, MonsterDen, Nest,
		// Sacred and arcane
		RitualSite, StoneCircle, Temple, Monastery, FeyRing, LeyNexus, FallenStar, CorruptedGrove, Portal,
		// The dead
		Graveyard, Barrow, Crypt, Battlefield, Ossuary,
		// Ruins and monuments
		Ruins, RuinedTower, Statue, Obelisk, AncientRoad, AbandonedFarm, Hermitage, Oasis,
		// Settlements and strongholds
		Village, Town, City, Capital, Port, Keep, Castle, Fortress, TradingPost, Waystation, Mine, Quarry, Lighthouse,
		// Coast and sea
		SmugglersCove, PirateCove, CoralReef, SunkenShip, SunkenRuins, SunkenCity,
		// Encounters and resources
		DungeonEntrance, BossLair, OreVein, CrystalFormation, AncientTree, HerbGrove,
		// Alien worlds
		IceGeyserField, Cryovolcano, MethaneLake, ImpactBasin, TidalRift,
		// Appended (the values are serialized): a shipwright's yard on the coast.
		Dockyard,
		// Appended (2026-10-14): mills, a post mill on a rise, a watermill and a sawmill on a river bank.
		Windmill, Watermill, LumberMill,
		// Appended (2026-10-25, phase I2m, decision 36 (b)): the sea floor's own sites, sited only where a biome hosts them.
		SunkenShrine, SeaMonsterDen, SeaCrystalFormation, SunkenMonument, SunkenStatue,
		// Appended (2026-10-25, phase I2p, decision 42): the open sea's surface sites, adrift a kilometre and more from any shore.
		AdriftDerelict, WhaleFall, BarnacleLog, Longline, WhaleTow,
		// Appended (2026-10-25, phase NEW-POI-KINDS): the kinds the biome rounds named, each sited only where a biome hosts it.
		// Underwater Canyon (decision 92): the lip of the canyon's drop, a cave mouth in its wall.
		TrenchEdge, SeaCavern,
		// Volcanic (decision 93): a black cone, an island of old ground in the flows, a stone sled course, lava over a ledge.
		CinderCone, Kipuka, HoluaSlide, LavaFall,
		// Volcanic Temple (decision 95): the smiths' relics, the phoenix's pyre, a sanctuary in a tube mouth.
		AncientForge, PhoenixNest, LavaCaveTemple,
		// Swamp Ruins (decision 87) and Swamp Temple (decision 88): the drowned city's gate and square, and the marsh's own sites.
		MireGate, SunkenPlaza, MarshWatchtower, ShatteredBridge, FallenColossus, SealedVault, DrownedForest,
		FrogShrine, FloatingIsland, FowlersHide, EelWeir, BrickKiln,
		// Wasteland (decision 96).
		BlightedCrater, Chapel, HolyWell, PlagueVillage, DeadForest, WarEngineGraveyard,
		// Abyssal Plain (its Q4): a whale's carcass on the FLOOR, apart from WhaleFall adrift on the surface; Subsurface Ocean
		// Vent (decision 83): the hall of the sea under an ice shell, whose cave form phase I2n-b builds.
		SunkenWhaleFall, SeaHall,
		// The hot ground's own sites (2026-10-27): the geysers, mud pots, fumaroles and sulphur pools the cut digs (a spring's is HotSpring).
		Geyser, MudPot, Fumarole, SulphurPool,
		// The fantasy wave (2026-10-27): 121 kinds hosted only by the biomes whose .Sites name them.
		DragonLair, TrollBridge, GiantsSteading, GoblinWarren, HauntedManor, HangingTree, BuriedHoard, AmbushSite, WreckedCaravan,
		Crossroads, BoundaryStone, SignalBeacon, TollGate, Hamlet, ManorHouse, SiegeCamp, BurnedVillage, WarbandCamp, CultistHideout,
		AncientWall, SpellScar, PlanarRift, PetrifiedBattle, TitansBones, WishingStone, ElderMonolith, WaygateRing, GolemGraveyard,
		KoboldDelving, LostLibrary, DruidGrove, TreeVillage, TrollStones, SpiderThicket, CharcoalClamps, GreenwoodHideout, DryadOak,
		GoblinMarket, ElfWaystone, WolfDen, MossColossus, StepPyramid, Cenote, JaguarTemple, SerpentIdol, LostCity, LizardfolkVillage,
		IdolVault, Grange, Dovecote, BeeGarden, VillageGreen, ScarecrowField, LongBarrow, Henge, GallowsHill, KurganField, NomadCamp,
		StoneFigures, Kraal, Baobab, Shieling, Necropolis, Pyramid, Sphinx, RockCutFacade, SandBuriedTemple, DjinnPrison, SandwormPit,
		CaravanHalt, BeastBoneyard, CliffPalace, DreadRoad, DwarvenGate, MountainKingsHall, HangingMonastery, PassFort, Eyrie,
		GriffinRoost, WyvernNest, CliffRelief, SummitCairn, PrayerFlagPass, SilverMineTown, FrostGiantSteading, RuneStone, FrozenArmy,
		DragonInTheIce, MammothGraveyard, WinterWolfDen, AuroraShrine, LichSpire, IcePalace, FrozenShipwreck, Crannog, HagsCauldron,
		HydraLair, ReedVillage, CovenCircle, LizardfolkHatchery, SwampCathedral, TidalIslandAbbey, CliffCastle, SmugglersTunnel,
		WreckersBeacon, SirenRock, SeaWitchGrotto, LongshipLanding, KrakenWreck, FireTemple, FireGiantHall, SalamanderLair,
		AshBuriedCity, MerfolkVillage, KrakenGrounds, LadyOfTheLake, NymphPool, LakeMonsterLair, Commandery, LostThrone,
		RoyalMausoleum,
	}

	/// <summary>Phonology + vocabulary for a biome.</summary>
	public class BiomePhonology
	{
		public string[] Onsets { get; set; }
		public string[] Nuclei { get; set; }
		public string[] Codas { get; set; }
		public string[] Middles { get; set; }
		public int SyllMin { get; set; }
		public int SyllMax { get; set; }
		public string[] DungeonSuffixes { get; set; }
		public string[] DungeonPrefixes { get; set; }
		public string[] POISuffixes { get; set; }
		public string[] Adjectives { get; set; }
		/// <summary>
		/// This biome's own words for point-of-interest kinds (lowercase <see cref="POIType"/> name →
		/// words): a desert's spring is an Oasis, a swamp's shrine a Bog-altar. Preferred over the
		/// grammar's global type words; a kind with no entry uses those.
		/// </summary>
		public IReadOnlyDictionary<string, string[]> POITypeWords { get; set; } = new Dictionary<string, string[]>();
		public string Description { get; set; }
	}

	/// <summary>Category of legendary item to generate.</summary>
	public enum ItemType { Any, Weapon, Armor, Artifact, Relic, Trinket }

	/// <summary>A generated legendary item entry.</summary>
	public class ItemNameEntry
	{
		public string Name { get; set; }
		public string Meaning { get; set; }
		public string Race { get; set; }
		public string ItemCategory { get; set; }
		public List<string> NameFragments { get; set; } = new();

		public string FragmentBreakdown =>
			NameFragments.Count > 0 ? string.Join(" + ", NameFragments) + " → " + Name : Name;

		public override string ToString() => Name;
	}
}
