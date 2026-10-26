using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The cave-crystal catalogue's kinds, habits and looks as the 83 biome drafts name them (2026-10-26, phase NEW-CRYSTALS,
	/// decision 34): every draft's kind resolves to one kind (a kind two drafts share is one kind with each biome's own look,
	/// decision 97), every habit a draft names to a habit that builds, the fantasy kinds glow and the real ones do not, and no
	/// biome grows any of it until its round names it. The meshes themselves are BiomeCaveContentTests' (every habit, native).
	/// </summary>
	[TestFixture]
	public class BiomeCrystalCatalogueTests
	{
		/// <summary>
		/// Every crystal the drafts name, as they spell it, with the kind it is and the look (null: the kind's own). Kept by hand
		/// from the drafts' "Cave crystals (.Crystals)" lines.
		/// </summary>
		private static readonly (string Draft, string Biome, string Kind, string Look)[] DraftCrystals =
		{
			("Phillipsite", "AbyssalPlain", "Phillipsite", null), ("Lodeheart", "AbyssalPlain", "Lodeheart", null),
			("FireIce", "Abyss", "FireIce", null), ("Hadalite", "Abyss", "Hadalite", null),
			("Gwindel", "Alpine", "Gwindel", null), ("PinkFluorite", "Alpine", "PinkFluorite", null), ("Starfrost", "Alpine", "Starfrost", null),
			("Stibnite", "AlpineMeadow", "Stibnite", null), ("Rhodochrosite", "AlpineMeadow", "Rhodochrosite", null), ("Stormglass", "AlpineMeadow", "Stormglass", null),
			("FireOpal", "Badlands", "FireOpal", null), ("Obsidian (Badlands variant)", "Badlands", "Obsidian", "Badlands"), ("Perilight", "Badlands", "Perilight", null),
			("Twinquartz", "BambooForest", "Twinquartz", null), ("Jadeite", "BambooForest", "Jadeite", null), ("Hotarubi", "BambooForest", "Hotarubi", null),
			("Aragonite", "Beach", "Aragonite", null), ("Pearlspar", "Beach", "Pearlspar", null),
			("Galena", "Castle", "Galena", null), ("Chalcopyrite", "Castle", "Chalcopyrite", null), ("Wyrmglass", "Castle", "Wyrmglass", null),
			("BlueJohn", "Cave", "BlueJohn", null), ("Helictite", "Cave", "Helictite", null), ("Echospar", "Cave", "Echospar", null),
			("Cassiterite", "CoastalWater", "Cassiterite", null), ("Bellspar", "CoastalWater", "Bellspar", null),
			("Apatite", "CoralReef", "Apatite", null), ("Lagoonglass", "CoralReef", "Lagoonglass", null),
			("Tektite", "Crater", "Tektite", null), ("Starglass", "Crater", "Starglass", null),
			("SalAmmoniac", "CryovolcanicPlain", "SalAmmoniac", null), ("Eitrglass", "CryovolcanicPlain", "Eitrglass", null),
			("Dogtooth", "DeepOcean", "Dogtooth", null), ("Stellamaris", "DeepOcean", "Stellamaris", null),
			("Malachite", "Desert", "Malachite", null), ("Sunglass", "Desert", "Sunglass", null),
			("Ilmenite", "DustSea", "Ilmenite", null), ("Phaseglass", "DustSea", "Phaseglass", null),
			("Vivianite", "Estuary", "Vivianite", null), ("Ebbspar", "Estuary", "Ebbspar", null),
			("Celestine", "Farmland", "Celestine", null), ("Aragonite (flos-ferri)", "Farmland", "Aragonite", "FlosFerri"), ("Fogpearl", "Farmland", "Fogpearl", null),
			("Green fluorite", "Forest", "GreenFluorite", null), ("Wyrdstone", "Forest", "Wyrdstone", null),
			("Cinnabar", "ForestRuins", "Cinnabar", null), ("Farrstone", "ForestRuins", "Farrstone", null),
			("Realgar", "Fortress", "Realgar", null), ("Orpiment", "Fortress", "Orpiment", null), ("Dalistone", "Fortress", "Dalistone", null),
			("IcelandSpar", "GeyserBasin", "IcelandSpar", null), ("Stilbite", "GeyserBasin", "Stilbite", null), ("Sólarsteinn", "GeyserBasin", "Solarsteinn", null),
			("CupHoar", "Glacier", "CupHoar", null), ("FrostCrystal", "Glacier", "FrostCrystal", null), ("Nāgamani", "Glacier", "Nagamani", null),
			("Calcite dog-tooth", "Grassland", "Dogtooth", "Grassland"), ("Skyglass", "Grassland", "Skyglass", null),
			("Turquoise", "HighDesert", "Turquoise", null), ("Gueststar", "HighDesert", "Gueststar", null),
			("Garnet", "Hills", "Garnet", null), ("Staurolite", "Hills", "Staurolite", null), ("ClearQuartz (Hills variant)", "Hills", "ClearQuartz", "Hills"), ("Heartgarnet", "Hills", "Heartgarnet", null),
			("Hoarfeather", "IceCave", "Hoarfeather", null), ("Cryocalcite", "IceCave", "Cryocalcite", null), ("Chudfire", "IceCave", "Chudfire", null),
			("Hydrohalite", "IceGeyserField", "Hydrohalite", null), ("Hyalite", "IceGeyserField", "Hyalite", null),
			("Nightpearl", "IcePalace", "Nightpearl", null), ("Obsidian", "IcePalace", "Obsidian", null),
			("Crystallus", "IceRuin", "ClearQuartz", "Crystallus"), ("ScythianEmerald", "IceRuin", "ScythianEmerald", null),
			("Blue quartz", "IceSheet", "ClearQuartz", "BlueQuartz"), ("Hornice", "IceSheet", "Hornice", null),
			("Hoar chandelier", "IceShelf", "FrostCrystal", "HoarChandelier"), ("Jadeice", "IceShelf", "Jadeice", null), ("Swellglass", "IceShelf", "Swellglass", null),
			("Chrysolite", "ImpactBasin", "Chrysolite", null), ("Mansion Pearl", "ImpactBasin", "MansionPearl", null),
			("WatermelonTourmaline", "Jungle", "WatermelonTourmaline", null), ("Leafglass", "Jungle", "Leafglass", null),
			("Specularite", "JungleRuins", "Specularite", null), ("Glyphjade", "JungleRuins", "Glyphjade", null),
			("Zircon", "JungleTemple", "Zircon", null), ("Monsoonglass", "JungleTemple", "Monsoonglass", null),
			("Smithsonite", "Karst", "Smithsonite", null), ("Mistjade", "Karst", "Mistjade", null),
			("CavePearl", "Lake", "CavePearl", null), ("Nixglass", "Lake", "Nixglass", null),
			("Thenardite", "LavaTube", "Thenardite", null), ("OlivineBomb", "LavaTube", "OlivineBomb", null), ("Magecstone", "LavaTube", "Magecstone", null),
			("Marcasite", "Mangrove", "Marcasite", null), ("Nuru", "Mangrove", "Nuru", null),
			("Swallowtail", "MarblePalace", "Selenite", "Swallowtail"), ("Simetite", "MarblePalace", "Simetite", null), ("Fatastone", "MarblePalace", "Fatastone", null),
			("Waxspar", "MethaneLake", "Waxspar", null), ("Adamant", "MethaneLake", "Adamant", null), ("Orphanstone", "MethaneLake", "Orphanstone", null),
			("Cristobalite", "MoltenSurface", "Cristobalite", null), ("Brimstone Amber", "MoltenSurface", "BrimstoneAmber", null), ("Dracontite", "MoltenSurface", "Dracontite", null),
			("Hydrophane", "MountainSlope", "Hydrophane", null), ("Moonstone", "MountainSlope", "Moonstone", null), ("Amethyst (basalt geodes)", "MountainSlope", "Amethyst", "MountainSlope"),
			("NitrogenSpar", "NitrogenIceField", "NitrogenSpar", null), ("Tearglass", "NitrogenIceField", "Tearglass", null),
			("DesertRose", "Oasis", "DesertRose", null), ("Alum", "Oasis", "Alum", null), ("Oraclestone", "Oasis", "Oraclestone", null),
			("Siderite", "Ocean", "Siderite", null), ("Galerna-stone", "Ocean", "GalernaStone", null),
			("Vivianite", "PeatBog", "Vivianite", "PeatBog"), ("Wispglass", "PeatBog", "Wispglass", null),
			("Iris glass", "PermanentIce", "Obsidian", "IrisGlass"), ("Stillglass", "PermanentIce", "Stillglass", null),
			("BariteRose", "Plains", "BariteRose", null), ("SatinSpar", "Plains", "SatinSpar", null), ("Sunspoke", "Plains", "Sunspoke", null),
			("Antozonite", "RadiationPlain", "Antozonite", null), ("Septentrion", "RadiationPlain", "Septentrion", null),
			("Balas", "RegolithPlain", "Balas", null), ("Levanite", "RegolithPlain", "Levanite", null),
			("Sapphire", "Rille", "Sapphire", null), ("Shamirspar", "Rille", "Shamirspar", null),
			("Agate", "River", "Agate", null), ("Rhinegold Seam", "River", "RhinegoldSeam", null),
			("Kyanite", "RockyCoast", "Kyanite", null), ("Haarglass", "RockyCoast", "Haarglass", null),
			("Lajvard", "RockyTerrain", "Lajvard", null), ("Shabchiragh", "RockyTerrain", "Shabchiragh", null),
			("Wollastonite", "RunawayGreenhousePlain", "Wollastonite", null), ("Pyrrhotite", "RunawayGreenhousePlain", "Pyrrhotite", null), ("Sibylleaf", "RunawayGreenhousePlain", "Sibylleaf", null),
			("Halite", "SaltFlat", "Halite", null), ("Sylvite", "SaltFlat", "Sylvite", null), ("Embersalt", "SaltFlat", "Embersalt", null),
			("Reefgold", "Savanna", "Reefgold", null), ("Emerald", "Savanna", "Emerald", null), ("Rainglass", "Savanna", "Rainglass", null),
			("Eonyang", "Scree", "Amethyst", "Eonyang"), ("Goblinfire", "Scree", "Goblinfire", null),
			("Peridot", "Scrubland", "Peridot", null), ("Sunshell", "Scrubland", "Sunshell", null),
			("Lapastone", "Seamount", "Lapastone", null), ("Analcime", "Seamount", "Analcime", null),
			("Rutilquartz", "Snow", "Rutilquartz", null), ("Midwinter Spar", "Snow", "MidwinterSpar", null),
			("Dioptase", "Steppe", "Dioptase", null), ("Wolf's-eye", "Steppe", "WolfsEye", null),
			("Covellite", "SubsurfaceOceanVent", "Covellite", null), ("Anhydrite", "SubsurfaceOceanVent", "Anhydrite", null), ("Gledstone", "SubsurfaceOceanVent", "Gledstone", null),
			("NativeSulphur", "SulphurFlats", "NativeSulphur", null), ("SalAmmoniac", "SulphurFlats", "SalAmmoniac", "SulphurFlats"), ("Bluebrim", "SulphurFlats", "Bluebrim", null),
			("Leadfeather", "SulphuricCloudDeck", "Leadfeather", null), ("Pyrite", "SulphuricCloudDeck", "Pyrite", null), ("Cloudheart", "SulphuricCloudDeck", "Cloudheart", null),
			("Pyrite", "Swamp", "Pyrite", "Swamp"), ("Mireopal", "Swamp", "Mireopal", null),
			("Aetite", "SwampCave", "Aetite", null), ("Bolla's Eye", "SwampCave", "BollasEye", null),
			("Hauyne", "SwampRuins", "Hauyne", null), ("Leucite", "SwampRuins", "Leucite", null), ("Saeculite", "SwampRuins", "Saeculite", null),
			("TrapicheEmerald", "SwampTemple", "TrapicheEmerald", null), ("Mistberyl", "SwampTemple", "Mistberyl", null),
			("Smoky quartz", "Taiga", "SmokyQuartz", null), ("Amberlight", "Taiga", "Amberlight", null),
			("Quicksilver", "TholinPlain", "Quicksilver", null), ("Hazeamber", "TholinPlain", "Hazeamber", null),
			("Epsomite", "TidalFracture", "Epsomite", null), ("Sakhrastone", "TidalFracture", "Sakhrastone", null),
			("Labradorite", "Tundra", "Labradorite", null), ("Aurorite", "Tundra", "Aurorite", null),
			("Ikaite", "UnderwaterCanyon", "Ikaite", null), ("Glendonite", "UnderwaterCanyon", "Ikaite", "Glendonite"), ("Tremorspar", "UnderwaterCanyon", "Tremorspar", null),
			("Demantoid", "Valley", "Demantoid", null), ("Sphene", "Valley", "Sphene", null), ("Laurinite", "Valley", "Laurinite", null),
			("Itzsheen", "VolcanicCave", "Obsidian", "Itzsheen"), ("SulphurNeedles", "VolcanicCave", "SulphurNeedles", null), ("Swordblade", "VolcanicCave", "Selenite", "Swordblade"),
			("Smokestone", "VolcanicCave", "Smokestone", null),
			("Obsidian", "Volcanic", "Obsidian", null), ("Thenardite", "Volcanic", "Thenardite", "Volcanic"), ("Hearthglass", "Volcanic", "Hearthglass", null),
			("Rossa", "VolcanicTemple", "Obsidian", "Rossa"), ("Hieratite", "VolcanicTemple", "Hieratite", null), ("Forgeheart", "VolcanicTemple", "Forgeheart", null),
			("Goethite", "Wasteland", "Goethite", null), ("Weepglass", "Wasteland", "Weepglass", null),
			("Carnelian", "Wetlands", "Carnelian", null), ("Selenite (Wetlands variant)", "Wetlands", "Selenite", "Wetlands"), ("Abzulite", "Wetlands", "Abzulite", null),
			("Flint geode", "Woodland", "FlintGeode", null), ("Sunstone", "Woodland", "Sunstone", null),
		};

		/// <summary>Every habit the drafts name, as they spell it, and the habit it is built in.</summary>
		private static readonly (string Draft, string Habit)[] DraftHabits =
		{
			("Botryoidal", "Botryoidal"), ("Grape-cluster", "Botryoidal"), ("Twinned", "Twinned"), ("Helix", "Helix"), ("Massive", "Massive"),
			("Coralloid", "Coralloid"), ("Foliated", "Foliated"), ("Fibrous", "Fibrous"), ("Rosette", "Rosette"), ("Octahedral", "Octahedral"),
			("Bipyramid", "Bipyramid"), ("Dendrite", "Dendrite"), ("Dendritic", "Dendrite"), ("Pearls", "Pearls"), ("Floss", "Floss"), ("Cupped", "Cupped"),
			("Barrel", "Barrel"), ("Stellate", "Stellate"), ("Droplet", "Droplet"), ("Rhomb", "Rhomb"), ("Rhombohedral", "Rhomb"), ("Tabular", "Tabular"),
			("Plates", "Tabular"), ("Platy", "Tabular"), ("Radiating", "Radiating"), ("Acicular", "Radiating"), ("Leaf", "Leaf"),
			("Dodecahedral", "Dodecahedral"), ("Trapezohedral", "Trapezohedral"), ("Trapiche", "Trapiche"), ("Matrix", "Matrix"),
			("Nodules", "Nodules"), ("Nodule", "Nodules"), ("Prisms", "Prisms"), ("Prismatic", "Prisms"), ("Cubes", "Cubes"), ("Cubic", "Cubes"),
			("Blades", "Blades"), ("Bladed", "Blades"), ("Druse", "Druse"), ("Geode", "Geode"), ("Shards", "Shards"), ("Rounded", "Pearls"),
		};

		private static CaveMineral[] All => ProceduralArtCatalogue.CaveMinerals;

		/// <summary>A biome's asset name (its spec's, "Permanent Ice") from the drafts' file name ("PermanentIce").</summary>
		private static string Asset(string draft) => BiomeArtSpec.Entries.Select(e => e.Biome).FirstOrDefault(b => b.Replace(" ", "") == draft) ?? draft;

		[Test]
		public void EveryDraftCrystal_ResolvesToOneKind_OrItsBiomesLook()
		{
			foreach ((string draft, string biome, string kindName, string lookName) in DraftCrystals)
			{
				Assert.That(Enum.TryParse(kindName, out BiomeCaveCrystalKind kind), Is.True, $"{biome}'s {draft}: no kind {kindName}");
				Assert.That((int)kind, Is.LessThan(All.Length), $"{draft}: no catalogue row");
				Assert.That(All[(int)kind].Kind, Is.EqualTo(kind));
				Assert.That(BiomeArtSpec.For(Asset(biome)), Is.Not.Null, $"{draft}: no biome {biome}");
				byte look = ProceduralArtCatalogue.CaveMineralLookFor(kind, Asset(biome));
				if (lookName == null)
				{
					Assert.That(look, Is.EqualTo(0), $"{biome}'s {draft} is the kind's own look");
					continue;
				}
				// Glendonite is chosen by hand (the draft shows it by condition, not by biome); the rest follow their biome.
				CaveMineralLook own = ProceduralArtCatalogue.CaveMineralLooks.Single(l => l.Kind == kind && l.Name == lookName);
				Assert.That(own.Biome == null ? own.Look : look, Is.EqualTo(own.Look), $"{biome}'s {draft} is its biome's look of {kindName}");
				Assert.That(ProceduralArtCatalogue.TryCaveMineralLook(kind, own.Look, out CaveMineralLook again) && again.Name == lookName, Is.True);
			}
			// Every kind the catalogue holds is a draft's (or one of the first ten, also drafts'), and every look is a draft's.
			var named = new HashSet<string>(DraftCrystals.Select(d => d.Kind));
			foreach (CaveMineral m in All)
			{
				Assert.That(named, Does.Contain(m.Kind.ToString()), $"{m.Kind} is no draft's");
			}
			var looks = new HashSet<string>(DraftCrystals.Where(d => d.Look != null).Select(d => d.Kind + "/" + d.Look));
			Assert.That(ProceduralArtCatalogue.CaveMineralLooks.Select(l => l.Kind + "/" + l.Name), Is.EquivalentTo(looks));
		}

		[Test]
		public void SharedKinds_AreOneKind_WithEachBiomesLook()
		{
			// Decision 97 (C2, C3): Aragonite and Sal Ammoniac are one kind each, the second biome its own look.
			string[] names = Enum.GetNames(typeof(BiomeCaveCrystalKind));
			Assert.That(names.Count(n => n.IndexOf("Aragonite", StringComparison.OrdinalIgnoreCase) >= 0), Is.EqualTo(1));
			Assert.That(names.Count(n => n.IndexOf("SalAmmoniac", StringComparison.OrdinalIgnoreCase) >= 0), Is.EqualTo(1));
			Assert.That(ProceduralArtCatalogue.CaveMineralLookFor(BiomeCaveCrystalKind.Aragonite, Asset("Beach")), Is.EqualTo(0));
			Assert.That(ProceduralArtCatalogue.CaveMineralLookFor(BiomeCaveCrystalKind.Aragonite, Asset("Farmland")), Is.GreaterThan(0));
			Assert.That(ProceduralArtCatalogue.CaveMineralLookFor(BiomeCaveCrystalKind.SalAmmoniac, Asset("CryovolcanicPlain")), Is.EqualTo(0));
			Assert.That(ProceduralArtCatalogue.CaveMineralLookFor(BiomeCaveCrystalKind.SalAmmoniac, Asset("SulphurFlats")), Is.GreaterThan(0));
			// No draft name is a kind twice: every pair of drafts naming one crystal names the same kind.
			// (A look chosen by hand, as Ikaite's glendonite, sits beside its kind's own in the one biome.)
			var byHand = new HashSet<string>(ProceduralArtCatalogue.CaveMineralLooks.Where(l => l.Biome == null).Select(l => l.Name));
			foreach (IGrouping<string, (string Draft, string Biome, string Kind, string Look)> g in DraftCrystals.Where(d => d.Look == null || !byHand.Contains(d.Look)).GroupBy(d => d.Biome + "/" + d.Kind))
			{
				Assert.That(g.Count(), Is.EqualTo(1), $"{g.Key}: a biome grows one look of a kind");
			}
		}

		[Test]
		public void EveryDraftHabit_IsAHabit_ThatSomeKindOrLookIsBuiltIn()
		{
			var used = new HashSet<CaveMineralHabit>(ProceduralArtCatalogue.CaveMineralHabits());
			foreach ((string draft, string habitName) in DraftHabits)
			{
				Assert.That(Enum.TryParse(habitName, out CaveMineralHabit habit), Is.True, $"{draft}: no habit {habitName}");
				Assert.That(used, Does.Contain(habit), $"{draft} ({habit}) is built by no kind or look, so has no meshes");
			}
			// Append-only: the first seven where they were, the drafts' 25 after.
			string[] habits =
			{
				"Druse", "Prisms", "Geode", "Cubes", "Shards", "Nodules", "Blades",
				"Botryoidal", "Twinned", "Helix", "Massive", "Coralloid", "Foliated", "Fibrous", "Rosette", "Octahedral", "Bipyramid", "Dendrite", "Pearls",
				"Floss", "Cupped", "Barrel", "Stellate", "Droplet", "Rhomb", "Tabular", "Radiating", "Leaf", "Dodecahedral", "Trapezohedral", "Trapiche", "Matrix",
			};
			Assert.That(Enum.GetNames(typeof(CaveMineralHabit)), Is.EqualTo(habits));
			Assert.That(used.Count, Is.EqualTo(habits.Length), "every habit has a kind or look built in it");
		}

		[Test]
		public void FantasyKindsGlow_RealOnesDoNot_AndTheRuntimeAgrees()
		{
			int glowing = 0;
			foreach (CaveMineral m in All)
			{
				Assert.That(BiomeCaveCrystals.Glows(m.Kind), Is.EqualTo(m.Glows), $"{m.Kind}: the runtime's glow rule is the catalogue's");
				bool fantasy = m.Description.IndexOf("fantasy", StringComparison.OrdinalIgnoreCase) >= 0;
				Assert.That(m.Glows, Is.EqualTo(fantasy && m.Kind != BiomeCaveCrystalKind.Smokestone), $"{m.Kind}: a fantasy kind glows, a real one does not");
				if (m.Flickers)
				{
					Assert.That(m.Glows, Is.True, $"{m.Kind} flickers, so it glows");
				}
				glowing += m.Glows ? 1 : 0;
			}
			Assert.That(glowing, Is.EqualTo(81), "the three of 2026-10-16 and the drafts' 78");
			// Smokestone is fantasy but no light (its draft: "NOT a light").
			Assert.That(BiomeCaveCrystals.Glows(BiomeCaveCrystalKind.Smokestone), Is.False);
			Assert.That(All.Count(m => m.Flickers), Is.EqualTo(9), "stormglass and the drafts' twinkles, sparkles and darts");
		}

		[Test]
		public void Looks_AreNumberedInTheirKind_Unique_AndListedWhereTheGeneratorLooks()
		{
			var payload = new HashSet<string>(ProceduralArtCatalogue.PayloadPaths());
			var wrappers = new HashSet<string>(ProceduralArtCatalogue.WrapperPaths());
			var prefabs = new HashSet<string>(ProceduralArtCatalogue.AllPrefabNames());
			var names = new HashSet<string>();
			foreach (IGrouping<BiomeCaveCrystalKind, CaveMineralLook> kind in ProceduralArtCatalogue.CaveMineralLooks.GroupBy(l => l.Kind))
			{
				Assert.That(kind.Select(l => (int)l.Look), Is.EqualTo(Enumerable.Range(1, kind.Count())), $"{kind.Key}'s looks are 1, 2, … in order");
				Assert.That(kind.Where(l => l.Biome != null).Select(l => l.Biome).Distinct().Count(), Is.EqualTo(kind.Count(l => l.Biome != null)), $"{kind.Key}: one look a biome");
			}
			foreach (CaveMineralLook look in ProceduralArtCatalogue.CaveMineralLooks)
			{
				Assert.That(names.Add(look.Kind + "_" + look.Name), Is.True);
				Assert.That(look.Description, Is.Not.Empty);
				Assert.That(look.Biome == null || BiomeArtSpec.For(look.Biome) != null, Is.True, $"{look.Name}: no biome {look.Biome}");
				CaveGlowHue hue = ProceduralArtCatalogue.CaveMineralLookHue(in look);
				CaveMineral kind = ProceduralArtCatalogue.CaveMineralOf(look.Kind);
				Assert.That(hue.Emission, Is.EqualTo(kind.Hue.Emission), "a look glows as its kind does");
				Assert.That(hue.Albedo, Is.Not.EqualTo(kind.Hue.Albedo), $"{look.Name} is its own colour");
				Assert.That(wrappers, Does.Contain(ProceduralArtCatalogue.MaterialPath(ProceduralArtCatalogue.CaveMineralLookMaterial(in look))));
				for (int v = 0; v < ProceduralArtCatalogue.CaveMineralVariants; v++)
				{
					string prefab = CaveArt.MineralPrefabName(look.Kind, v, look.Look);
					Assert.That(prefab, Is.EqualTo(ProceduralArtCatalogue.CaveMineralLookPrefab(in look, v)));
					Assert.That(prefabs, Does.Contain(prefab));
					Assert.That(wrappers, Does.Contain(ProceduralArtCatalogue.PrefabPath(prefab)));
					for (int lod = 0; lod < CaveGlowMeshes.Levels; lod++)
					{
						Assert.That(payload, Does.Contain(ProceduralArtCatalogue.MeshPath(ProceduralArtCatalogue.CaveMineralMesh(look.Habit, v, lod))), "its habit's meshes are made");
					}
				}
			}
			// A look of 0, or one the kind has not, is the kind's own art: the names of 2026-10-16 unchanged.
			Assert.That(CaveArt.MineralPrefabName(BiomeCaveCrystalKind.Obsidian, 1, 0), Is.EqualTo(CaveArt.MineralPrefabName(BiomeCaveCrystalKind.Obsidian, 1)));
			Assert.That(CaveArt.MineralPrefabName(BiomeCaveCrystalKind.Amethyst, 2, 99), Is.EqualTo("Cave/Cave_Mineral_Amethyst_2"));
			Assert.That(CaveArt.MineralPrefabName(BiomeCaveCrystalKind.Obsidian, 1, ProceduralArtCatalogue.CaveMineralLookFor(BiomeCaveCrystalKind.Obsidian, Asset("VolcanicTemple"))),
				Is.EqualTo("Cave/Cave_Mineral_Obsidian_Rossa_1"), "paths are a contract");
			Assert.That(CaveArt.MineralPrefabName(BiomeCaveCrystalKind.Selenite, 0, 0), Is.EqualTo(RockArtNames.FormationPrefab("Gypsum", "Crystal", 0)), "selenite is still gypsum's crystal");
		}

		[Test]
		public void NoBiomeGrowsAnythingNew_UntilItsRound_AndANamedKindTakesItsBiomesLook()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				List<BiomeCaveCrystal> crystals = entry.CaveCrystals();
				bool byDefault = crystals.Count == 1 && crystals[0].Kind == BiomeCaveCrystalKind.Selenite && crystals[0].Weight == 1f;
				foreach (BiomeCaveCrystal c in crystals)
				{
					// A kind the biome's file names takes the biome's look (the default selenite never does: below).
					Assert.That(c.Look, Is.EqualTo(byDefault ? 0 : ProceduralArtCatalogue.CaveMineralLookFor(c.Kind, entry.Biome)), $"{entry.Biome} {c.Kind}");
				}
				if (byDefault)
				{
					Assert.That(crystals[0].Look, Is.EqualTo(0), $"{entry.Biome}: the default selenite is the old crystal");
					Assert.That(crystals[0].ToString(), Is.EqualTo("Selenite×1"), "its mark unchanged");
				}
			}
			Assert.That(new BiomeCaveCrystal(BiomeCaveCrystalKind.Obsidian, 0.5f, 4).ToString(), Is.EqualTo("Obsidian#4×0.5"));
			Assert.That(new BiomeCaveCrystal(BiomeCaveCrystalKind.Obsidian, 0.5f).ToString(), Is.EqualTo("Obsidian×0.5"));
		}

		[Test]
		public void LookOf_IsTheListsFirstWeightedEntryOfTheKind()
		{
			var list = new List<BiomeCaveCrystal>
			{
				new BiomeCaveCrystal(BiomeCaveCrystalKind.Obsidian, 0f, 2),
				new BiomeCaveCrystal(BiomeCaveCrystalKind.Obsidian, 0.5f, 3),
				new BiomeCaveCrystal(BiomeCaveCrystalKind.Selenite, 1f),
			};
			Assert.That(BiomeCaveCrystals.LookOf(list, BiomeCaveCrystalKind.Obsidian), Is.EqualTo(3));
			Assert.That(BiomeCaveCrystals.LookOf(list, BiomeCaveCrystalKind.Selenite), Is.EqualTo(0));
			Assert.That(BiomeCaveCrystals.LookOf(list, BiomeCaveCrystalKind.Amethyst), Is.EqualTo(0));
			Assert.That(BiomeCaveCrystals.LookOf(null, BiomeCaveCrystalKind.Amethyst), Is.EqualTo(0));
			// Picking by weight is unchanged by a look (the kind is what is picked).
			Assert.That(BiomeCaveCrystals.TryPick(list, 0.1f, out BiomeCaveCrystalKind picked) && picked == BiomeCaveCrystalKind.Obsidian, Is.True);
		}
	}
}
