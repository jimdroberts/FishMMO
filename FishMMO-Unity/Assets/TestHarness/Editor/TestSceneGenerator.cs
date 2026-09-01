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
	/// safe. Cleanup for the entire harness is: delete <c>Assets/TestHarness/</c> and
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
		[MenuItem("FishMMO/Test Scenes/Generate All")]
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
		[MenuItem("FishMMO/Test Scenes/Generate Interactable Sim")]
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
		[MenuItem("FishMMO/Test Scenes/Generate Region Sim")]
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
		[MenuItem("FishMMO/Test Scenes/Generate Platform Sim")]
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
		/// refreshing its manifest of mock content (which is deliberately not addressable-registered,
		/// so the bootstrap needs direct references).</summary>
		[MenuItem("FishMMO/Test Scenes/Generate Combat Sim")]
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

		private const string ManifestPath = "Assets/TestHarness/Combat/CombatSimManifest.asset";
		private const string NpcPrefabPath =
			"Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc warrior.prefab";

		/// <summary>Scans <c>Assets/Templates</c> for mock content and (re)writes the manifest
		/// asset the CombatSim bootstrap reads at runtime.</summary>
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

			foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/Templates" }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.Contains("/Mock"))
				{
					continue;
				}
				ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
				if (asset == null || !(asset is ICachedObject))
				{
					continue;
				}
				manifest.CacheAssets.Add(asset);
				if (asset is AbilityTemplate template)
				{
					manifest.Roster.Add(template);
				}
				if (asset is AbilityEvent abilityEvent)
				{
					if (asset.name == "Mock Channel Marker Event")
					{
						manifest.ChannelMarker = abilityEvent;
					}
					else if (asset.name == "Mock Charge Marker Event")
					{
						manifest.ChargeMarker = abilityEvent;
					}
				}
			}

			if (manifest.Roster.Count == 0)
			{
				Debug.LogError("[TestSceneGenerator] No mock AbilityTemplates found under Assets/Templates — " +
					"run the mock generator first (FishMMO → Mock Content).");
			}
			if (manifest.ChannelMarker == null || manifest.ChargeMarker == null)
			{
				Debug.LogWarning("[TestSceneGenerator] Channel/Charge marker events not found — channelled and " +
					"charged mocks will degrade to instant casts in the sim.");
			}

			if (created)
			{
				AssetDatabase.CreateAsset(manifest, ManifestPath);
			}
			EditorUtility.SetDirty(manifest);
			AssetDatabase.SaveAssetIfDirty(manifest);
			Debug.Log($"[TestSceneGenerator] Manifest: {manifest.CacheAssets.Count} mock assets cached, " +
				$"{manifest.Roster.Count} roster abilities, markers " +
				$"{(manifest.ChannelMarker != null ? "ok" : "MISSING")}/{(manifest.ChargeMarker != null ? "ok" : "MISSING")}.");
			return manifest;
		}

		private const string NetworkPrefabPath = "Assets/TestHarness/Combat/CombatSimNetwork.prefab";

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
