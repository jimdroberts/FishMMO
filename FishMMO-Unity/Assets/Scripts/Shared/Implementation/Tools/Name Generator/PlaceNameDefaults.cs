using System;
using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// The built-in place-naming vocabulary: one composition table, a type word list for every
	/// <see cref="POIType"/>, fused endings, generic adjectives and nouns, the water-name phonology and
	/// the weights <see cref="POIType.Any"/> draws kinds with.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every table here is what the generator falls back to when the grammar asset has no entry, so
	/// names work before the content tool has written anything, and a kind appended to
	/// <see cref="POIType"/> without content still names as itself rather than failing. The same tables
	/// are what <c>PlaceNamingContentTool.WriteGrammarDefaults</c> copies into the asset, where designers
	/// then own them (Jim, 2026-10-10: patterns live in the grammar asset, modelled on title templates).
	/// </para>
	/// <para>
	/// Type words for kinds whose name already carries a qualifier ("Sunken Temple", "Hunting Lodge")
	/// are used by compositions that do not stack another adjective in front of them, so the table and
	/// the compositions are written together: change one, read the other.
	/// </para>
	/// </remarks>
	public static class PlaceNameDefaults
	{
		/// <summary>The longest place name the generator returns; map labels and the name toast are sized for it.</summary>
		public const int MaxLength = 40;

		/// <summary>The grammar key a kind's type words are filed under: its lowercase enum name.</summary>
		public static string KeyOf(POIType kind) => kind.ToString().ToLowerInvariant();

		/// <summary>
		/// Kinds whose name depends only on (RegionSeed, ObjectSeed, kind, Index) — never on the biome,
		/// climate variant or race in the request.
		/// </summary>
		/// <remarks>
		/// A planet river runs through many scenes and biomes, and every scene must call it the same thing
		/// (Jim, 2026-10-10: rivers named planet-wide from the body seed and the PlanetRiver). Its falls,
		/// rapids and mouth take the same treatment so that, given the river's seeds, they share its root:
		/// "River Tamar", "Tamar Falls", "Mouth of the Tamar". Lakes follow so a lake reads like the water
		/// that feeds it. The root comes from the shared water phonology and adjectives from the generic
		/// list, never the biome's.
		/// </remarks>
		public static bool IsBiomeFree(POIType kind)
		{
			switch (kind)
			{
				case POIType.River:
				case POIType.Lake:
				case POIType.Waterfall:
				case POIType.Rapids:
				case POIType.RiverMouth:
				case POIType.Delta:
					return true;
				default:
					return false;
			}
		}

		// ── Type words ─────────────────────────────────────────────────

		private static readonly Dictionary<POIType, string[]> typeWords = new Dictionary<POIType, string[]>
		{
			[POIType.Landmark] = new[] { "Stone", "Marker", "Pillar", "Monolith", "Cairn", "Standing Stone", "Waymark", "Stele" },
			[POIType.Camp] = new[] { "Camp", "Encampment", "Bivouac", "Outpost", "Fire-ring", "Shelter", "Tents" },
			[POIType.Shrine] = new[] { "Shrine", "Altar", "Sanctum", "Chapel", "Fane", "Reliquary", "Prayer-stone" },
			[POIType.Tower] = new[] { "Tower", "Watchtower", "Spire", "Turret", "Lookout", "Beacon", "Watch" },
			[POIType.Bridge] = new[] { "Bridge", "Crossing", "Span", "Arch", "Causeway", "Ford", "Stepping Stones" },
			[POIType.Clearing] = new[] { "Clearing", "Glade", "Dell", "Hollow", "Meadow", "Lea", "Green" },
			[POIType.Spring] = new[] { "Spring", "Well", "Pool", "Font", "Source", "Seep", "Wellspring" },
			[POIType.Cave] = new[] { "Cave", "Cavern", "Grotto", "Den", "Hollow", "Hole", "Warren", "Delve" },
			[POIType.Monument] = new[] { "Monument", "Memorial", "Effigy", "Colossus", "Cenotaph", "Pillar", "Cairn" },
			[POIType.Wreck] = new[] { "Wreck", "Hulk", "Shipwreck", "Wreckage", "Keel", "Wrack" },

			[POIType.Waterfall] = new[] { "Falls", "Force", "Cascade", "Linn", "Spout", "Foss", "Cataract", "Chute" },
			[POIType.Rapids] = new[] { "Rapids", "Race", "Shoals", "Riffles", "Narrows", "Whitewater", "Cauldron" },
			[POIType.River] = new[] { "River", "Water", "Run", "Flow", "Stream", "Rill" },
			[POIType.Lake] = new[] { "Lake", "Mere", "Water", "Tarn", "Loch", "Pool", "Broad" },
			[POIType.HotSpring] = new[] { "Hot Spring", "Springs", "Steam Pool", "Baths", "Kettle", "Warm Wells", "Cauldron" },
			[POIType.RiverMouth] = new[] { "Mouth", "Firth", "Estuary", "Outfall", "Inlet", "Water-foot" },
			[POIType.Delta] = new[] { "Delta", "Fens", "Braids", "Mouths", "Splay", "Channels" },

			[POIType.Peak] = new[] { "Peak", "Crag", "Pike", "Horn", "Summit", "Fell", "Tor", "Spire" },
			[POIType.Pass] = new[] { "Pass", "Gap", "Col", "Saddle", "Notch", "Gate", "Defile" },
			[POIType.Gorge] = new[] { "Gorge", "Ravine", "Canyon", "Chasm", "Cleft", "Gulch", "Gill" },
			[POIType.Mesa] = new[] { "Mesa", "Table", "Tableland", "Plateau", "Bench", "Shelf" },
			[POIType.Butte] = new[] { "Butte", "Stack", "Tor", "Pinnacle", "Needle", "Chimney", "Knob" },
			[POIType.Valley] = new[] { "Valley", "Vale", "Dale", "Glen", "Hollow", "Combe", "Strath" },
			[POIType.Island] = new[] { "Isle", "Island", "Holm", "Eyot", "Key", "Skerry", "Ait" },
			[POIType.Bay] = new[] { "Bay", "Cove", "Bight", "Inlet", "Sound", "Haven" },
			[POIType.Headland] = new[] { "Head", "Point", "Ness", "Cape", "Promontory", "Bluff" },
			[POIType.NaturalArch] = new[] { "Arch", "Stone Bridge", "Span", "Eye", "Gate", "Window" },
			[POIType.Sinkhole] = new[] { "Sinkhole", "Swallet", "Pit", "Doline", "Shakehole", "Sink" },
			[POIType.Crater] = new[] { "Crater", "Bowl", "Pit", "Basin", "Cauldron", "Ring" },
			[POIType.DuneSea] = new[] { "Dunes", "Sand Sea", "Erg", "Sands", "Drifts", "Barchans" },
			[POIType.SaltFlat] = new[] { "Salt Flat", "Salt Pan", "Playa", "Salar", "White Flats", "Pan" },
			[POIType.Glacier] = new[] { "Glacier", "Icefield", "Ice River", "Icefall", "Ice Tongue", "Firn" },

			[POIType.Volcano] = new[] { "Volcano", "Cone", "Caldera", "Firemount", "Smokepeak", "Ashcone" },
			[POIType.LavaLake] = new[] { "Lava Lake", "Fire Lake", "Molten Pool", "Crucible", "Ember Mere", "Magma Pool" },
			[POIType.FumaroleField] = new[] { "Fumaroles", "Vents", "Steam Field", "Smokes", "Reek", "Breathing Ground" },
			[POIType.ObsidianField] = new[] { "Glassfield", "Obsidian Field", "Black Glass", "Shards", "Glass Flats", "Knapping Field" },

			[POIType.SunkenTemple] = new[] { "Sunken Temple", "Drowned Fane", "Mire Temple", "Silted Sanctum", "Sunken Shrine", "Drowned Temple" },
			[POIType.DrownedVillage] = new[] { "Hamlet", "Steads", "Village", "Crofts", "Thorpe", "Wick" },
			[POIType.WitchHut] = new[] { "Hut", "Hovel", "Cottage", "Croft", "Shack", "Hutch" },
			[POIType.StiltVillage] = new[] { "Stilt Village", "Stilt-town", "Pile Village", "Raised Hamlet", "Stilthouses", "Platforms" },
			[POIType.BogShrine] = new[] { "Bog-altar", "Fen-shrine", "Mire Altar", "Peat Shrine", "Reed Altar", "Bog Shrine" },
			[POIType.MangroveMaze] = new[] { "Mangroves", "Tangle", "Root-maze", "Snarl", "Labyrinth", "Knot" },
			[POIType.WispHollow] = new[] { "Wisp Hollow", "Lantern Hollow", "Glimmer Dell", "Will-light Hollow", "Corpse-light Glade", "Wisp Dell" },

			[POIType.Grotto] = new[] { "Grotto", "Hollow", "Alcove", "Chamber", "Bower", "Nook" },
			[POIType.SeaCave] = new[] { "Sea Cave", "Sea Cavern", "Blowhole", "Zawn", "Surge Cave", "Tide Cave" },
			[POIType.IceCave] = new[] { "Ice Cave", "Ice Cavern", "Frost Grotto", "Ice Hollow", "Rime Cave", "Ice Vault" },
			[POIType.LavaTube] = new[] { "Lava Tube", "Fire Tube", "Ember Tunnel", "Conduit", "Cinder Tube", "Magma Run" },
			[POIType.Overhang] = new[] { "Overhang", "Ledge", "Lip", "Brow", "Shelf", "Hood" },

			[POIType.BanditCamp] = new[] { "Hideout", "Den", "Roost", "Camp", "Hole", "Lair", "Bolt-hole" },
			[POIType.HuntingLodge] = new[] { "Hunting Lodge", "Lodge", "Hunters' Rest", "Hunting Blind", "Trapper's Cabin", "Hunt Hall" },
			[POIType.LumberCamp] = new[] { "Lumber Camp", "Logging Camp", "Sawpit", "Woodyard", "Timber Camp", "Axe Camp" },
			[POIType.FishingCamp] = new[] { "Fishing Camp", "Fish Weir", "Net-huts", "Smokehouse", "Landing", "Fishery" },
			[POIType.MonsterDen] = new[] { "Den", "Lair", "Burrow", "Warren", "Hole", "Lurk" },
			[POIType.Nest] = new[] { "Nest", "Eyrie", "Rookery", "Roost", "Hive", "Brood" },

			[POIType.RitualSite] = new[] { "Ritual Site", "Blood Ring", "Altar", "Offering Stone", "Rite-ground", "Sacrifice Stone" },
			[POIType.StoneCircle] = new[] { "Stone Circle", "Ring", "Henge", "Stones", "Standing Stones", "Dancers" },
			[POIType.Temple] = new[] { "Temple", "Fane", "Sanctum", "Sanctuary", "Basilica", "House" },
			[POIType.Monastery] = new[] { "Monastery", "Abbey", "Priory", "Cloister", "Friary", "Retreat" },
			[POIType.FeyRing] = new[] { "Fairy Ring", "Fey Ring", "Toadstool Ring", "Dancing Ring", "Elf-circle", "Mushroom Ring" },
			[POIType.LeyNexus] = new[] { "Nexus", "Confluence", "Wellspring", "Lodestone", "Heart", "Crossing" },
			[POIType.FallenStar] = new[] { "Fallen Star", "Starfall", "Skystone", "Star-crater", "Thunderstone", "Starstone" },
			[POIType.CorruptedGrove] = new[] { "Blighted Grove", "Rotwood", "Withered Grove", "Canker Grove", "Sickwood", "Blightwood" },
			[POIType.Portal] = new[] { "Portal", "Gate", "Door", "Threshold", "Waygate", "Rift" },

			[POIType.Graveyard] = new[] { "Graveyard", "Boneyard", "Cemetery", "Barrow-field", "Gravefield", "Burying Ground" },
			[POIType.Barrow] = new[] { "Barrow", "Mound", "Tumulus", "Howe", "Cairn", "Long Barrow" },
			[POIType.Crypt] = new[] { "Crypt", "Tomb", "Vault", "Catacombs", "Sepulchre", "Mausoleum" },
			[POIType.Battlefield] = new[] { "Battlefield", "Field", "Killing Field", "War-field", "Last Stand", "Bloodfield" },
			[POIType.Ossuary] = new[] { "Ossuary", "Charnel House", "Bone Hall", "Bonehouse", "Skull-hall", "Charnel" },

			[POIType.Ruins] = new[] { "Ruins", "Ruin", "Remains", "Rubble", "Old Walls", "Foundations" },
			[POIType.RuinedTower] = new[] { "Ruined Tower", "Broken Tower", "Fallen Spire", "Old Tower", "Tower Stump", "Shell" },
			[POIType.Statue] = new[] { "Statue", "Effigy", "Colossus", "Idol", "Likeness", "Figure" },
			[POIType.Obelisk] = new[] { "Obelisk", "Needle", "Pillar", "Column", "Stele", "Monolith" },
			[POIType.AncientRoad] = new[] { "Old Road", "Way", "Causeway", "Highway", "Road", "Track" },
			[POIType.AbandonedFarm] = new[] { "Farm", "Croft", "Homestead", "Steading", "Farmstead", "Grange" },
			[POIType.Hermitage] = new[] { "Hermitage", "Cell", "Anchorhold", "Retreat", "Refuge", "Hermit's Hut" },
			[POIType.Oasis] = new[] { "Oasis", "Wells", "Palms", "Green", "Spring", "Garden" },

			[POIType.Village] = new[] { "Village", "Hamlet", "Thorpe", "Steading", "Wick", "Stead" },
			[POIType.Town] = new[] { "Town", "Burgh", "Market Town", "Borough", "Stead", "Ton" },
			[POIType.City] = new[] { "City", "Burg", "Citadel", "Metropolis", "Walled City", "Seat" },
			[POIType.Capital] = new[] { "Capital", "Throne-city", "Crown City", "High Seat", "Royal City", "Seat" },
			[POIType.Port] = new[] { "Port", "Harbour", "Haven", "Docks", "Quay", "Anchorage" },
			[POIType.Keep] = new[] { "Keep", "Hold", "Stronghold", "Donjon", "Bastion", "Tower" },
			[POIType.Castle] = new[] { "Castle", "Citadel", "Palace", "Hall", "Bastion", "Court" },
			[POIType.Fortress] = new[] { "Fortress", "Fort", "Fastness", "Bulwark", "Stronghold", "Redoubt" },
			[POIType.TradingPost] = new[] { "Trading Post", "Post", "Market", "Exchange", "Bazaar", "Mart" },
			[POIType.Waystation] = new[] { "Waystation", "Inn", "Waypost", "Coaching Inn", "Halt", "Hostel" },
			[POIType.Mine] = new[] { "Mine", "Pit", "Delving", "Shaft", "Diggings", "Workings", "Adit" },
			[POIType.Quarry] = new[] { "Quarry", "Stonepit", "Cut", "Delf", "Stoneworks", "Diggings" },
			[POIType.Lighthouse] = new[] { "Lighthouse", "Light", "Beacon", "Pharos", "Lamp", "Watchlight" },

			[POIType.SmugglersCove] = new[] { "Smugglers' Cove", "Hidden Cove", "Cove", "Smugglers' Hole", "Free-traders' Landing", "Run" },
			[POIType.PirateCove] = new[] { "Pirate Cove", "Corsair Cove", "Reavers' Bay", "Raiders' Cove", "Hideaway", "Anchorage" },
			[POIType.CoralReef] = new[] { "Reef", "Coral Reef", "Shoals", "Coral Gardens", "Atoll", "Bank" },
			[POIType.SunkenShip] = new[] { "Sunken Ship", "Wreck", "Hulk", "Drowned Galleon", "Sunken Hull", "Lost Ship" },
			[POIType.SunkenRuins] = new[] { "Sunken Ruins", "Drowned Ruins", "Sea Ruins", "Drowned Halls", "Drowned Walls", "Tide Ruins" },
			[POIType.SunkenCity] = new[] { "Sunken City", "Drowned City", "Lost City", "Deep City", "Sea-city", "Drowned Halls" },

			[POIType.DungeonEntrance] = new[] { "Entrance", "Gate", "Mouth", "Descent", "Door", "Stair", "Delve" },
			[POIType.BossLair] = new[] { "Lair", "Throne", "Domain", "Sanctum", "Den", "Seat", "Roost" },
			[POIType.OreVein] = new[] { "Vein", "Lode", "Seam", "Outcrop", "Strike", "Ore Vein" },
			[POIType.CrystalFormation] = new[] { "Crystals", "Crystal Garden", "Geode", "Crystal Spires", "Prisms", "Shardfield" },
			[POIType.AncientTree] = new[] { "Elder Tree", "Great Oak", "Old One", "Father Tree", "Mother Tree", "Grandfather Tree" },
			[POIType.HerbGrove] = new[] { "Herb Grove", "Physic Garden", "Herb Garden", "Simples Grove", "Wortgarth", "Herbwood" },

			[POIType.IceGeyserField] = new[] { "Ice Geysers", "Plumes", "Frost Jets", "Geyser Field", "Cryo-vents", "Ice Fountains" },
			[POIType.Cryovolcano] = new[] { "Cryovolcano", "Ice Volcano", "Frost Cone", "Slush Mount", "Ice Cone", "Cold Caldera" },
			[POIType.MethaneLake] = new[] { "Mare", "Black Lake", "Still Mere", "Tar Lake", "Lacus", "Dark Sea" },
			[POIType.ImpactBasin] = new[] { "Basin", "Crater", "Ring", "Strike", "Scar", "Impact" },
			[POIType.TidalRift] = new[] { "Rift", "Fracture", "Linea", "Chasm", "Rent", "Fissure" },
		};

		/// <summary>The built-in type words for a kind; never empty (an unknown kind answers its spaced name).</summary>
		public static string[] TypeWordsFor(POIType kind)
		{
			return typeWords.TryGetValue(kind, out string[] words) && words.Length > 0
				? words
				: new[] { PointOfInterestKinds.Spaced(kind.ToString()) };
		}

		/// <summary>True when the built-in table has its own words for the kind.</summary>
		public static bool HasTypeWords(POIType kind) => typeWords.ContainsKey(kind);

		// ── Fused endings ({Root}{Suffix}) ─────────────────────────────

		private static readonly Dictionary<POIType, string[]> fusedSuffixes = new Dictionary<POIType, string[]>
		{
			[POIType.River] = new[] { "brook", "burn", "wash", "rill", "bourne", "beck", "water" },
			[POIType.Lake] = new[] { "mere", "tarn", "water", "pool", "loch", "lyn" },
			[POIType.Waterfall] = new[] { "force", "foss", "linn", "spout" },
			[POIType.RiverMouth] = new[] { "mouth", "firth", "wick", "haven" },
			[POIType.Spring] = new[] { "well", "wells", "spring", "font", "seep" },
			[POIType.HotSpring] = new[] { "kettle", "well", "springs" },
			[POIType.Oasis] = new[] { "well", "wells", "spring", "garth" },
			[POIType.Peak] = new[] { "horn", "fell", "crag", "pike", "tor", "berg" },
			[POIType.Valley] = new[] { "dale", "combe", "vale", "glen", "dell" },
			[POIType.Pass] = new[] { "gate", "gap", "col" },
			[POIType.Island] = new[] { "holm", "ey", "isle", "skerry", "ay" },
			[POIType.Bay] = new[] { "wick", "haven", "cove", "bight" },
			[POIType.Headland] = new[] { "ness", "head", "point" },
			[POIType.Clearing] = new[] { "lea", "ley", "glade", "field", "hurst" },
		};

		/// <summary>The built-in fused endings for a kind, or null when it has none of its own.</summary>
		public static string[] FusedSuffixesFor(POIType kind) => fusedSuffixes.TryGetValue(kind, out string[] s) ? s : null;

		// ── Generic adjectives and nouns ───────────────────────────────

		/// <summary>Adjectives any place may take; the only adjectives a planet-wide water name uses, since those must not depend on the biome.</summary>
		public static readonly string[] Adjectives =
		{
			"Old", "Lost", "Forgotten", "Silent", "Hidden", "Broken", "Black", "White", "Red", "Grey", "Green",
			"High", "Deep", "Lonely", "Weeping", "Whispering", "Sleeping", "Sunken", "Hollow", "Shining", "Bitter",
			"Cold", "Golden", "Silver", "Iron", "Long", "Crooked", "Twin", "Still", "Wild", "Bright", "Dark", "Pale",
			"Ashen", "Howling", "Restless", "Drowned", "Ancient", "Wandering", "Singing",
		};

		/// <summary>Nouns for "of the {Adjective} {Noun}" and inn or ship names ("The Grey Gull").</summary>
		public static readonly string[] Nouns =
		{
			"Moon", "Sun", "Star", "Serpent", "Crown", "Raven", "Wolf", "Stag", "Flame", "Tide", "Storm", "Oak",
			"Thorn", "Bone", "Mist", "Lantern", "Hand", "Eye", "Chain", "Bell", "Mother", "Maiden", "King", "Queen",
			"Hound", "Heron", "Gull", "Rose", "Blade", "Shield", "Harp", "Crow", "Owl", "Hart", "Boar", "Bear",
			"Wyrm", "Saint", "Shepherd", "Pilgrim", "Widow", "Giant", "Spear", "Anvil", "Hearth", "Fox", "Hawk",
		};

		// ── Water-name phonology ───────────────────────────────────────

		/// <summary>
		/// The sound of rivers, lakes and falls. Planet-wide rivers cross biomes, so their names cannot come
		/// from any one biome's phonology (Jim, 2026-10-10: rivers named planet-wide from the body seed and
		/// planet river); one shared, old-sounding phonology gives every world's water a family resemblance.
		/// Onsets end in a consonant and codas start with a vowel so roots fuse without smoothing.
		/// </summary>
		public static BiomePhonology WaterPhonology() => new BiomePhonology
		{
			Onsets = new[]
			{
				"Av", "Tam", "Der", "Is", "Sev", "Wen", "Ald", "Cal", "Dun", "Esk", "Glas", "Ken", "Lod", "Mor",
				"Nen", "Ar", "Brath", "Col", "Dov", "El", "Fen", "Gar", "Hal", "Lin", "Sar", "Tem", "Ur", "Var",
			},
			Nuclei = new[] { "a", "e", "i", "o", "ae", "ea", "y", "ou" },
			Codas = new[] { "on", "ar", "er", "en", "an", "el", "ey", "ach", "wy", "is", "et", "ell", "ent", "ock", "ay", "ern", "a", "e" },
			Middles = new[] { "an", "er", "ow", "el", "in", "ad", "or", "ith" },
			SyllMin = 2,
			SyllMax = 3,
			DungeonSuffixes = Array.Empty<string>(),
			DungeonPrefixes = Array.Empty<string>(),
			POISuffixes = Array.Empty<string>(),
			Adjectives = Array.Empty<string>(),
			POITypeWords = new Dictionary<string, string[]>(),
			Description = "Water names",
		};

		// ── Kind weights for POIType.Any ───────────────────────────────

		/// <summary>How often <see cref="POIType.Any"/> draws each kind; kinds not listed draw at <see cref="DefaultKindWeight"/>.</summary>
		public const int DefaultKindWeight = 4;

		private static readonly Dictionary<POIType, int> kindWeights = new Dictionary<POIType, int>
		{
			// The ten kinds that predate the catalogue keep their old relative shares.
			[POIType.Landmark] = 15, [POIType.Camp] = 10, [POIType.Shrine] = 10, [POIType.Tower] = 10,
			[POIType.Bridge] = 10, [POIType.Clearing] = 10, [POIType.Spring] = 10, [POIType.Cave] = 10,
			[POIType.Monument] = 8, [POIType.Wreck] = 7,
			// Big places are rare; alien kinds only make sense on their own worlds.
			[POIType.Capital] = 1, [POIType.City] = 2, [POIType.Town] = 3, [POIType.Fortress] = 2, [POIType.Castle] = 2,
			[POIType.SunkenCity] = 1, [POIType.BossLair] = 2, [POIType.Portal] = 2, [POIType.FallenStar] = 2, [POIType.LeyNexus] = 2,
			[POIType.IceGeyserField] = 1, [POIType.Cryovolcano] = 1, [POIType.MethaneLake] = 1, [POIType.ImpactBasin] = 1, [POIType.TidalRift] = 1,
		};

		/// <summary>The built-in draw weight for a kind under <see cref="POIType.Any"/>.</summary>
		public static int KindWeight(POIType kind)
		{
			if (kind == POIType.Any)
			{
				return 0;
			}
			return kindWeights.TryGetValue(kind, out int weight) ? weight : DefaultKindWeight;
		}

		// ── Compositions ───────────────────────────────────────────────

		// Kind sets the compositions below share.
		private static readonly POIType[] RaceCaves = { POIType.Cave, POIType.IceCave, POIType.LavaTube };
		private static readonly POIType[] RaceCamps = { POIType.Camp, POIType.BanditCamp };
		private static readonly POIType[] Dens = { POIType.MonsterDen, POIType.Nest };
		private static readonly POIType[] WorkCamps = { POIType.HuntingLodge, POIType.LumberCamp, POIType.FishingCamp };
		private static readonly POIType[] Springs = { POIType.Spring, POIType.HotSpring };
		private static readonly POIType[] Mounts = { POIType.Volcano, POIType.Cryovolcano };
		/// <summary>Ground shapes and natural features named "{Root} {Type}" or "The {Adjective} {Type}".</summary>
		private static readonly POIType[] Nature =
		{
			POIType.Clearing, POIType.Pass, POIType.Gorge, POIType.Mesa, POIType.Butte, POIType.Valley, POIType.Bay,
			POIType.Headland, POIType.NaturalArch, POIType.Sinkhole, POIType.Crater, POIType.DuneSea, POIType.SaltFlat,
			POIType.Glacier, POIType.LavaLake, POIType.FumaroleField, POIType.ObsidianField, POIType.MangroveMaze,
			POIType.Grotto, POIType.SeaCave, POIType.Overhang, POIType.CoralReef, POIType.IceGeyserField,
			POIType.MethaneLake, POIType.ImpactBasin, POIType.TidalRift, POIType.WispHollow,
		};
		/// <summary>Natural kinds whose plain type word takes an adjective well ("The Black Gorge").</summary>
		private static readonly POIType[] PlainNature =
		{
			POIType.Clearing, POIType.Pass, POIType.Gorge, POIType.Mesa, POIType.Butte, POIType.Valley, POIType.Bay,
			POIType.Headland, POIType.NaturalArch, POIType.Sinkhole, POIType.Crater, POIType.DuneSea, POIType.Glacier,
			POIType.MangroveMaze, POIType.Grotto, POIType.Overhang, POIType.CoralReef, POIType.MethaneLake,
			POIType.ImpactBasin, POIType.TidalRift,
		};
		private static readonly POIType[] FusedNature = { POIType.Valley, POIType.Pass, POIType.Bay, POIType.Headland, POIType.Clearing };
		private static readonly POIType[] Sacred =
		{
			POIType.Shrine, POIType.Temple, POIType.StoneCircle, POIType.RitualSite, POIType.Monastery, POIType.FeyRing,
			POIType.LeyNexus, POIType.FallenStar, POIType.CorruptedGrove, POIType.Portal, POIType.BogShrine,
			POIType.SunkenTemple,
		};
		private static readonly POIType[] PlainSacred = { POIType.Shrine, POIType.Temple, POIType.StoneCircle, POIType.Monastery, POIType.Portal, POIType.LeyNexus };
		private static readonly POIType[] RaceSacred = { POIType.Shrine, POIType.Temple, POIType.RitualSite, POIType.Monastery, POIType.SunkenTemple, POIType.StoneCircle };
		private static readonly POIType[] Dead = { POIType.Graveyard, POIType.Barrow, POIType.Crypt, POIType.Battlefield, POIType.Ossuary };
		private static readonly POIType[] Likenesses = { POIType.Monument, POIType.Statue, POIType.Obelisk };
		private static readonly POIType[] Homes = { POIType.AbandonedFarm, POIType.Hermitage };
		private static readonly POIType[] Towns = { POIType.Village, POIType.Town, POIType.City, POIType.Capital, POIType.Port, POIType.StiltVillage };
		private static readonly POIType[] Works = { POIType.Tower, POIType.Bridge, POIType.Mine, POIType.Quarry, POIType.Lighthouse, POIType.Waystation };
		private static readonly POIType[] RaceWorks = { POIType.Tower, POIType.Mine, POIType.Quarry };
		private static readonly POIType[] Coves = { POIType.SmugglersCove, POIType.PirateCove };
		private static readonly POIType[] Wrecks = { POIType.Wreck, POIType.SunkenShip };
		private static readonly POIType[] Resources = { POIType.OreVein, POIType.CrystalFormation, POIType.HerbGrove };

		private static PlaceNameTemplate P(string pattern, int weight, params POIType[] kinds)
			=> new PlaceNameTemplate { Pattern = pattern, Weight = weight, Kinds = new List<POIType>(kinds) };

		/// <summary>
		/// Compositions used when the grammar asset has none. Slots: <c>{Type} {Root} {Suffix} {Adjective}
		/// {Noun} {RacePlural} {RaceAdjective} {Founder} {City} {Dungeon}</c>; <c>{Root}{Suffix}</c> written
		/// together fuses into one word ("Morrowmere").
		/// </summary>
		public static readonly PlaceNameTemplate[] Templates =
		{
			// Water. Rivers, their falls and mouths, and lakes are biome-free (see PlaceNaming.IsBiomeFree).
			P("{Root} River", 30, POIType.River),
			P("River {Root}", 20, POIType.River),
			P("The {Root}", 20, POIType.River),
			P("{Root}{Suffix}", 15, POIType.River),
			P("{Root} {Type}", 8, POIType.River),
			P("The {Adjective} {Type}", 6, POIType.River),
			P("Lake {Root}", 25, POIType.Lake),
			P("{Root} Mere", 15, POIType.Lake),
			P("{Root} Water", 12, POIType.Lake),
			P("{Root}{Suffix}", 15, POIType.Lake),
			P("{Root} {Type}", 12, POIType.Lake),
			P("The {Adjective} {Type}", 10, POIType.Lake),
			P("{Founder}'s {Type}", 4, POIType.Lake),
			P("{Root} Falls", 30, POIType.Waterfall),
			P("The {Adjective} Falls", 15, POIType.Waterfall),
			P("{Root} {Type}", 15, POIType.Waterfall),
			P("{Root}{Suffix}", 8, POIType.Waterfall),
			P("The {Adjective} {Type}", 8, POIType.Waterfall),
			P("{Founder}'s Leap", 4, POIType.Waterfall),
			P("{Root} {Type}", 30, POIType.Rapids, POIType.RiverMouth, POIType.Delta),
			P("The {Adjective} {Type}", 15, POIType.Rapids, POIType.Delta),
			P("{Founder}'s {Type}", 5, POIType.Rapids),
			P("Mouth of the {Root}", 15, POIType.RiverMouth),
			P("{Root}{Suffix}", 8, POIType.RiverMouth),
			P("The {Root} {Type}", 10, POIType.Delta),

			// Springs and oases.
			P("{Root} {Type}", 30, POIType.Spring, POIType.HotSpring, POIType.Oasis),
			P("The {Adjective} {Type}", 18, POIType.Spring, POIType.Oasis),
			P("{Founder}'s {Type}", 10, POIType.Spring, POIType.HotSpring, POIType.Oasis),
			P("{Root}{Suffix}", 10, POIType.Spring, POIType.HotSpring, POIType.Oasis),
			P("{Type} of the {Adjective} {Noun}", 4, Springs),

			// Heights.
			P("Mount {Root}", 20, POIType.Peak),
			P("{Root} {Type}", 25, POIType.Peak),
			P("{Root}{Suffix}", 15, POIType.Peak),
			P("The {Adjective} {Type}", 12, POIType.Peak),
			P("The {Adjective} Spire", 4, POIType.Peak),
			P("Mount {Root}", 25, Mounts),
			P("{Root} {Type}", 25, Mounts),
			P("The {Adjective} {Type}", 12, Mounts),
			P("The {Adjective} Mountain", 5, POIType.Volcano),
			P("{Root} Mons", 10, POIType.Cryovolcano),

			// Islands.
			P("{Root} {Type}", 25, POIType.Island),
			P("Isle of {Root}", 15, POIType.Island),
			P("{Root}{Suffix}", 15, POIType.Island),
			P("The {Adjective} {Type}", 12, POIType.Island),
			P("{Founder}'s {Type}", 6, POIType.Island),

			// Ground shapes and natural features.
			P("{Root} {Type}", 35, Nature),
			P("The {Adjective} {Type}", 22, PlainNature),
			P("{Type} of {Root}", 6, Nature),
			P("{Root}{Suffix}", 10, FusedNature),
			P("Bay of {Root}", 10, POIType.Bay),
			P("{Founder}'s {Type}", 5, POIType.Pass, POIType.Overhang, POIType.Grotto, POIType.Clearing, POIType.SeaCave),

			// Caves people live in.
			P("{Type} of {RacePlural}", 20, RaceCaves),
			P("{Type} of the {RacePlural}", 8, RaceCaves),
			P("{RaceAdjective} {Type}", 18, RaceCaves),
			P("{Root} {Type}", 25, RaceCaves),
			P("The {Adjective} {Type}", 18, POIType.Cave),
			P("{Type} of the {Adjective} {Noun}", 4, RaceCaves),

			// Camps, dens and lodges.
			P("{RaceAdjective} {Type}", 20, RaceCamps),
			P("{Type} of {RacePlural}", 8, RaceCamps),
			P("{Founder}'s {Type}", 15, RaceCamps),
			P("{Root} {Type}", 20, RaceCamps),
			P("The {Adjective} {Type}", 12, RaceCamps),
			P("{RaceAdjective} {Type}", 25, Dens),
			P("{Type} of {RacePlural}", 15, Dens),
			P("{Root} {Type}", 20, Dens),
			P("The {Adjective} {Type}", 15, Dens),
			P("{Founder}'s {Type}", 25, WorkCamps),
			P("{Root} {Type}", 25, WorkCamps),

			// Wetland.
			P("{Founder}'s {Type}", 20, POIType.WitchHut),
			P("Mother {Founder}'s {Type}", 6, POIType.WitchHut),
			P("{Root} {Type}", 15, POIType.WitchHut),
			P("The {Adjective} {Type}", 10, POIType.WitchHut),
			P("Drowned {City}", 25, POIType.DrownedVillage),
			P("Sunken {City}", 10, POIType.DrownedVillage),
			P("The Drowned {Type}", 10, POIType.DrownedVillage),
			P("{Dungeon}", 15, POIType.SunkenTemple),

			// Sacred and arcane.
			P("{Type} of the {Adjective} {Noun}", 18, Sacred),
			P("{Root} {Type}", 22, Sacred),
			P("{Type} of {Root}", 8, Sacred),
			P("The {Adjective} {Type}", 15, PlainSacred),
			P("{Founder}'s {Type}", 8, POIType.Shrine, POIType.Temple, POIType.Monastery, POIType.Portal, POIType.StoneCircle),
			P("{Type} of {Founder}", 6, POIType.Shrine, POIType.Temple, POIType.Monastery),
			P("{RaceAdjective} {Type}", 10, RaceSacred),

			// The dead.
			P("{Founder}'s {Type}", 15, POIType.Graveyard, POIType.Barrow, POIType.Crypt, POIType.Ossuary),
			P("{Type} of {Founder}", 6, POIType.Barrow, POIType.Crypt),
			P("{Root} {Type}", 20, Dead),
			P("The {Adjective} {Type}", 15, Dead),
			P("{Type} of the {Adjective} {Noun}", 10, Dead),
			P("{RaceAdjective} {Type}", 10, Dead),
			P("{Type} of the {RacePlural}", 6, Dead),
			P("{Dungeon}", 12, POIType.Crypt),

			// Ruins and monuments.
			P("Ruins of {Root}", 20, POIType.Ruins),
			P("{Root} {Type}", 15, POIType.Ruins),
			P("The {Adjective} {Type}", 12, POIType.Ruins),
			P("{RaceAdjective} {Type}", 15, POIType.Ruins, POIType.RuinedTower, POIType.AncientRoad),
			P("{Type} of the {RacePlural}", 5, POIType.Ruins),
			P("{Root} {Type}", 20, POIType.RuinedTower, POIType.AncientRoad),
			P("{Founder}'s {Type}", 10, POIType.RuinedTower, POIType.AncientRoad),
			P("The {Adjective} {Type}", 12, POIType.AncientRoad),
			P("{Type} of {Founder}", 15, Likenesses),
			P("{Founder}'s {Type}", 10, Likenesses),
			P("The {Adjective} {Type}", 15, Likenesses),
			P("{Root} {Type}", 15, Likenesses),
			P("{Type} of the {Adjective} {Noun}", 8, Likenesses),
			P("{RaceAdjective} {Type}", 6, Likenesses),
			P("{Founder}'s {Type}", 25, Homes),
			P("{Root} {Type}", 18, Homes),
			P("The {Adjective} {Type}", 8, Homes),
			P("{Type} of {Founder}", 5, POIType.Hermitage),

			// Settlements: the city builder names the place, compositions dress it.
			P("{City}", 100, Towns),
			P("{Root} {Type}", 4, Towns),
			P("{City} Keep", 25, POIType.Keep),
			P("{City}", 15, POIType.Keep, POIType.Castle),
			P("{Root} {Type}", 15, POIType.Keep, POIType.Castle),
			P("{Founder}'s {Type}", 10, POIType.Keep),
			P("The {Adjective} {Type}", 6, POIType.Keep, POIType.Castle, POIType.Fortress),
			P("Castle {Root}", 15, POIType.Castle),
			P("{City} Castle", 20, POIType.Castle),
			P("{City}", 25, POIType.Fortress),
			P("Fort {Root}", 12, POIType.Fortress),
			P("{City} {Type}", 15, POIType.Fortress, POIType.TradingPost),
			P("{Founder}'s {Type}", 20, POIType.TradingPost),
			P("{Root} {Type}", 15, POIType.TradingPost),
			P("{Root} {Type}", 25, Works),
			P("{Founder}'s {Type}", 15, Works),
			P("The {Adjective} {Type}", 12, POIType.Tower, POIType.Bridge, POIType.Lighthouse),
			P("The {Adjective} {Noun}", 15, POIType.Waystation),
			P("{RaceAdjective} {Type}", 10, RaceWorks),

			// Coast and sea.
			P("Wreck of the {Adjective} {Noun}", 20, POIType.Wreck),
			P("{Type} of the {Adjective} {Noun}", 15, Wrecks),
			P("{Root} {Type}", 15, Wrecks),
			P("The {Adjective} {Noun}", 5, Wrecks),
			P("{Root} {Type}", 25, Coves),
			P("{Founder}'s {Type}", 15, Coves),
			P("{Type} of {Root}", 8, Coves),
			P("{Type} of {Root}", 15, POIType.SunkenRuins, POIType.SunkenCity),
			P("{Root} {Type}", 15, POIType.SunkenRuins),
			P("{RaceAdjective} {Type}", 10, POIType.SunkenRuins, POIType.SunkenCity),
			P("Drowned {City}", 20, POIType.SunkenCity),
			P("Sunken {City}", 15, POIType.SunkenCity),
			P("{Type} of {RacePlural}", 5, POIType.SunkenCity),

			// Encounters and resources.
			P("{Dungeon}", 40, POIType.DungeonEntrance),
			P("{Root} {Type}", 15, POIType.DungeonEntrance, POIType.BossLair),
			P("The {Adjective} {Type}", 12, POIType.DungeonEntrance, POIType.BossLair),
			P("{Type} of {Root}", 8, POIType.DungeonEntrance),
			P("{Dungeon}", 20, POIType.BossLair),
			P("{Type} of the {Adjective} {Noun}", 15, POIType.BossLair),
			P("{RaceAdjective} {Type}", 5, POIType.BossLair),
			P("{Root} {Type}", 25, Resources),
			P("The {Adjective} {Type}", 15, Resources),
			P("{Founder}'s {Type}", 8, Resources),
			P("The {Adjective} {Type}", 25, POIType.AncientTree),
			P("{Root} {Type}", 15, POIType.AncientTree),
			P("{Founder}'s {Type}", 10, POIType.AncientTree),
			P("The {Type} of {Root}", 8, POIType.AncientTree),

			// Landmarks.
			P("{Root} {Type}", 25, POIType.Landmark),
			P("The {Adjective} {Type}", 15, POIType.Landmark),
			P("{Founder}'s {Type}", 10, POIType.Landmark),
			P("{Type} of the {Adjective} {Noun}", 4, POIType.Landmark),
		};
	}
}
