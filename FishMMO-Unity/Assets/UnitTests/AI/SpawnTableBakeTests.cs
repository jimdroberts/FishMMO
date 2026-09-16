using System.Collections.Generic;
using System.Linq;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the spawner move to the server: spawners are authoring-only, baked into server tables,
	/// and absent from everything a client build contains.
	/// </summary>
	/// <remarks>
	/// World scenes are built into one addressable bundle that clients and servers share, so the
	/// only protection is that a spawner never reaches a built scene at all — its GameObject is
	/// tagged <c>EditorOnly</c> — and that the server reads its copy from a baked table instead.
	/// </remarks>
	[TestFixture]
	public class SpawnTableBakeTests
	{
		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
		}

		private ObjectSpawner NewAuthoringSpawner(string name)
		{
			GameObject go = new GameObject(name);
			created.Add(go);
			return go.AddComponent<ObjectSpawner>();
		}

		// --- Baking ------------------------------------------------------------------------------

		[Test]
		public void ToDefinition_CopiesTheAuthoredValuesAndWhereTheSpawnerStands()
		{
			ObjectSpawner spawner = NewAuthoringSpawner("Camp");
			spawner.transform.SetPositionAndRotation(new Vector3(3f, 4f, 5f), Quaternion.Euler(0f, 90f, 0f));
			spawner.MaxSpawnCount = 7;
			spawner.InitialSpawnCount = 2;
			spawner.SpawnType = ObjectSpawnType.Weighted;
			spawner.UniqueSpawnables = true;
			spawner.BoundingBoxSize = new Vector3(10f, 2f, 10f);
			spawner.RespawnCheckIntervalMinimum = 1f;
			spawner.RespawnCheckIntervalMaximum = 2f;
			spawner.Spawnables = new List<SpawnableSettings> { new NPCSpawnableSettings { SpawnChance = 0.25f } };

			SpawnerDefinition definition = SpawnTableBaker.ToDefinition(spawner, _ => -1);

			Assert.AreEqual("Camp", definition.Name);
			Assert.AreEqual(new Vector3(3f, 4f, 5f), definition.Position);
			Assert.That(Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), definition.Rotation), Is.LessThan(0.01f));
			Assert.AreEqual(7, definition.MaxSpawnCount);
			Assert.AreEqual(2, definition.InitialSpawnCount);
			Assert.AreEqual(ObjectSpawnType.Weighted, definition.SpawnType);
			Assert.IsTrue(definition.UniqueSpawnables);
			Assert.AreEqual(new Vector3(10f, 2f, 10f), definition.BoundingBoxSize);
			Assert.AreEqual(1f, definition.RespawnCheckIntervalMinimum);
			Assert.AreEqual(2f, definition.RespawnCheckIntervalMaximum);
			Assert.AreEqual(1, definition.Spawnables.Count);
			Assert.IsInstanceOf<NPCSpawnableSettings>(definition.Spawnables[0], "the concrete settings type must survive the bake");
			Assert.AreEqual(0.25f, definition.Spawnables[0].SpawnChance);
		}

		[Test]
		public void ToDefinition_CopiesTheSettingsRatherThanSharingThem()
		{
			/* A [SerializeReference] object cannot belong to two UnityEngine.Objects, and a table
			 * that shared the scene's instance would change whenever the scene was edited. */
			ObjectSpawner spawner = NewAuthoringSpawner("Camp");
			NPCSpawnableSettings authored = new NPCSpawnableSettings { MinimumScale = 2f, MaximumScale = 3f };
			spawner.Spawnables = new List<SpawnableSettings> { authored };

			SpawnerDefinition definition = SpawnTableBaker.ToDefinition(spawner, _ => -1);

			Assert.AreNotSame(authored, definition.Spawnables[0]);
			authored.MaximumScale = 9f;
			Assert.AreEqual(3f, ((NPCSpawnableSettings)definition.Spawnables[0]).MaximumScale);
		}

		[Test]
		public void ToDefinition_KeepsEmptyEntriesSoIndicesMatchTheScene()
		{
			ObjectSpawner spawner = NewAuthoringSpawner("Camp");
			spawner.Spawnables = new List<SpawnableSettings> { null, new ItemSpawnableSettings() };

			SpawnerDefinition definition = SpawnTableBaker.ToDefinition(spawner, _ => -1);

			Assert.AreEqual(2, definition.Spawnables.Count);
			Assert.IsNull(definition.Spawnables[0]);
			Assert.IsNotNull(definition.Spawnables[1]);
		}

		[Test]
		public void ToDefinition_TurnsSpawnerReferencesIntoTableIndices()
		{
			/* A scene reference cannot live in an asset the server loads without the scene. The
			 * boss-camp condition names its guard spawner; the bake must turn that into the guard's
			 * index in the same table and drop the reference. */
			ObjectSpawner guard = NewAuthoringSpawner("Guard");
			ObjectSpawner camp = NewAuthoringSpawner("Camp");
			SpawnersClearedCondition condition = new SpawnersClearedCondition();
			condition.Spawners.Add(guard);
			condition.Spawners.Add(null);
			camp.TrueConditions.Add(condition);

			SpawnerDefinition definition = SpawnTableBaker.ToDefinition(camp, s => s == guard ? 4 : -1);

			SpawnersClearedCondition baked = definition.TrueConditions.Single() as SpawnersClearedCondition;
			Assert.IsNotNull(baked);
			CollectionAssert.AreEqual(new[] { 4 }, baked.SpawnerIndices);
			Assert.IsEmpty(baked.Spawners, "the baked copy must hold no scene references");
			Assert.AreEqual(1, condition.Spawners.Count(s => s == guard), "the authored condition must be left alone");
		}

		// --- The shipped scenes ------------------------------------------------------------------

		private static IEnumerable<string> WorldScenesWithSpawners()
		{
			foreach (string path in SpawnTableBaker.FindWorldScenePaths())
			{
				Scene scene = EditorSceneManager.OpenPreviewScene(path);
				try
				{
					if (SpawnTableBaker.CollectSpawners(scene).Count > 0)
					{
						yield return path;
					}
				}
				finally
				{
					EditorSceneManager.ClosePreviewScene(scene);
				}
			}
		}

		[Test]
		public void EveryShippedSpawner_IsEditorOnlyAndNotNetworked()
		{
			List<string> blocking = new List<string>();
			int scanned = 0;

			foreach (string path in SpawnTableBaker.FindWorldScenePaths())
			{
				Scene scene = EditorSceneManager.OpenPreviewScene(path);
				try
				{
					foreach (ObjectSpawner spawner in SpawnTableBaker.CollectSpawners(scene))
					{
						scanned++;
						SpawnTableBaker.Validate(scene, spawner, null, blocking);
					}
				}
				finally
				{
					EditorSceneManager.ClosePreviewScene(scene);
				}
			}

			Assert.Greater(scanned, 0, "no spawners found in the world scenes; the scan is broken");
			Assert.IsEmpty(blocking, string.Join("\n", blocking));
		}

		[Test]
		public void EveryShippedSpawner_SpawnsSomething()
		{
			/* An empty or prefab-less entry is skipped at runtime without a word, so a spawner that
			 * has only those stands in the scene and never spawns — which is how the Dungeon's
			 * spawners and the tutorial banker shipped until 2026-09-16. */
			List<string> empty = new List<string>();
			int scanned = 0;

			foreach (string path in SpawnTableBaker.FindWorldScenePaths())
			{
				Scene scene = EditorSceneManager.OpenPreviewScene(path);
				try
				{
					foreach (ObjectSpawner spawner in SpawnTableBaker.CollectSpawners(scene))
					{
						scanned++;
						List<string> problems = new List<string>();
						SpawnTableBaker.Validate(scene, spawner, problems);
						empty.AddRange(problems);
					}
				}
				finally
				{
					EditorSceneManager.ClosePreviewScene(scene);
				}
			}

			Assert.Greater(scanned, 0, "no spawners found in the world scenes; the scan is broken");
			Assert.IsEmpty(empty, string.Join("\n", empty));
		}

		[Test]
		public void TheDungeonChest_IsAContainerThatOpensAndHoldsLoot()
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Shared/Entity/Interactables/Containers/Dungeon Chest.prefab");
			Assert.IsNotNull(prefab, "the Dungeon chest prefab is missing");

			Container container = prefab.GetComponent<Container>();
			Assert.IsNotNull(container);
			Assert.IsNull(prefab.GetComponent<WorldItem>(), "the chest was cloned from a world item and must not still be one");
			Assert.IsNotNull(container.Template, "a container without a template refuses every interaction");
			Assert.IsNotNull(container.Template.LootTable, "a chest without a loot table spawns empty");
			Assert.IsNotEmpty(container.Template.LootTable.Entries);
			Assert.IsTrue(container.Template.LootTable.Entries.All(e => e != null && e.ItemTemplate != null));
			Assert.Greater(container.Template.SlotCount, 0);

			Assert.IsTrue(container.OnInteractTriggers.Any(t => t != null &&
					t.OnConditionsMetActions.Any(a => a is SendContainerOpenBroadcastAction)),
				"nothing would open the chest's window");

			SceneObjectNamer namer = prefab.GetComponent<SceneObjectNamer>();
			Assert.AreEqual(SceneObjectNamingMode.Authored, namer.Settings.Mode,
				"a chest has no race or biome to be named after; any other mode logs a naming failure every spawn");

			/* The client resolves the window's template by ID, which only exists for a template
			 * loaded through the shared addressable label. */
			string groups = System.IO.File.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Shared_Static_Permanent.asset");
			Assert.That(groups, Does.Contain(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(container.Template))),
				"the container template must be in the shared addressable group");
			Assert.That(groups, Does.Contain(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab))),
				"the chest prefab must be in the shared addressable group");
			Assert.IsTrue(prefab.GetComponent<FishNet.Object.NetworkObject>().GetIsSpawnable(), "a spawner can only spawn a spawnable prefab");
		}

		[Test]
		public void EverySceneWithSpawners_HasABakedTableThatMatchesIt()
		{
			/* A table that has fallen behind its scene is a world that spawns something other than
			 * what its designer sees. The bake runs on save and before builds; this catches a scene
			 * edited some other way. */
			SpawnTableCatalogue catalogue = AssetDatabase.LoadAssetAtPath<SpawnTableCatalogue>(SpawnTableBaker.CataloguePath);
			Assert.IsNotNull(catalogue, $"no spawn table catalogue at {SpawnTableBaker.CataloguePath}");
			catalogue.Invalidate();

			int checkedScenes = 0;
			foreach (string path in SpawnTableBaker.FindWorldScenePaths())
			{
				Scene scene = EditorSceneManager.OpenPreviewScene(path);
				try
				{
					List<ObjectSpawner> spawners = SpawnTableBaker.CollectSpawners(scene);
					string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
					bool hasTable = catalogue.TryGet(sceneName, out SceneSpawnTable table);

					if (spawners.Count == 0)
					{
						Assert.IsFalse(hasTable, $"{sceneName} has no spawners but still has a table");
						continue;
					}

					checkedScenes++;
					Assert.IsTrue(hasTable, $"{sceneName} has spawners but no baked table; run FishMMO/Spawners/Bake Spawn Tables");
					Assert.AreEqual(spawners.Count, table.Spawners.Count, $"{sceneName}: table is out of date");

					for (int i = 0; i < spawners.Count; ++i)
					{
						SpawnerDefinition expected = SpawnTableBaker.ToDefinition(spawners[i], _ => -1);
						SpawnerDefinition baked = table.Spawners[i];
						string where = $"{sceneName}/{spawners[i].name}";
						Assert.AreEqual(expected.Name, baked.Name, where);
						Assert.That(Vector3.Distance(expected.Position, baked.Position), Is.LessThan(0.001f), where);
						Assert.AreEqual(expected.MaxSpawnCount, baked.MaxSpawnCount, where);
						Assert.AreEqual(expected.InitialSpawnCount, baked.InitialSpawnCount, where);
						Assert.AreEqual(expected.Spawnables.Count, baked.Spawnables.Count, where);
						for (int s = 0; s < expected.Spawnables.Count; ++s)
						{
							Assert.AreEqual(expected.Spawnables[s]?.GetType(), baked.Spawnables[s]?.GetType(), $"{where} entry {s}");
							Assert.AreEqual(expected.Spawnables[s]?.NetworkObject, baked.Spawnables[s]?.NetworkObject, $"{where} entry {s}");
						}
					}
				}
				finally
				{
					EditorSceneManager.ClosePreviewScene(scene);
				}
			}

			Assert.Greater(checkedScenes, 0, "no world scene has spawners; the check is vacuous");
		}

		[Test]
		public void ABuiltWorldScene_ReferencesNoSpawnerAndNoSpawnerData()
		{
			/* The dependency calculation the scriptable build pipeline runs for every scene bundle.
			 * It processes the scene as a player would load it, EditorOnly objects removed, so what
			 * it reports is what a client downloads. The spawner's script, and the prefabs only a
			 * spawner names, must not be in it. */
			string spawnerScript = AssetDatabase.AssetPathToGUID(
				"Assets/Scripts/Server/Implementation/World/SceneServer/Spawner/ObjectSpawner.cs");
			Assert.IsNotEmpty(spawnerScript);

			BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
			BuildSettings settings = new BuildSettings
			{
				target = target,
				group = BuildPipeline.GetBuildTargetGroup(target),
			};

			int checkedScenes = 0;
			try
			{
				foreach (string path in WorldScenesWithSpawners().ToList())
				{
					SceneDependencyInfo info = ContentBuildInterface.CalculatePlayerDependenciesForScene(path, settings, new BuildUsageTagSet());
					HashSet<string> referenced = new HashSet<string>(info.referencedObjects.Select(o => o.guid.ToString()));

					Assert.IsFalse(referenced.Contains(spawnerScript),
						$"{path}: a built scene still carries the spawner component");
					checkedScenes++;
				}
			}
			finally
			{
				/* The calculation leaves the last scene it processed open as the active scene. Every
				 * later fixture would then build its rig on top of that scene's terrain and colliders,
				 * so the empty scene the test runner started with is put back. */
				EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			}

			Assert.Greater(checkedScenes, 0, "no world scene has spawners; the check is vacuous");
		}

		[Test]
		public void TheDependencyCheck_WouldSeeASpawnerThatReachedTheBuild()
		{
			/* The control for the test above: an absence check is only worth something if the same
			 * calculation reports the spawner when it IS there. An untagged spawner is reported;
			 * the same spawner tagged EditorOnly is not. */
			const string folder = "Assets/UnitTests/Generated";
			const string path = folder + "/SpawnerStripControl.unity";
			bool folderExisted = AssetDatabase.IsValidFolder(folder);
			if (!folderExisted)
			{
				AssetDatabase.CreateFolder("Assets/UnitTests", "Generated");
			}

			string spawnerScript = AssetDatabase.AssetPathToGUID(
				"Assets/Scripts/Server/Implementation/World/SceneServer/Spawner/ObjectSpawner.cs");
			BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
			BuildSettings settings = new BuildSettings { target = target, group = BuildPipeline.GetBuildTargetGroup(target) };

			try
			{
				Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
				GameObject go = new GameObject("ControlSpawner");
				go.AddComponent<ObjectSpawner>();
				go.tag = "Untagged";
				EditorSceneManager.SaveScene(scene, path);

				bool seenUntagged = ContentBuildInterface.CalculatePlayerDependenciesForScene(path, settings, new BuildUsageTagSet())
					.referencedObjects.Any(o => o.guid.ToString() == spawnerScript);

				scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
				GameObject reopened = scene.GetRootGameObjects().Single(g => g.name == "ControlSpawner");
				reopened.tag = ObjectSpawner.EditorOnlyTag;
				EditorSceneManager.SaveScene(scene);

				bool seenTagged = ContentBuildInterface.CalculatePlayerDependenciesForScene(path, settings, new BuildUsageTagSet())
					.referencedObjects.Any(o => o.guid.ToString() == spawnerScript);

				Assert.IsTrue(seenUntagged, "the dependency calculation must report a spawner that is not stripped, or the absence check proves nothing");
				Assert.IsFalse(seenTagged, "an EditorOnly spawner must not reach a built scene");
			}
			finally
			{
				EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
				AssetDatabase.DeleteAsset(path);
				if (!folderExisted)
				{
					AssetDatabase.DeleteAsset(folder);
				}
			}
		}

		// --- Build stripping ---------------------------------------------------------------------

		[Test]
		public void TheBuildStripper_RemovesSpawnersFromAProcessedScene()
		{
			Scene scene = EditorSceneManager.NewPreviewScene();
			try
			{
				GameObject tagged = new GameObject("Tagged");
				tagged.AddComponent<ObjectSpawner>();
				tagged.tag = ObjectSpawner.EditorOnlyTag;
				SceneManager.MoveGameObjectToScene(tagged, scene);

				GameObject untagged = new GameObject("Untagged");
				untagged.AddComponent<ObjectSpawner>();
				untagged.tag = "Untagged";
				SceneManager.MoveGameObjectToScene(untagged, scene);

				UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not tagged EditorOnly"));
				new SpawnerBuildStripper().OnProcessScene(scene, null);

				Assert.IsEmpty(SpawnTableBaker.CollectSpawners(scene), "every spawner component must be gone from a built scene");
			}
			finally
			{
				EditorSceneManager.ClosePreviewScene(scene);
			}
		}

		[Test]
		public void ANewSpawner_TagsItselfEditorOnly()
		{
			GameObject go = new GameObject("Fresh");
			created.Add(go);
			ObjectSpawner spawner = go.AddComponent<ObjectSpawner>();

			// Reset is what the editor runs when the component is added from the inspector.
			typeof(ObjectSpawner).GetMethod("Reset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.Invoke(spawner, null);

			Assert.IsTrue(go.CompareTag(ObjectSpawner.EditorOnlyTag));
		}
	}
}
