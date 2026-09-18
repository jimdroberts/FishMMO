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
					Assert.IsTrue(hasTable, $"{sceneName} has spawners but no baked table; run FishMMO Dashboard → World → Spawn Tables → Rebuild Spawn Tables");
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

		/// <summary>
		/// The GUIDs of every prefab the spawners of <paramref name="scenePath"/> can produce.
		/// </summary>
		/// <remarks>
		/// These are what a leaking spawner would drag into a client's scene bundle: the prefab, and
		/// through it the NPC's AI archetype and state templates. Nothing else in a world scene names
		/// them, so their absence from the built scene is the measurable form of "the spawner is gone".
		/// </remarks>
		private static HashSet<string> SpawnedPrefabGuids(string scenePath)
		{
			HashSet<string> guids = new HashSet<string>();
			Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);
			try
			{
				foreach (ObjectSpawner spawner in SpawnTableBaker.CollectSpawners(scene))
				{
					if (spawner.Spawnables == null)
					{
						continue;
					}

					foreach (SpawnableSettings settings in spawner.Spawnables)
					{
						if (settings?.NetworkObject == null)
						{
							continue;
						}

						string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(settings.NetworkObject));
						if (!string.IsNullOrEmpty(guid))
						{
							guids.Add(guid);
						}
					}
				}
			}
			finally
			{
				EditorSceneManager.ClosePreviewScene(scene);
			}
			return guids;
		}

		/// <summary>
		/// Every GUID a client would receive in <paramref name="scenePath"/>'s bundle.
		/// </summary>
		/// <remarks>
		/// This is the dependency calculation the scriptable build pipeline runs for each scene
		/// bundle: it processes the scene as a player loads it, with EditorOnly objects removed. It
		/// leaves the scene it processed open as the active scene, so callers put an empty one back.
		/// </remarks>
		private static HashSet<string> BuiltSceneDependencies(string scenePath)
		{
			BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
			BuildSettings settings = new BuildSettings
			{
				target = target,
				group = BuildPipeline.GetBuildTargetGroup(target),
			};

			SceneDependencyInfo info = ContentBuildInterface.CalculatePlayerDependenciesForScene(scenePath, settings, new BuildUsageTagSet());
			return new HashSet<string>(info.referencedObjects.Select(o => o.guid.ToString()));
		}

		[Test]
		public void ABuiltWorldScene_ReferencesNoSpawnerDataAndNoServerScript()
		{
			/* What a client downloads for a world scene. Two things must be absent: the prefabs only
			 * a spawner names, and any script from the server assembly — the second catches an NPC
			 * prefab, AI archetype or state template reaching a client bundle through some other
			 * reference, since a referenced asset brings its components' scripts with it.
			 *
			 * Note what this canNOT see, and why the check is shaped this way: the calculation does
			 * not report the script of a component serialized directly into the scene, only the
			 * scripts of assets the scene references. Asserting on ObjectSpawner.cs therefore always
			 * passed no matter what the scene held, which is how this guard sat green while proving
			 * nothing. TheDependencyCheck_WouldSeeSpawnerDataThatReachedTheBuild is the control. */
			const string serverScripts = "Assets/Scripts/Server/";

			int checkedScenes = 0;
			try
			{
				foreach (string path in WorldScenesWithSpawners().ToList())
				{
					HashSet<string> spawned = SpawnedPrefabGuids(path);
					Assert.IsNotEmpty(spawned, $"{path}: its spawners name no prefab, so the check is vacuous");

					HashSet<string> referenced = BuiltSceneDependencies(path);

					List<string> leaked = spawned.Where(referenced.Contains)
						.Select(AssetDatabase.GUIDToAssetPath)
						.ToList();
					Assert.IsEmpty(leaked,
						$"{path}: a client bundle carries prefabs only a spawner names — {string.Join(", ", leaked)}");

					List<string> serverSide = referenced.Select(AssetDatabase.GUIDToAssetPath)
						.Where(p => p.StartsWith(serverScripts, System.StringComparison.Ordinal))
						.ToList();
					Assert.IsEmpty(serverSide,
						$"{path}: a client bundle carries server-assembly scripts — {string.Join(", ", serverSide)}");

					checkedScenes++;
				}
			}
			finally
			{
				/* Every later fixture would otherwise build its rig on the last processed scene's
				 * terrain and colliders. */
				EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			}

			Assert.Greater(checkedScenes, 0, "no world scene has spawners; the check is vacuous");
		}

		/// <summary>
		/// Writes <paramref name="source"/> to <paramref name="destination"/> with every spawner's
		/// GameObject untagged, and returns how many were untagged.
		/// </summary>
		/// <remarks>
		/// The scene text is rewritten rather than edited through the editor, because
		/// <c>SpawnerTagEnforcer</c> re-tags every spawner as its scene is saved — there is no way to
		/// save an untagged one. A copy of a real scene is used rather than a scene built from
		/// nothing: the dependency calculation reports only scene defaults for a scene whose contents
		/// are a bare component, which is the shape that made the previous control fail and left the
		/// guard above unproven.
		/// </remarks>
		private static int WriteSceneWithSpawnersUntagged(string source, string destination)
		{
			string spawnerScript = AssetDatabase.AssetPathToGUID(
				"Assets/Scripts/Server/Implementation/World/SceneServer/Spawner/ObjectSpawner.cs");
			Assert.IsNotEmpty(spawnerScript, "the spawner script has moved; this control cannot find it");

			string text = System.IO.File.ReadAllText(source);

			/* Each YAML document starts "--- !u!<classId> &<fileId>". Find the GameObject ids the
			 * spawner components belong to, then retag those GameObjects' documents. */
			string[] documents = System.Text.RegularExpressions.Regex.Split(text, @"(?m)^(?=--- !u!)");

			HashSet<string> spawnerOwners = new HashSet<string>();
			foreach (string document in documents)
			{
				if (!document.StartsWith("--- !u!114 ", System.StringComparison.Ordinal) ||
					!document.Contains("guid: " + spawnerScript))
				{
					continue;
				}

				System.Text.RegularExpressions.Match owner =
					System.Text.RegularExpressions.Regex.Match(document, @"m_GameObject: \{fileID: (\d+)\}");
				if (owner.Success)
				{
					spawnerOwners.Add(owner.Groups[1].Value);
				}
			}

			int untagged = 0;
			for (int i = 0; i < documents.Length; ++i)
			{
				System.Text.RegularExpressions.Match header =
					System.Text.RegularExpressions.Regex.Match(documents[i], @"^--- !u!1 &(\d+)");
				if (!header.Success || !spawnerOwners.Contains(header.Groups[1].Value))
				{
					continue;
				}

				string retagged = System.Text.RegularExpressions.Regex.Replace(
					documents[i], @"(?m)^(\s*m_TagString: ).*$", "${1}Untagged");
				if (retagged != documents[i])
				{
					documents[i] = retagged;
					untagged++;
				}
			}

			System.IO.File.WriteAllText(destination, string.Concat(documents));
			AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
			return untagged;
		}

		[Test]
		public void TheDependencyCheck_WouldSeeSpawnerDataThatReachedTheBuild()
		{
			/* The control for the test above: an absence check is worth nothing unless the same
			 * calculation reports the data when it IS there. The same scene, differing only in the
			 * tag, is measured both ways. */
			string source = WorldScenesWithSpawners().FirstOrDefault();
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			Assert.IsNotNull(source, "no world scene has spawners; the control is vacuous");

			HashSet<string> spawned = SpawnedPrefabGuids(source);
			Assert.IsNotEmpty(spawned, $"{source}: its spawners name no prefab");

			/* Outside the world scene folder: a save there re-bakes a spawn table and would leave a
			 * stray entry in the catalogue. */
			const string folder = "Assets/UnitTests/Generated";
			const string copy = folder + "/SpawnerStripControl.unity";
			bool folderExisted = AssetDatabase.IsValidFolder(folder);
			if (!folderExisted)
			{
				AssetDatabase.CreateFolder("Assets/UnitTests", "Generated");
			}

			try
			{
				int untaggedCount = WriteSceneWithSpawnersUntagged(source, copy);
				Assert.Greater(untaggedCount, 0, $"{source}: no spawner GameObject was untagged; the control is vacuous");

				HashSet<string> untagged = BuiltSceneDependencies(copy);
				EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

				Assert.IsNotEmpty(spawned.Where(untagged.Contains).ToList(),
					"the dependency calculation must report the prefabs of a spawner that is not stripped, or the absence check proves nothing");

				/* The same bytes with the tag left alone report none of them. */
				System.IO.File.Copy(source, copy, true);
				AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);

				HashSet<string> tagged = BuiltSceneDependencies(copy);
				Assert.IsEmpty(spawned.Where(tagged.Contains).Select(AssetDatabase.GUIDToAssetPath).ToList(),
					"an EditorOnly spawner must take its prefabs out of the built scene");
			}
			finally
			{
				EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
				AssetDatabase.DeleteAsset(copy);
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
		public void ABakedTable_KeepsItsReferenceIdsAcrossRebakes()
		{
			/* Random [SerializeReference] ids made every build rewrite every table with nothing
			 * else changed. The bake numbers them in table order and copies them onto the asset. */
			SceneSpawnTable Stage()
			{
				SceneSpawnTable staged = ScriptableObject.CreateInstance<SceneSpawnTable>();
				staged.Spawners = new List<SpawnerDefinition>
				{
					new SpawnerDefinition { Spawnables = new List<SpawnableSettings> { new NPCSpawnableSettings(), null, new ItemSpawnableSettings() } },
					null,
					new SpawnerDefinition
					{
						Spawnables = new List<SpawnableSettings> { new SpawnableSettings() },
						OrConditions = new List<RespawnCondition> { new SpawnersClearedCondition() },
					},
				};
				Assert.IsTrue(SpawnTableBaker.AssignStableReferenceIds(staged));
				return staged;
			}

			SceneSpawnTable asset = ScriptableObject.CreateInstance<SceneSpawnTable>();
			SceneSpawnTable first = Stage();
			SceneSpawnTable second = Stage();
			try
			{
				EditorUtility.CopySerialized(first, asset);
				string once = EditorJsonUtility.ToJson(asset);
				EditorUtility.CopySerialized(second, asset);
				Assert.AreEqual(once, EditorJsonUtility.ToJson(asset), "an unchanged rebake must serialize identically");

				long Id(object managed) => UnityEngine.Serialization.ManagedReferenceUtility.GetManagedReferenceIdForObject(asset, managed);
				Assert.AreEqual(1, Id(asset.Spawners[0].Spawnables[0]));
				Assert.AreEqual(2, Id(asset.Spawners[0].Spawnables[2]));
				Assert.AreEqual(3, Id(asset.Spawners[2].Spawnables[0]));
				Assert.AreEqual(4, Id(asset.Spawners[2].OrConditions[0]));
			}
			finally
			{
				Object.DestroyImmediate(first);
				Object.DestroyImmediate(second);
				Object.DestroyImmediate(asset);
			}
		}

		[Test]
		public void ServerDataAssets_AreAddressableInTheServerGroupOnly()
		{
			/* The server loads its data by the Server_Static_Permanent label, and client builds drop
			 * every group named "Server". An entry anywhere else would ship to players. */
			List<string> paths = new List<string>();
			foreach (string type in new[] { nameof(SceneSpawnTable), nameof(SpawnTableCatalogue), "AIBrainCatalogue" })
			{
				paths.AddRange(AssetDatabase.FindAssets("t:" + type, new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath));
			}
			Assert.IsNotEmpty(paths);

			string[] groupFiles = System.IO.Directory.GetFiles("Assets/AddressableAssetsData/AssetGroups", "*.asset");
			string serverGroup = System.IO.File.ReadAllText("Assets/AddressableAssetsData/AssetGroups/" + ServerAddressables.GroupName + ".asset");
			foreach (string path in paths)
			{
				string guid = AssetDatabase.AssetPathToGUID(path);
				Assert.That(serverGroup, Does.Contain("m_GUID: " + guid), $"{path} must be in {ServerAddressables.GroupName}");
				foreach (string file in groupFiles)
				{
					if (System.IO.Path.GetFileName(file).IndexOf("Server", System.StringComparison.OrdinalIgnoreCase) >= 0)
					{
						continue;
					}
					Assert.That(System.IO.File.ReadAllText(file), Does.Not.Contain("m_GUID: " + guid), $"{path} is in {file}, which clients build");
				}
			}
		}

		[Test]
		public void EveryBuildPath_BakesSpawnTablesBeforeItsAddressables()
		{
			/* The dashboard's Build Game and the Build Addressables button build bundles through
			 * different methods; a server built by one without the bake runs yesterday's spawners. */
			string source = System.IO.File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/FishMMO Dashboard/CustomBuildTool/Core/CustomBuildTool.cs");
			const string build = "addressableManager.BuildAddressablesWithExclusions(";
			const string bake = "BakeSpawnTables();";

			int calls = 0;
			int from = 0;
			for (int at = source.IndexOf(build, System.StringComparison.Ordinal); at >= 0; at = source.IndexOf(build, at + build.Length, System.StringComparison.Ordinal))
			{
				calls++;
				string before = source.Substring(from, at - from);
				Assert.That(before, Does.Contain(bake), $"addressables build #{calls} is not preceded by a spawn table bake");
				from = at + build.Length;
			}
			Assert.GreaterOrEqual(calls, 2, "expected both the game build and the addressables-only build");
		}

		[Test]
		public void AnUntaggedSpawner_IsRetaggedBeforeItsSceneIsSaved()
		{
			/* OnValidate does not run when someone changes the GameObject's tag afterwards; the
			 * scene-saving hook is what keeps an untagged spawner out of every saved scene. */
			Scene scene = EditorSceneManager.NewPreviewScene();
			try
			{
				GameObject go = new GameObject("Untagged");
				go.AddComponent<ObjectSpawner>();
				go.tag = "Untagged";
				SceneManager.MoveGameObjectToScene(go, scene);

				System.Type enforcer = typeof(SpawnTableBaker).Assembly.GetType("FishMMO.Shared.SpawnerTagEnforcer");
				Assert.IsNotNull(enforcer, "the save hook is gone");
				enforcer.GetMethod("OnSceneSaving", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
					.Invoke(null, new object[] { scene, "Assets/Untagged.unity" });

				Assert.IsTrue(go.CompareTag(ObjectSpawner.EditorOnlyTag));
				Assert.IsFalse(ObjectSpawner.EnforceEditorOnlyTag(go.GetComponent<ObjectSpawner>()), "an already tagged spawner is left alone");
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
