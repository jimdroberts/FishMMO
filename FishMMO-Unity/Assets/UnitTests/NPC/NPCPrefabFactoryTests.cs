using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.NPCs
{
	/// <summary>
	/// Pins what the dashboard's NPC designer produces: a prefab cloned from a working base with
	/// exactly the recipe's race, brain, stats, kit and role written onto it, and nothing foreign
	/// left inside.
	/// </summary>
	/// <remarks>
	/// EditMode only. Prefabs are written under a scratch folder and deleted again; the shipped
	/// base prefabs are asserted untouched afterwards.
	/// </remarks>
	[TestFixture]
	public class NPCPrefabFactoryTests
	{
		private const string GENERATED_ROOT = "Assets/UnitTests/Generated";
		private const string SCRATCH = GENERATED_ROOT + "/NPCPrefabFactory";
		private const string ORC = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc.prefab";
		private const string BANKER = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Banker/HumanBanker.prefab";
		private const string HUMAN = "Assets/Templates/Entity/Races/Humanoid/Human.asset";
		private const string ARCHER = "Assets/Templates/Entity/NPCs/AI/Archetypes/Enemy - Archer.asset";

		private bool generatedRootExisted;

		[OneTimeSetUp]
		public void CreateScratchFolder()
		{
			generatedRootExisted = AssetDatabase.IsValidFolder(GENERATED_ROOT);
			if (!generatedRootExisted)
			{
				AssetDatabase.CreateFolder("Assets/UnitTests", "Generated");
			}
			if (!AssetDatabase.IsValidFolder(SCRATCH))
			{
				AssetDatabase.CreateFolder(GENERATED_ROOT, "NPCPrefabFactory");
			}
		}

		[OneTimeTearDown]
		public void DeleteScratchFolder()
		{
			AssetDatabase.DeleteAsset(SCRATCH);
			if (!generatedRootExisted)
			{
				AssetDatabase.DeleteAsset(GENERATED_ROOT);
			}
			AssetDatabase.Refresh();
		}

		private static GameObject Load(string path)
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			Assume.That(prefab, Is.Not.Null, $"Shipped prefab '{path}' is missing; the tests need it as a base.");
			return prefab;
		}

		private static T LoadAsset<T>(string path) where T : UnityEngine.Object
		{
			T asset = AssetDatabase.LoadAssetAtPath<T>(path);
			Assume.That(asset, Is.Not.Null, $"Shipped asset '{path}' is missing.");
			return asset;
		}

		private static int SerializedRaceID(GameObject prefab)
		{
			return new SerializedObject(prefab.GetComponent<FactionController>()).FindProperty("raceTemplateID").intValue;
		}

		// --- Reading recipes -----------------------------------------------------------------

		[Test]
		public void RecipeFrom_ReadsTheOrcsRaceBrainAndKit()
		{
			GameObject orc = Load(ORC);
			NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(orc);

			Assert.IsNotNull(recipe);
			Assert.AreSame(orc, recipe.BasePrefab);
			Assert.IsNotNull(recipe.Race, "the race is stored as an ID and must resolve back to its asset");
			Assert.AreEqual("Orc", recipe.Race.name);
			Assert.AreSame(orc.GetComponent<AIController>().Archetype, recipe.Archetype);
			Assert.AreEqual(orc.GetComponent<NPC>().Abilities.Count, recipe.Abilities.Count);
			Assert.AreEqual(NPCInteraction.None, recipe.Interaction);
			Assert.AreEqual(NPCPrefabFactory.KIND_MONSTER, NPCPrefabFactory.Classify(orc));
		}

		[Test]
		public void RecipeFrom_ReadsACiviliansRole()
		{
			GameObject banker = Load(BANKER);
			NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(banker);

			Assert.AreEqual(NPCInteraction.Banker, recipe.Interaction);
			Assert.AreEqual(NPCPrefabFactory.KIND_CIVILIAN, NPCPrefabFactory.Classify(banker));
		}

		[Test]
		public void ComputeTemplateID_MatchesWhatThePrefabsCarry()
		{
			/* FactionController stores the race as CachedScriptableObject's deterministic ID. If
			 * the factory's hash ever drifted from it, every NPC it made would be raceless. */
			GameObject orc = Load(ORC);
			RaceTemplate orcRace = NPCPrefabFactory.FindTemplateByID<RaceTemplate>(SerializedRaceID(orc));
			Assert.IsNotNull(orcRace);
			Assert.AreEqual(SerializedRaceID(orc), NPCPrefabFactory.ComputeTemplateID(orcRace));
		}

		// --- Validation ------------------------------------------------------------------------

		[Test]
		public void Validate_RefusesARecipeMissingWhatAnNPCCannotSpawnWithout()
		{
			NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(Load(ORC));
			recipe.Name = "Validate Test";
			recipe.Folder = SCRATCH;
			recipe.Race = null;
			recipe.Archetype = null;
			recipe.AttributeDatabase = null;

			List<string> problems = new List<string>();
			Assert.IsFalse(NPCPrefabFactory.Validate(recipe, problems));
			Assert.AreEqual(3, problems.Count, string.Join("\n", problems));
		}

		[Test]
		public void Validate_RefusesAMerchantWithNothingToSell()
		{
			NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(Load(ORC));
			recipe.Name = "Validate Merchant";
			recipe.Folder = SCRATCH;
			recipe.Interaction = NPCInteraction.Merchant;
			recipe.MerchantTemplate = null;

			List<string> problems = new List<string>();
			Assert.IsFalse(NPCPrefabFactory.Validate(recipe, problems));
			Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
		}

		// --- Creation --------------------------------------------------------------------------

		[Test]
		public void Create_ClonesTheBaseAndWritesTheRecipeOntoIt()
		{
			GameObject orc = Load(ORC);
			RaceTemplate human = LoadAsset<RaceTemplate>(HUMAN);
			AIArchetypeTemplate archer = LoadAsset<AIArchetypeTemplate>(ARCHER);
			AIArchetypeTemplate orcBrain = orc.GetComponent<AIController>().Archetype;
			int orcRaceID = SerializedRaceID(orc);

			NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(orc);
			recipe.Name = "Factory Human Archer";
			recipe.Folder = SCRATCH;
			recipe.Race = human;
			recipe.Archetype = archer;
			recipe.IsAggressive = true;
			recipe.IsCharmable = false;
			recipe.RegisterAddressable = false;

			GameObject created = NPCPrefabFactory.Create(recipe);

			Assert.IsNotNull(created);
			Assert.AreEqual(SCRATCH + "/Factory Human Archer.prefab", AssetDatabase.GetAssetPath(created));
			Assert.AreEqual("Factory Human Archer", created.name);

			// The recipe landed.
			Assert.AreSame(archer, created.GetComponent<AIController>().Archetype);
			Assert.AreEqual(NPCPrefabFactory.ComputeTemplateID(human), SerializedRaceID(created));
			Assert.IsTrue(created.GetComponent<FactionController>().IsAggressive);
			Assert.IsFalse(created.GetComponent<NPC>().IsCharmable);
			CollectionAssert.AreEqual(recipe.Abilities, created.GetComponent<NPC>().Abilities);
			Assert.AreSame(recipe.AttributeDatabase, created.GetComponent<CharacterAttributeController>().CharacterAttributeDatabase);

			// The boilerplate came along.
			Assert.IsNotNull(created.GetComponent<FishNet.Object.NetworkObject>());
			Assert.IsNotNull(created.GetComponent<CharacterPredictionController>());
			Assert.IsNotNull(created.GetComponent<CooldownController>());
			Assert.IsNotNull(created.GetComponent<AbilityController>());
			Assert.IsNotNull(created.GetComponent<NPC>().CharacterNameLabel, "the name label sub-prefab must survive the clone");

			// Nothing inside it points at another asset's NetworkObject.
			Assert.IsEmpty(NetworkObjectBindingValidator.Scan(AssetDatabase.GetAssetPath(created)),
				"a cloned prefab must own every NetworkBehaviour it carries");

			// And the base was not edited in the process.
			Assert.AreSame(orcBrain, orc.GetComponent<AIController>().Archetype);
			Assert.AreEqual(orcRaceID, SerializedRaceID(orc));
		}

		[Test]
		public void Create_GivesAMonsterAnInteractionRole()
		{
			NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(Load(ORC));
			recipe.Name = "Factory Orc Banker";
			recipe.Folder = SCRATCH;
			recipe.Interaction = NPCInteraction.Banker;
			recipe.RegisterAddressable = false;

			GameObject created = NPCPrefabFactory.Create(recipe);

			Assert.IsNotNull(created.GetComponent<Banker>());
			Assert.IsNull(created.GetComponent<Merchant>());
			Assert.AreEqual(NPCPrefabFactory.KIND_CIVILIAN, NPCPrefabFactory.Classify(created));
		}

		[Test]
		public void Create_TakesACiviliansRoleAway()
		{
			NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(Load(BANKER));
			recipe.Name = "Factory Plain Human";
			recipe.Folder = SCRATCH;
			recipe.Interaction = NPCInteraction.None;
			recipe.RegisterAddressable = false;

			GameObject created = NPCPrefabFactory.Create(recipe);

			Assert.IsNull(created.GetComponent<Interactable>());
			Assert.AreEqual(NPCPrefabFactory.KIND_MONSTER, NPCPrefabFactory.Classify(created));
		}

		[Test]
		public void Create_RefusesToOverwriteAnExistingPrefab()
		{
			NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(Load(ORC));
			recipe.Name = "Factory Twice";
			recipe.Folder = SCRATCH;
			recipe.RegisterAddressable = false;

			NPCPrefabFactory.Create(recipe);

			Assert.Throws<InvalidOperationException>(() => NPCPrefabFactory.Create(recipe),
				"a second create at the same path must refuse rather than clobber a designer's prefab");
		}
	}
}
