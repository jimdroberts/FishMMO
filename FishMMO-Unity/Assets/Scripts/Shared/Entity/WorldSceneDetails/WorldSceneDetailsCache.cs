using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "WorldSceneDetails", menuName = "World Scene Details")]
	public class WorldSceneDetailsCache : ScriptableObject
	{
		public const string CACHE_PATH = "Assets/Prefabs/Shared/";
		public const string CACHE_FILE_NAME = "WorldSceneDetails.asset";
		public const string CACHE_FULL_PATH = CACHE_PATH + CACHE_FILE_NAME;

		public WorldSceneDetailsDictionary Scenes = new WorldSceneDetailsDictionary();

		public bool Rebuild()
		{
#if UNITY_EDITOR
			// unity only uses forward slash for paths apparently
			string worldScenePath = Constants.Configuration.WorldScenePath.Replace(@"\", @"/");

			Debug.Log("WorldSceneDetails: Rebuilding");

			Scenes.Clear();
			Scenes = new WorldSceneDetailsDictionary();

			Dictionary<string, Dictionary<string, SceneTeleporterDetails>> teleporterCache = new Dictionary<string, Dictionary<string, SceneTeleporterDetails>>();
			Dictionary<string, TeleporterDestinationDetails> teleporterDestinationCache = new Dictionary<string, TeleporterDestinationDetails>();

			// get our initial scene so we can return to it..
			Scene initialScene = EditorSceneManager.GetActiveScene();
			string initialScenePath = initialScene.path;
			if (initialScene.path.Contains(worldScenePath))
			{
				// load any other scene besides a world scene.. this is easier
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

			foreach (string scenePath in worldScenes)
			{
				// load the scene
				Scene currentScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
				if (!Scenes.ContainsKey(currentScene.name) &&
					currentScene.IsValid())
				{
					Debug.Log("WorldSceneDetails: Scene Loaded[" + currentScene.name + "]");

					IBoundary boundary = GameObject.FindFirstObjectByType<IBoundary>();
					if (boundary == null)
					{
						Debug.LogError(currentScene.name + " has no IBoundary. Boundaries are required for safety purposes. Try adding a SceneBoundary!");
						continue;
					}

					// add the scene to our world scenes list
					WorldSceneDetails sceneDetails = new WorldSceneDetails();
					Scenes.Add(currentScene.name, sceneDetails);

					// search for settings
					WorldSceneSettings worldSceneSettings = GameObject.FindFirstObjectByType<WorldSceneSettings>();
					if (worldSceneSettings != null)
					{
						sceneDetails.MaxClients = worldSceneSettings.MaxClients;
						sceneDetails.SceneTransitionImage = worldSceneSettings.SceneTransitionImage;
					}

					// search for initialSpawnPositions
					CharacterInitialSpawnPosition[] characterSpawnPositions = GameObject.FindObjectsByType<CharacterInitialSpawnPosition>(FindObjectsSortMode.None);
					foreach (CharacterInitialSpawnPosition obj in characterSpawnPositions)
					{
						Debug.Log("WorldSceneDetails: Found new Initial Spawn Position[" + obj.name + " Pos:" + obj.transform.position + " Rot:" + obj.transform.rotation + "]");

						sceneDetails.InitialSpawnPositions.Add(obj.name, new CharacterInitialSpawnPositionDetails()
						{
							SpawnerName = obj.name,
							SceneName = currentScene.name,
							Position = obj.transform.position,
							Rotation = obj.transform.rotation,
							AllowedRaces = obj.AllowedRaces,
						});
					}

					// search for respawnPositions
					CharacterRespawnPosition[] respawnPositions = GameObject.FindObjectsByType<CharacterRespawnPosition>(FindObjectsSortMode.None);
					foreach (CharacterRespawnPosition obj in respawnPositions)
					{
						Debug.Log("WorldSceneDetails: Found new Respawn Position[" + obj.name + " " + obj.transform + "]");

						sceneDetails.RespawnPositions.Add(obj.name, new CharacterRespawnPositionDetails()
						{
							Position = obj.transform.position,
							Rotation = obj.transform.rotation,
						});
					}

					// Search for world bounds (bounds activate when outside of all of them)
					IBoundary[] sceneBoundaries = GameObject.FindObjectsByType<IBoundary>(FindObjectsSortMode.None);
					foreach (IBoundary obj in sceneBoundaries)
					{
						Debug.Log($"WorldSceneDetails: Found new Boundary[Name: {obj.name}, Center: {obj.GetBoundaryOffset()}, Size: {obj.GetBoundarySize()}]");

						sceneDetails.Boundaries.Add(obj.name, new SceneBoundaryDetails()
						{
							BoundaryOrigin = obj.GetBoundaryOffset(),
							BoundarySize = obj.GetBoundarySize()
						});
					}

					// search for scene teleporters
					SceneTeleporter[] teleports = GameObject.FindObjectsByType<SceneTeleporter>(FindObjectsSortMode.None);
					foreach (SceneTeleporter obj in teleports)
					{
						obj.name = obj.name.Trim();

						Debug.Log("WorldSceneDetails: Found new SceneTeleporter[" + obj.name + "]");

						SceneTeleporterDetails newDetails = new SceneTeleporterDetails()
						{
							From = obj.name, // used for validation
											 // we still need to set toScene and toPosition later
						};

						if (!teleporterCache.TryGetValue(currentScene.name, out Dictionary<string, SceneTeleporterDetails> teleporters))
						{
							teleporterCache.Add(currentScene.name, teleporters = new Dictionary<string, SceneTeleporterDetails>());
						}
						teleporters.Add(obj.name, newDetails);
					}

					// search for teleports
					Teleporter[] interactableTeleporters = GameObject.FindObjectsByType<Teleporter>(FindObjectsSortMode.None);
					foreach (Teleporter obj in interactableTeleporters)
					{
						obj.name = obj.name.Trim();

						Debug.Log("WorldSceneDetails: Found new Teleporter[" + obj.name + "]");

						SceneTeleporterDetails newDetails = new SceneTeleporterDetails()
						{
							From = obj.name, // used for validation
											 // we still need to set toScene and toPosition later
						};

						if (!teleporterCache.TryGetValue(currentScene.name, out Dictionary<string, SceneTeleporterDetails> teleporters))
						{
							teleporterCache.Add(currentScene.name, teleporters = new Dictionary<string, SceneTeleporterDetails>());
						}
						teleporters.Add(obj.name, newDetails);
					}

					// search for teleporter destinations
					TeleporterDestination[] teleportDestinations = GameObject.FindObjectsByType<TeleporterDestination>(FindObjectsSortMode.None);
					foreach (TeleporterDestination obj in teleportDestinations)
					{
						string teleporterDestinationName = obj.name.Trim();

						Debug.Log("WorldSceneDetails: Found new Teleporter Destination[Destination:" + teleporterDestinationName + " " + obj.transform.position + "]");

						teleporterDestinationCache.Add(teleporterDestinationName, new TeleporterDestinationDetails()
						{
							Scene = currentScene.name,
							Position = obj.transform.position,
							Rotation = obj.transform.rotation,
						});
					}
				}
				//EditorSceneManager.SaveOpenScenes();
				Debug.Log("WorldSceneDetails Scene Unloaded[" + currentScene.name + "]");
				EditorSceneManager.CloseScene(currentScene, true);
			}
			
			if (!initialScene.path.Equals(initialScenePath))
			{
				Scene nonWorldScene = EditorSceneManager.OpenScene(initialScenePath, OpenSceneMode.Additive);
				EditorSceneManager.CloseScene(initialScene, true);
			}

			Debug.Log("WorldSceneDetails: Connecting teleporters...");

			// assign teleporter destination positions
			foreach (KeyValuePair<string, Dictionary<string, SceneTeleporterDetails>> teleporterDetailsPair in teleporterCache)
			{
				foreach (KeyValuePair<string, SceneTeleporterDetails> pair in teleporterDetailsPair.Value)
				{
					string destinationName = "From" + pair.Value.From;

					if (teleporterDestinationCache.TryGetValue(destinationName, out TeleporterDestinationDetails destination))
					{
						Debug.Log("WorldSceneDetails: Connecting " + teleporterDetailsPair.Key + " -> " + destinationName);
						if (Scenes.TryGetValue(teleporterDetailsPair.Key, out WorldSceneDetails sceneDetails))
						{
							pair.Value.ToScene = destination.Scene;
							pair.Value.ToPosition = destination.Position;
							pair.Value.ToRotation = destination.Rotation;

							Debug.Log("WorldSceneDetails: Teleporter " + pair.Key + " connected to Scene[" + destination.Scene + ": Destination:" + "From" + pair.Value.From + " Position:" + pair.Value.ToPosition + " Rotation:" + pair.Value.ToRotation.eulerAngles + "]");

							sceneDetails.Teleporters.Add(pair.Key, pair.Value);
						}
					}
				}
			}
			Debug.Log("WorldSceneDetails: Rebuild Complete");
#endif
			return true;
		}
	}
}