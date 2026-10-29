using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase CODE-TABLE CLEARANCE (2026-10-26, decision 1: the biome template is the single source of truth): the code tables
	/// that decided a biome's content by its NAME (the vine table, the gathering catalogue's biome lists, the point-of-interest
	/// catalogue's kind rows, the point-of-interest templates' biome lists, the weathering keywords, the terrain process table,
	/// the weather content generator's ground table) were written into every biome's spec file as columns, which the authoring
	/// writes onto the template (marked). These tests pin, for all 83 biomes, that each file's columns give exactly what the old
	/// table decided (so a re-author and a re-cut change nothing), that the templates already hold what the moved columns write,
	/// and, by a source scan, that no code decides a biome's content by its name outside the spec files and the single fallback
	/// reader (BiomeTemplateArt), which covers a template never authored and warns once.
	/// </summary>
	public class BiomeCodeTableClearanceTests
	{
		private const string WorldFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";

		private static IEnumerable<BiomeArtSpec.Entry> Entries => BiomeArtSpec.Entries;

		[Test]
		public void EveryFile_CarriesTheMovedColumns()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			string folder = Path.Combine(WorldFolder, "ProceduralArt/BiomeArtSpec");
			string[] files = Directory.GetFiles(folder, "BiomeArtSpec.*.cs");
			Assert.That(files.Length, Is.EqualTo(83));
			foreach (string file in files)
			{
				string code = CodeOnly(File.ReadAllText(file));
				foreach (string column in new[] { ".Sites(", ".Gathers(", ".Weathers(", ".ErodesAs(" })
				{
					Assert.That(code, Does.Contain(column), $"{Path.GetFileName(file)}: {column}");
				}
			}
		}

		// ── Each moved table, pinned against its old decision for all 83 ──

		/// <summary>The vines each file names are the vine table's row for its name, field for field; none where the table had no row.</summary>
		[Test]
		public void Vines_TheFilesGiveTheVineTablesRows()
		{
			int rows = 0;
			foreach (BiomeArtSpec.Entry entry in Entries)
			{
				BiomeVines spec = entry.VineGrowth();
				VineBiomes.Habitat old = VineBiomes.For(entry.Biome);
				if (old == null)
				{
					// A biome the old table never grew vines in: none, unless its authored round names its own (Badlands, 2026-10-26).
					if (!BiomeAuthoredRounds.Contains(entry.Biome))
					{
						Assert.That(spec.Species, Is.Empty, entry.Biome);
						Assert.That(spec.Caves + spec.Ruins + spec.Cliffs + spec.Trees, Is.EqualTo(0f), entry.Biome);
					}
					continue;
				}
				rows++;
				if (BiomeAuthoredRounds.Contains(entry.Biome))
				{
					// An authored round names its own vines (BiomeAuthoredRoundTests); the row still belongs to its file.
					continue;
				}
				Assert.That(spec.Caves, Is.EqualTo(old.Caves), entry.Biome);
				Assert.That(spec.Ruins, Is.EqualTo(old.Ruins), entry.Biome);
				Assert.That(spec.Cliffs, Is.EqualTo(old.Cliffs), entry.Biome);
				Assert.That(spec.Trees, Is.EqualTo(old.Trees), entry.Biome);
				Assert.That(spec.Species.Select(s => (s.Species, s.Weight)), Is.EqualTo(old.Species.Select(s => (s.species, s.weight))), entry.Biome);
			}
			Assert.That(rows, Is.EqualTo(VineBiomes.All.Length), "every row of the table belongs to a biome file");
		}

		/// <summary>The gathering resources each file names are the catalogue's rows that grow in its biome by name, in the catalogue's order.</summary>
		[Test]
		public void Gathering_TheFilesGiveTheCataloguesRows()
		{
			foreach (BiomeArtSpec.Entry entry in Entries)
			{
				var old = new List<string>();
				foreach (GatheringResource resource in GatheringResourceCatalogue.All)
				{
					if (GatheringResourceCatalogue.GrowsIn(resource, entry.Biome) && !old.Contains(resource.Node))
					{
						old.Add(resource.Node);
					}
				}
				Assert.That(entry.GatheringResources(), Is.EqualTo(old), entry.Biome);
				if (BiomeAuthoredRounds.Contains(entry.Biome))
				{
					// An authored round names its own grove herbs (BiomeAuthoredRoundTests); the catalogue's biome lists follow its file.
					continue;
				}
				Assert.That(entry.GroveHerbs(), Is.EqualTo(BiomeArtSpec.HerbsOf(old)), entry.Biome);
			}
		}

		/// <summary>Each file's weathering is what its name's keywords gave (the finish and its chances; a coat is the file's own).</summary>
		[Test]
		public void Weathering_TheFilesGiveWhatTheNameGave()
		{
			foreach (BiomeArtSpec.Entry entry in Entries.Where(e => !BiomeAuthoredRounds.Contains(e.Biome)))
			{
				BiomeWeathering spec = entry.Weathering(), old = BiomeArtSpec.WeatheringByName(entry.Biome);
				Assert.That((spec.Finish, spec.Chance, spec.ChancePerDecay), Is.EqualTo((old.Finish, old.Chance, old.ChancePerDecay)), entry.Biome);
			}
			Assert.That(new BiomeArtSpec.Entry { Biome = "Swamp" }.Weathering(), Is.EqualTo(BiomeArtSpec.WeatheringByName(null)), "a file naming none: no name read");
			Assert.That(BiomeArtSpec.DefaultWeathering, Is.EqualTo(BiomeArtSpec.WeatheringByName(null)));
		}

		/// <summary>Each file's sites are the weights every catalogue row gave its biome by name (KindWeightsFor), kind for kind, in kind order.</summary>
		[Test]
		public void SiteKinds_TheFilesGiveTheCatalogueRowsWeights()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			int kinds = 0;
			foreach (BiomeArtSpec.Entry entry in Entries)
			{
				List<BiomeSiteKind> spec = entry.SiteKinds();
				List<(POIType Kind, float Weight)> old = rules.KindWeightsFor(entry.Biome);
				if (!BiomeAuthoredRounds.Contains(entry.Biome))
				{
					Assert.That(spec.Select(k => (k.Kind, k.Weight)), Is.EqualTo(old), entry.Biome);
				}
				foreach (BiomeSiteKind k in spec)
				{
					Assert.That(k.NeedsBreathableAir, Is.EqualTo(entry.NeedsAir(k.Kind)), $"{entry.Biome} {k.Kind}");
					Assert.That(k.NeedsLiquidWater, Is.EqualTo(entry.NeedsWater(k.Kind)), $"{entry.Biome} {k.Kind}");
				}
				kinds += spec.Count;
			}
			Assert.That(kinds, Is.GreaterThan(83 * 20));
			Assert.That(new BiomeArtSpec.Entry { Biome = "Forest" }.SiteKinds(), Is.Empty, "a file naming none hosts none: no name read");
		}

		/// <summary>
		/// Each file's variant weights are what the point-of-interest templates' biome lists gave its biome by name, in the templates'
		/// order: the list (and so its mark) is the old one. A hand-made template naming the biome still lands after them, and one
		/// the catalogue writes is still skipped.
		/// </summary>
		[Test]
		public void SiteVariants_TheFilesGiveTheTemplatesBiomeLists_AndHandMadeStillCounts()
		{
			var handMade = new List<PointOfInterestTemplateSpec>();
			var own = new PointOfInterestTemplateSpec { Kind = POIType.Village, Variant = "Hand Built", DefaultBiomeWeight = 1f };
			own.BiomeWeights.Add(new PointOfInterestBiomeWeight("Forest", 3f));
			own.BiomeWeights.Add(new PointOfInterestBiomeWeight("Desert", 0.25f));
			handMade.Add(own);
			var copy = new PointOfInterestTemplateSpec { Kind = POIType.Village, Variant = "Timber", DefaultBiomeWeight = 1f };
			copy.BiomeWeights.Add(new PointOfInterestBiomeWeight("Forest", 9f));
			handMade.Add(copy);
			int variants = 0, unauthored = 0;
			foreach (BiomeArtSpec.Entry entry in Entries.Where(e => !BiomeAuthoredRounds.Contains(e.Biome)))
			{
				Assert.That(Prints(entry.SiteVariants()), Is.EqualTo(Prints(OldVariants(entry.Biome, null))), entry.Biome);
				Assert.That(Prints(entry.SiteVariants(handMade)), Is.EqualTo(Prints(OldVariants(entry.Biome, handMade))), entry.Biome + " with hand-made templates");
				variants += entry.SiteVariants().Count;
				unauthored++;
			}
			// Over 400 among all 83 before the rounds (about five a biome). The rounds take biomes out of this loop with more variants
			// than the average or fewer, so the floor is the old algorithm's own count for the biomes still unauthored (each pinned above).
			int oldVariants = Entries.Where(e => !BiomeAuthoredRounds.Contains(e.Biome)).Sum(e => OldVariants(e.Biome, null).Count);
			Assert.That(variants, Is.EqualTo(oldVariants));
			Assert.That(variants, Is.GreaterThan(unauthored * 3));
			Assert.That(BiomeArtSpec.For("Forest").SiteVariants(handMade).Any(v => v.Variant == "Hand Built" && v.Weight == 3f), Is.True);
			Assert.That(BiomeArtSpec.For("Forest").SiteVariants(handMade).Any(v => v.Variant == "Timber" && v.Weight == 9f), Is.False, "the catalogue's own variant is skipped");
		}

		/// <summary>The pre-2026-10-26 algorithm, by the biome's name: the catalogue's templates, then the hand-made ones.</summary>
		private static List<BiomeSiteVariant> OldVariants(string biome, IEnumerable<PointOfInterestTemplateSpec> handMade)
		{
			var list = new List<BiomeSiteVariant>();
			void Set(POIType kind, string variant, float weight)
			{
				int at = list.FindIndex(v => v.Kind == kind && string.Equals(v.Variant, variant, StringComparison.OrdinalIgnoreCase));
				if (at >= 0)
				{
					list[at] = new BiomeSiteVariant(kind, variant, weight);
				}
				else
				{
					list.Add(new BiomeSiteVariant(kind, variant, weight));
				}
			}
			var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (PointOfInterestTemplateSpec spec in PointOfInterestTemplateSpecs.All())
			{
				written.Add(spec.Kind + "/" + spec.Variant);
				float weight = spec.BiomeWeightByName(biome);
				if (weight != spec.DefaultBiomeWeight)
				{
					Set(spec.Kind, spec.Variant, weight);
				}
			}
			foreach (PointOfInterestTemplateSpec own in handMade ?? Enumerable.Empty<PointOfInterestTemplateSpec>())
			{
				if (!written.Add(own.Kind + "/" + own.Variant.Trim()))
				{
					continue;
				}
				float weight = own.BiomeWeightByName(biome);
				if (weight != own.DefaultBiomeWeight)
				{
					Set(own.Kind, own.Variant.Trim(), weight);
				}
			}
			return list;
		}

		private static List<string> Prints(List<BiomeSiteVariant> variants) => variants.ConvertAll(v => v.ToString());

		/// <summary>The terrain process table's assignments until 2026-10-26, by exact asset name (moved into the files since).</summary>
		private static readonly (string Biome, string Profile)[] OldTerrainProcesses =
		{
			("Deep Ocean", "Seabed"), ("Abyssal Plain", "Seabed"), ("Abyss", "Seabed"), ("Ocean", "Seabed"), ("Underwater Canyon", "Seabed"),
			("Seamount", "Seabed"), ("Coastal Water", "Seabed"), ("Coral Reef", "Seabed"), ("Subsurface Ocean Vent", "Seabed"), ("Beach", "Coastal"),
			("Rocky Coast", "Coastal"), ("Estuary", "Wetland"), ("Mangrove", "Wetland"), ("Forest", "Temperate"), ("Woodland", "Temperate"),
			("Valley", "Temperate"), ("Hills", "Temperate"), ("Jungle", "Rainforest"), ("Bamboo Forest", "Rainforest"), ("Plains", "Open"),
			("Grassland", "Open"), ("Steppe", "Open"), ("Savanna", "Open"), ("Farmland", "Open"), ("Desert", "Arid"), ("Scrubland", "Arid"),
			("High Desert", "Arid"), ("Salt Flat", "Arid"), ("Wasteland", "Arid"), ("Badlands", "Badlands"), ("Wetlands", "Wetland"),
			("Peat Bog", "Wetland"), ("Swamp", "Wetland"), ("Oasis", "Wetland"), ("Tundra", "Periglacial"), ("Taiga", "Periglacial"),
			("Snow", "Periglacial"), ("Karst", "Karst"), ("Alpine Meadow", "Mountain"), ("Mountain Slope", "Mountain"),
			("Rocky Terrain", "Mountain"), ("Alpine", "Mountain"), ("Scree", "Mountain"), ("Crater", "Mountain"), ("Permanent Ice", "Periglacial"),
			("Glacier", "Periglacial"), ("Ice Sheet", "Periglacial"), ("Volcanic", "Volcanic"), ("Geyser Basin", "Volcanic"),
			("Sulphur Flats", "Volcanic"), ("Molten Surface", "Volcanic"), ("Lava Tube", "Volcanic"), ("Runaway Greenhouse Plain", "Volcanic"),
			("Sulphuric Cloud Deck", "Volcanic"), ("Tholin Plain", "Arid"), ("Methane Lake", "Wetland"), ("Regolith Plain", "Barren"),
			("Impact Basin", "Barren"), ("Rille", "Barren"), ("Dust Sea", "Barren"), ("Radiation Plain", "Barren"), ("Nitrogen Ice Field", "Barren"),
			("Cryovolcanic Plain", "Barren"), ("Ice Geyser Field", "Barren"), ("Ice Shelf", "Barren"), ("Tidal Fracture", "Barren"),
			("Lake", "Temperate"), ("River", "Temperate"), ("Cave", "Karst"), ("Ice Cave", "Periglacial"), ("Volcanic Cave", "Volcanic"),
			("Swamp Cave", "Wetland"), ("Ice Palace", "Periglacial"), ("Ice Ruin", "Periglacial"), ("Jungle Ruins", "Rainforest"),
			("Jungle Temple", "Rainforest"), ("Swamp Ruins", "Wetland"), ("Swamp Temple", "Wetland"), ("Volcanic Temple", "Volcanic"),
			("Forest Ruins", "Temperate"), ("Castle", "Temperate"), ("Fortress", "Mountain"), ("Marble Palace", "Open"),
		};

		/// <summary>Each file's terrain process is the old table's; every biome has one and every one is a profile.</summary>
		[Test]
		public void TerrainProcess_TheFilesGiveTheOldTablesAssignments()
		{
			Assert.That(OldTerrainProcesses.Length, Is.EqualTo(83));
			var old = OldTerrainProcesses.ToDictionary(a => a.Biome, a => a.Profile);
			foreach (BiomeArtSpec.Entry entry in Entries)
			{
				// An authored round may give its biome a profile of its own (Desert's Erg, 2026-10-26); every name is still a profile.
				if (!BiomeAuthoredRounds.Contains(entry.Biome))
				{
					Assert.That(entry.TerrainProcessName, Is.EqualTo(old[entry.Biome]), entry.Biome);
				}
				Assert.That(TerrainProcessTable.TryGetProfile(entry.TerrainProcessName, out _), Is.True, entry.Biome);
			}
			Assert.That(TerrainProcessTable.Assignments.Where(a => !BiomeAuthoredRounds.Contains(a.Biome)).ToDictionary(a => a.Biome, a => a.Profile),
				Is.EquivalentTo(old.Where(a => !BiomeAuthoredRounds.Contains(a.Key))), "the button assigns what the files name");
			Assert.That(TerrainProcessTable.Assignments.Select(a => a.Biome), Is.EquivalentTo(old.Keys), "every biome is assigned");
		}

		/// <summary>The weather content generator's ground table until 2026-10-26: loose ground, emission and rate by asset name.</summary>
		private static readonly (string Biome, string Loose, string Emits, float Rate)[] OldGround =
		{
			("Badlands", "Silicate Sand", null, 0f), ("Desert", "Silicate Sand", null, 0f), ("High Desert", "Silicate Sand", null, 0f),
			("Salt Flat", "Silicate Sand", null, 0f), ("Dust Sea", "Regolith Dust", null, 0f), ("Impact Basin", "Regolith Dust", null, 0f),
			("Regolith Plain", "Regolith Dust", null, 0f), ("Rille", "Regolith Dust", null, 0f), ("Radiation Plain", "Irradiated Dust", null, 0f),
			("Volcanic", null, "Volcanic Ash", 0.15f), ("Volcanic Temple", null, "Volcanic Ash", 0.15f), ("Cryovolcanic Plain", null, "Cryo Tephra", 0.08f),
			("Ice Geyser Field", null, "Cryo Tephra", 0.08f), ("Tidal Fracture", null, "Cryo Tephra", 0.08f), ("Molten Surface", null, "Sulphur Dust", 0.08f),
			("Runaway Greenhouse Plain", null, "Sulphur Dust", 0.08f), ("Sulphur Flats", null, "Sulphur Dust", 0.08f),
			("Sulphuric Cloud Deck", null, "Sulphur Dust", 0.08f), ("Methane Lake", null, "Tholin Haze", 0.08f), ("Tholin Plain", null, "Tholin Haze", 0.08f),
			("Geyser Basin", null, "Steam", 0.5f),
		};

		/// <summary>The files that named their own emission before this phase (decisions 39, 47 and the vent rounds): theirs stands.</summary>
		private static readonly HashSet<string> OwnEmission = new HashSet<string>
		{
			"Crater", "Geyser Basin", "Ice Geyser Field", "Nitrogen Ice Field", "Volcanic", "Sulphuric Cloud Deck", "Runaway Greenhouse Plain", "Methane Lake",
		};

		/// <summary>
		/// Each file's loose ground is the old ground table's, and its emission the table's unless the file had named its own; and every
		/// template asset already holds exactly that (what the generator seeded, then the earlier columns wrote), so the authoring's
		/// once-over-the-original writes nothing new.
		/// </summary>
		[Test]
		public void Ground_TheFilesGiveTheOldTable_AndTheTemplatesAlreadyHoldIt()
		{
			var old = OldGround.ToDictionary(g => g.Biome);
			Dictionary<string, string> substances = GuidNames(Path.Combine("Assets/Templates/Weather/Substances"));
			foreach (BiomeArtSpec.Entry entry in Entries)
			{
				old.TryGetValue(entry.Biome, out var row);
				string loose = string.IsNullOrEmpty(entry.LooseGround) ? null : entry.LooseGround;
				Assert.That(loose, Is.EqualTo(row.Loose), entry.Biome + " loose ground");
				if (!OwnEmission.Contains(entry.Biome))
				{
					Assert.That(entry.Emission.HasValue, Is.EqualTo(row.Emits != null), entry.Biome);
					if (row.Emits != null)
					{
						Assert.That(entry.Emission.Value, Is.EqualTo((row.Emits, row.Rate)), entry.Biome);
					}
				}

				string yaml = File.ReadAllText($"{BiomeFolder}/{entry.Biome}.asset");
				Assert.That(Ref(yaml, "LooseGround", substances), Is.EqualTo(loose), entry.Biome + ": the template's loose ground");
				string emits = entry.Emission.HasValue ? entry.Emission.Value.Substance : null;
				Assert.That(Ref(yaml, "Emits", substances), Is.EqualTo(emits), entry.Biome + ": the template's emission");
				float rate = float.Parse(Regex.Match(yaml, @"\n  EmissionRate: ([^\n]+)").Groups[1].Value, CultureInfo.InvariantCulture);
				Assert.That(rate, Is.EqualTo(entry.Emission.HasValue ? entry.Emission.Value.Rate : 0f).Within(1e-6f), entry.Biome + ": the template's rate");
			}
		}

		/// <summary>Every template asset already wears the profile its file names, so the authoring's once-over-the-original writes nothing new.</summary>
		[Test]
		public void TerrainProcess_TheTemplatesAlreadyWearIt()
		{
			Dictionary<string, string> profiles = GuidNames(TerrainProcessTable.Folder);
			foreach (BiomeArtSpec.Entry entry in Entries)
			{
				// An authored round's new profile reaches its template at the next Author run (Desert's Erg, 2026-10-26).
				if (BiomeAuthoredRounds.Contains(entry.Biome) && !profiles.ContainsValue(entry.TerrainProcessName))
				{
					continue;
				}
				string yaml = File.ReadAllText($"{BiomeFolder}/{entry.Biome}.asset");
				Assert.That(Ref(yaml, "TerrainProcess", profiles), Is.EqualTo(entry.TerrainProcessName), entry.Biome);
			}
		}

		private static Dictionary<string, string> GuidNames(string folder)
		{
			var names = new Dictionary<string, string>();
			foreach (string meta in Directory.GetFiles(folder, "*.asset.meta"))
			{
				Match guid = Regex.Match(File.ReadAllText(meta), @"guid: (\w+)");
				names[guid.Groups[1].Value] = Path.GetFileName(meta).Replace(".asset.meta", string.Empty);
			}
			return names;
		}

		private static string Ref(string yaml, string field, Dictionary<string, string> names)
		{
			Match m = Regex.Match(yaml, @"\n  " + field + @": \{fileID: (\d+)(?:, guid: (\w+))?");
			Assert.That(m.Success, Is.True, field);
			if (!m.Groups[2].Success)
			{
				return null;
			}
			return names.TryGetValue(m.Groups[2].Value, out string name) ? name : "(unknown " + m.Groups[2].Value + ")";
		}

		// ── The authoring writes the two new columns, marked ──

		[Test]
		public void Authoring_WritesTheLooseGroundAndTheTerrainProcess_Marked()
		{
			Assert.That(BiomeTemplate.Authored.LooseGround, Is.EqualTo("LooseGround"));
			Assert.That(BiomeTemplate.Authored.TerrainProcess, Is.EqualTo("TerrainProcess"));
			string authoring = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "ProceduralArt/BiomeArtAuthoring.cs")));
			Assert.That(authoring, Does.Contain("AuthorLooseGround(biome, entry, report, apply);"));
			Assert.That(authoring, Does.Contain("AuthorTerrainProcess(biome, entry, report, apply);"));
			Assert.That(authoring, Does.Contain("AuthorText(biome, BiomeTemplate.Authored.LooseGround,"));
			Assert.That(authoring, Does.Contain("AuthorText(biome, BiomeTemplate.Authored.TerrainProcess,"));
			Assert.That(authoring, Does.Contain("List<BiomeSiteKind> kinds = entry.SiteKinds();"), "the sites come from the file, not the catalogue by name");
			string weather = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "WeatherContentGenerator.cs")));
			Assert.That(weather, Does.Contain("BiomeArtSpec.Entry entry = BiomeArtSpec.For(biomeName);"), "the generator seeds a template's ground from its file");
		}

		// ── Source scans ─────────────────────────────────────────────

		/// <summary>The spec's defaults read no name table: a column a file leaves unset gives nothing, never the old table's row.</summary>
		[Test]
		public void TheSpecsDefaults_ReadNoNameTable()
		{
			string spec = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "ProceduralArt/BiomeArtSpec.cs")));
			foreach (string read in new[] { "VineBiomes.For(", "GatheringResourceCatalogue.GrowsIn(", "KindWeightsFor(", "spec.BiomeWeightByName(Biome)", "WeatheringByName(Biome)" })
			{
				Assert.That(spec, Does.Not.Contain(read), read);
			}
			Assert.That(Regex.Matches(spec, @"\bWeatheringByName\(").Count, Is.EqualTo(1), "only its definition: the keyword reading is the cut's fallback alone");
		}

		/// <summary>The old tables' readings by name are made by the single fallback reader alone (BiomeTemplateArt), plus each table's own file.</summary>
		[Test]
		public void TheNameTables_AreReadOnlyByTheFallbackReader()
		{
			var reads = new (string Call, string Home)[]
			{
				("VineBiomes.For(", "VineBiomes.cs"),
				("GatheringResourceCatalogue.GrowsIn(", "GatheringResourceCatalogue.cs"),
				("GatheringResourceCatalogue.AnyGrowsIn(", "GatheringResourceCatalogue.cs"),
				("KindWeightsFor(", "PointOfInterestRules.cs"),
				("WeatheringByName(", "BiomeArtSpec.cs"),
			};
			var found = new List<string>();
			foreach (string file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
			{
				string name = Path.GetFileName(file);
				if (name == "BiomeTemplateArt.cs")
				{
					continue;
				}
				string code = CodeOnly(File.ReadAllText(file));
				foreach ((string call, string home) in reads)
				{
					if (name != home && code.Contains(call))
					{
						found.Add($"{name}: {call}");
					}
				}
			}
			Assert.That(found, Is.Empty, "a name table read outside the fallback reader");
			string art = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "Terrain/BiomeTemplateArt.cs")));
			Assert.That(art, Does.Contain("VineBiomes.For(biome.name)"));
			Assert.That(art, Does.Contain("KindWeightsFor(biome.name)"));
			Assert.That(art, Does.Contain("Fallback(biome"), "the fallbacks warn (once a field)");
		}

		/// <summary>
		/// Every file whose CODE names a biome by its exact asset name (comments aside) is one of these, each for the reason given;
		/// the per-biome spec files (BiomeArtSpec/) are the source and not scanned. A new file naming a biome fails here: put the
		/// decision on the template through a spec column instead (decision 1). The two cleared tables name none.
		/// </summary>
		private static readonly Dictionary<string, string> MayNameABiome = new Dictionary<string, string>
		{
			// The cut's single fallback reader and the old tables it falls back to for a template never authored.
			["BiomeTemplateArt.cs"] = "fallback reader (water roles, legacy riverbeds)",
			["VineBiomes.cs"] = "fallback table",
			["GatheringResourceCatalogue.cs"] = "fallback table (resource biome lists)",
			["PointOfInterestRules.cs"] = "fallback table (kind rows' biome families)",
			["BiomeArtSpec.cs"] = "fallback keywords (WeatheringByName)",
			["PointOfInterestTemplateSpecs.cs"] = "POI template authoring: the templates' own biome lists, the variant fallback",
			["SeaLifeSettings.cs"] = "client fallback (SeaLifePlacement.PrefersByName) for a template with no sea life",
			// Tables still to move (CODE-TABLE CLEARANCE LEFT; see the progress log).
			["BiomeNamingGenerator.cs"] = "LEFT: naming seeder (fills only missing naming; the template is the store)",
			["BiomeSpecTable.cs"] = "LEFT: selection envelopes (needs the SELECTION row + Requires column, Q13)",
			["ProceduralArtCatalogue.Cave.cs"] = "LEFT: crystal looks by biome (the spec's crystal default)",
			["BiomeCaveInterior.cs"] = "LEFT: cave interiors' default links (I2n per-form tables, Q16)",
			["PointOfInterestCaveDressing.cs"] = "word, not a biome: the Scree shape",
			// Words that are also a biome's name, deciding nothing about a biome.
			["BiomeLandformPresets.cs"] = "word: the round a preset was asked for by (AskedBy)",
			["RegionalRocks.cs"] = "word: the Scree stone set",
			["RockTypes.cs"] = "word: the Scree stone set",
			["TerrainProcessTable.cs"] = "word: profile names (Badlands, Karst, Volcanic)",
			["PlaceNameDefaults.cs"] = "word: place-name words of a kind",
			["GeneratedSceneNames.cs"] = "word: region words",
			["PointOfInterestNaming.cs"] = "word: kind words and the description's grammar over the display name",
			["ProceduralArtCatalogue.cs"] = "word: tree species names",
			["ProceduralArtCatalogue.Vines.cs"] = "word: a tree species name",
			["VegetationVariationRender.cs"] = "word: a probe's prefab prefixes",
			["WorldEditorPhotoImport.cs"] = "tool: photo import's mapping by name",
			["BiomeTemplate.cs"] = "word: elevation band names",
			["MapFilters.cs"] = "word: a map filter's label",
			["CaveWalkRender.cs"] = "word: a probe's object name",
			["CaveShaper.cs"] = "word: the cave shaper's name",
			["SurfaceCatalogue.cs"] = "word: surface names",
			["IceSheetSurface.cs"] = "word: the ice-sheet surface's own label",
			["WaterSettle.cs"] = "word: a lake's label",
			["PrefabSpawnRule.cs"] = "word: an inspector header",
		};

		[Test]
		public void NoCode_NamesABiome_OutsideTheSpecFilesAndTheFallback()
		{
			var names = new HashSet<string>(BiomeArtSpec.Entries.Select(e => e.Biome));
			var quoted = new Regex("\"(" + string.Join("|", names.OrderByDescending(n => n.Length).Select(Regex.Escape)) + ")\"");
			var strays = new List<string>();
			var naming = new HashSet<string>();
			foreach (string file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
			{
				string path = file.Replace('\\', '/');
				if (path.Contains("/ProceduralArt/BiomeArtSpec/"))
				{
					continue;
				}
				string code = CodeOnly(File.ReadAllText(file));
				MatchCollection found = quoted.Matches(code);
				if (found.Count > 0)
				{
					naming.Add(Path.GetFileName(file));
				}
				if (found.Count > 0 && !MayNameABiome.ContainsKey(Path.GetFileName(file)))
				{
					strays.Add($"{path}: {string.Join(", ", found.Cast<Match>().Select(m => m.Groups[1].Value).Distinct())}");
				}
			}
			Assert.That(strays, Is.Empty, "code naming a biome outside the spec files and the fallback");
			Assert.That(naming, Is.SupersetOf(new[] { "VineBiomes.cs", "PointOfInterestRules.cs", "GatheringResourceCatalogue.cs", "BiomeTemplateArt.cs" }),
				"the scan reads code (the fallback tables are found)");

			// The cleared tables: the weather content generator names no biome; the terrain process table only its profiles' names.
			Assert.That(quoted.Matches(CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "WeatherContentGenerator.cs")))).Count, Is.EqualTo(0));
			var profileNames = new HashSet<string>(TerrainProcessTable.Profiles.Select(p => p.Name));
			foreach (Match m in quoted.Matches(CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "Terrain/TerrainProcessTable.cs")))))
			{
				Assert.That(profileNames, Does.Contain(m.Groups[1].Value), "the terrain process table names no biome, only profiles");
			}
		}

		/// <summary>The source with comments removed and string literals kept (verbatim and character literals too).</summary>
		private static string CodeOnly(string source)
		{
			var sb = new StringBuilder(source.Length);
			int i = 0, n = source.Length;
			while (i < n)
			{
				char c = source[i];
				if (c == '/' && i + 1 < n && source[i + 1] == '/')
				{
					while (i < n && source[i] != '\n')
					{
						i++;
					}
					continue;
				}
				if (c == '/' && i + 1 < n && source[i + 1] == '*')
				{
					int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
					end = end < 0 ? n : end + 2;
					for (int k = i; k < end; k++)
					{
						if (source[k] == '\n')
						{
							sb.Append('\n');
						}
					}
					i = end;
					continue;
				}
				if (c == '@' && i + 1 < n && source[i + 1] == '"')
				{
					int j = i + 2;
					while (j < n && !(source[j] == '"' && (j + 1 >= n || source[j + 1] != '"')))
					{
						j += source[j] == '"' ? 2 : 1;
					}
					sb.Append(source, i, Math.Min(n, j + 1) - i);
					i = j + 1;
					continue;
				}
				if (c == '"' || c == '\'')
				{
					int j = i + 1;
					while (j < n && source[j] != c && source[j] != '\n')
					{
						j += source[j] == '\\' ? 2 : 1;
					}
					sb.Append(source, i, Math.Min(n, j + 1) - i);
					i = j + 1;
					continue;
				}
				sb.Append(c);
				i++;
			}
			return sb.ToString();
		}
	}
}
