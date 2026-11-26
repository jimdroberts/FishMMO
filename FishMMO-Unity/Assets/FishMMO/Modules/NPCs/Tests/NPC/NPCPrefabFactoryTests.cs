using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Server.Implementation.World.SceneServer.AI;

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
		private const string GENERATED_ROOT = "Assets/FishMMO/Tests/UnitTests/Generated";
		private const string SCRATCH = GENERATED_ROOT + "/NPCPrefabFactory";
		private const string ORC = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc.prefab";
		private const string BANKER = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Banker/HumanBanker.prefab";
		private const string HUMAN = "Assets/FishMMO/Templates/Entity/Races/Humanoid/Human.asset";
		private const string ARCHER = "Assets/FishMMO/Modules/NPCs/Templates/AI/Archetypes/Enemy - Archer.asset";

		private bool generatedRootExisted;

		[OneTimeSetUp]
		public void CreateScratchFolder()
		{
			generatedRootExisted = AssetDatabase.IsValidFolder(GENERATED_ROOT);
			if (!generatedRootExisted)
			{
				AssetDatabase.CreateFolder("Assets/FishMMO/Tests/UnitTests", "Generated");
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

			// Creating a prefab gives it a brain catalogue entry; drop the ones just deleted.
			AIBrainCatalogueEditorUtility.PruneMissing();
		}

		private static GameObject Load(string path)
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectFiles.Path(path));
			Assume.That(prefab, Is.Not.Null, $"Shipped prefab '{path}' is missing; the tests need it as a base.");
			return prefab;
		}

		private static T LoadAsset<T>(string path) where T : UnityEngine.Object
		{
			T asset = AssetDatabase.LoadAssetAtPath<T>(ProjectFiles.Path(path));
			Assume.That(asset, Is.Not.Null, $"Shipped asset '{path}' is missing.");
			return asset;
		}

		private static int SerializedRaceID(GameObject prefab)
		{
			return new SerializedObject(prefab.GetComponent<NPC>()).FindProperty("raceTemplateID").intValue;
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
			Assert.IsNotNull(recipe.Archetype, "the orc's brain must be read from the server catalogue");
			Assert.AreSame(AIBrainCatalogueEditorUtility.GetArchetype(orc), recipe.Archetype);
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
		[Test]
		public void Create_ClonesTheBaseAndWritesTheRecipeOntoIt()
		{
			GameObject orc = Load(ORC);
			RaceTemplate human = LoadAsset<RaceTemplate>(HUMAN);
			AIArchetypeTemplate archer = LoadAsset<AIArchetypeTemplate>(ARCHER);
			AIArchetypeTemplate orcBrain = AIBrainCatalogueEditorUtility.GetArchetype(orc);
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
			Assert.AreSame(archer, AIBrainCatalogueEditorUtility.GetArchetype(created),
				"the recipe's brain must land in the server catalogue");
			Assert.IsNull(created.GetComponent<AIController>(), "the brain is server data; the prefab must not carry it");
			Assert.AreEqual(NPCPrefabFactory.ComputeTemplateID(human), SerializedRaceID(created));
			Assert.IsTrue(created.GetComponent<NPC>().IsAggressive);
			Assert.IsFalse(created.GetComponent<NPC>().IsCharmable);
			CollectionAssert.AreEqual(recipe.Abilities, created.GetComponent<NPC>().Abilities);
			Assert.AreSame(recipe.AttributeDatabase, created.GetComponent<CharacterAttributeController>().CharacterAttributeDatabase);

			// The boilerplate came along.
			Assert.IsNotNull(created.GetComponent<FishNet.Object.NetworkObject>());
			Assert.IsNotNull(created.GetComponent<CharacterPredictionController>());
			// The cooldown and ability controllers are parts the character adds at wake, never the prefab's.
			Assert.IsNull(created.GetComponent<CooldownController>());
			Assert.IsNull(created.GetComponent<AbilityController>());
			Assert.IsNotNull(created.GetComponent<NPC>().CharacterNameplate, "the overhead nameplate must survive the clone");

			// Nothing inside it points at another asset's NetworkObject.
			Assert.IsEmpty(NetworkObjectBindingValidator.Scan(AssetDatabase.GetAssetPath(created)),
				"a cloned prefab must own every NetworkBehaviour it carries");

			// And the base was not edited in the process.
			Assert.AreSame(orcBrain, AIBrainCatalogueEditorUtility.GetArchetype(orc));
			Assert.AreEqual(orcRaceID, SerializedRaceID(orc));
		}

		/// <summary>
		/// A clone's map marker follows its own role, not the base's: a townsperson cloned from a merchant
		/// was drawn as a vendor and a simulated player cloned from the banker as a service.
		/// </summary>
		[Test]
		public void MarkerTypeFor_FollowsTheRoleNotTheBase()
		{
			Assert.AreEqual(MapMarkerType.Vendor, NPCPrefabFactory.MarkerTypeFor(NPCInteraction.Merchant, false, MapMarkerType.Service));
			Assert.AreEqual(MapMarkerType.Service, NPCPrefabFactory.MarkerTypeFor(NPCInteraction.Banker, false, MapMarkerType.Vendor));
			Assert.AreEqual(MapMarkerType.Trainer, NPCPrefabFactory.MarkerTypeFor(NPCInteraction.AbilityCrafter, false, MapMarkerType.NPC));

			// No role: an inherited role type is dropped, to an enemy for an aggressive NPC.
			Assert.AreEqual(MapMarkerType.NPC, NPCPrefabFactory.MarkerTypeFor(NPCInteraction.None, false, MapMarkerType.Vendor));
			Assert.AreEqual(MapMarkerType.NPC, NPCPrefabFactory.MarkerTypeFor(NPCInteraction.None, false, MapMarkerType.Service));
			Assert.AreEqual(MapMarkerType.Enemy, NPCPrefabFactory.MarkerTypeFor(NPCInteraction.None, true, MapMarkerType.Trainer));

			// Anything else the prefab chose is kept.
			Assert.AreEqual(MapMarkerType.Enemy, NPCPrefabFactory.MarkerTypeFor(NPCInteraction.None, true, MapMarkerType.Enemy));
			Assert.AreEqual(MapMarkerType.NPC, NPCPrefabFactory.MarkerTypeFor(NPCInteraction.None, false, MapMarkerType.NPC));
		}

		/// <summary>
		/// Only an NPC that has a role is drawn as one: a shipped NPC prefab whose marker says vendor,
		/// service, trainer or quest giver carries an interactable. Character prefabs carry no static label;
		/// the map labels an NPC from itself.
		/// </summary>
		[Test]
		public void ShippedNpcMarkers_MatchTheirRoleAndCarryNoLabel()
		{
			List<string> problems = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Shared/Entity", "Assets/FishMMO/Modules" }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				MapMarker marker = prefab != null ? prefab.GetComponent<MapMarker>() : null;
				if (marker == null || prefab.GetComponent<FishMMO.Shared.Core.ICharacter>() == null)
				{
					continue;
				}

				if (!string.IsNullOrEmpty(marker.Label))
				{
					problems.Add($"{path}: character marker carries the static label '{marker.Label}'.");
				}

				bool roleType = marker.Type == MapMarkerType.Vendor || marker.Type == MapMarkerType.Service ||
					marker.Type == MapMarkerType.Trainer || marker.Type == MapMarkerType.QuestGiver;
				if (roleType && prefab.GetComponent<IInteractable>() == null)
				{
					problems.Add($"{path}: drawn as {marker.Type} but has no interactable.");
				}
			}
			Assert.IsEmpty(problems, string.Join("\n", problems));
		}
	}
}
