using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.NameGeneration.Editor;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The pure half of scene-object naming, <see cref="SceneObjectNameResolver"/>.
	/// The server sends a seed and a gender and both peers run this from the
	/// same inputs, so what matters is that identical inputs always give the
	/// same name, that the gender policy respects the race's models, and that
	/// inputs which cannot name an object say so instead of guessing.
	/// </summary>
	[TestFixture]
	public class SceneObjectNamerTests
	{
		private RaceTemplate human;
		private RaceTemplate elf;
		private BiomeNamingTemplate biome;
		private readonly List<ScriptableObject> created = new List<ScriptableObject>();

		[OneTimeSetUp]
		public void LoadAssets()
		{
			NamingTemplateEditorLoader.Reload();
			human = RaceRegistry.Get("human");
			elf = RaceRegistry.Get("elf");
			biome = BiomeRegistry.Get(BiomeRegistry.SupportedBiomes[0]);
			Assume.That(human, Is.Not.Null);
			Assume.That(elf, Is.Not.Null);
			Assume.That(biome, Is.Not.Null);
		}

		[TearDown]
		public void DestroyCreated()
		{
			foreach (ScriptableObject asset in created)
			{
				Object.DestroyImmediate(asset);
			}
			created.Clear();
		}

		private static SceneObjectNamingSettings Character(NamingTitlePolicy title = NamingTitlePolicy.None,
			CharacterNameFormat format = CharacterNameFormat.GivenAndFamily)
		{
			return new SceneObjectNamingSettings { Mode = SceneObjectNamingMode.Character, TitlePolicy = title, NameFormat = format };
		}

		private RaceTemplate RaceWithModels(params CharacterGender[] genders)
		{
			var race = ScriptableObject.CreateInstance<RaceTemplate>();
			race.name = "Synthetic";
			foreach (CharacterGender gender in genders)
			{
				race.GenderedModels.Add(new GenderedRaceModelSet
				{
					Gender = gender,
					ModelReferences = new List<AssetReference> { new AssetReference("00000000000000000000000000000001") },
				});
			}
			created.Add(race);
			return race;
		}

		// ── Determinism: what the wire format depends on ──────────────

		[Test]
		public void SameInputs_AlwaysGiveTheSameName()
		{
			SceneObjectNamingSettings settings = Character();
			Assert.IsTrue(SceneObjectNameResolver.TryBuild(settings, human, 1234, CharacterGender.Male, out string first, out _));
			Assert.IsTrue(SceneObjectNameResolver.TryBuild(Character(), human, 1234, CharacterGender.Male, out string second, out _));
			Assert.AreEqual(first, second, "Server and client must regenerate the identical name from the seed.");
		}

		[Test]
		public void DifferentSeeds_GiveDifferentNames()
		{
			var names = new HashSet<string>();
			for (int seed = 1; seed <= 20; seed++)
			{
				Assert.IsTrue(SceneObjectNameResolver.TryBuild(Character(), human, seed, CharacterGender.Female, out string name, out _));
				names.Add(name);
			}
			Assert.Greater(names.Count, 10);
		}

		[Test]
		public void Gender_ChangesTheNameForTheSameSeed()
		{
			int differing = 0;
			for (int seed = 1; seed <= 30; seed++)
			{
				SceneObjectNameResolver.TryBuild(Character(format: CharacterNameFormat.Given), elf, seed, CharacterGender.Male, out string male, out _);
				SceneObjectNameResolver.TryBuild(Character(format: CharacterNameFormat.Given), elf, seed, CharacterGender.Female, out string female, out _);
				if (male != female)
				{
					differing++;
				}
			}
			Assert.Greater(differing, 0);
		}

		[Test]
		public void ZeroSeed_MeansNoName()
		{
			Assert.IsFalse(SceneObjectNameResolver.TryBuild(Character(), human, 0, CharacterGender.Male, out _, out string error));
			StringAssert.Contains("seed", error);
		}

		[Test]
		public void DeriveSeed_IsNeverZero_AndStableUnderARegionSeed()
		{
			var region = new SceneObjectNamingSettings { RegionSeed = "azure-vale", ObjectSeed = "banker-1" };
			int a = SceneObjectNameResolver.DeriveSeed(region, "fallback");
			int b = SceneObjectNameResolver.DeriveSeed(region, "other-fallback");
			Assert.AreNotEqual(0, a);
			Assert.AreEqual(a, b, "An explicit object seed should make the fallback irrelevant.");

			var regionNoObject = new SceneObjectNamingSettings { RegionSeed = "azure-vale" };
			Assert.AreEqual(SceneObjectNameResolver.DeriveSeed(regionNoObject, "Banker"),
				SceneObjectNameResolver.DeriveSeed(regionNoObject, "Banker"));
			Assert.AreNotEqual(SceneObjectNameResolver.DeriveSeed(regionNoObject, "Banker"),
				SceneObjectNameResolver.DeriveSeed(regionNoObject, "Merchant"));

			Assert.AreNotEqual(0, SceneObjectNameResolver.DeriveSeed(new SceneObjectNamingSettings(), "x"));
		}

		// ── Name shape follows the settings ───────────────────────────

		[Test]
		public void GivenOnly_IsOneWord_GivenAndFamily_IsTwo()
		{
			SceneObjectNameResolver.TryBuild(Character(format: CharacterNameFormat.Given), human, 77, CharacterGender.Male, out string given, out _);
			SceneObjectNameResolver.TryBuild(Character(format: CharacterNameFormat.GivenAndFamily), human, 77, CharacterGender.Male, out string full, out _);
			Assert.AreEqual(1, given.Split(' ').Length, $"'{given}' should be a single word.");
			Assert.AreEqual(2, full.Split(' ').Length, $"'{full}' should be given + family.");
			StringAssert.StartsWith(given, full, "The family-name draw must not disturb the given name.");
		}

		[Test]
		public void TitlePolicy_AddsATitleAfterAComma()
		{
			SceneObjectNameResolver.TryBuild(Character(NamingTitlePolicy.None), human, 5, CharacterGender.Male, out string plain, out _);
			SceneObjectNameResolver.TryBuild(Character(NamingTitlePolicy.Honorific), human, 5, CharacterGender.Male, out string titled, out _);
			Assert.IsFalse(plain.Contains(","));
			StringAssert.Contains(", ", titled);
			StringAssert.StartsWith(plain, titled, "The title must be appended, not reshuffle the name.");
		}

		[Test]
		public void CityMode_NamesFromTheRace()
		{
			var settings = new SceneObjectNamingSettings { Mode = SceneObjectNamingMode.City, CityType = CityType.Port };
			Assert.IsTrue(SceneObjectNameResolver.TryBuild(settings, human, 9, CharacterGender.Unspecified, out string name, out string error), error);
			Assert.IsNotEmpty(name);
		}

		[Test]
		public void ItemMode_NamesFromTheRace()
		{
			var settings = new SceneObjectNamingSettings { Mode = SceneObjectNamingMode.Item, ItemType = ItemType.Weapon };
			Assert.IsTrue(SceneObjectNameResolver.TryBuild(settings, elf, 9, CharacterGender.Unspecified, out string name, out string error), error);
			Assert.IsNotEmpty(name);
		}

		[Test]
		public void DungeonAndPOI_NeedABiome_AndWorkWithOne()
		{
			var dungeon = new SceneObjectNamingSettings { Mode = SceneObjectNamingMode.Dungeon };
			Assert.IsFalse(SceneObjectNameResolver.TryBuild(dungeon, null, 3, CharacterGender.Unspecified, out _, out string error));
			StringAssert.Contains("Biome", error);

			dungeon.BiomeID = BiomeRegistry.IDOf(biome);
			Assert.IsTrue(SceneObjectNameResolver.TryBuild(dungeon, null, 3, CharacterGender.Unspecified, out string dungeonName, out error), error);
			Assert.IsNotEmpty(dungeonName);

			var poi = new SceneObjectNamingSettings { Mode = SceneObjectNamingMode.PointOfInterest, POIType = POIType.Shrine, BiomeID = BiomeRegistry.IDOf(biome) };
			Assert.IsTrue(SceneObjectNameResolver.TryBuild(poi, null, 3, CharacterGender.Unspecified, out string poiName, out error), error);
			Assert.IsNotEmpty(poiName);
		}

		[Test]
		public void CharacterMode_WithoutARace_KeepsTheAuthoredName()
		{
			Assert.IsFalse(SceneObjectNameResolver.TryBuild(Character(), null, 3, CharacterGender.Male, out _, out string error));
			StringAssert.Contains("race", error);
		}

		[Test]
		public void Modifier_ByID_IsApplied()
		{
			NameModifierTemplate modifier = ModifierRegistry.TryGet("ashen", out NameModifierTemplate found) ? found : null;
			Assume.That(modifier, Is.Not.Null);

			var plain = Character(format: CharacterNameFormat.Given);
			var modified = Character(format: CharacterNameFormat.Given);
			modified.ModifierID = ModifierRegistry.IDOf(modifier);

			int differing = 0;
			for (int seed = 1; seed <= 30; seed++)
			{
				SceneObjectNameResolver.TryBuild(plain, human, seed, CharacterGender.Male, out string a, out _);
				SceneObjectNameResolver.TryBuild(modified, human, seed, CharacterGender.Male, out string b, out _);
				if (a != b)
				{
					differing++;
				}
			}
			Assert.Greater(differing, 0, "A modifier that changes the syllable pool should change some names.");
		}

		// ── Race resolution ───────────────────────────────────────────

		[Test]
		public void RaceOverride_WinsOverTheFaction()
		{
			var settings = new SceneObjectNamingSettings { RaceOverrideID = RaceRegistry.IDOf(elf) };
			Assert.AreSame(elf, SceneObjectNameResolver.ResolveRace(settings, null));
			Assert.IsNull(SceneObjectNameResolver.ResolveRace(new SceneObjectNamingSettings(), null),
				"No override and no faction means no race.");
		}

		// ── Gender policy ─────────────────────────────────────────────

		[Test]
		public void RaceModels_OnlyPicksGendersTheRaceHasModelsFor()
		{
			RaceTemplate maleOnly = RaceWithModels(CharacterGender.Male);
			RaceTemplate femaleOnly = RaceWithModels(CharacterGender.Female);
			RaceTemplate both = RaceWithModels(CharacterGender.Male, CharacterGender.Female);
			RaceTemplate none = RaceWithModels();

			var seenBoth = new HashSet<CharacterGender>();
			var seenNone = new HashSet<CharacterGender>();
			for (int seed = 1; seed <= 40; seed++)
			{
				DeterministicRNG rng = SceneObjectNameResolver.GenderRng(seed);
				Assert.AreEqual(CharacterGender.Male, SceneObjectNameResolver.ResolveGender(NamingGenderPolicy.RaceModels, maleOnly, rng));
				Assert.AreEqual(CharacterGender.Female, SceneObjectNameResolver.ResolveGender(NamingGenderPolicy.RaceModels, femaleOnly, rng));
				seenBoth.Add(SceneObjectNameResolver.ResolveGender(NamingGenderPolicy.RaceModels, both, rng));
				seenNone.Add(SceneObjectNameResolver.ResolveGender(NamingGenderPolicy.RaceModels, none, rng));
			}
			CollectionAssert.AreEquivalent(new[] { CharacterGender.Male, CharacterGender.Female }, seenBoth);
			CollectionAssert.AreEquivalent(new[] { CharacterGender.Male, CharacterGender.Female }, seenNone,
				"A race with no gendered sets should fall back to a coin flip, never to Unspecified.");
		}

		[Test]
		public void FixedPolicies_AreFixed()
		{
			DeterministicRNG rng = SceneObjectNameResolver.GenderRng(1);
			Assert.AreEqual(CharacterGender.Male, SceneObjectNameResolver.ResolveGender(NamingGenderPolicy.Male, null, rng));
			Assert.AreEqual(CharacterGender.Female, SceneObjectNameResolver.ResolveGender(NamingGenderPolicy.Female, null, rng));
			Assert.AreEqual(CharacterGender.Unspecified, SceneObjectNameResolver.ResolveGender(NamingGenderPolicy.Unspecified, null, rng));
		}

		[Test]
		public void GenderRoll_IsSeparateFromTheNameDraw()
		{
			// Same seed, same gender: the policy used to pick that gender must not change the name.
			SceneObjectNameResolver.TryBuild(Character(), human, 21, CharacterGender.Female, out string a, out _);
			var settings = Character();
			settings.GenderPolicy = NamingGenderPolicy.Female;
			SceneObjectNameResolver.TryBuild(settings, human, 21, CharacterGender.Female, out string b, out _);
			Assert.AreEqual(a, b);
		}
	}
}
