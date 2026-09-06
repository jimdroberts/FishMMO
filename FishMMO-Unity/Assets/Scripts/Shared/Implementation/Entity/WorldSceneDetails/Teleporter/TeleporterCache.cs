using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject cache for storing all known teleporter destinations.
	/// Pre-baked by scanning world scenes for TeleporterDestination components, keyed by stable DestinationID.
	/// Must be rebuilt before WorldSceneDetailsCache to ensure teleporter connections are validated.
	/// </summary>
	[CreateAssetMenu(fileName = "TeleporterCache", menuName = "FishMMO/Teleporter Cache")]
	public class TeleporterCache : ScriptableObject
	{
		/// <summary>
		/// Path to the folder where the cache asset is stored.
		/// </summary>
		public const string CACHE_PATH = "Assets/Prefabs/Shared/";

		/// <summary>
		/// File name of the cache asset.
		/// </summary>
		public const string CACHE_FILE_NAME = "TeleporterCache.asset";

		/// <summary>
		/// Full path to the cache asset file.
		/// </summary>
		public const string CACHE_FULL_PATH = CACHE_PATH + CACHE_FILE_NAME;

		/// <summary>
		/// Dictionary of all known teleporter destinations, keyed by DestinationID.
		/// </summary>
		public TeleporterCacheDictionary Destinations = new TeleporterCacheDictionary();

		/// <summary>
		/// Dictionary of all known scene teleporters, keyed by composite "SceneName/TeleporterName".
		/// </summary>
		public SceneTeleporterCacheDictionary Teleporters = new SceneTeleporterCacheDictionary();

#if UNITY_EDITOR
		/// <summary>
		/// Loads the TeleporterCache asset from disk, creating it if it does not exist.
		/// </summary>
		/// <returns>The loaded or newly created TeleporterCache asset.</returns>
		public static TeleporterCache GetOrCreateCache()
		{
			TeleporterCache cache = AssetDatabase.LoadAssetAtPath<TeleporterCache>(CACHE_FULL_PATH);
			if (cache == null)
			{
				if (!AssetDatabase.IsValidFolder(CACHE_PATH.TrimEnd('/')))
				{
					System.IO.Directory.CreateDirectory(CACHE_PATH);
					AssetDatabase.Refresh();
				}
				cache = CreateInstance<TeleporterCache>();
				AssetDatabase.CreateAsset(cache, CACHE_FULL_PATH);
				AssetDatabase.SaveAssets();
				Log.Debug("TeleporterCache", $"Created new TeleporterCache at {CACHE_FULL_PATH}");
			}
			return cache;
		}

		/// <summary>
		/// Registers a TeleporterDestination in the cache. Creates the cache asset if it does not exist.
		/// Updates the entry if the DestinationID already exists.
		/// </summary>
		/// <param name="destination">The TeleporterDestination to register.</param>
		/// <param name="sceneName">The name of the scene containing the destination.</param>
		public static void RegisterDestination(TeleporterDestination destination, string sceneName)
		{
			if (destination == null || string.IsNullOrEmpty(destination.DestinationID))
			{
				return;
			}

			TeleporterCache cache = GetOrCreateCache();

			TeleporterCacheEntry entry = new TeleporterCacheEntry()
			{
				DestinationID = destination.DestinationID,
				SceneName = sceneName,
				DisplayName = destination.name,
				Position = destination.transform.position,
				Rotation = destination.transform.rotation,
			};

			if (cache.Destinations.ContainsKey(destination.DestinationID))
			{
				cache.Destinations[destination.DestinationID] = entry;
			}
			else
			{
				cache.Destinations.Add(destination.DestinationID, entry);
			}

			EditorUtility.SetDirty(cache);
			Log.Debug("TeleporterCache", $"Registered destination '{destination.name}' (ID:{destination.DestinationID}) in scene '{sceneName}'");
		}

		/// <summary>
		/// Removes a TeleporterDestination from the cache by its DestinationID.
		/// </summary>
		/// <param name="destinationID">The DestinationID to remove.</param>
		public static void UnregisterDestination(string destinationID)
		{
			if (string.IsNullOrEmpty(destinationID))
			{
				return;
			}

			TeleporterCache cache = AssetDatabase.LoadAssetAtPath<TeleporterCache>(CACHE_FULL_PATH);
			if (cache == null || !cache.Destinations.ContainsKey(destinationID))
			{
				return;
			}

			cache.Destinations.Remove(destinationID);
			EditorUtility.SetDirty(cache);
			Log.Debug("TeleporterCache", $"Unregistered destination ID:{destinationID}");
		}
#endif

		/// <summary>
		/// Rebuilds the teleporter cache by scanning all world scenes for TeleporterDestination components.
		/// Returns true if the rebuild completes successfully; false if errors are encountered.
		/// </summary>
		/// <returns>True if the rebuild completed successfully; otherwise, false.</returns>
		public bool Rebuild()
		{
			bool success = false;
#if UNITY_EDITOR
			string worldScenePath = Constants.Configuration.WorldScenePath.Replace(@"\", @"/");

			Log.Debug("TeleporterCache", "Rebuilding");

			Destinations.Clear();
			Destinations = new TeleporterCacheDictionary();

			Teleporters.Clear();
			Teleporters = new SceneTeleporterCacheDictionary();

			Scene initialScene = EditorSceneManager.GetActiveScene();
			string initialScenePath = initialScene.path;
			if (initialScene.path.Contains(worldScenePath))
			{
				foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
				{
					if (!scene.path.Contains(worldScenePath))
					{
						Scene tmp = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Additive);
						EditorSceneManager.CloseScene(initialScene, true);
						initialScene = tmp;
						break;
					}
				}
			}

			HashSet<string> worldScenes = DirectoryExtensions.GetAllFiles(Constants.Configuration.WorldScenePath, ".unity");

			if (EditorPrefs.GetBool("FishMMOEnableLocalDirectory"))
			{
				HashSet<string> localScenes = DirectoryExtensions.GetAllFiles(Constants.Configuration.LocalScenePath, ".unity");
				worldScenes.UnionWith(localScenes);
			}

			success = true;

			foreach (string scenePath in worldScenes)
			{
				Scene currentScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
				if (currentScene.IsValid())
				{
					TeleporterDestination[] destinations = GameObject.FindObjectsByType<TeleporterDestination>(FindObjectsSortMode.None);
					foreach (TeleporterDestination dest in destinations)
					{
						if (dest == null || dest.gameObject.scene != currentScene)
						{
							continue;
						}

						// Auto-assign DestinationIDs to legacy destinations that predate the GUID system.
						if (string.IsNullOrEmpty(dest.DestinationID))
						{
							SerializedObject so = new SerializedObject(dest);
							SerializedProperty idProp = so.FindProperty("destinationID");
							idProp.stringValue = System.Guid.NewGuid().ToString();
							so.ApplyModifiedProperties();
							EditorUtility.SetDirty(dest);
							EditorSceneManager.MarkSceneDirty(currentScene);
							Log.Debug("TeleporterCache", $"Auto-assigned DestinationID '{dest.DestinationID}' to '{dest.name}' in scene '{currentScene.name}'. Save the scene to persist.");
						}

						if (Destinations.ContainsKey(dest.DestinationID))
						{
							Log.Error("TeleporterCache", $"Duplicate DestinationID '{dest.DestinationID}' found on '{dest.name}' in scene '{currentScene.name}'. IDs must be unique.");
							success = false;
							continue;
						}

						Log.Debug("TeleporterCache", $"Found TeleporterDestination[{dest.name} ID:{dest.DestinationID} Scene:{currentScene.name}]");

						Destinations.Add(dest.DestinationID, new TeleporterCacheEntry()
						{
							DestinationID = dest.DestinationID,
							SceneName = currentScene.name,
							DisplayName = dest.name,
							Position = dest.transform.position,
							Rotation = dest.transform.rotation,
						});
					}

					// Scan for SceneTeleporters and cache them by composite key.
					SceneTeleporter[] teleporters = GameObject.FindObjectsByType<SceneTeleporter>(FindObjectsSortMode.None);
					foreach (SceneTeleporter teleporter in teleporters)
					{
						if (teleporter == null || teleporter.gameObject.scene != currentScene)
						{
							continue;
						}

						string trimmedName = teleporter.name.Trim();
						string compositeKey = currentScene.name + "/" + trimmedName;

						if (Teleporters.ContainsKey(compositeKey))
						{
							Log.Error("TeleporterCache", $"Duplicate SceneTeleporter '{trimmedName}' in scene '{currentScene.name}'. Teleporter names must be unique within a scene.");
							success = false;
							continue;
						}

						if (string.IsNullOrEmpty(teleporter.DestinationID))
						{
							Log.Warning("TeleporterCache", $"SceneTeleporter '{trimmedName}' in scene '{currentScene.name}' has no DestinationID assigned. Select a destination in the inspector.");
						}

						Log.Debug("TeleporterCache", $"Found SceneTeleporter[{trimmedName} Scene:{currentScene.name} DestinationID:{teleporter.DestinationID}]");

						Teleporters.Add(compositeKey, new SceneTeleporterCacheEntry()
						{
							TeleporterName = trimmedName,
							SceneName = currentScene.name,
							ScenePath = scenePath,
							DestinationID = teleporter.DestinationID,
							TeleporterGlobalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(teleporter.gameObject).ToString(),
							Position = teleporter.transform.position,
						});
					}
				}

				Log.Debug("TeleporterCache", $"Scene Unloaded[{currentScene.name}]");
				// Save the scene if any destinations were assigned new IDs.
				if (currentScene.isDirty)
				{
					EditorSceneManager.SaveScene(currentScene);
				}
				EditorSceneManager.CloseScene(currentScene, true);
			}

			if (!initialScene.path.Equals(initialScenePath))
			{
				Scene nonWorldScene = EditorSceneManager.OpenScene(initialScenePath, OpenSceneMode.Additive);
				EditorSceneManager.CloseScene(initialScene, true);
			}

			Log.Debug("TeleporterCache", $"Rebuild Complete. {Destinations.Count} destinations, {Teleporters.Count} teleporters cached. Success: {success}");
#endif
			return success;
		}
	}
}