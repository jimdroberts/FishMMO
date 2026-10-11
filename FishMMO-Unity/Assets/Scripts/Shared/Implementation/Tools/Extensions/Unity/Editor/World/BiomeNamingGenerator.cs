#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.NameGeneration.Editor;

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
			["Ice Sheet"] = new Voice
			{
				Onsets = new[] { "Fim", "Sas", "Nun", "Kat", "Rime", "Hoar", "Gla", "Bra", "Sker", "Vos" },
				Nuclei = new[] { "a", "e", "i", "ei", "o", "au" },
				Codas = new[] { "fell", "dome", "cap", "front", "rime", "firn", "hold", "wold" },
				Middles = new[] { "an", "ar", "el", "is", "or" },
				DungeonSuffixes = CryoDungeons,
				DungeonPrefixes = new[] { "The Buried", "The White", "The Endless", "The Silent", "The Howling", "The Blue", "The Drowned", "The Frozen" },
				POISuffixes = new[] { "Dome", "Front", "Nunatak", "Icefall", "Cap", "Wall" },
				Adjectives = new[] { "White", "Buried", "Howling", "Endless", "Blue", "Silent" },
				Description = "Land buried under ice that never thaws, ending in cliffs where it meets the sea.",
			},
			["Ice Shelf"] = new Voice
			{
				Onsets = new[] { "Shel", "Pack", "Flo", "Ber", "Lea", "Pol", "Ny", "Rime", "Sas", "Gri" },
				Nuclei = new[] { "a", "e", "i", "ei", "ae", "ou" },
				Codas = new[] { "shelf", "floe", "pack", "lead", "ridge", "sheet", "rime", "berg" },
				Middles = new[] { "an", "or", "el", "is", "ur" },
				DungeonSuffixes = CryoDungeons,
				DungeonPrefixes = new[] { "The Frozen", "The Groaning", "The Endless", "The Pale", "The Drowned", "The Cracking", "The Still", "The Sunken" },
				POISuffixes = new[] { "Shelf", "Floe", "Ridge", "Lead", "Pack", "Front" },
				Adjectives = new[] { "Frozen", "Groaning", "Endless", "Pale", "Cracking", "Still" },
				Description = "A sea frozen flat to the horizon, its ridges groaning as the shelf shifts.",
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

		// ── Point-of-interest words ───────────────────────────────────────

		/// <summary>
		/// Kind → words for one family of biomes. A biome takes the words of the families it is listed
		/// under in <see cref="PoiFamiliesByBiome"/>, first family first.
		/// </summary>
		private static Dictionary<POIType, string[]> Words(params (POIType kind, string[] words)[] rows)
		{
			var table = new Dictionary<POIType, string[]>();
			foreach ((POIType kind, string[] words) in rows)
			{
				table[kind] = words;
			}
			return table;
		}

		private static (POIType, string[]) W(POIType kind, params string[] words) => (kind, words);

		private static readonly Dictionary<POIType, string[]> Wetland = Words(
			W(POIType.Shrine, "Bog-altar", "Fen-shrine", "Mire Altar", "Reed Shrine", "Peat Altar", "Moss Shrine"),
			W(POIType.BogShrine, "Bog-altar", "Fen-shrine", "Mire Shrine", "Peat-altar", "Reed Altar", "Sump Shrine"),
			W(POIType.Spring, "Seep", "Sump", "Wellhead", "Bog-spring", "Fen Well", "Mire-spring"),
			W(POIType.Camp, "Fen Camp", "Reed Camp", "Stilt Camp", "Mire Camp", "Bog Camp", "Raft Camp"),
			W(POIType.Cave, "Sump", "Mire-hole", "Bog Hollow", "Root Cave", "Peat Hollow", "Drowned Cave"),
			W(POIType.Graveyard, "Bog-graves", "Drowned Graves", "Mire-yard", "Fen Barrows", "Peat Graves", "Sunken Yard"),
			W(POIType.Clearing, "Hummock", "Tussock", "Dry Isle", "Holm", "Reed-bed", "Fen Glade"),
			W(POIType.Ruins, "Mire-ruins", "Drowned Walls", "Silted Halls", "Bog Ruins", "Moss Walls", "Sunken Ruins"),
			W(POIType.WitchHut, "Bog Hut", "Fen Hovel", "Mire Croft", "Reed Hut", "Stilt Hut", "Hag's Hut"),
			W(POIType.MonsterDen, "Wallow", "Mire-den", "Bog Hole", "Sump", "Root-lair", "Fen Lair"),
			W(POIType.Bridge, "Boardwalk", "Causeway", "Duckboards", "Log Bridge", "Plank-walk", "Corduroy Road"),
			W(POIType.AncientRoad, "Causeway", "Trackway", "Corduroy Road", "Boardwalk", "Plank Road", "Fen Road"),
			W(POIType.Island, "Holm", "Hummock", "Dry Isle", "Eyot", "Tussock", "Mound"),
			W(POIType.Waystation, "Fen Inn", "Ferry House", "Stilt Inn", "Causeway Inn", "Reed Inn", "Landing"));

		private static readonly Dictionary<POIType, string[]> Desert = Words(
			W(POIType.Spring, "Oasis", "Wells", "Water-hole", "Cistern", "Soak", "Seep"),
			W(POIType.Oasis, "Oasis", "Palms", "Wells", "Green", "Date Grove", "Spring-garden"),
			W(POIType.Camp, "Caravan Camp", "Tent-camp", "Oasis Camp", "Nomad Camp", "Dune Camp", "Well Camp"),
			W(POIType.Waystation, "Caravanserai", "Waterstop", "Well-house", "Khan", "Rest-house", "Cistern Inn"),
			W(POIType.TradingPost, "Bazaar", "Souk", "Caravan Market", "Trade-wells", "Market", "Exchange"),
			W(POIType.Ruins, "Sand-buried Ruins", "Dune Ruins", "Tell", "Buried City", "Sand Ruins", "Old Walls"),
			W(POIType.Graveyard, "Sand Graves", "Cairn-field", "Tomb-field", "Necropolis", "Bone Sands", "Dune Graves"),
			W(POIType.Crypt, "Tomb", "Pyramid", "Sepulchre", "Necropolis", "Rock Tomb", "Sun Tomb"),
			W(POIType.Cave, "Rock Shelter", "Sand Cave", "Wind Cave", "Dune Hollow", "Canyon Cave", "Sun Cave"),
			W(POIType.Valley, "Wadi", "Canyon", "Draw", "Arroyo", "Gulch", "Basin"),
			W(POIType.Clearing, "Flat", "Pan", "Hardpan", "Reg", "Gravel Plain", "Stony Flat"),
			W(POIType.Monument, "Sun Pillar", "Sand Colossus", "Waystone", "Sun Stone", "Desert Colossus", "Stele"),
			W(POIType.Statue, "Colossus", "Sand Idol", "Sun Statue", "Sphinx", "Effigy", "Guardian"));

		private static readonly Dictionary<POIType, string[]> Arctic = Words(
			W(POIType.Spring, "Ice-well", "Frost Spring", "Thaw-pool", "Cold Spring", "Rime Well", "Meltwater"),
			W(POIType.Cave, "Ice Cave", "Frost Hollow", "Snow Cave", "Rime Grotto", "Blue Hollow", "Ice Hollow"),
			W(POIType.Camp, "Snow Camp", "Ice Camp", "Snow-huts", "Frost Camp", "Hide Tents", "Winter Camp"),
			W(POIType.Shrine, "Ice Shrine", "Frost Altar", "Rime Shrine", "Snow Cairn", "Winter Altar", "Cold Fane"),
			W(POIType.Graveyard, "Frozen Graves", "Ice Tombs", "Snow Barrows", "Rime-yard", "Cairn Field", "Frost Graves"),
			W(POIType.Ruins, "Ice-bound Ruins", "Frozen Ruins", "Buried Halls", "Snow Ruins", "Rime Walls", "Frost Ruins"),
			W(POIType.MonsterDen, "Snow Den", "Ice Lair", "Frost Burrow", "Drift Den", "Rime Lair", "Winter Den"),
			W(POIType.Clearing, "Snowfield", "Drift", "Ice Flat", "Frost Glade", "White Field", "Rime Meadow"),
			W(POIType.Landmark, "Cairn", "Ice Pillar", "Frost Stone", "Rime Stone", "Snow Cairn", "Waymark"),
			W(POIType.Peak, "Nunatak", "Ice Peak", "Snow Horn", "Frost Spire", "White Peak", "Rime Crag"),
			W(POIType.Nest, "Ice Eyrie", "Snow Roost", "Frost Nest", "Rookery", "Drift Nest", "Cliff Roost"));

		private static readonly Dictionary<POIType, string[]> Forest = Words(
			W(POIType.Clearing, "Glade", "Dell", "Clearing", "Ride", "Lea", "Assart"),
			W(POIType.Shrine, "Grove Shrine", "Oak Altar", "Tree Shrine", "Leaf Shrine", "Root Altar", "Wood Shrine"),
			W(POIType.Camp, "Woodcamp", "Charcoal Camp", "Forest Camp", "Hunters' Camp", "Greenwood Camp", "Log Camp"),
			W(POIType.Cave, "Root Cave", "Tree Hollow", "Badger Hole", "Wood Grotto", "Hollow", "Moss Cave"),
			W(POIType.MonsterDen, "Den", "Lair", "Sett", "Burrow", "Root-den", "Briar Lair"),
			W(POIType.Spring, "Well", "Holy Well", "Wood Spring", "Moss Well", "Font", "Wellspring"),
			W(POIType.Ruins, "Overgrown Ruins", "Mossy Walls", "Greenwood Ruins", "Ivied Ruins", "Old Halls", "Forest Ruins"),
			W(POIType.Hermitage, "Woodland Cell", "Forest Hermitage", "Anchor Hut", "Hermit's Hollow", "Greenwood Cell", "Retreat"),
			W(POIType.AncientTree, "Elder Oak", "Old Ash", "Great Yew", "Father Oak", "Mother Beech", "Grandfather Pine"),
			W(POIType.HerbGrove, "Herb Glade", "Simples Grove", "Wort Garden", "Leech-glade", "Herb Dell", "Physic Grove"),
			W(POIType.Graveyard, "Greenyard", "Yew-yard", "Moss Graves", "Wood Graves", "Forest Barrows", "Grove Graves"),
			W(POIType.Valley, "Glen", "Dell", "Combe", "Dingle", "Hollow", "Vale"),
			W(POIType.FeyRing, "Fairy Ring", "Toadstool Ring", "Elf-dance", "Mushroom Ring", "Fey Glade", "Moonring"));

		private static readonly Dictionary<POIType, string[]> Jungle = Words(
			W(POIType.Temple, "Step Temple", "Ziggurat", "Pyramid", "Vine Temple", "Jungle Fane", "Sun Temple"),
			W(POIType.Ruins, "Overgrown Ruins", "Vine-choked Ruins", "Lost City", "Root-split Ruins", "Jungle Ruins", "Green Walls"),
			W(POIType.Shrine, "Vine Shrine", "Idol Shrine", "Jungle Altar", "Spirit House", "Root Altar", "Totem Shrine"),
			W(POIType.Cave, "Cenote", "Vine Cave", "Root Cave", "Jungle Grotto", "Bat Cave", "Green Hollow"),
			W(POIType.Camp, "Jungle Camp", "Machete Camp", "Vine Camp", "Expedition Camp", "River Camp", "Hammock Camp"),
			W(POIType.Statue, "Idol", "Stone Head", "Vine-wrapped Statue", "Totem", "Effigy", "Jade Idol"),
			W(POIType.MonsterDen, "Lair", "Den", "Hollow", "Nest", "Root-den", "Vine-lair"),
			W(POIType.Clearing, "Clearing", "Gap", "Sunpatch", "Glade", "Dell", "Opening"),
			W(POIType.Spring, "Cenote", "Spring", "Pool", "Font", "Basin", "Well"),
			W(POIType.Sinkhole, "Cenote", "Sinkhole", "Sacred Well", "Pit", "Blue Hole", "Well"));

		private static readonly Dictionary<POIType, string[]> Grassland = Words(
			W(POIType.Clearing, "Meadow", "Lea", "Field", "Common", "Green", "Sward"),
			W(POIType.Camp, "Herders' Camp", "Yurts", "Tent Ring", "Drovers' Camp", "Horse Camp", "Plains Camp"),
			W(POIType.Landmark, "Standing Stone", "Way-stone", "Cairn", "Waymark", "Boundary Stone", "Lone Stone"),
			W(POIType.Barrow, "Kurgan", "Mound", "Tumulus", "Howe", "Long Barrow", "Low"),
			W(POIType.Spring, "Well", "Water-hole", "Spring", "Trough", "Pond", "Dew-pond"),
			W(POIType.Battlefield, "Field", "Plain", "Killing Field", "War-field", "Bloodfield", "Last Stand"),
			W(POIType.Ruins, "Old Steading", "Broken Walls", "Foundations", "Ruins", "Grassy Ruins", "Earthworks"),
			W(POIType.Waystation, "Coaching Inn", "Way Inn", "Drovers' Inn", "Halfway House", "Stage-house", "Inn"),
			W(POIType.AbandonedFarm, "Empty Croft", "Deserted Steading", "Old Farm", "Forsaken Farm", "Burnt Farm", "Fallow Croft"),
			W(POIType.MonsterDen, "Burrow", "Warren", "Den", "Hole", "Sett", "Earth"));

		private static readonly Dictionary<POIType, string[]> Mountain = Words(
			W(POIType.Peak, "Peak", "Crag", "Horn", "Pike", "Spire", "Tor"),
			W(POIType.Pass, "Pass", "Col", "Saddle", "Notch", "Gap", "Gate"),
			W(POIType.Cave, "Cave", "Cavern", "Rift", "Delving", "Crag Hollow", "Rock Hollow"),
			W(POIType.Camp, "High Camp", "Base Camp", "Climbers' Camp", "Ridge Camp", "Rock Camp", "Crag Camp"),
			W(POIType.Shrine, "Summit Cairn", "Peak Shrine", "Prayer Cairn", "High Altar", "Stone Shrine", "Sky Altar"),
			W(POIType.Monastery, "High Monastery", "Cliff Abbey", "Peak Monastery", "Mountain Priory", "Rock Abbey", "Sky Cloister"),
			W(POIType.Mine, "Delving", "Deep Mine", "Adit", "Shaft", "Diggings", "Lode"),
			W(POIType.Nest, "Eyrie", "Aerie", "Crag Nest", "Cliff Roost", "High Nest", "Roost"),
			W(POIType.Spring, "Spring", "Well", "Rock-spring", "Spout", "Seep", "Font"),
			W(POIType.Keep, "Crag Keep", "Hold", "Eyrie", "Rock Keep", "Mountain Hold", "Peak Fort"));

		private static readonly Dictionary<POIType, string[]> Volcanic = Words(
			W(POIType.Spring, "Hot Spring", "Steam Well", "Sulphur Spring", "Boiling Spring", "Scald Pool", "Kettle"),
			W(POIType.Cave, "Lava Cave", "Ash Cave", "Smoke Hole", "Vent Cave", "Cinder Hollow", "Fire Grotto"),
			W(POIType.Shrine, "Fire Altar", "Flame Shrine", "Ash Altar", "Ember Shrine", "Cinder Shrine", "Forge Shrine"),
			W(POIType.Temple, "Fire Temple", "Flame Fane", "Forge Temple", "Ash Temple", "Ember Sanctum", "Furnace Temple"),
			W(POIType.Camp, "Ash Camp", "Cinder Camp", "Sulphur Camp", "Ember Camp", "Vent Camp", "Smoke Camp"),
			W(POIType.Ruins, "Ash-buried Ruins", "Cinder Ruins", "Burnt Halls", "Lava-swallowed Ruins", "Scorched Walls", "Ash Ruins"),
			W(POIType.MonsterDen, "Vent Lair", "Cinder Den", "Ash Hole", "Fire Pit", "Ember Lair", "Smoke Den"),
			W(POIType.Clearing, "Ash Flat", "Cinder Field", "Clinker", "Lava Field", "Ash Plain", "Scoria Flat"),
			W(POIType.Peak, "Cone", "Fire Peak", "Ash Peak", "Smoke Peak", "Cinder Cone", "Burning Crag"),
			W(POIType.Mine, "Sulphur Pit", "Obsidian Delving", "Cinder Mine", "Fire Mine", "Ash Pit", "Lava Diggings"));

		private static readonly Dictionary<POIType, string[]> Coast = Words(
			W(POIType.Cave, "Sea Cave", "Zawn", "Blowhole", "Tide Cave", "Surge Cave", "Gull Cave"),
			W(POIType.Camp, "Beach Camp", "Fishers' Camp", "Driftwood Camp", "Dune Camp", "Shore Camp", "Wreckers' Camp"),
			W(POIType.Shrine, "Sea Shrine", "Tide Altar", "Drowned Shrine", "Shell Shrine", "Wave Altar", "Salt Shrine"),
			W(POIType.Bay, "Cove", "Bay", "Bight", "Haven", "Inlet", "Strand"),
			W(POIType.Landmark, "Sea Stack", "Beacon Stone", "Tide Stone", "Old Man", "Needle", "Skerry"),
			W(POIType.Spring, "Spring", "Well", "Sweetwater Spring", "Seep", "Dune Well", "Salt Spring"),
			W(POIType.Graveyard, "Drowned Men's Yard", "Sailors' Graves", "Shore Graves", "Wreckers' Yard", "Sea Graves", "Gull Graves"),
			W(POIType.Ruins, "Sea-worn Ruins", "Salt Ruins", "Shore Ruins", "Wrecked Walls", "Tide Ruins", "Dune Ruins"),
			W(POIType.Tower, "Sea Tower", "Watchtower", "Lookout", "Signal Tower", "Beacon", "Coast Watch"),
			W(POIType.FishingCamp, "Fish Weir", "Net Huts", "Smokehouse", "Landing", "Staithe", "Fish Quay"));

		private static readonly Dictionary<POIType, string[]> Sea = Words(
			W(POIType.Shrine, "Drowned Shrine", "Sea Altar", "Tide Shrine", "Coral Altar", "Abyss Shrine", "Deep Fane"),
			W(POIType.Ruins, "Sunken Ruins", "Drowned Halls", "Sea Ruins", "Coral-grown Ruins", "Deep Ruins", "Lost Halls"),
			W(POIType.SunkenRuins, "Drowned Halls", "Coral Ruins", "Sea-ruins", "Deep Walls", "Kelp Ruins", "Sunken Halls"),
			W(POIType.SunkenShip, "Wreck", "Hulk", "Drowned Ship", "Sunken Galleon", "Coral Wreck", "Lost Ship"),
			W(POIType.SunkenCity, "Drowned City", "Sunken City", "Deep City", "Coral City", "Abyssal City", "Lost City"),
			W(POIType.CoralReef, "Reef", "Coral Gardens", "Shoals", "Atoll", "Coral Bank", "Coral Shelf"),
			W(POIType.Cave, "Sea Cave", "Undersea Grotto", "Kelp Cave", "Deep Hollow", "Coral Cave", "Trench Cave"),
			W(POIType.MonsterDen, "Deep Lair", "Trench Den", "Kelp Lair", "Abyss Hole", "Reef Den", "Coral Lair"),
			W(POIType.Nest, "Spawning Ground", "Brood Reef", "Egg Bed", "Clutch", "Kelp Nest", "Deep Nest"));

		private static readonly Dictionary<POIType, string[]> Underground = Words(
			W(POIType.Cave, "Cavern", "Gallery", "Hall", "Chamber", "Grotto", "Deep"),
			W(POIType.Camp, "Delvers' Camp", "Underground Camp", "Glowcamp", "Deep Camp", "Cave Camp", "Torch Camp"),
			W(POIType.Shrine, "Deep Shrine", "Stone Altar", "Cave Shrine", "Underfane", "Hollow Altar", "Glow Shrine"),
			W(POIType.CrystalFormation, "Crystal Grotto", "Geode", "Crystal Hall", "Glittering Caves", "Prism Hall", "Crystal Vault"),
			W(POIType.Mine, "Deep Delving", "Old Workings", "Lower Mine", "Undermine", "Deep Shaft", "Lode"),
			W(POIType.MonsterDen, "Burrow", "Warren", "Lair", "Hole", "Deep Den", "Pit"),
			W(POIType.Spring, "Drip-pool", "Cave Spring", "Rising", "Black Pool", "Still Well", "Seep"));

		private static readonly Dictionary<POIType, string[]> Built = Words(
			W(POIType.Ruins, "Ruins", "Broken Halls", "Fallen Walls", "Old Courts", "Shattered Halls", "Rubble"),
			W(POIType.Crypt, "Undercroft", "Crypt", "Vault", "Catacombs", "Tomb", "Sepulchre"),
			W(POIType.Shrine, "Chapel", "Oratory", "Altar", "Shrine", "Sanctum", "Reliquary"),
			W(POIType.Statue, "Statue", "Effigy", "Bust", "Likeness", "Colossus", "Figure"),
			W(POIType.Tower, "Tower", "Bastion", "Turret", "Spire", "Belfry", "Barbican"),
			W(POIType.Keep, "Keep", "Donjon", "Hold", "Citadel", "Bastion", "Ward"),
			W(POIType.Graveyard, "Churchyard", "Cemetery", "Mausoleum Yard", "Garden of Rest", "Court of Graves", "Lych-yard"));

		private static readonly Dictionary<POIType, string[]> Airless = Words(
			W(POIType.Crater, "Crater", "Basin", "Ring", "Pit", "Bowl", "Scar"),
			W(POIType.Cave, "Lava Tube", "Skylight", "Pit Cave", "Regolith Hollow", "Crater Cave", "Dust Hollow"),
			W(POIType.Camp, "Outpost", "Station", "Dome Camp", "Shelter", "Landing Camp", "Survey Camp"),
			W(POIType.Landmark, "Monolith", "Boulder", "Ejecta Block", "Waymark", "Beacon", "Marker"),
			W(POIType.Ruins, "Ruined Station", "Broken Dome", "Old Outpost", "Dead Station", "Silent Ruins", "Wreckage"),
			W(POIType.Wreck, "Wreck", "Crashed Lander", "Fallen Ship", "Hulk", "Wreckage", "Debris Field"),
			W(POIType.FallenStar, "Impactor", "Skystone", "Meteorite", "Starfall", "Fallen Star", "Thunderstone"),
			W(POIType.Mine, "Regolith Pit", "Ore Pit", "Dust Mine", "Extraction Site", "Strip Mine", "Dig"),
			W(POIType.Valley, "Rille", "Graben", "Trough", "Channel", "Valley", "Furrow"),
			W(POIType.Peak, "Massif", "Rim Peak", "Central Peak", "Mons", "Ridge", "Spire"));

		private static readonly Dictionary<POIType, string[]> Cryo = Words(
			W(POIType.Cave, "Ice Vault", "Cryo Grotto", "Fissure Cave", "Frost Hollow", "Blue Vault", "Rime Hollow"),
			W(POIType.Camp, "Station", "Outpost", "Shelter", "Dome Camp", "Ice Camp", "Survey Camp"),
			W(POIType.Peak, "Mons", "Ice Peak", "Cryo Cone", "Frost Massif", "Rime Spire", "Ridge"),
			W(POIType.Crater, "Crater", "Basin", "Ice Pit", "Ring", "Bowl", "Cold Pit"),
			W(POIType.Landmark, "Ice Monolith", "Frost Pillar", "Plume Marker", "Beacon", "Rime Stone", "Marker"),
			W(POIType.CrystalFormation, "Ice Crystals", "Frost Garden", "Rime Spires", "Clathrate Spires", "Ice Prisms", "Glitterfield"),
			W(POIType.Spring, "Cryo-spring", "Slush Well", "Brine Seep", "Ice Fountain", "Cold Vent", "Rime Well"));

		private static readonly Dictionary<POIType, string[]> HotThick = Words(
			W(POIType.Cave, "Vent Cave", "Furnace Hollow", "Sulphur Cave", "Heat Vault", "Slag Grotto", "Fire Hollow"),
			W(POIType.Camp, "Station", "Heat Shelter", "Dome", "Outpost", "Furnace Camp", "Survey Camp"),
			W(POIType.Landmark, "Slag Pillar", "Fire Stone", "Beacon", "Spire", "Marker", "Melt Stone"),
			W(POIType.Peak, "Mons", "Fire Peak", "Furnace Crag", "Slag Spire", "Sulphur Peak", "Ash Mons"),
			W(POIType.Crater, "Crater", "Caldera", "Slag Bowl", "Melt Pit", "Furnace", "Ring"),
			W(POIType.LavaLake, "Lava Sea", "Molten Lake", "Sulphur Lake", "Fire Mere", "Melt Pool", "Slag Pool"));

		private static readonly Dictionary<POIType, string[]> Farmland = Words(
			W(POIType.AbandonedFarm, "Empty Croft", "Fallow Farm", "Old Homestead", "Burnt Barn", "Deserted Grange", "Forsaken Steading"),
			W(POIType.Waystation, "Inn", "Tavern", "Halt", "Coaching Inn", "Way-house", "Alehouse"),
			W(POIType.Clearing, "Field", "Paddock", "Meadow", "Croft", "Green", "Common"),
			W(POIType.Landmark, "Boundary Stone", "Mere-stone", "Way-cross", "Market Cross", "Milestone", "Old Oak"),
			W(POIType.Graveyard, "Churchyard", "Parish Graves", "Lych-yard", "Field Graves", "Acre", "Burying Ground"));

		private static readonly Dictionary<POIType, string[]> Karst = Words(
			W(POIType.Sinkhole, "Swallet", "Shakehole", "Doline", "Pothole", "Cenote", "Sink"),
			W(POIType.Cave, "Pothole", "Swallet Cave", "Gallery", "Cavern", "Rift", "Grike"),
			W(POIType.Spring, "Rising", "Resurgence", "Well", "Spring", "Fountain", "Font"));

		private static readonly Dictionary<POIType, string[]> Geyser = Words(
			W(POIType.Spring, "Geyser", "Hot Spring", "Kettle", "Paint Pot", "Mud Pot", "Steam Spring"),
			W(POIType.HotSpring, "Kettle", "Paint Pot", "Steam Pool", "Scald Pool", "Mud Pot", "Geyser"),
			W(POIType.FumaroleField, "Steam Vents", "Fumaroles", "Smokes", "Mud Pots", "Reek", "Breathing Ground"));

		private static readonly Dictionary<POIType, string[]> Riverside = Words(
			W(POIType.Bridge, "Ford", "Bridge", "Crossing", "Stepping Stones", "Ferry", "Weir"),
			W(POIType.FishingCamp, "Fish Weir", "Eel Traps", "Net Huts", "Smokehouse", "Landing", "Mill Weir"),
			W(POIType.Camp, "River Camp", "Ford Camp", "Ferry Camp", "Bank Camp", "Reed Camp", "Landing Camp"),
			W(POIType.Spring, "Spring", "Source", "Rising", "Wellspring", "Head", "Font"),
			W(POIType.Island, "Ait", "Eyot", "Holm", "Isle", "Bar", "Ham"));

		/// <summary>
		/// Every biome by its exact asset name, and the word families it takes, first family first. Explicit,
		/// like <see cref="Voices"/>, for the same reason: a keyword guess over names is how biomes got
		/// corrupted before, and a biome missing here is reported rather than guessed at.
		/// </summary>
		private static readonly Dictionary<string, Dictionary<POIType, string[]>[]> PoiFamiliesByBiome = new Dictionary<string, Dictionary<POIType, string[]>[]>
		{
			["Abyssal Plain"] = new[] { Sea },
			["Abyss"] = new[] { Sea },
			["Alpine"] = new[] { Mountain, Arctic },
			["Alpine Meadow"] = new[] { Grassland, Mountain },
			["Badlands"] = new[] { Desert, Mountain },
			["Bamboo Forest"] = new[] { Forest, Jungle },
			["Beach"] = new[] { Coast },
			["Castle"] = new[] { Built },
			["Cave"] = new[] { Underground },
			["Coastal Water"] = new[] { Sea, Coast },
			["Coral Reef"] = new[] { Sea },
			["Crater"] = new[] { Volcanic },
			["Cryovolcanic Plain"] = new[] { Cryo },
			["Deep Ocean"] = new[] { Sea },
			["Desert"] = new[] { Desert },
			["Dust Sea"] = new[] { Airless },
			["Estuary"] = new[] { Wetland, Coast, Riverside },
			["Farmland"] = new[] { Farmland, Grassland },
			["Forest"] = new[] { Forest },
			["Forest Ruins"] = new[] { Forest, Built },
			["Fortress"] = new[] { Built },
			["Geyser Basin"] = new[] { Geyser, Volcanic },
			["Glacier"] = new[] { Arctic, Mountain },
			["Grassland"] = new[] { Grassland },
			["High Desert"] = new[] { Desert, Mountain },
			["Hills"] = new[] { Grassland, Mountain },
			["Ice Cave"] = new[] { Underground, Arctic },
			["Ice Geyser Field"] = new[] { Cryo },
			["Ice Palace"] = new[] { Built, Arctic },
			["Ice Ruin"] = new[] { Built, Arctic },
			["Ice Sheet"] = new[] { Arctic },
			["Ice Shelf"] = new[] { Arctic, Coast },
			["Impact Basin"] = new[] { Airless },
			["Jungle"] = new[] { Jungle },
			["Jungle Ruins"] = new[] { Jungle, Built },
			["Jungle Temple"] = new[] { Jungle, Built },
			["Karst"] = new[] { Karst, Mountain },
			["Lake"] = new[] { Riverside },
			["Lava Tube"] = new[] { Underground, Volcanic },
			["Mangrove"] = new[] { Wetland, Jungle, Coast },
			["Marble Palace"] = new[] { Built },
			["Methane Lake"] = new[] { Cryo },
			["Molten Surface"] = new[] { HotThick, Volcanic },
			["Mountain Slope"] = new[] { Mountain },
			["Nitrogen Ice Field"] = new[] { Cryo },
			["Oasis"] = new[] { Desert },
			["Ocean"] = new[] { Sea },
			["Peat Bog"] = new[] { Wetland },
			["Permanent Ice"] = new[] { Arctic },
			["Plains"] = new[] { Grassland },
			["Radiation Plain"] = new[] { Airless },
			["Regolith Plain"] = new[] { Airless },
			["Rille"] = new[] { Airless },
			["River"] = new[] { Riverside },
			["Rocky Coast"] = new[] { Coast, Mountain },
			["Rocky Terrain"] = new[] { Mountain },
			["Runaway Greenhouse Plain"] = new[] { HotThick },
			["Salt Flat"] = new[] { Desert },
			["Savanna"] = new[] { Grassland },
			["Scree"] = new[] { Mountain },
			["Scrubland"] = new[] { Desert, Grassland },
			["Seamount"] = new[] { Sea },
			["Snow"] = new[] { Arctic },
			["Steppe"] = new[] { Grassland },
			["Subsurface Ocean Vent"] = new[] { Cryo, Sea },
			["Sulphur Flats"] = new[] { HotThick, Volcanic },
			["Sulphuric Cloud Deck"] = new[] { HotThick },
			["Swamp"] = new[] { Wetland },
			["Swamp Cave"] = new[] { Underground, Wetland },
			["Swamp Ruins"] = new[] { Wetland, Built },
			["Swamp Temple"] = new[] { Wetland, Built },
			["Taiga"] = new[] { Forest, Arctic },
			["Tholin Plain"] = new[] { Cryo },
			["Tidal Fracture"] = new[] { Cryo },
			["Tundra"] = new[] { Arctic, Grassland },
			["Underwater Canyon"] = new[] { Sea },
			["Valley"] = new[] { Grassland, Riverside },
			["Volcanic"] = new[] { Volcanic },
			["Volcanic Cave"] = new[] { Underground, Volcanic },
			["Volcanic Temple"] = new[] { Volcanic, Built },
			["Wasteland"] = new[] { Desert, Volcanic },
			["Wetlands"] = new[] { Wetland, Riverside },
			["Woodland"] = new[] { Forest, Grassland },
		};

		/// <summary>Most words one biome keeps per kind after its families are merged.</summary>
		private const int MaxWordsPerKind = 10;

		/// <summary>The biome names the POI word table covers, for tests that pin it against the assets.</summary>
		public static IReadOnlyCollection<string> PoiWordBiomes => PoiFamiliesByBiome.Keys;

		/// <summary>
		/// The merged kind → words a biome would be given (first family first, de-duplicated), or null when
		/// the table does not name the biome. Pure, so tests read it without touching assets.
		/// </summary>
		public static SortedDictionary<string, string[]> PoiWordsFor(string biomeName)
		{
			if (biomeName == null || !PoiFamiliesByBiome.TryGetValue(biomeName, out Dictionary<POIType, string[]>[] families))
			{
				return null;
			}
			var merged = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
			foreach (POIType kind in System.Enum.GetValues(typeof(POIType)))
			{
				var words = new List<string>();
				var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
				foreach (Dictionary<POIType, string[]> family in families)
				{
					if (!family.TryGetValue(kind, out string[] familyWords))
					{
						continue;
					}
					foreach (string word in familyWords)
					{
						if (words.Count < MaxWordsPerKind && seen.Add(word))
						{
							words.Add(word);
						}
					}
				}
				if (words.Count > 0)
				{
					merged[PlaceNameDefaults.KeyOf(kind)] = words.ToArray();
				}
			}
			return merged;
		}

		[DashboardTool(DashboardToolAttribute.Maintenance, "Add missing POI words to biomes", Section = "Content", Order = 5,
			Tooltip = "Gives every biome its own words for the point-of-interest kinds that occur there (a desert spring is an Oasis, a swamp shrine a Bog-altar). Kinds a biome already has words for are left exactly as authored.")]
		public static void FillMissingPOIWordsFromDashboard()
		{
			FillMissingPOIWords();
		}

		/// <summary>
		/// Adds the table's words for every kind a biome has no words for. NEVER touches an existing entry,
		/// so authored words survive any number of runs. Headless:
		/// <c>-executeMethod FishMMO.Shared.WorldDesign.BiomeNamingGenerator.FillMissingPOIWords</c>.
		/// </summary>
		public static int FillMissingPOIWords()
		{
			var unknown = new List<string>();
			int changedBiomes = 0;
			int addedKinds = 0;
			var paths = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate)))
			{
				paths.Add(AssetDatabase.GUIDToAssetPath(guid));
			}
			paths.Sort(System.StringComparer.Ordinal);

			foreach (string path in paths)
			{
				BiomeTemplate biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(path);
				if (biome == null)
				{
					continue;
				}
				SortedDictionary<string, string[]> words = PoiWordsFor(biome.name);
				if (words == null)
				{
					unknown.Add(biome.name);
					continue;
				}
				biome.Naming ??= new BiomeNamingData();
				biome.Naming.Phonology ??= new SerializableBiomePhonology();
				List<StringListMapping> rows = biome.Naming.Phonology.POITypeWords ??= new List<StringListMapping>();

				var present = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
				foreach (StringListMapping row in rows)
				{
					if (row != null && !string.IsNullOrEmpty(row.Key))
					{
						present.Add(row.Key);
					}
				}
				var toAdd = new List<StringListMapping>();
				foreach (KeyValuePair<string, string[]> pair in words)
				{
					if (!present.Contains(pair.Key))
					{
						toAdd.Add(new StringListMapping(pair.Key, pair.Value));
					}
				}
				if (toAdd.Count == 0)
				{
					continue;
				}
				Undo.RecordObject(biome, "Biome POI words");
				rows.AddRange(toAdd);
				biome.Naming.BuildRuntime();
				EditorUtility.SetDirty(biome);
				changedBiomes++;
				addedKinds += toAdd.Count;
			}
			if (changedBiomes > 0)
			{
				AssetDatabase.SaveAssets();
			}
			Debug.Log(changedBiomes > 0
				? $"[Biome naming] Added POI words for {addedKinds} kind(s) across {changedBiomes} biome(s)."
				: "[Biome naming] Every biome already has its POI words.");
			if (unknown.Count > 0)
			{
				Debug.LogWarning($"[Biome naming] {unknown.Count} biome(s) have no row in the POI word table and were left alone: " +
					string.Join(", ", unknown) + ". Add them to BiomeNamingGenerator.PoiFamiliesByBiome.");
			}
			return addedKinds;
		}

		// ── Place-naming content (grammar and races) ──────────────────────

		[DashboardTool(DashboardToolAttribute.Maintenance, "Write place-naming defaults", Section = "Content", Order = 6,
			Tooltip = "Adds the built-in place-name compositions, type words for every POI kind, fused endings, adjectives, nouns, kind weights and water phonology to the Name Grammar wherever it has none. Authored rows are kept.")]
		public static void WritePlaceNamingDefaultsFromDashboard()
		{
			PlaceNamingContentTool.WriteGrammarDefaults();
		}

		[DashboardTool(DashboardToolAttribute.Maintenance, "Fill race plural/adjective forms", Section = "Content", Order = 7,
			Tooltip = "Sets each race's empty Plural ('Orcs', 'Dwarves') and Adjective ('Elven', 'Goblin') for place names. Forms already set are kept.")]
		public static void FillRaceFormsFromDashboard()
		{
			PlaceNamingContentTool.FillRaceForms();
		}

		/// <summary>
		/// Every place-naming content step, in order, for one batch run:
		/// <c>-executeMethod FishMMO.Shared.WorldDesign.BiomeNamingGenerator.RunPlaceNamingTools</c>.
		/// </summary>
		public static void RunPlaceNamingTools()
		{
			Fill(out List<string> unknown);
			if (unknown.Count > 0)
			{
				Debug.LogWarning("[Biome naming] Biomes with no naming and no voice: " + string.Join(", ", unknown));
			}
			PlaceNamingContentTool.WriteGrammarDefaults();
			PlaceNamingContentTool.FillRaceForms();
			FillMissingPOIWords();
		}
	}
}
#endif
