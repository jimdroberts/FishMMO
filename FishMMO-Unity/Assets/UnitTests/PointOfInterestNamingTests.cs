using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.NameGeneration.Editor;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Point-of-interest naming against the project's real naming assets: every kind names in every
	/// sampled biome, names replay exactly, race slots resolve for every race, and planet-wide water
	/// names depend on nothing but their seeds (Jim, 2026-10-10).
	/// </summary>
	[TestFixture]
	public class PointOfInterestNamingTests
	{
		/// <summary>Biomes every kind is named in: one of each kind of world, then a stride through the rest.</summary>
		private static readonly string[] PreferredBiomes =
		{
			"forest", "swamp", "desert", "snow", "volcanic", "ocean", "mountainslope", "grassland", "cave", "regolithplain",
		};

		private List<string> sampleBiomes;
		private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

		[OneTimeSetUp]
		public void LoadAssets()
		{
			NamingTemplateEditorLoader.Reload();
			List<string> nameable = BiomeRegistry.NameableBiomes.ToList();
			sampleBiomes = PreferredBiomes.Where(nameable.Contains).ToList();
			for (int i = 0; sampleBiomes.Count < 10 && i < nameable.Count; i += Math.Max(1, nameable.Count / 10))
			{
				if (!sampleBiomes.Contains(nameable[i]))
				{
					sampleBiomes.Add(nameable[i]);
				}
			}
		}

		[TearDown]
		public void DestroyCreated()
		{
			foreach (UnityEngine.Object o in created)
			{
				if (o != null)
				{
					UnityEngine.Object.DestroyImmediate(o);
				}
			}
			created.Clear();
		}

		private static IEnumerable<POIType> Kinds() => Enum.GetValues(typeof(POIType)).Cast<POIType>().Where(k => k != POIType.Any);

		private static int BiomeID(string key) => BiomeRegistry.IDOf(BiomeRegistry.Get(key));

		private static void AssertWellFormed(string name, string what)
		{
			Assert.IsFalse(string.IsNullOrWhiteSpace(name), $"{what}: empty name.");
			Assert.LessOrEqual(name.Length, PlaceNameDefaults.MaxLength, $"{what}: '{name}' is longer than {PlaceNameDefaults.MaxLength}.");
			Assert.IsFalse(name.Contains("{") || name.Contains("}"), $"{what}: '{name}' has an unfilled slot.");
			Assert.IsFalse(name.Contains("  "), $"{what}: '{name}' has a double space.");
			Assert.AreEqual(name.Trim(), name, $"{what}: '{name}' has stray whitespace.");
			Assert.IsTrue(char.IsUpper(name[0]), $"{what}: '{name}' does not start with a capital.");
		}

		// ── Every kind names ──────────────────────────────────────────

		[Test]
		public void EveryKind_NamesInASampleOfBiomes_WithAndWithoutARace()
		{
			Assert.GreaterOrEqual(sampleBiomes.Count, 10, "The sample needs ten nameable biomes.");
			var generator = new NameGenerator();
			foreach (POIType kind in Kinds())
			{
				foreach (string biome in sampleBiomes)
				{
					foreach (string race in new[] { null, "orc", "dwarf" })
					{
						for (int i = 0; i < 3; i++)
						{
							string what = $"{kind} in {biome} ({race ?? "no race"}) #{i}";
							POINameEntry entry = generator.Generate(new POIRequest
							{
								BiomeID = BiomeID(biome),
								POIType = kind,
								Race = race,
								RegionSeed = "suite-scene",
								ObjectSeed = $"{kind}-{biome}-{i}",
							});
							AssertWellFormed(entry.Name, what);
						}
					}
				}
			}
		}

		[Test]
		public void AnyKind_NamesWithoutSayingWhichKind()
		{
			var generator = new NameGenerator();
			for (int i = 0; i < 200; i++)
			{
				POINameEntry entry = generator.Generate(new POIRequest { Biome = "forest", POIType = POIType.Any, RegionSeed = "any", Index = i });
				AssertWellFormed(entry.Name, $"Any #{i}");
				Assert.AreEqual("mixed", entry.POIType);
			}
		}

		[Test]
		public void SameRequest_SameName()
		{
			foreach (POIType kind in new[] { POIType.Cave, POIType.Shrine, POIType.Village, POIType.Keep, POIType.River, POIType.Lake, POIType.Waterfall, POIType.Graveyard, POIType.DungeonEntrance })
			{
				var request = new POIRequest { Biome = "swamp", POIType = kind, Race = "goblin", RegionSeed = "scene-a", ObjectSeed = "poi-17", Index = 2 };
				string first = new NameGenerator().Generate(request).Name;
				string second = new NameGenerator(99).Generate(request).Name;
				Assert.AreEqual(first, second, $"{kind}: a seeded request must replay exactly whatever generator runs it.");
			}
		}

		[Test]
		public void DifferentObjectSeeds_UsuallyNameDifferently()
		{
			var generator = new NameGenerator();
			foreach (POIType kind in new[] { POIType.Cave, POIType.Shrine, POIType.Village, POIType.River, POIType.Lake, POIType.Peak, POIType.Barrow, POIType.Camp })
			{
				var names = new HashSet<string>();
				for (int i = 0; i < 40; i++)
				{
					names.Add(generator.Generate(new POIRequest { Biome = "forest", POIType = kind, RegionSeed = "scene-b", ObjectSeed = "poi-" + i }).Name);
				}
				Assert.GreaterOrEqual(names.Count, 30, $"{kind}: 40 object seeds gave only {names.Count} distinct names.");
			}
		}

		// ── Water is biome-free ───────────────────────────────────────

		[Test]
		public void PlanetRiver_NameDependsOnlyOnBodySeedAndRiver()
		{
			var generator = new NameGenerator();
			foreach (POIType kind in new[] { POIType.River, POIType.Waterfall, POIType.Lake, POIType.RiverMouth, POIType.Rapids, POIType.Delta })
			{
				// Two scenes on the same body that the same planet river crosses: different biomes, variants and races.
				string inForest = generator.Generate(new POIRequest
				{
					BiomeID = BiomeID("forest"), POIType = kind, Race = "elf", RegionSeed = "body-7", ObjectSeed = "planet-river-12",
				}).Name;
				string inDesert = generator.Generate(new POIRequest
				{
					BiomeID = BiomeID("desert"), POIType = kind, Race = "orc", ClimateVariant = "scorched", RegionSeed = "body-7", ObjectSeed = "planet-river-12",
				}).Name;
				string withNoBiome = generator.Generate(new POIRequest { POIType = kind, RegionSeed = "body-7", ObjectSeed = "planet-river-12" }).Name;
				Assert.AreEqual(inForest, inDesert, $"{kind}: a planet river's name must not depend on the scene's biome or race.");
				Assert.AreEqual(inForest, withNoBiome, $"{kind}: a biome-free kind needs no biome at all.");
				AssertWellFormed(inForest, kind.ToString());
			}

			var rivers = new HashSet<string>();
			for (int i = 0; i < 30; i++)
			{
				rivers.Add(generator.Generate(new POIRequest { POIType = POIType.River, RegionSeed = "body-7", ObjectSeed = "planet-river-" + i }).Name);
			}
			Assert.GreaterOrEqual(rivers.Count, 24, "Different planet rivers should mostly have different names.");
		}

		// ── Races ─────────────────────────────────────────────────────

		[Test]
		public void TypeOfRacePlural_ResolvesForEveryRace()
		{
			BiomePhonology forest = BiomeRegistry.Get("forest").Naming.RuntimePhonology;
			int seed = 1;
			foreach (string key in RaceRegistry.SupportedRaces)
			{
				RaceTemplate race = RaceRegistry.Get(key);
				string plural = RaceNaming.PluralOf(race);
				string adjective = RaceNaming.AdjectiveOf(race);
				Assert.IsFalse(string.IsNullOrWhiteSpace(plural), $"{race.Name} has no plural.");
				Assert.IsFalse(string.IsNullOrWhiteSpace(adjective), $"{race.Name} has no adjective.");

				var rng = new DeterministicRNG(seed++);
				var ctx = new PlaceContext { Kind = POIType.Cave, Biome = forest, Race = race, Rng = rng, RootRng = rng };
				string cave = POINameBuilder.FillPattern(ctx, "{Type} of {RacePlural}");
				Assert.IsNotNull(cave, $"{race.Name}: the race pattern did not fill.");
				StringAssert.EndsWith(" of " + plural, cave, $"{race.Name}: '{cave}'.");
				string warren = POINameBuilder.FillPattern(ctx, "{RaceAdjective} {Type}");
				StringAssert.StartsWith(adjective + " ", warren, $"{race.Name}: '{warren}'.");
			}
		}

		[Test]
		public void RacePatterns_AreSkippedWithoutARace()
		{
			BiomePhonology forest = BiomeRegistry.Get("forest").Naming.RuntimePhonology;
			var rng = new DeterministicRNG(5);
			var ctx = new PlaceContext { Kind = POIType.Cave, Biome = forest, Rng = rng, RootRng = rng };
			Assert.IsNull(POINameBuilder.FillPattern(ctx, "{Type} of {RacePlural}"));
			Assert.IsNull(POINameBuilder.FillPattern(ctx, "{RaceAdjective} {Type}"));
			Assert.IsNotNull(POINameBuilder.FillPattern(ctx, "The {Adjective} {Type}"));
		}

		[TestCase("Orc", "Orcs")]
		[TestCase("Wolf", "Wolves")]
		[TestCase("Dire Wolf", "Dire Wolves")]
		[TestCase("Werewolf", "Werewolves")]
		[TestCase("Dwarf", "Dwarves")]
		[TestCase("Elf", "Elves")]
		[TestCase("Half-Elf", "Half-Elves")]
		[TestCase("Fish", "Fish")]
		[TestCase("Mouse", "Mice")]
		[TestCase("Goose", "Geese")]
		[TestCase("Man", "Men")]
		[TestCase("Beastman", "Beastmen")]
		[TestCase("Human", "Humans")]
		[TestCase("Ratfolk", "Ratfolk")]
		[TestCase("Frostkin", "Frostkin")]
		[TestCase("Hellborn", "Hellborn")]
		[TestCase("Void Spawn", "Void Spawn")]
		[TestCase("Ashen Dead", "Ashen Dead")]
		[TestCase("Undead", "Undead")]
		[TestCase("Mindreaver", "Mindreavers")]
		[TestCase("Stone Golem", "Stone Golems")]
		[TestCase("Drowned One", "Drowned Ones")]
		[TestCase("Lich", "Liches")]
		[TestCase("Jelly", "Jellies")]
		[TestCase("Mummy", "Mummies")]
		[TestCase("Phoenix", "Phoenixes")]
		[TestCase("Cyclops", "Cyclopes")]
		[TestCase("Succubus", "Succubi")]
		[TestCase("Seraph", "Seraphim")]
		[TestCase("Great Elk", "Great Elk")]
		[TestCase("Fae", "Fae")]
		public void Pluraliser_HandlesTheExceptions(string race, string plural)
		{
			Assert.AreEqual(plural, RaceNaming.DerivePlural(race));
		}

		[TestCase("Elf", "Elven")]
		[TestCase("Dark Elf", "Dark Elven")]
		[TestCase("Half-Elf", "Half-Elven")]
		[TestCase("Dwarf", "Dwarven")]
		[TestCase("Gnome", "Gnomish")]
		[TestCase("Goblin", "Goblin")]
		[TestCase("Stone Golem", "Stone Golem")]
		public void Adjectives_HandleTheExceptions(string race, string adjective)
		{
			Assert.AreEqual(adjective, RaceNaming.DeriveAdjective(race));
		}

		[Test]
		public void AuthoredForms_WinOverTheRules()
		{
			var race = ScriptableObject.CreateInstance<RaceTemplate>();
			race.name = "Test Wyrmling";
			created.Add(race);
			Assert.AreEqual("Test Wyrmlings", RaceNaming.PluralOf(race), "Empty fields fall back to the rules.");
			race.Naming.Plural = "Wyrmbrood";
			race.Naming.Adjective = "Wyrmish";
			Assert.AreEqual("Wyrmbrood", RaceNaming.PluralOf(race));
			Assert.AreEqual("Wyrmish", RaceNaming.AdjectiveOf(race));
		}

		// ── Settlements ───────────────────────────────────────────────

		[Test]
		public void Settlements_AreNamedLikeCities()
		{
			var generator = new NameGenerator();
			string[] villageWords = PlaceNameDefaults.TypeWordsFor(POIType.Village);
			int bare = 0;
			for (int i = 0; i < 40; i++)
			{
				string name = generator.Generate(new POIRequest { Biome = "forest", POIType = POIType.Village, Race = "dwarf", RegionSeed = "scene-c", ObjectSeed = "v" + i }).Name;
				if (!villageWords.Any(w => name.EndsWith(" " + w, StringComparison.Ordinal)))
				{
					bare++;
				}
			}
			Assert.GreaterOrEqual(bare, 30, "Villages should mostly carry a city-builder name, not '<Root> Village'.");
			Assert.AreEqual(CityType.Fortress, NameGenerator.CityTypeFor(POIType.Castle));
			Assert.AreEqual(CityType.Capital, NameGenerator.CityTypeFor(POIType.Capital));
			Assert.AreEqual(CityType.Port, NameGenerator.CityTypeFor(POIType.Port));
			Assert.AreEqual(CityType.Village, NameGenerator.CityTypeFor(POIType.Village));
		}

		// ── Vocabulary coverage ───────────────────────────────────────

		[Test]
		public void EveryKind_HasTypeWords_AWeight_AndAComposition()
		{
			foreach (POIType kind in Kinds())
			{
				Assert.IsTrue(PlaceNameDefaults.HasTypeWords(kind), $"{kind} has no built-in type words.");
				Assert.GreaterOrEqual(PlaceNameDefaults.TypeWordsFor(kind).Length, 6, $"{kind} needs at least six type words.");
				Assert.Greater(PlaceNameDefaults.KindWeight(kind), 0, $"{kind} can never be drawn for POIType.Any.");
				Assert.IsTrue(PlaceNameDefaults.Templates.Any(t => t.Applies(kind)), $"{kind} has no built-in composition.");
			}
		}

		[Test]
		public void BiomeTypeWords_ArePreferredOverTheGlobalOnes()
		{
			BiomePhonology forest = BiomeRegistry.Get("forest").Naming.RuntimePhonology;
			var own = new BiomePhonology
			{
				Onsets = forest.Onsets, Nuclei = forest.Nuclei, Codas = forest.Codas, Middles = forest.Middles,
				Adjectives = forest.Adjectives, POISuffixes = forest.POISuffixes,
				DungeonPrefixes = forest.DungeonPrefixes, DungeonSuffixes = forest.DungeonSuffixes,
				POITypeWords = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["spring"] = new[] { "Zzwell" } },
			};
			var rng = new DeterministicRNG(3);
			int ownWord = 0;
			for (int i = 0; i < 200; i++)
			{
				var ctx = new PlaceContext { Kind = POIType.Spring, Biome = own, Rng = rng, RootRng = rng };
				if (POINameBuilder.FillPattern(ctx, "{Type}") == "Zzwell")
				{
					ownWord++;
				}
			}
			Assert.That(ownWord, Is.InRange(80, 160), "The biome's own word should lead about 60% of the time.");
		}

		[Test]
		public void BiomePhonology_RoundTripsItsTypeWords()
		{
			var serial = new SerializableBiomePhonology
			{
				Onsets = new[] { "Ka" }, Codas = new[] { "ron" },
				POITypeWords = new List<StringListMapping> { new StringListMapping("spring", new[] { "Oasis", "Wells" }) },
			};
			BiomePhonology runtime = serial.ToRuntime();
			CollectionAssert.AreEqual(new[] { "Oasis", "Wells" }, runtime.POITypeWords["SPRING"], "Lookup is case-insensitive.");
			SerializableBiomePhonology back = SerializableBiomePhonology.From(runtime);
			Assert.AreEqual(1, back.POITypeWords.Count);
			Assert.AreEqual("spring", back.POITypeWords[0].Key);
		}

		[Test]
		public void BiomePoiWordTable_CoversEveryBiomeAsset()
		{
			var missing = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BiomeTemplate)))
			{
				BiomeTemplate biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (biome == null)
				{
					continue;
				}
				SortedDictionary<string, string[]> words = BiomeNamingGenerator.PoiWordsFor(biome.name);
				if (words == null)
				{
					missing.Add(biome.name);
					continue;
				}
				Assert.Greater(words.Count, 0, $"{biome.name} is in the table but gets no words.");
				foreach (KeyValuePair<string, string[]> pair in words)
				{
					Assert.IsTrue(Enum.TryParse(pair.Key, true, out POIType _), $"{biome.name}: '{pair.Key}' is not a POI kind.");
					Assert.GreaterOrEqual(pair.Value.Length, 6, $"{biome.name}: '{pair.Key}' has fewer than six words.");
				}
			}
			CollectionAssert.IsEmpty(missing, "Biomes with no row in BiomeNamingGenerator.PoiFamiliesByBiome.");
		}

		// ── Dungeon meaning (the TrimStart bug) ───────────────────────

		[TestCase("the hollow", "hollow")]
		[TestCase("the ethereal", "ethereal")]
		[TestCase("the the", "the")]
		[TestCase("theatre of bones", "theatre of bones")]
		[TestCase("hollow", "hollow")]
		public void DungeonMeaning_StripsTheWordNotItsLetters(string prefix, string expected)
		{
			Assert.AreEqual(expected, DungeonNameBuilder.StripLeadingArticle(prefix));
		}

		[Test]
		public void DungeonMeaning_KeepsThePrefixWordWhole()
		{
			var generator = new NameGenerator();
			int checkedNames = 0;
			for (int i = 0; i < 200 && checkedNames < 20; i++)
			{
				DungeonNameEntry entry = generator.Generate(new DungeonRequest { Biome = "forest", RegionSeed = "dungeons", Index = i });
				if (!entry.Name.StartsWith("The ", StringComparison.Ordinal))
				{
					continue;
				}
				string word = entry.Name.Split(' ')[1].ToLowerInvariant();
				StringAssert.StartsWith(word, entry.Meaning, $"'{entry.Name}' glossed as '{entry.Meaning}'.");
				checkedNames++;
			}
			Assert.Greater(checkedNames, 0, "No prefixed dungeon names were drawn to check.");
		}
	}
}
