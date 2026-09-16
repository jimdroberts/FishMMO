using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace FishMMO.Shared
{
	/// <summary>
	/// Bakes the <see cref="ObjectSpawner"/> components of every world scene into server-only
	/// <see cref="SceneSpawnTable"/> assets.
	/// </summary>
	/// <remarks>
	/// <para>
	/// World scenes are built into one bundle that clients and servers share, so anything left in
	/// a scene reaches every player. Spawners are therefore authoring-only: tagged
	/// <c>EditorOnly</c> (stripped from every build) and copied out here into tables that only the
	/// scene server loads, through the <see cref="SpawnTableCatalogue"/> its
	/// <see cref="SpawnerSystem"/> references.
	/// </para>
	/// <para>
	/// Runs from the FishMMO Dashboard (World → Spawn Tables), whenever a world scene is saved,
	/// and before every addressables build, so a table cannot fall behind its scene in anything
	/// that ships. The tables are generated but checked in, like the world scene details cache.
	/// </para>
	/// </remarks>
	public static class SpawnTableBaker
	{

		/// <summary>
		/// Where the tables and their catalogue are written.
		/// </summary>
		public const string TableFolder = "Assets/Prefabs/Server/SceneServer/SpawnTables";

		/// <summary>
		/// The catalogue the scene server's <see cref="SpawnerSystem"/> references.
		/// </summary>
		public const string CataloguePath = TableFolder + "/SpawnTableCatalogue.asset";

		/// <summary>
		/// Every world scene path the bake covers.
		/// </summary>
		/// <remarks>
		/// The same set the world scene details cache reads: the world scene folder, plus the local
		/// scene folder when the editor preference enables it.
		/// </remarks>
		public static List<string> FindWorldScenePaths()
		{
			List<string> folders = new List<string> { Constants.Configuration.WorldScenePath.TrimEnd('/') };
			if (EditorPrefs.GetBool("FishMMOEnableLocalDirectory"))
			{
				string local = Constants.Configuration.LocalScenePath.TrimEnd('/');
				if (AssetDatabase.IsValidFolder(local))
				{
					folders.Add(local);
				}
			}

			List<string> paths = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:Scene", folders.ToArray()))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) && !paths.Contains(path))
				{
					paths.Add(path);
				}
			}
			paths.Sort(StringComparer.Ordinal);
			return paths;
		}

		/// <summary>
		/// True when <paramref name="scenePath"/> is a world scene the bake covers.
		/// </summary>
		public static bool IsWorldScenePath(string scenePath)
		{
			if (string.IsNullOrEmpty(scenePath))
			{
				return false;
			}
			string normalized = scenePath.Replace('\\', '/');
			return normalized.StartsWith(Constants.Configuration.WorldScenePath.TrimEnd('/') + "/", StringComparison.Ordinal) ||
				(EditorPrefs.GetBool("FishMMOEnableLocalDirectory") &&
				 normalized.StartsWith(Constants.Configuration.LocalScenePath.TrimEnd('/') + "/", StringComparison.Ordinal));
		}

		/// <summary>
		/// Bakes every world scene, opening each one that is not already open.
		/// </summary>
		/// <param name="problems">Receives one line per problem found.</param>
		/// <param name="blocking">Receives the problems that would ship spawner data to clients; may be null.</param>
		/// <returns>The total number of spawners baked.</returns>
		public static int BakeAll(List<string> problems, List<string> blocking = null)
		{
			int total = 0;
			List<string> bakedScenes = new List<string>();

			foreach (string path in FindWorldScenePaths())
			{
				Scene scene = SceneManager.GetSceneByPath(path);
				bool opened = false;
				if (!scene.IsValid() || !scene.isLoaded)
				{
					scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
					opened = true;
				}

				try
				{
					SceneSpawnTable table = BakeScene(scene, problems, blocking);
					if (table != null)
					{
						total += table.Spawners.Count;
					}
					bakedScenes.Add(scene.name);
				}
				finally
				{
					if (opened)
					{
						EditorSceneManager.CloseScene(scene, true);
					}
				}
			}

			PruneCatalogue(bakedScenes);
			AssetDatabase.SaveAssets();
			return total;
		}

		/// <summary>
		/// Bakes one open scene. A scene with no spawners loses its table.
		/// </summary>
		/// <param name="scene">An open world scene.</param>
		/// <param name="problems">Receives one line per problem found; may be null.</param>
		/// <param name="blocking">Receives the problems that would ship spawner data to clients; may be null.</param>
		/// <returns>The table, or null when the scene has no spawners.</returns>
		public static SceneSpawnTable BakeScene(Scene scene, List<string> problems, List<string> blocking = null)
		{
			List<ObjectSpawner> spawners = CollectSpawners(scene);
			string tablePath = TablePathFor(scene.name);
			SceneSpawnTable table = AssetDatabase.LoadAssetAtPath<SceneSpawnTable>(tablePath);

			if (spawners.Count == 0)
			{
				if (table != null)
				{
					RemoveFromCatalogue(table);
					AssetDatabase.DeleteAsset(tablePath);
				}
				return null;
			}

			Dictionary<ObjectSpawner, int> indices = new Dictionary<ObjectSpawner, int>(spawners.Count);
			for (int i = 0; i < spawners.Count; ++i)
			{
				indices[spawners[i]] = i;
			}
			int ResolveIndex(ObjectSpawner spawner) => spawner != null && indices.TryGetValue(spawner, out int index) ? index : -1;

			List<SpawnerDefinition> definitions = new List<SpawnerDefinition>(spawners.Count);
			for (int i = 0; i < spawners.Count; ++i)
			{
				ObjectSpawner spawner = spawners[i];
				Validate(scene, spawner, problems, blocking);
				definitions.Add(ToDefinition(spawner, ResolveIndex));
			}

			if (table == null)
			{
				EnsureFolder(TableFolder);
				table = ScriptableObject.CreateInstance<SceneSpawnTable>();
				table.name = Path.GetFileNameWithoutExtension(tablePath);
				AssetDatabase.CreateAsset(table, tablePath);
			}

			/* Staged on a fresh instance so the reference ids can be chosen: the previous bake's
			 * objects still hold ids in the table's own registry. CopySerialized then carries the
			 * ids over, so rebaking an unchanged scene rewrites the table byte for byte — without
			 * this every build left every table modified with nothing but new random rids. */
			SceneSpawnTable staged = ScriptableObject.CreateInstance<SceneSpawnTable>();
			try
			{
				staged.name = table.name;
				staged.SceneName = scene.name;
				staged.ScenePath = scene.path;
				staged.Spawners = definitions;
				AssignStableReferenceIds(staged);
				EditorUtility.CopySerialized(staged, table);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(staged);
			}
			EditorUtility.SetDirty(table);

			// Every bake, not only on creation: a table that predates the rule is fixed by the next build.
			ServerAddressables.Register(tablePath);
			AddToCatalogue(table);
			AssetDatabase.SaveAssetIfDirty(table);
			return table;
		}

		/// <summary>
		/// Copies one authoring spawner into a definition, detached from the scene.
		/// </summary>
		/// <param name="spawner">The authoring component.</param>
		/// <param name="resolveSpawnerIndex">Maps a sibling spawner to its table index.</param>
		/// <returns>The definition.</returns>
		public static SpawnerDefinition ToDefinition(ObjectSpawner spawner, Func<ObjectSpawner, int> resolveSpawnerIndex)
		{
			Transform transform = spawner.transform;
			SpawnerDefinition definition = new SpawnerDefinition
			{
				Name = spawner.gameObject.name,
				Position = transform.position,
				Rotation = transform.rotation,
				InitialRespawnTime = spawner.InitialRespawnTime,
				InitialSpawnCount = spawner.InitialSpawnCount,
				MaxSpawnCount = spawner.MaxSpawnCount,
				UniqueSpawnables = spawner.UniqueSpawnables,
				SpawnType = spawner.SpawnType,
				PrewarmPool = spawner.PrewarmPool,
				PrewarmHeadroom = spawner.PrewarmHeadroom,
				RandomRespawnTime = spawner.RandomRespawnTime,
				RespawnCheckIntervalMinimum = spawner.RespawnCheckIntervalMinimum,
				RespawnCheckIntervalMaximum = spawner.RespawnCheckIntervalMaximum,
				RandomSpawnPosition = spawner.RandomSpawnPosition,
				SphereRadius = spawner.SphereRadius,
				BoundingBoxSize = spawner.BoundingBoxSize,
			};

			/* Copies, never the scene's own instances. A [SerializeReference] object cannot be
			 * shared between two UnityEngine.Objects, and the table must not change when the scene
			 * is edited afterwards. Empty entries are kept so indices match the authored list. */
			definition.Spawnables = new List<SpawnableSettings>();
			if (spawner.Spawnables != null)
			{
				for (int i = 0; i < spawner.Spawnables.Count; ++i)
				{
					SpawnableSettings copy = CloneManaged(spawner.Spawnables[i]);
					// Computes YOffset from the prefab's collider and drops an unspawnable prefab.
					copy?.OnValidate();
					definition.Spawnables.Add(copy);
				}
			}

			definition.OrConditions = CloneConditions(spawner.OrConditions, resolveSpawnerIndex);
			definition.TrueConditions = CloneConditions(spawner.TrueConditions, resolveSpawnerIndex);
			return definition;
		}

		/// <summary>
		/// Numbers a table's <c>[SerializeReference]</c> objects 1, 2, 3… in table order, so the
		/// same content always serializes to the same text.
		/// </summary>
		/// <param name="table">A table whose objects have not been serialized yet.</param>
		/// <returns>False if Unity refused an id; that table falls back to random ids.</returns>
		public static bool AssignStableReferenceIds(SceneSpawnTable table)
		{
			long next = 1;
			bool all = true;

			void Assign<T>(List<T> list) where T : class
			{
				if (list == null)
				{
					return;
				}
				for (int i = 0; i < list.Count; ++i)
				{
					if (list[i] != null)
					{
						all &= ManagedReferenceUtility.SetManagedReferenceIdForObject(table, list[i], next++);
					}
				}
			}

			if (table.Spawners != null)
			{
				for (int i = 0; i < table.Spawners.Count; ++i)
				{
					SpawnerDefinition definition = table.Spawners[i];
					if (definition == null)
					{
						continue;
					}
					Assign(definition.Spawnables);
					Assign(definition.OrConditions);
					Assign(definition.TrueConditions);
				}
			}

			if (!all)
			{
				Debug.LogWarning($"[SpawnTableBaker] {table.name}: could not assign stable reference ids; the table will diff on every bake.");
			}
			return all;
		}

		/// <summary>
		/// The spawners of one scene, in hierarchy order — the order the table keeps.
		/// </summary>
		public static List<ObjectSpawner> CollectSpawners(Scene scene)
		{
			List<ObjectSpawner> spawners = new List<ObjectSpawner>();
			if (!scene.IsValid() || !scene.isLoaded)
			{
				return spawners;
			}

			GameObject[] roots = scene.GetRootGameObjects();
			for (int i = 0; i < roots.Length; ++i)
			{
				spawners.AddRange(roots[i].GetComponentsInChildren<ObjectSpawner>(true));
			}
			return spawners;
		}

		/// <summary>
		/// The spawners of an open scene that would reach a build.
		/// </summary>
		/// <remarks>
		/// A spawner not tagged <c>EditorOnly</c> — or still carrying a <see cref="NetworkObject"/>,
		/// which FishNet would spawn for nothing — ships its configuration to every client. The build
		/// refuses to run while any exist.
		/// </remarks>
		public static void Validate(Scene scene, ObjectSpawner spawner, List<string> problems, List<string> blocking = null)
		{
			if (spawner == null)
			{
				return;
			}

			string where = $"{scene.name}/{spawner.gameObject.name}";
			if (!spawner.CompareTag(ObjectSpawner.EditorOnlyTag))
			{
				Report(problems, blocking, $"{where} is not tagged {ObjectSpawner.EditorOnlyTag}; it would ship to clients. Run FishMMO Dashboard → World → Spawn Tables → Migrate Scene Spawners.");
			}
			if (spawner.GetComponent<NetworkObject>() != null)
			{
				Report(problems, blocking, $"{where} still has a NetworkObject; spawners are not networked any more. Run FishMMO Dashboard → World → Spawn Tables → Migrate Scene Spawners.");
			}
			if (spawner.Spawnables == null || spawner.Spawnables.Count == 0)
			{
				problems?.Add($"{where} has nothing to spawn.");
				return;
			}
			for (int i = 0; i < spawner.Spawnables.Count; ++i)
			{
				SpawnableSettings settings = spawner.Spawnables[i];
				if (settings == null)
				{
					problems?.Add($"{where} has an empty spawnable entry at {i}; it is skipped at runtime.");
				}
				else if (settings.NetworkObject == null)
				{
					problems?.Add($"{where} has a spawnable entry at {i} with no prefab; it is skipped at runtime.");
				}
			}
		}

		private static void Report(List<string> problems, List<string> blocking, string line)
		{
			problems?.Add(line);
			blocking?.Add(line);
		}

		/// <summary>
		/// The table asset path for a scene.
		/// </summary>
		public static string TablePathFor(string sceneName)
		{
			return $"{TableFolder}/{sceneName}.asset";
		}

		/// <summary>
		/// The project's spawn table catalogue, created at <see cref="CataloguePath"/> when missing.
		/// </summary>
		public static SpawnTableCatalogue GetOrCreateCatalogue()
		{
			SpawnTableCatalogue catalogue = AssetDatabase.LoadAssetAtPath<SpawnTableCatalogue>(CataloguePath);
			if (catalogue == null)
			{
				EnsureFolder(TableFolder);
				catalogue = ScriptableObject.CreateInstance<SpawnTableCatalogue>();
				AssetDatabase.CreateAsset(catalogue, CataloguePath);
			}
			ServerAddressables.Register(CataloguePath);
			return catalogue;
		}

		private static void AddToCatalogue(SceneSpawnTable table)
		{
			SpawnTableCatalogue catalogue = GetOrCreateCatalogue();
			if (!catalogue.Tables.Contains(table))
			{
				catalogue.Tables.Add(table);
			}
			SortAndSave(catalogue);
		}

		private static void RemoveFromCatalogue(SceneSpawnTable table)
		{
			SpawnTableCatalogue catalogue = AssetDatabase.LoadAssetAtPath<SpawnTableCatalogue>(CataloguePath);
			if (catalogue != null && catalogue.Tables.Remove(table))
			{
				SortAndSave(catalogue);
			}
		}

		/// <summary>
		/// Drops tables for scenes that no longer exist or were not part of this bake.
		/// </summary>
		private static void PruneCatalogue(List<string> bakedScenes)
		{
			SpawnTableCatalogue catalogue = AssetDatabase.LoadAssetAtPath<SpawnTableCatalogue>(CataloguePath);
			if (catalogue == null)
			{
				return;
			}

			for (int i = catalogue.Tables.Count - 1; i >= 0; --i)
			{
				SceneSpawnTable table = catalogue.Tables[i];
				if (table == null)
				{
					catalogue.Tables.RemoveAt(i);
					continue;
				}
				if (!bakedScenes.Contains(table.SceneName))
				{
					catalogue.Tables.RemoveAt(i);
					AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(table));
				}
			}
			SortAndSave(catalogue);
		}

		private static void SortAndSave(SpawnTableCatalogue catalogue)
		{
			catalogue.Tables.Sort((a, b) => string.CompareOrdinal(a != null ? a.SceneName : string.Empty, b != null ? b.SceneName : string.Empty));
			catalogue.Invalidate();
			EditorUtility.SetDirty(catalogue);
			AssetDatabase.SaveAssetIfDirty(catalogue);
		}

		private static List<RespawnCondition> CloneConditions(List<RespawnCondition> source, Func<ObjectSpawner, int> resolveSpawnerIndex)
		{
			List<RespawnCondition> copies = new List<RespawnCondition>();
			if (source == null)
			{
				return copies;
			}
			for (int i = 0; i < source.Count; ++i)
			{
				RespawnCondition copy = CloneManaged(source[i]);
				if (copy == null)
				{
					continue;
				}
				copy.Bake(source[i], resolveSpawnerIndex);
				copies.Add(copy);
			}
			return copies;
		}

		/// <summary>
		/// Deep-copies a plain serialized object, keeping its concrete type and its asset references.
		/// </summary>
		private static T CloneManaged<T>(T source) where T : class
		{
			if (source == null)
			{
				return null;
			}
			T copy = (T)Activator.CreateInstance(source.GetType());
			EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), copy);
			return copy;
		}

		private static void EnsureFolder(string folder)
		{
			if (AssetDatabase.IsValidFolder(folder))
			{
				return;
			}
			string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
		}
	}

	/// <summary>
	/// Re-bakes a world scene's spawn table every time the scene is saved.
	/// </summary>
	[InitializeOnLoad]
	internal static class SpawnTableBakeOnSave
	{
		static SpawnTableBakeOnSave()
		{
			EditorSceneManager.sceneSaved += OnSceneSaved;
		}

		private static void OnSceneSaved(Scene scene)
		{
			if (!SpawnTableBaker.IsWorldScenePath(scene.path))
			{
				return;
			}

			try
			{
				List<string> problems = new List<string>();
				SpawnTableBaker.BakeScene(scene, problems);
				for (int i = 0; i < problems.Count; ++i)
				{
					Debug.LogWarning("[SpawnTableBaker] " + problems[i]);
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[SpawnTableBaker] Could not bake the spawn table for '{scene.path}': {ex}");
			}
		}
	}
}
