using System.Collections.Generic;
using FishMMO.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.TestHarness.Editor
{
	/// <summary>
	/// Creates the self-running simulation scenes under <c>Assets/Scenes/Test/</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each scene is deliberately TINY — one bootstrap GameObject carrying its harness component,
	/// plus a camera and a light. The harness builds its whole world procedurally in
	/// <c>Start()</c>, so the scene files never drift from the code and regeneration is always
	/// safe. Cleanup for the entire harness is: delete <c>Assets/FishMMO/Tests/TestHarness/</c> and
	/// <c>Assets/Scenes/Test/</c>.
	/// </para>
	/// <para>
	/// Headless: <c>-executeMethod FishMMO.TestHarness.Editor.TestSceneGenerator.GenerateAll</c>.
	/// </para>
	/// </remarks>
	public static class TestSceneGenerator
	{
		private const string SceneFolder = "Assets/Scenes/Test";

		/// <summary>Creates or refreshes every simulation scene.</summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate All Test Scenes", Section = "Test Scenes", Order = 0, Tooltip = "Regenerates every sim scene below.", Confirm = "Regenerate every test harness scene?")]
		public static void GenerateAll()
		{
			EnsureFolder();
			GeneratePlatformSim();
			GenerateCombatSim();
			GenerateInteractableSim();
			GenerateRegionSim();
			AssetDatabase.SaveAssets();
			Debug.Log("[TestSceneGenerator] DONE — scenes written under " + SceneFolder);
		}

		/// <summary>Creates <c>InteractableSim.unity</c>: the server interact-chain scene. The
		/// harness locates its prefabs and manifest itself (editor fallbacks), so the scene is
		/// only the bootstrap.</summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate Interactable Sim", Section = "Test Scenes", Order = 1)]
		public static void GenerateInteractableSim()
		{
			EnsureFolder();
			BuildCombatManifest();
			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			new GameObject("InteractableSimHarness").AddComponent<InteractableSimHarness>();
			AddCameraAndLight(new Vector3(2f, 8f, -10f), 35f);
			SaveScene(scene, SceneFolder + "/InteractableSim.unity");
		}

		/// <summary>Creates <c>RegionSim.unity</c>: the region enter/exit/nesting/ledger scene.</summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate Region Sim", Section = "Test Scenes", Order = 2)]
		public static void GenerateRegionSim()
		{
			EnsureFolder();
			BuildCombatManifest();
			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			new GameObject("RegionSimHarness").AddComponent<RegionSimHarness>();
			AddCameraAndLight(new Vector3(0f, 18f, -20f), 42f);
			SaveScene(scene, SceneFolder + "/RegionSim.unity");
		}

		private static void AddCameraAndLight(Vector3 cameraPosition, float pitch)
		{
			GameObject cameraGo = new GameObject("SimCamera");
			Camera camera = cameraGo.AddComponent<Camera>();
			camera.transform.position = cameraPosition;
			camera.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
			cameraGo.tag = "MainCamera";

			GameObject lightGo = new GameObject("SimLight");
			Light light = lightGo.AddComponent<Light>();
			light.type = LightType.Directional;
			light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
			light.intensity = 1.1f;
		}

		/// <summary>Creates <c>PlatformSim.unity</c>: the twin-world platform prediction scene.</summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate Platform Sim", Section = "Test Scenes", Order = 3)]
		public static void GeneratePlatformSim()
		{
			EnsureFolder();
			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

			GameObject bootstrap = new GameObject("PlatformSimHarness");
			bootstrap.AddComponent<PlatformSimHarness>();
			AddCameraAndLight(new Vector3(0f, 9f, -14f), 28f);
			SaveScene(scene, SceneFolder + "/PlatformSim.unity");
		}

		/// <summary>Creates <c>CombatSim.unity</c>: the zero-client server combat + lag-comp scene,
		/// refreshing its manifest of the ability roster (direct references, so the bootstrap does not wait on
		/// the addressable labels).</summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate Combat Sim", Section = "Test Scenes", Order = 4)]
		public static void GenerateCombatSim()
		{
			EnsureFolder();
			CombatSimManifest manifest = BuildCombatManifest();

			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

			GameObject bootstrap = new GameObject("CombatSimBootstrap");
			CombatSimBootstrap sim = bootstrap.AddComponent<CombatSimBootstrap>();
			sim.Manifest = manifest;
			AddCameraAndLight(new Vector3(0f, 14f, -16f), 40f);
			SaveScene(scene, SceneFolder + "/CombatSim.unity");
		}

		/// <summary>Rebuilds only the CombatSim manifest (its roster and cache) from the shipped ability suite; no scene is touched.</summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Refresh Combat Sim Roster", Section = "Test Scenes", Order = 5,
			Tooltip = "Rebuilds the CombatSim manifest's roster from the ability templates under Modules/Abilities/Templates/Types. The scene is left alone.")]
		public static void RefreshCombatManifest()
		{
			EnsureFolder();
			BuildCombatManifest();
		}

		private const string ManifestPath = "Assets/FishMMO/Tests/TestHarness/Combat/CombatSimManifest.asset";

		/// <summary>Where the roster comes from: every ability the test suite ships (the Mock set is gone).</summary>
		private const string RosterFolder = "Assets/FishMMO/Modules/Abilities/Templates/Types";

		/// <summary>What the roster's abilities are built from, cached beside them.</summary>
		private static readonly string[] RosterContentFolders =
		{
			"Assets/FishMMO/Modules/Abilities/Templates/Types",
			"Assets/FishMMO/Modules/Abilities/Templates/Events",
			"Assets/FishMMO/Modules/Buffs/Templates",
		};

		/// <summary>
		/// Left out of the sim's roster: Mesmerize turns a fighter to the other team mid-fight (the fighters are charmable
		/// orcs), and Invisibility takes a fighter out of the other team's sweeps; both are different tests from the one the
		/// sim runs.
		/// </summary>
		private static readonly string[] RosterExcluded = { "Mesmerize", "Invisibility" };
		private const string NpcPrefabPath =
			"Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc warrior.prefab";

		/// <summary>Scans the shipped ability suite (<see cref="RosterFolder"/>) and (re)writes the manifest asset the CombatSim
		/// bootstrap reads at runtime.</summary>
		private static CombatSimManifest BuildCombatManifest()
		{
			CombatSimManifest manifest = AssetDatabase.LoadAssetAtPath<CombatSimManifest>(ManifestPath);
			bool created = manifest == null;
			if (created)
			{
				manifest = ScriptableObject.CreateInstance<CombatSimManifest>();
			}

			manifest.NpcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabPath);
			if (manifest.NpcPrefab == null)
			{
				Debug.LogError($"[TestSceneGenerator] NPC prefab missing at {NpcPrefabPath} — CombatSim cannot spawn fighters.");
			}

			manifest.SpawnablePrefabs = AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.PrefabObjects>(
				"Assets/DefaultPrefabObjects.asset");
			if (manifest.SpawnablePrefabs == null)
			{
				Debug.LogError("[TestSceneGenerator] Assets/DefaultPrefabObjects.asset not found — the sim's NetworkManager will refuse to initialize.");
			}
			manifest.NetworkPrefab = BuildNetworkPrefab(manifest.SpawnablePrefabs);

			manifest.CacheAssets.Clear();
			manifest.Roster.Clear();
			manifest.ChannelMarker = null;
			manifest.ChargeMarker = null;

			foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", RosterContentFolders))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
				if (asset == null || !(asset is ICachedObject) || manifest.CacheAssets.Contains(asset))
				{
					continue;
				}
				manifest.CacheAssets.Add(asset);
				if (asset is AbilityTemplate template && path.StartsWith(RosterFolder + "/") &&
					System.Array.IndexOf(RosterExcluded, asset.name) < 0)
				{
					manifest.Roster.Add(template);
				}
			}
			// Sorted, so a regenerated manifest differs only where the content did.
			manifest.Roster.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
			manifest.CacheAssets.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));

			if (manifest.Roster.Count == 0)
			{
				Debug.LogError($"[TestSceneGenerator] No AbilityTemplates found under {RosterFolder} — " +
					"run the test ability suite generator first (FishMMO Dashboard → Maintenance → Ability Content).");
			}

			if (created)
			{
				AssetDatabase.CreateAsset(manifest, ManifestPath);
			}
			EditorUtility.SetDirty(manifest);
			AssetDatabase.SaveAssetIfDirty(manifest);
			Debug.Log($"[TestSceneGenerator] Manifest: {manifest.CacheAssets.Count} assets cached, " +
				$"{manifest.Roster.Count} roster abilities.");
			return manifest;
		}

		private const string NetworkPrefabPath = "Assets/FishMMO/Tests/TestHarness/Combat/CombatSimNetwork.prefab";

		/// <summary>
		/// Authors the sim's NetworkManager prefab: Tugboat + TimeManager (physics mode
		/// TimeManager, matching the server scenes) + NetworkManager with the spawnable prefabs
		/// collection assigned, saved INACTIVE so a runtime instance can set its port before
		/// Awake. Built here because adding NetworkManager at runtime trips its OnValidate
		/// "SpawnablePrefabs is null" error before any field can be assigned.
		/// </summary>
		private static GameObject BuildNetworkPrefab(FishNet.Managing.Object.PrefabObjects spawnablePrefabs)
		{
			GameObject temp = new GameObject("CombatSimNetwork");
			temp.SetActive(false);
			try
			{
				temp.AddComponent<FishNet.Transporting.Tugboat.Tugboat>();
				FishNet.Managing.Timing.TimeManager timeManager =
					temp.AddComponent<FishNet.Managing.Timing.TimeManager>();
				SerializedObject serializedTime = new SerializedObject(timeManager);
				serializedTime.FindProperty("_physicsMode").enumValueIndex =
					(int)FishNet.Managing.Timing.PhysicsMode.TimeManager;
				serializedTime.ApplyModifiedPropertiesWithoutUndo();

				FishNet.Managing.NetworkManager networkManager =
					temp.AddComponent<FishNet.Managing.NetworkManager>();
				SerializedObject serializedManager = new SerializedObject(networkManager);
				serializedManager.FindProperty("_spawnablePrefabs").objectReferenceValue = spawnablePrefabs;
				/* Sim servers must die with their harness. FishNet's default is
				 * DontDestroyOnLoad + destroy-duplicates, so a leaked manager from one test
				 * would make FishNet destroy the NEXT test's manager on activation. */
				serializedManager.FindProperty("_dontDestroyOnLoad").boolValue = false;
				serializedManager.ApplyModifiedPropertiesWithoutUndo();

				return PrefabUtility.SaveAsPrefabAsset(temp, NetworkPrefabPath);
			}
			finally
			{
				Object.DestroyImmediate(temp);
			}
		}

		private static void EnsureFolder()
		{
			if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
			{
				AssetDatabase.CreateFolder("Assets", "Scenes");
			}
			if (!AssetDatabase.IsValidFolder(SceneFolder))
			{
				AssetDatabase.CreateFolder("Assets/Scenes", "Test");
			}
		}

		private static void SaveScene(Scene scene, string path)
		{
			if (!EditorSceneManager.SaveScene(scene, path))
			{
				Debug.LogError($"[TestSceneGenerator] FAILED to save {path}");
				return;
			}
			Debug.Log($"[TestSceneGenerator] Wrote {path}");
		}
	}
}
