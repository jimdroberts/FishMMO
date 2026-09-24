#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Gives a biome that carries no naming data a phonology, so the name generator can name its
	/// dungeons and landmarks.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The 23 biomes added when the catalogue grew from 59 to 82 — the airless, cryogenic, volcanic
	/// and gas-giant-moon sets, plus the Earth gaps — were never given naming data, so
	/// <c>BiomeRegistry.NameableBiomes</c> offered 58 of 81 and every one of those zones produced
	/// no place names at all. Nothing reported it: a biome without naming is still registered for
	/// terrain and maps, and is quietly skipped by the generator.
	/// </para>
	/// <para>
	/// <b>An explicit table, never a guesser.</b> A keyword classifier over asset names has already
	/// corrupted deliberately-authored biomes in this project once — "plain", "lake" and "geyser"
	/// matched Regolith Plain, Methane Lake and Ice Geyser Field and gave them Earth requirements.
	/// Every entry below is written out, matched on the exact asset name, and a biome that already
	/// has usable naming is left alone, so this is safe to re-run and cannot reach anything it was
	/// not named for.
	/// </para>
	/// </remarks>
	public static class BiomeNamingGenerator
	{
		/// <summary>One biome's sound and vocabulary.</summary>
		private sealed class Voice
		{
			public string[] Onsets;
			public string[] Nuclei;
			public string[] Codas;
			public string[] Middles;
			public string[] DungeonSuffixes;
			public string[] DungeonPrefixes;
			public string[] POISuffixes;
			public string[] Adjectives;
			public string Description;
		}

		/// <summary>Shared endings, so a set of related biomes sounds like a set.</summary>
		private static readonly string[] AirlessDungeons = { "Cavern", "Vault", "Shaft", "Hollow", "Pit", "Chamber", "Tube", "Rift" };
		private static readonly string[] CryoDungeons = { "Grotto", "Vault", "Hollow", "Fissure", "Chamber", "Gallery", "Shaft", "Crevasse" };
		private static readonly string[] VolcanicDungeons = { "Forge", "Vent", "Chamber", "Tube", "Conduit", "Furnace", "Shaft", "Crucible" };

		private static readonly Dictionary<string, Voice> Voices = new Dictionary<string, Voice>
		{
			// ── Earth gaps ────────────────────────────────────────────
			["Bamboo Forest"] = new Voice
			{
				Onsets = new[] { "Bam", "Cul", "Shin", "Rin", "Tak", "Sasa", "Mio", "Hasu", "Kure", "Nao" },
				Nuclei = new[] { "a", "e", "i", "o", "ai", "ou" },
				Codas = new[] { "grove", "cane", "stalk", "thicket", "shade", "node", "reed", "stand" },
				Middles = new[] { "no", "ka", "shi", "ra", "ma" },
				DungeonSuffixes = new[] { "Grove", "Hollow", "Shrine", "Thicket", "Path", "Retreat", "Gallery", "Stand" },
				DungeonPrefixes = new[] { "The Whispering", "The Green", "The Endless", "The Swaying", "The Hidden", "The Rustling", "The Shaded", "The Silent" },
				POISuffixes = new[] { "Grove", "Stand", "Clearing", "Walk", "Screen", "Thicket" },
				Adjectives = new[] { "Whispering", "Green", "Swaying", "Shaded", "Dense", "Rustling" },
				Description = "Dense canes that creak and whisper in any wind.",
			},
			["Karst"] = new Voice
			{
				Onsets = new[] { "Kar", "Dol", "Pol", "Sink", "Grik", "Ven", "Cav", "Lim", "Tor", "Sce" },
				Nuclei = new[] { "a", "o", "i", "au", "ar", "or" },
				Codas = new[] { "stone", "hole", "shaft", "spire", "pavement", "sink", "gorge", "clint" },
				Middles = new[] { "an", "ov", "er", "il", "as" },
				DungeonSuffixes = new[] { "Sinkhole", "Cavern", "Shaft", "Gallery", "Passage", "Swallet", "Chamber", "Descent" },
				DungeonPrefixes = new[] { "The Hollow", "The Riddled", "The Sunken", "The Pale", "The Dripping", "The Fluted", "The Honeycombed", "The Unmapped" },
				POISuffixes = new[] { "Pavement", "Spire", "Sink", "Gorge", "Arch", "Pinnacle" },
				Adjectives = new[] { "Hollow", "Fluted", "Pale", "Riddled", "Dripping", "Sunken" },
				Description = "Limestone eaten through until the ground is more hole than rock.",
			},
			["Geyser Basin"] = new Voice
			{
				Onsets = new[] { "Gey", "Sput", "Bois", "Ful", "Stro", "Halde", "Mud", "Scald", "Brim", "Ker" },
				Nuclei = new[] { "a", "e", "u", "ey", "ou", "i" },
				Codas = new[] { "spring", "pool", "vent", "terrace", "basin", "fumarole", "sinter", "cauldron" },
				Middles = new[] { "an", "er", "ul", "is", "ot" },
				DungeonSuffixes = new[] { "Cauldron", "Vent", "Basin", "Chamber", "Conduit", "Terrace", "Hollow", "Spring" },
				DungeonPrefixes = new[] { "The Boiling", "The Scalding", "The Sulphur", "The Roaring", "The Steaming", "The Bitter", "The Hissing", "The Mineral" },
				POISuffixes = new[] { "Spring", "Pool", "Terrace", "Vent", "Basin", "Cauldron" },
				Adjectives = new[] { "Boiling", "Scalding", "Steaming", "Bitter", "Roaring", "Mineral" },
				Description = "Ground that breathes steam and throws boiling water without warning.",
			},
			["Peat Bog"] = new Voice
			{
				Onsets = new[] { "Mir", "Fen", "Quag", "Tur", "Sphag", "Moss", "Bol", "Duns", "Gleen", "Hag" },
				Nuclei = new[] { "a", "o", "e", "oo", "ir", "ai" },
				Codas = new[] { "bog", "mire", "fen", "moss", "peat", "hag", "slough", "marsh" },
				Middles = new[] { "en", "an", "ul", "er", "ow" },
				DungeonSuffixes = new[] { "Sump", "Hollow", "Mire", "Warren", "Pit", "Burrow", "Chamber", "Cut" },
				DungeonPrefixes = new[] { "The Sunken", "The Black", "The Drowned", "The Sodden", "The Silent", "The Clinging", "The Old", "The Peat-dark" },
				POISuffixes = new[] { "Mire", "Fen", "Moss", "Cut", "Hollow", "Flat" },
				Adjectives = new[] { "Sodden", "Black", "Drowned", "Clinging", "Silent", "Peat-dark" },
				Description = "Wet ground that keeps everything it swallows.",
			},
			["Salt Flat"] = new Voice
			{
				Onsets = new[] { "Sal", "Pla", "Bri", "Sod", "Hal", "Cru", "Alk", "Ver", "Sec", "Nit" },
				Nuclei = new[] { "a", "i", "e", "ay", "ar", "ou" },
				Codas = new[] { "pan", "flat", "crust", "playa", "salar", "waste", "rime", "bed" },
				Middles = new[] { "an", "er", "il", "os", "ar" },
				DungeonSuffixes = new[] { "Pan", "Hollow", "Shaft", "Cellar", "Vault", "Cut", "Chamber", "Works" },
				DungeonPrefixes = new[] { "The White", "The Blinding", "The Cracked", "The Endless", "The Bitter", "The Glaring", "The Dry", "The Level" },
				POISuffixes = new[] { "Pan", "Flat", "Crust", "Waste", "Bed", "Rim" },
				Adjectives = new[] { "White", "Blinding", "Cracked", "Bitter", "Glaring", "Level" },
				Description = "A white sheet of salt that gives back every scrap of light.",
			},
			["Steppe"] = new Voice
			{
				Onsets = new[] { "Step", "Kur", "Tar", "Yur", "Ald", "Cher", "Bay", "Otar", "Sar", "Dzu" },
				Nuclei = new[] { "a", "o", "u", "ai", "an", "ur" },
				Codas = new[] { "grass", "steppe", "reach", "sweep", "range", "waste", "veldt", "run" },
				Middles = new[] { "an", "ar", "un", "ol", "er" },
				DungeonSuffixes = new[] { "Kurgan", "Barrow", "Hollow", "Pit", "Cairn", "Tomb", "Warren", "Shelter" },
				DungeonPrefixes = new[] { "The Windswept", "The Endless", "The Open", "The Dry", "The Trackless", "The Wide", "The Grass-hidden", "The Riders'" },
				POISuffixes = new[] { "Reach", "Sweep", "Range", "Run", "Waste", "Rise" },
				Adjectives = new[] { "Windswept", "Endless", "Open", "Trackless", "Dry", "Wide" },
				Description = "Grass to the horizon in every direction, and nothing to break the wind.",
			},

			// ── Airless ───────────────────────────────────────────────
			["Regolith Plain"] = new Voice
			{
				Onsets = new[] { "Reg", "Mar", "Sel", "Cin", "Dus", "Alb", "Ter", "Nub", "Ser", "Gri" },
				Nuclei = new[] { "a", "o", "i", "ae", "eu", "ia" },
				Codas = new[] { "dust", "mare", "plain", "regolith", "grey", "waste", "expanse", "floor" },
				Middles = new[] { "an", "or", "el", "us", "ar" },
				DungeonSuffixes = AirlessDungeons,
				DungeonPrefixes = new[] { "The Airless", "The Grey", "The Silent", "The Cratered", "The Static", "The Sunbaked", "The Lifeless", "The Unshielded" },
				POISuffixes = new[] { "Plain", "Mare", "Waste", "Expanse", "Flat", "Floor" },
				Adjectives = new[] { "Airless", "Grey", "Silent", "Cratered", "Lifeless", "Static" },
				Description = "Ground-up rock that has never known weather, and holds every footprint.",
			},
			["Impact Basin"] = new Voice
			{
				Onsets = new[] { "Imp", "Cra", "Ej", "Bas", "Ring", "Pal", "Cal", "Struc", "Rim", "Ter" },
				Nuclei = new[] { "a", "e", "i", "ae", "ou", "ia" },
				Codas = new[] { "crater", "basin", "rim", "ring", "scar", "ejecta", "floor", "wall" },
				Middles = new[] { "an", "or", "el", "us", "ir" },
				DungeonSuffixes = AirlessDungeons,
				DungeonPrefixes = new[] { "The Shattered", "The Ringed", "The Deep", "The Glassed", "The Ancient", "The Struck", "The Terraced", "The Buried" },
				POISuffixes = new[] { "Basin", "Rim", "Ring", "Scar", "Floor", "Terrace" },
				Adjectives = new[] { "Shattered", "Ringed", "Glassed", "Ancient", "Struck", "Deep" },
				Description = "A hole a mountain-sized rock left, with its rim still standing.",
			},
			["Rille"] = new Voice
			{
				Onsets = new[] { "Ril", "Sin", "Ari", "Hyg", "Vall", "Gra", "Cle", "Ser", "Tor", "Hae" },
				Nuclei = new[] { "a", "i", "e", "ae", "ei", "ou" },
				Codas = new[] { "rille", "channel", "trench", "furrow", "groove", "valley", "cleft", "run" },
				Middles = new[] { "an", "in", "el", "us", "ar" },
				DungeonSuffixes = AirlessDungeons,
				DungeonPrefixes = new[] { "The Sinuous", "The Collapsed", "The Long", "The Narrow", "The Winding", "The Sunken", "The Dark", "The Straight" },
				POISuffixes = new[] { "Rille", "Channel", "Trench", "Cleft", "Groove", "Run" },
				Adjectives = new[] { "Sinuous", "Collapsed", "Narrow", "Winding", "Sunken", "Long" },
				Description = "A collapsed lava channel cut straight across dead ground.",
			},
			["Lava Tube"] = new Voice
			{
				Onsets = new[] { "Tub", "Con", "Ves", "Pahoe", "Bas", "Sker", "Ig", "Fum", "Cav", "Thur" },
				Nuclei = new[] { "a", "o", "e", "au", "oe", "ur" },
				Codas = new[] { "tube", "conduit", "vault", "gallery", "run", "throat", "shaft", "hollow" },
				Middles = new[] { "an", "or", "el", "ur", "is" },
				DungeonSuffixes = VolcanicDungeons,
				DungeonPrefixes = new[] { "The Hollow", "The Black", "The Glassy", "The Sealed", "The Winding", "The Cooled", "The Deep", "The Echoing" },
				POISuffixes = new[] { "Tube", "Conduit", "Gallery", "Throat", "Vault", "Run" },
				Adjectives = new[] { "Hollow", "Glassy", "Black", "Sealed", "Echoing", "Cooled" },
				Description = "A drained lava conduit, smooth-walled and perfectly dark.",
			},
			["Dust Sea"] = new Voice
			{
				Onsets = new[] { "Dus", "Pow", "Fin", "Drif", "Sif", "Mot", "Pal", "Vel", "Shoa", "Ashe" },
				Nuclei = new[] { "a", "u", "i", "ou", "ea", "ai" },
				Codas = new[] { "sea", "drift", "shoal", "deep", "powder", "dune", "swell", "bed" },
				Middles = new[] { "an", "er", "ul", "or", "is" },
				DungeonSuffixes = AirlessDungeons,
				DungeonPrefixes = new[] { "The Bottomless", "The Silent", "The Drowning", "The Grey", "The Soft", "The Shifting", "The Trackless", "The Deep" },
				POISuffixes = new[] { "Sea", "Drift", "Shoal", "Deep", "Swell", "Dune" },
				Adjectives = new[] { "Bottomless", "Drowning", "Shifting", "Soft", "Trackless", "Silent" },
				Description = "Dust so fine and so deep that it swallows whatever stands on it.",
			},
			["Radiation Plain"] = new Voice
			{
				Onsets = new[] { "Rad", "Ion", "Bel", "Flux", "Cher", "Gam", "Sear", "Volt", "Arc", "Sync" },
				Nuclei = new[] { "a", "i", "o", "ae", "eu", "io" },
				Codas = new[] { "flux", "belt", "glare", "storm", "field", "burn", "sleet", "wash" },
				Middles = new[] { "an", "or", "el", "is", "us" },
				DungeonSuffixes = AirlessDungeons,
				DungeonPrefixes = new[] { "The Scoured", "The Glowing", "The Unshielded", "The Sleeting", "The Hard", "The Blistered", "The Screaming", "The Bright" },
				POISuffixes = new[] { "Flux", "Belt", "Field", "Glare", "Burn", "Wash" },
				Adjectives = new[] { "Scoured", "Unshielded", "Blistered", "Glowing", "Sleeting", "Hard" },
				Description = "Ground under a magnetosphere's fire, where the sky itself is the hazard.",
			},
			["Tidal Fracture"] = new Voice
			{
				Onsets = new[] { "Tid", "Fra", "Lin", "Cyc", "Flex", "Stra", "Chas", "Rup", "Ten", "Sul" },
				Nuclei = new[] { "a", "i", "e", "ae", "ou", "ia" },
				Codas = new[] { "fracture", "linea", "chasm", "rift", "seam", "cleft", "band", "split" },
				Middles = new[] { "an", "or", "el", "us", "ir" },
				DungeonSuffixes = CryoDungeons,
				DungeonPrefixes = new[] { "The Flexing", "The Grinding", "The Open", "The Restless", "The Split", "The Groaning", "The Shifting", "The Banded" },
				POISuffixes = new[] { "Fracture", "Linea", "Chasm", "Rift", "Seam", "Band" },
				Adjectives = new[] { "Flexing", "Grinding", "Restless", "Groaning", "Split", "Banded" },
				Description = "Crust kneaded open and shut by the pull of the world it circles.",
			},

			// ── Cryogenic ─────────────────────────────────────────────
			["Cryovolcanic Plain"] = new Voice
			{
				Onsets = new[] { "Cry", "Gla", "Rime", "Amm", "Bri", "Slur", "Fros", "Nive", "Pal", "Sye" },
				Nuclei = new[] { "a", "o", "i", "ae", "ei", "ou" },
				Codas = new[] { "flow", "plain", "slurry", "rime", "crust", "vent", "sheet", "apron" },
				Middles = new[] { "an", "or", "el", "is", "ur" },
				DungeonSuffixes = CryoDungeons,
				DungeonPrefixes = new[] { "The Freezing", "The Pale", "The Weeping", "The Slow", "The Brittle", "The Bitter", "The Ammonia", "The Glassy" },
				POISuffixes = new[] { "Flow", "Plain", "Vent", "Apron", "Sheet", "Rime" },
				Adjectives = new[] { "Freezing", "Brittle", "Weeping", "Pale", "Bitter", "Slow" },
				Description = "Volcanoes that erupt slush and freeze where they fall.",
			},
			["Ice Geyser Field"] = new Voice
			{
				Onsets = new[] { "Plu", "Jet", "Gey", "Cry", "Ves", "Spum", "Fros", "Nive", "Sal", "Bri" },
				Nuclei = new[] { "a", "e", "i", "ou", "ei", "ia" },
				Codas = new[] { "plume", "jet", "geyser", "vent", "spray", "fountain", "column", "field" },
				Middles = new[] { "an", "er", "ul", "is", "or" },
				DungeonSuffixes = CryoDungeons,
				DungeonPrefixes = new[] { "The Venting", "The Roaring", "The Frozen", "The Plumed", "The Screaming", "The Silver", "The Rising", "The Bitter" },
				POISuffixes = new[] { "Plume", "Jet", "Vent", "Fountain", "Column", "Field" },
				Adjectives = new[] { "Venting", "Plumed", "Roaring", "Rising", "Silver", "Frozen" },
				Description = "Jets of ice thrown into a sky too thin to slow them.",
			},
			["Nitrogen Ice Field"] = new Voice
			{
				Onsets = new[] { "Nit", "Gla", "Spu", "Fros", "Pal", "Nive", "Cri", "Sel", "Bore", "Vyn" },
				Nuclei = new[] { "a", "i", "o", "ei", "ou", "ae" },
				Codas = new[] { "ice", "field", "sheet", "glacier", "pack", "rime", "plain", "drift" },
				Middles = new[] { "an", "or", "el", "is", "ur" },
				DungeonSuffixes = CryoDungeons,
				DungeonPrefixes = new[] { "The Nitrogen", "The Blue", "The Unbroken", "The Creeping", "The Silent", "The Deep-frozen", "The Pale", "The Glassy" },
				POISuffixes = new[] { "Field", "Sheet", "Pack", "Drift", "Plain", "Glacier" },
				Adjectives = new[] { "Deep-frozen", "Blue", "Unbroken", "Creeping", "Pale", "Glassy" },
				Description = "Ice so cold that nitrogen lies on it as snow.",
			},
			["Methane Lake"] = new Voice
			{
				Onsets = new[] { "Meth", "Eth", "Lig", "Kra", "Pun", "Lac", "Mar", "Ont", "Jing", "Vid" },
				Nuclei = new[] { "a", "e", "i", "ae", "ou", "ia" },
				Codas = new[] { "lake", "mare", "sea", "shore", "shallow", "basin", "channel", "delta" },
				Middles = new[] { "an", "or", "el", "us", "in" },
				DungeonSuffixes = CryoDungeons,
				DungeonPrefixes = new[] { "The Black", "The Still", "The Mirror", "The Oily", "The Cold", "The Sunless", "The Rippling", "The Deep" },
				POISuffixes = new[] { "Lake", "Mare", "Shore", "Basin", "Delta", "Shallow" },
				Adjectives = new[] { "Black", "Still", "Oily", "Mirror", "Sunless", "Cold" },
				Description = "Liquid hydrocarbon lying utterly still under an orange sky.",
			},
			["Tholin Plain"] = new Voice
			{
				Onsets = new[] { "Tho", "Rus", "Umb", "Och", "Tar", "Sien", "Bru", "Cop", "Fuli", "Amb" },
				Nuclei = new[] { "a", "o", "i", "ou", "ae", "ia" },
				Codas = new[] { "plain", "stain", "waste", "crust", "haze", "drift", "field", "bed" },
				Middles = new[] { "an", "or", "el", "is", "ur" },
				DungeonSuffixes = CryoDungeons,
				DungeonPrefixes = new[] { "The Rust", "The Stained", "The Amber", "The Tarred", "The Sunless", "The Orange", "The Sticky", "The Ancient" },
				POISuffixes = new[] { "Plain", "Stain", "Waste", "Drift", "Field", "Bed" },
				Adjectives = new[] { "Rust", "Stained", "Amber", "Tarred", "Orange", "Sticky" },
				Description = "Organic haze that has settled out of the sky for a billion years.",
			},
			["Subsurface Ocean Vent"] = new Voice
			{
				Onsets = new[] { "Ven", "Aby", "Chem", "Smo", "Ther", "Bath", "Hyd", "Sul", "Nere", "Pel" },
				Nuclei = new[] { "a", "e", "i", "ou", "ae", "ia" },
				Codas = new[] { "vent", "smoker", "spire", "deep", "throat", "chimney", "field", "bloom" },
				Middles = new[] { "an", "or", "el", "us", "ir" },
				DungeonSuffixes = new[] { "Vent", "Chimney", "Deep", "Throat", "Gallery", "Chamber", "Spire", "Hollow" },
				DungeonPrefixes = new[] { "The Lightless", "The Warm", "The Crowded", "The Black", "The Ancient", "The Smoking", "The Buried", "The Living" },
				POISuffixes = new[] { "Vent", "Smoker", "Spire", "Deep", "Field", "Bloom" },
				Adjectives = new[] { "Lightless", "Smoking", "Warm", "Crowded", "Black", "Living" },
				Description = "The one warm, crowded place under kilometres of ice.",
			},

			// ── Hot and thick ─────────────────────────────────────────
			["Runaway Greenhouse Plain"] = new Voice
			{
				Onsets = new[] { "Swel", "Bra", "Ign", "Tor", "Fer", "Cald", "Aes", "Pyr", "Ard", "Sul" },
				Nuclei = new[] { "a", "e", "i", "ae", "ou", "ia" },
				Codas = new[] { "plain", "swelter", "waste", "glare", "bake", "flat", "oven", "reach" },
				Middles = new[] { "an", "or", "el", "us", "ir" },
				DungeonSuffixes = VolcanicDungeons,
				DungeonPrefixes = new[] { "The Sweltering", "The Crushing", "The Baked", "The Endless", "The Leaden", "The Airless", "The Blistering", "The Dim" },
				POISuffixes = new[] { "Plain", "Waste", "Flat", "Reach", "Glare", "Bake" },
				Adjectives = new[] { "Sweltering", "Crushing", "Baked", "Leaden", "Blistering", "Dim" },
				Description = "A world that kept its own heat until nothing could shed it.",
			},
			["Sulphuric Cloud Deck"] = new Voice
			{
				Onsets = new[] { "Sul", "Vit", "Aci", "Nim", "Pall", "Cir", "Vap", "Cau", "Fum", "Lact" },
				Nuclei = new[] { "a", "u", "i", "ou", "ae", "ia" },
				Codas = new[] { "deck", "veil", "shroud", "layer", "haze", "cloud", "sea", "ceiling" },
				Middles = new[] { "an", "or", "ul", "is", "er" },
				DungeonSuffixes = new[] { "Gallery", "Vault", "Chamber", "Shaft", "Hollow", "Deck", "Cell", "Well" },
				DungeonPrefixes = new[] { "The Acid", "The Yellow", "The Choking", "The Drifting", "The Eternal", "The Sour", "The Boiling", "The Veiled" },
				POISuffixes = new[] { "Deck", "Veil", "Shroud", "Layer", "Ceiling", "Haze" },
				Adjectives = new[] { "Acid", "Yellow", "Choking", "Sour", "Drifting", "Veiled" },
				Description = "A permanent ceiling of acid that never once breaks.",
			},
			["Molten Surface"] = new Voice
			{
				Onsets = new[] { "Mol", "Mag", "Pyr", "Ign", "Cal", "Fla", "Emb", "Ard", "Fer", "Lav" },
				Nuclei = new[] { "a", "o", "e", "au", "ae", "ia" },
				Codas = new[] { "flow", "lake", "crust", "forge", "melt", "sea", "flare", "furnace" },
				Middles = new[] { "an", "or", "el", "us", "ir" },
				DungeonSuffixes = VolcanicDungeons,
				DungeonPrefixes = new[] { "The Molten", "The Blazing", "The Glowing", "The Unquenched", "The Open", "The Searing", "The Red", "The Roaring" },
				POISuffixes = new[] { "Flow", "Lake", "Sea", "Forge", "Melt", "Flare" },
				Adjectives = new[] { "Molten", "Searing", "Blazing", "Unquenched", "Red", "Roaring" },
				Description = "Rock that never finished cooling, and still moves.",
			},
			["Sulphur Flats"] = new Voice
			{
				Onsets = new[] { "Sul", "Bri", "Cro", "Fum", "Och", "Cit", "Mof", "Sol", "Ver", "Pyr" },
				Nuclei = new[] { "a", "u", "i", "ou", "ae", "ia" },
				Codas = new[] { "flat", "crust", "field", "waste", "bed", "pan", "terrace", "bloom" },
				Middles = new[] { "an", "or", "ul", "is", "er" },
				DungeonSuffixes = VolcanicDungeons,
				DungeonPrefixes = new[] { "The Yellow", "The Reeking", "The Brittle", "The Crusted", "The Bitter", "The Smoking", "The Golden", "The Scalded" },
				POISuffixes = new[] { "Flat", "Field", "Crust", "Bed", "Terrace", "Bloom" },
				Adjectives = new[] { "Yellow", "Reeking", "Brittle", "Crusted", "Smoking", "Scalded" },
				Description = "Yellow crust over ground that has been venting for centuries.",
			},
		};

		[DashboardTool(DashboardToolAttribute.Maintenance, "Fill missing biome naming", Section = "Content", Order = 4,
			Tooltip = "Gives every biome with no naming data a phonology, so the name generator can name its dungeons and landmarks. Biomes that already have naming are left alone.")]
		public static void FillFromDashboard()
		{
			int filled = Fill(out List<string> unknown);
			Debug.Log(filled > 0
				? $"[Biome naming] Filled {filled} biome(s)."
				: "[Biome naming] Every biome already has naming data.");
			if (unknown.Count > 0)
			{
				Debug.LogWarning(
					$"[Biome naming] {unknown.Count} biome(s) have no naming and no entry in the table, so they were left alone: " +
					string.Join(", ", unknown) + ". Add them to BiomeNamingGenerator.Voices.");
			}
		}

		/// <summary>
		/// Fills in the biomes that have no usable naming. Returns how many were changed.
		/// </summary>
		/// <param name="unknown">
		/// Biomes that still have no naming because the table does not name them — reported rather
		/// than guessed at, which is the whole point of the table.
		/// </param>
		public static int Fill(out List<string> unknown)
		{
			unknown = new List<string>();
			int filled = 0;
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate)))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				BiomeTemplate biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(path);
				if (biome == null || biome.Naming != null && biome.Naming.IsUsable)
				{
					continue;
				}
				if (!Voices.TryGetValue(biome.name, out Voice voice))
				{
					unknown.Add(biome.name);
					continue;
				}

				Undo.RecordObject(biome, "Biome naming");
				biome.Naming ??= new BiomeNamingData();
				SerializableBiomePhonology p = biome.Naming.Phonology ?? new SerializableBiomePhonology();
				p.Onsets = voice.Onsets;
				p.Nuclei = voice.Nuclei;
				p.Codas = voice.Codas;
				p.Middles = voice.Middles;
				p.SyllMin = 2;
				p.SyllMax = 3;
				p.DungeonSuffixes = voice.DungeonSuffixes;
				p.DungeonPrefixes = voice.DungeonPrefixes;
				p.POISuffixes = voice.POISuffixes;
				p.Adjectives = voice.Adjectives;
				p.Description = voice.Description;
				biome.Naming.Phonology = p;
				biome.Naming.BuildRuntime();

				EditorUtility.SetDirty(biome);
				filled++;
			}
			if (filled > 0)
			{
				AssetDatabase.SaveAssets();
			}
			return filled;
		}
	}
}
#endif
