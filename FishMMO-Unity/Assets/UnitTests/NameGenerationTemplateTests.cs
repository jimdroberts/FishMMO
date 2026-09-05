using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration.Editor;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The name generator against the project's real naming assets: every race,
	/// biome and modifier that ships must load, register, and produce names, and
	/// seeded output must replay identically — that is what the wire format relies on.
	/// </summary>
	[TestFixture]
	public class NameGenerationTemplateTests
	{
		private NamingTemplateEditorLoader.LoadReport report;

		[OneTimeSetUp]
		public void LoadAssets()
		{
			report = NamingTemplateEditorLoader.Reload();
		}

		// ── Loading ───────────────────────────────────────────────────

		[Test]
		public void Loader_RegistersTheShippedContent()
		{
			Assert.IsTrue(NameGenerator.IsReady, "Generator should be ready once the assets are registered.");
			Assert.GreaterOrEqual(report.Races, 135, "Every exported race should register.");
			Assert.GreaterOrEqual(report.Biomes, 56, "Every biome asset should register.");
			Assert.AreEqual(12, report.Modifiers);
			Assert.AreEqual(1, report.Grammars, "Exactly one Name Grammar asset should exist.");
		}

		[Test]
		public void PlayableRaces_AreOnlyTheShippedThree()
		{
			List<string> playable = RaceRegistry.SupportedRaces
				.Where(k => RaceRegistry.Get(k).Playable)
				.OrderBy(k => k)
				.ToList();
			CollectionAssert.AreEqual(new[] { "elf", "human", "orc" }, playable);
		}

		[Test]
		public void NamingOnlyRaces_HaveNoPrefabOrModels()
		{
			foreach (string key in RaceRegistry.SupportedRaces)
			{
				RaceTemplate race = RaceRegistry.Get(key);
				if (race.Playable)
				{
					continue;
				}
				Assert.IsNull(race.Prefab, $"{race.Name} is naming-only but has a prefab.");
				Assert.AreEqual(0, race.GetModelCount(CharacterGender.Unspecified), $"{race.Name} is naming-only but has models.");
			}
		}

		[Test]
		public void RaceAssetNames_RoundTripThroughTheNamingKey()
		{
			Assert.AreEqual("Wood Elf", RaceRegistry.Get("woodelf").Name);
			Assert.AreEqual("Human", RaceRegistry.Get("Human").Name, "Lookup should normalise the key.");
			Assert.AreEqual("halfelf", RaceRegistry.Get("Half-Elf").NamingKey);
		}

		[Test]
		public void Grammar_TablesAreLoaded()
		{
			Assert.Greater(NameGrammar.MeaningOnsets.Count, 0);
			Assert.Greater(NameGrammar.MeaningMiddles.Count, 0);
			Assert.Greater(NameGrammar.CityPrefixes.Length, 0);
			Assert.Greater(NameGrammar.DeedVerbs.Length, 0);
			Assert.IsTrue(NameGrammar.POITypeSuffixes.ContainsKey("shrine"));
			Assert.IsTrue(NameGrammar.ItemTypeSuffixes.ContainsKey("weapon"));
			Assert.IsTrue(NameGrammar.ItemTypeNouns.ContainsKey("relic"));
		}

		// ── Every asset generates ─────────────────────────────────────

		[Test]
		public void EveryRace_GeneratesACharacterWithATitle()
		{
			var generator = new NameGenerator(1);
			foreach (string key in RaceRegistry.SupportedRaces)
			{
				CharacterEntry entry = generator.GenerateCharacter(key, CharacterGender.Female, regionSeed: "suite");
				Assert.IsNotEmpty(entry.Name, $"{key} produced an empty name.");
				Assert.IsNotEmpty(entry.Title, $"{key} produced no title.");
				Assert.IsFalse(entry.Name.Contains(" "), $"{key} given name should be one word: '{entry.Name}'.");
			}
		}

		[Test]
		public void EveryRace_GeneratesACityName()
		{
			var generator = new NameGenerator(2);
			foreach (string key in RaceRegistry.SupportedRaces)
			{
				Assert.IsNotEmpty(generator.GenerateCityName(key, regionSeed: "suite").Name, $"{key} produced an empty city name.");
			}
		}

		[Test]
		public void EveryBiome_GeneratesDungeonAndPOINames()
		{
			var generator = new NameGenerator(3);
			foreach (string key in BiomeRegistry.SupportedBiomes)
			{
				Assert.IsNotEmpty(generator.GenerateDungeonName(key, "suite").Name, $"{key} produced an empty dungeon name.");
				Assert.IsNotEmpty(generator.GeneratePOIName(key, POIType.Shrine, "suite").Name, $"{key} produced an empty POI name.");
			}
		}

		[Test]
		public void EveryModifier_AppliesToHuman()
		{
			RacePhonology plain = RaceRegistry.ResolvePhonology("human", null);
			var generator = new NameGenerator(4);
			foreach (string key in ModifierRegistry.SupportedModifiers)
			{
				RacePhonology modified = RaceRegistry.ResolvePhonology("human", null, key);
				Assert.Greater(modified.Onsets.Length, plain.Onsets.Length, $"{key} added no onsets.");

				CharacterEntry entry = generator.Generate(new NameRequest { Race = "human", Modifier = key, RegionSeed = "suite" });
				Assert.IsNotEmpty(entry.Name);
				StringAssert.Contains($"[{key}]", entry.Race);
			}
		}

		[Test]
		public void EveryItemType_GeneratesForElf()
		{
			var generator = new NameGenerator(5);
			foreach (ItemType type in Enum.GetValues(typeof(ItemType)))
			{
				Assert.IsNotEmpty(generator.GenerateItemName("elf", type, regionSeed: "suite").Name, $"{type} produced an empty item name.");
			}
		}

		// ── Determinism ───────────────────────────────────────────────

		[Test]
		public void RegionSeed_ReplaysAcrossGeneratorInstances()
		{
			List<string> first = new NameGenerator(11).GenerateNames("orc", 10, CharacterGender.Male, regionSeed: "grey-vale");
			List<string> second = new NameGenerator(99).GenerateNames("orc", 10, CharacterGender.Male, regionSeed: "grey-vale");
			CollectionAssert.AreEqual(first, second);
			Assert.Greater(first.Distinct().Count(), 1, "A seeded batch should still vary by index.");
		}

		[Test]
		public void SeededInstance_ReplaysWithoutARegionSeed()
		{
			// This is the path SceneObjectNamer uses: one seed, then unseeded requests.
			CharacterEntry first = new NameGenerator(4242).GenerateCharacter("human", CharacterGender.Female);
			CharacterEntry second = new NameGenerator(4242).GenerateCharacter("human", CharacterGender.Female);
			Assert.AreEqual(first.FullTitle, second.FullTitle);
		}

		[Test]
		public void DifferentSeeds_ProduceDifferentNames()
		{
			var names = new HashSet<string>();
			for (int seed = 1; seed <= 20; seed++)
			{
				names.Add(new NameGenerator(seed).GenerateName("human"));
			}
			Assert.Greater(names.Count, 10, "Twenty seeds should not collapse onto a handful of names.");
		}

		// ── Request features ──────────────────────────────────────────

		[Test]
		public void FamilyName_IsASecondDraw()
		{
			CharacterEntry entry = new NameGenerator(7).Generate(new NameRequest
			{
				Race = "human",
				IncludeFamilyName = true,
				NameOnly = true,
			});
			Assert.IsNotEmpty(entry.FamilyName);
			Assert.AreEqual($"{entry.Name} {entry.FamilyName}", entry.FullName);
			Assert.AreEqual(entry.FullName, entry.FullTitle, "NameOnly should leave no title.");
		}

		[Test]
		public void Culture_UsesItsOwnPhonology()
		{
			RacePhonology plain = RaceRegistry.ResolvePhonology("human", null);
			RacePhonology northern = RaceRegistry.ResolvePhonology("human", "northern");
			Assert.AreNotSame(plain, northern);
			CollectionAssert.AreNotEqual(plain.Onsets, northern.Onsets);
			CollectionAssert.Contains(RaceRegistry.GetCultures("human"), "northern");
		}

		[Test]
		public void UnknownCulture_FallsBackToTheRace()
		{
			Assert.AreSame(RaceRegistry.ResolvePhonology("human", null), RaceRegistry.ResolvePhonology("human", "martian"));
		}

		[Test]
		public void Gender_ChangesTheName()
		{
			int differing = 0;
			for (int seed = 1; seed <= 30; seed++)
			{
				string male = new NameGenerator(seed).GenerateName("elf", CharacterGender.Male);
				string female = new NameGenerator(seed).GenerateName("elf", CharacterGender.Female);
				if (male != female)
				{
					differing++;
				}
			}
			Assert.Greater(differing, 0, "Gender never changed the suffix across thirty seeds.");
		}

		[Test]
		public void Hybrid_BlendsBothRaces()
		{
			CharacterEntry entry = new NameGenerator(8).GenerateHybrid("human", "orc");
			Assert.IsNotEmpty(entry.Name);
			Assert.AreEqual("Human/Orc", entry.Race);
		}

		[Test]
		public void UnknownRace_ThrowsWithTheSupportedList()
		{
			var ex = Assert.Throws<ArgumentException>(() => new NameGenerator().GenerateName("martian"));
			StringAssert.Contains("Supported:", ex.Message);
		}

		[Test]
		public void RegistryByID_FindsTemplatesOutsidePlayMode()
		{
			RaceTemplate human = RaceRegistry.Get("human");
			Assert.IsTrue(RaceRegistry.TryGetByID(RaceRegistry.IDOf(human), out RaceTemplate found));
			Assert.AreSame(human, found);

			BiomeTemplate biome = BiomeRegistry.Get(BiomeRegistry.SupportedBiomes[0]);
			Assert.IsTrue(BiomeRegistry.TryGetByID(BiomeRegistry.IDOf(biome), out BiomeTemplate foundBiome));
			Assert.AreSame(biome, foundBiome);
		}
	}
}
