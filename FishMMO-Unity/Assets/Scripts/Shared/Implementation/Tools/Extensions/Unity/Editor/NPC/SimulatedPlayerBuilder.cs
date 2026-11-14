using System;
using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishNet.Component.Observing;
using FishNet.Observing;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Builds the "Simulated Player" the admin <c>simulate</c> command spawns for stress tests: a humanoid NPC with no
	/// interaction, never aggressive, streamed to viewers under the PLAYER observer classification (so the player visibility
	/// budget and the byte budget treat it exactly as a player), with a brain that keeps it walking around where it was
	/// placed.
	/// </summary>
	/// <remarks>
	/// Three assets, each made only when missing (re-running is safe): a wander state that rarely idles, an archetype using
	/// it, and the prefab, cloned from the human banker through <see cref="NPCPrefabFactory"/> (which also registers its
	/// brain in the server's catalogue and the prefab in Addressables).
	/// </remarks>
	public static class SimulatedPlayerBuilder
	{
		public const string PrefabName = "Simulated Player";
		public const string PrefabFolder = "Assets/Prefabs/Shared/Entity/NPCs/Testing";
		private const string BasePrefabPath = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Banker/HumanBanker.prefab";
		private const string HumanRacePath = "Assets/Templates/Entity/Races/Humanoid/Human.asset";
		private const string BaseArchetypePath = "Assets/Templates/Entity/NPCs/AI/Archetypes/Civilian - Townsfolk.asset";
		private const string BaseWanderPath = "Assets/Templates/Entity/NPCs/AI/States/Movement/Basic Wander State.asset";
		private const string WanderPath = "Assets/Templates/Entity/NPCs/AI/States/Movement/Simulated Player Wander State.asset";
		private const string ArchetypePath = "Assets/Templates/Entity/NPCs/AI/Archetypes/Civilian - Simulated Player.asset";
		private const string PlayerConditionPath = "Assets/Settings/ObserverConditions/PlayerDistanceCondition.asset";

		[DashboardTool(DashboardToolAttribute.Maintenance, "Build simulated player", Section = "Testing", Order = 1,
			Tooltip = "Makes the 'Simulated Player' NPC the admin simulate command spawns for stress tests (only what is missing).")]
		public static void BuildFromDashboard() => Debug.Log("[Simulated player]\n  " + string.Join("\n  ", Build()));

		/// <summary>Headless: <c>-executeMethod FishMMO.Shared.SimulatedPlayerBuilder.BuildBatch</c>. Exits 0 on success.</summary>
		public static void BuildBatch()
		{
			bool ok = false;
			try
			{
				List<string> notes = Build();
				Debug.Log("[Simulated player]\n  " + string.Join("\n  ", notes));
				ok = !notes.Exists(n => n.StartsWith("FAILED", StringComparison.Ordinal));
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Simulated player] {ex}");
			}
			EditorApplication.Exit(ok ? 0 : 1);
		}

		public static List<string> Build()
		{
			var notes = new List<string>();
			WanderState wander = EnsureWander(notes);
			AIArchetypeTemplate archetype = wander != null ? EnsureArchetype(wander, notes) : null;
			if (archetype == null)
			{
				notes.Add("FAILED: no archetype.");
				return notes;
			}
			GameObject prefab = EnsurePrefab(archetype, notes);
			if (prefab != null)
			{
				ClassifyAsPlayer(prefab, notes);
			}
			AssetDatabase.SaveAssets();
			return notes;
		}

		/// <summary>Walks almost all the time: a stress test wants characters moving, not standing about.</summary>
		private static WanderState EnsureWander(List<string> notes)
		{
			var existing = AssetDatabase.LoadAssetAtPath<WanderState>(WanderPath);
			if (existing != null)
			{
				notes.Add($"Kept {WanderPath}.");
				return existing;
			}
			var source = AssetDatabase.LoadAssetAtPath<WanderState>(BaseWanderPath);
			if (source == null)
			{
				notes.Add($"FAILED: no {BaseWanderPath} to start from.");
				return null;
			}
			WanderState wander = UnityEngine.Object.Instantiate(source);
			AssetDatabase.CreateAsset(wander, WanderPath);
			var so = new SerializedObject(wander);
			Set(so, "updateRate", 2f);
			Set(so, "MaxUpdateRate", 5f);
			Set(so, "IdleChance", 0.1f);
			Set(so, "WanderRadius", 25f);
			so.ApplyModifiedPropertiesWithoutUndo();
			notes.Add($"Made {WanderPath} (2–5 s between destinations, 10% idle, 25 m).");
			return wander;
		}

		private static AIArchetypeTemplate EnsureArchetype(WanderState wander, List<string> notes)
		{
			var existing = AssetDatabase.LoadAssetAtPath<AIArchetypeTemplate>(ArchetypePath);
			if (existing != null)
			{
				notes.Add($"Kept {ArchetypePath}.");
				return existing;
			}
			var source = AssetDatabase.LoadAssetAtPath<AIArchetypeTemplate>(BaseArchetypePath);
			if (source == null)
			{
				notes.Add($"FAILED: no {BaseArchetypePath} to start from.");
				return null;
			}
			AIArchetypeTemplate archetype = UnityEngine.Object.Instantiate(source);
			AssetDatabase.CreateAsset(archetype, ArchetypePath);
			var so = new SerializedObject(archetype);
			SetObject(so, "InitialState", wander);
			SetObject(so, "WanderState", wander);
			SetString(so, "Description", "Stress-test simulated player: a non-combatant that keeps walking around where it was placed. Spawned by /admin simulate.");
			so.ApplyModifiedPropertiesWithoutUndo();
			notes.Add($"Made {ArchetypePath} (Townsfolk, with the simulated player's wander).");
			return archetype;
		}

		private static GameObject EnsurePrefab(AIArchetypeTemplate archetype, List<string> notes)
		{
			string path = $"{PrefabFolder}/{PrefabName}.prefab";
			var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			if (existing != null)
			{
				AIBrainCatalogueEditorUtility.SetArchetype(existing, archetype);
				notes.Add($"Kept {path}.");
				return existing;
			}
			var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
			NPCRecipe recipe = NPCPrefabFactory.RecipeFrom(basePrefab);
			if (recipe == null)
			{
				notes.Add($"FAILED: {BasePrefabPath} is not an NPC prefab.");
				return null;
			}
			recipe.Name = PrefabName;
			recipe.Folder = PrefabFolder;
			recipe.Interaction = NPCInteraction.None;
			recipe.MerchantTemplate = null;
			recipe.Archetype = archetype;
			recipe.Race = AssetDatabase.LoadAssetAtPath<RaceTemplate>(HumanRacePath) ?? recipe.Race;
			recipe.IsAggressive = false;
			recipe.IsCharmable = false;
			recipe.LootTable = null;
			recipe.Abilities.Clear();
			recipe.RegisterAddressable = true;
			GameObject created = NPCPrefabFactory.Create(recipe);
			notes.Add($"Made {path} from {BasePrefabPath} (no interaction, not aggressive, human).");
			return created;
		}

		/// <summary>Streams it to viewers as a player: its distance condition becomes the Player classification's asset.</summary>
		private static void ClassifyAsPlayer(GameObject prefab, List<string> notes)
		{
			var player = AssetDatabase.LoadAssetAtPath<ObserverCondition>(PlayerConditionPath);
			if (player == null)
			{
				notes.Add($"FAILED: no {PlayerConditionPath}.");
				return;
			}
			string path = AssetDatabase.GetAssetPath(prefab);
			GameObject root = PrefabUtility.LoadPrefabContents(path);
			try
			{
				NetworkObserver observer = root.GetComponent<NetworkObserver>();
				if (observer == null)
				{
					notes.Add("FAILED: the prefab has no NetworkObserver.");
					return;
				}
				var so = new SerializedObject(observer);
				SerializedProperty conditions = so.FindProperty("_observerConditions");
				bool replaced = false;
				for (int i = 0; conditions != null && i < conditions.arraySize; ++i)
				{
					SerializedProperty element = conditions.GetArrayElementAtIndex(i);
					if (element.objectReferenceValue is DistanceCondition && element.objectReferenceValue != player)
					{
						element.objectReferenceValue = player;
						replaced = true;
					}
				}
				so.ApplyModifiedPropertiesWithoutUndo();
				if (replaced)
				{
					PrefabUtility.SaveAsPrefabAsset(root, path);
					notes.Add("Classified as a Player for observer streaming (the player visibility and byte budgets apply).");
				}
				else
				{
					notes.Add("Already classified as a Player.");
				}
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}
		}

		private static void Set(SerializedObject so, string field, float value)
		{
			SerializedProperty p = so.FindProperty(field);
			if (p != null)
			{
				p.floatValue = value;
			}
		}

		private static void SetObject(SerializedObject so, string field, UnityEngine.Object value)
		{
			SerializedProperty p = so.FindProperty(field);
			if (p != null)
			{
				p.objectReferenceValue = value;
			}
		}

		private static void SetString(SerializedObject so, string field, string value)
		{
			SerializedProperty p = so.FindProperty(field);
			if (p != null)
			{
				p.stringValue = value;
			}
		}
	}
}
