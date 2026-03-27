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
	/// ScriptableObject responsible for reading and rebuilding world scene details from Unity scenes.
	/// Scans all configured world scenes, extracts spawn positions, boundaries, teleporters, and validates destinations against the TeleporterCache.
	/// </summary>
	[CreateAssetMenu(fileName = "FishMMO World Scene Details Reader", menuName = "FishMMO/World Scene Details Reader")]
	public class WorldSceneDetailsCacheReader : ScriptableObject
	{
		/// <summary>
		/// Reference to the TeleporterCache used to validate teleporter destination connections during rebuild.
		/// </summary>
		public TeleporterCache TeleporterCache;

		/// <summary>
		/// Scans all world scenes and rebuilds the provided scene details dictionary.
		/// Extracts spawn positions, boundaries, and teleporters from each scene.
		/// Teleporter destinations are validated against the TeleporterCache; missing destinations produce errors.
		/// </summary>
		/// <param name="worldSceneDetailsDictionary">Reference to the dictionary to populate with scene details.</param>
		/// <returns>True if the rebuild process completes; otherwise, false.</returns>
		public virtual bool Rebuild(ref WorldSceneDetailsDictionary worldSceneDetailsDictionary)
		{
#if UNITY_EDITOR
			// Unity only uses forward slash for paths; normalize for consistency.
			string worldScenePath = Constants.Configuration.WorldScenePath.Replace(@"\", @"/");

			Log.Debug("WorldSceneDetailsCacheReader", "Rebuilding");

			// Clear and reinitialize the dictionary to ensure a fresh rebuild.
			worldSceneDetailsDictionary.Clear();
			worldSceneDetailsDictionary = new WorldSceneDetailsDictionary();

			// Get the initial scene so we can return to it after scanning.
			Scene initialScene = EditorSceneManager.GetActiveScene();
			string initialScenePath = initialScene.path;
			if (initialScene.path.Contains(worldScenePath))
			{
				// Load any other scene besides a world scene to avoid issues with additive loading.
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

			// Gather all world scene paths.
			HashSet<string> worldScenes = DirectoryExtensions.GetAllFiles(Constants.Configuration.WorldScenePath, ".unity");

			// Optionally include local scenes if enabled in editor preferences.
			if (EditorPrefs.GetBool("FishMMOEnableLocalDirectory"))
			{
				HashSet<string> localScenes = DirectoryExtensions.GetAllFiles(Constants.Configuration.LocalScenePath, ".unity");
				worldScenes.UnionWith(localScenes);
			}

			// Scan each world scene for relevant objects and configuration.
			foreach (string scenePath in worldScenes)
			{
				// Load the scene additively for scanning.
				Scene currentScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
				if (!worldSceneDetailsDictionary.ContainsKey(currentScene.name) && currentScene.IsValid())
				{
					Log.Debug("WorldSceneDetailsCacheReader", $"Scene Loaded[{currentScene.name}]");

					// Ensure the scene has a boundary for safety.
					IBoundary boundary = GameObject.FindFirstObjectByType<IBoundary>();
					if (boundary == null)
					{
						Log.Error("WorldSceneDetailsCacheReader", $"{currentScene.name} has no IBoundary. Boundaries are required for safety purposes. Try adding a SceneBoundary!");
						continue;
					}

					// Add the scene to our world scenes list.
					WorldSceneDetails sceneDetails = new WorldSceneDetails();
					worldSceneDetailsDictionary.Add(currentScene.name, sceneDetails);

					// Search for scene settings.
					WorldSceneSettings worldSceneSettings = GameObject.FindFirstObjectByType<WorldSceneSettings>();
					if (worldSceneSettings != null)
					{
						sceneDetails.MaxClients = worldSceneSettings.MaxClients;
						sceneDetails.SceneTransitionImage = worldSceneSettings.SceneTransitionImage;
					}

					// Search for initial spawn positions.
					CharacterInitialSpawnPosition[] characterSpawnPositions = GameObject.FindObjectsByType<CharacterInitialSpawnPosition>(FindObjectsSortMode.None);
					foreach (CharacterInitialSpawnPosition obj in characterSpawnPositions)
					{
						Log.Debug("WorldSceneDetailsCacheReader", $"Found new Initial Spawn Position[{obj.name} Pos:{obj.transform.position} Rot:{obj.transform.rotation}]");

						sceneDetails.InitialSpawnPositions.Add(obj.name, new CharacterInitialSpawnPositionDetails()
						{
							SpawnerName = obj.name,
							SceneName = currentScene.name,
							Position = obj.transform.position,
							Rotation = obj.transform.rotation,
							AllowedRaces = obj.AllowedRaces,
						});
					}

					// Search for respawn positions.
					CharacterRespawnPosition[] respawnPositions = GameObject.FindObjectsByType<CharacterRespawnPosition>(FindObjectsSortMode.None);
					foreach (CharacterRespawnPosition obj in respawnPositions)
					{
						Log.Debug("WorldSceneDetailsCacheReader", $"Found new Respawn Position[{obj.name} {obj.transform}]");

						sceneDetails.RespawnPositions.Add(obj.name, new CharacterRespawnPositionDetails()
						{
							Position = obj.transform.position,
							Rotation = obj.transform.rotation,
						});
					}

					// Search for world boundaries.
					IBoundary[] sceneBoundaries = GameObject.FindObjectsByType<IBoundary>(FindObjectsSortMode.None);
					foreach (IBoundary obj in sceneBoundaries)
					{
						Log.Debug($"WorldSceneDetailsCacheReader", $"Found new Boundary[Name: {obj.name}, Center: {obj.GetBoundaryOffset()}, Size: {obj.GetBoundarySize()}]");

						sceneDetails.Boundaries.Add(obj.name, new SceneBoundaryDetails()
						{
							BoundaryOrigin = obj.GetBoundaryOffset(),
							BoundarySize = obj.GetBoundarySize()
						});
					}

					// Search for scene teleporters and validate against TeleporterCache.
					SceneTeleporter[] teleports = GameObject.FindObjectsByType<SceneTeleporter>(FindObjectsSortMode.None);
					foreach (SceneTeleporter obj in teleports)
					{
						obj.name = obj.name.Trim();

						if (sceneDetails.Teleporters.ContainsKey(obj.name))
						{
							Log.Error("WorldSceneDetailsCacheReader", $"Duplicate teleporter name '{obj.name}' in scene '{currentScene.name}'. Teleporter names must be unique within a scene. Skipping duplicate.");
							continue;
						}

						if (string.IsNullOrEmpty(obj.DestinationID))
						{
							Log.Error("WorldSceneDetailsCacheReader", $"SceneTeleporter '{obj.name}' in scene '{currentScene.name}' has no DestinationID assigned. Select a destination in the inspector.");
							continue;
						}

						if (TeleporterCache == null || !TeleporterCache.Destinations.TryGetValue(obj.DestinationID, out TeleporterCacheEntry destinationEntry))
						{
							Log.Error("WorldSceneDetailsCacheReader", $"SceneTeleporter '{obj.name}' in scene '{currentScene.name}' references DestinationID '{obj.DestinationID}' which does not exist in the TeleporterCache. The destination may have been removed. Rebuild the TeleporterCache.");
							continue;
						}

						Log.Debug("WorldSceneDetailsCacheReader", $"Found SceneTeleporter[{obj.name}] -> Destination[{destinationEntry.DisplayName} in {destinationEntry.SceneName}]");

						SceneTeleporterDetails newDetails = new SceneTeleporterDetails()
						{
							From = obj.name,
							ToScene = destinationEntry.SceneName,
							ToPosition = destinationEntry.Position,
							ToRotation = destinationEntry.Rotation,
						};

						sceneDetails.Teleporters.Add(obj.name, newDetails);
					}

					// Search for interactable teleporters and validate against TeleporterCache.
					Teleporter[] interactableTeleporters = GameObject.FindObjectsByType<Teleporter>(FindObjectsSortMode.None);
					foreach (Teleporter obj in interactableTeleporters)
					{
						obj.name = obj.name.Trim();

						if (sceneDetails.Teleporters.ContainsKey(obj.name))
						{
							Log.Error("WorldSceneDetailsCacheReader", $"Duplicate teleporter name '{obj.name}' in scene '{currentScene.name}'. Teleporter names must be unique within a scene. Skipping duplicate.");
							continue;
						}

						if (string.IsNullOrEmpty(obj.DestinationID))
						{
							// Interactable teleporters with a direct Target don't need a DestinationID.
							if (obj.Target != null)
							{
								Log.Debug("WorldSceneDetailsCacheReader", $"Teleporter '{obj.name}' in scene '{currentScene.name}' uses a direct Target. Skipping destination validation.");
								continue;
							}

							Log.Error("WorldSceneDetailsCacheReader", $"Teleporter '{obj.name}' in scene '{currentScene.name}' has no DestinationID and no Target assigned. Select a destination in the inspector or assign a Target.");
							continue;
						}

						if (TeleporterCache == null || !TeleporterCache.Destinations.TryGetValue(obj.DestinationID, out TeleporterCacheEntry destinationEntry))
						{
							Log.Error("WorldSceneDetailsCacheReader", $"Teleporter '{obj.name}' in scene '{currentScene.name}' references DestinationID '{obj.DestinationID}' which does not exist in the TeleporterCache. The destination may have been removed. Rebuild the TeleporterCache.");
							continue;
						}

						Log.Debug("WorldSceneDetailsCacheReader", $"Found Teleporter[{obj.name}] -> Destination[{destinationEntry.DisplayName} in {destinationEntry.SceneName}]");

						SceneTeleporterDetails newDetails = new SceneTeleporterDetails()
						{
							From = obj.name,
							ToScene = destinationEntry.SceneName,
							ToPosition = destinationEntry.Position,
							ToRotation = destinationEntry.Rotation,
						};

						sceneDetails.Teleporters.Add(obj.name, newDetails);
					}
				}
				// Unload the scene after scanning.
				Log.Debug("WorldSceneDetailsCacheReader", $"Scene Unloaded[{currentScene.name}]");
				EditorSceneManager.CloseScene(currentScene, true);
			}

			// Restore the initial scene if it was changed during scanning.
			if (!initialScene.path.Equals(initialScenePath))
			{
				Scene nonWorldScene = EditorSceneManager.OpenScene(initialScenePath, OpenSceneMode.Additive);
				EditorSceneManager.CloseScene(initialScene, true);
			}

			Log.Debug("WorldSceneDetailsCacheReader", "Rebuild Complete");
#endif
			return true;
		}
	}
}