using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

[CreateAssetMenu(fileName = "WorldSceneDetails", menuName = "World Scene Details")]
public class WorldSceneDetailsCache : ScriptableObject
{
	public const string WORLD_SCENE_PATH = "/WorldScene/";
	public const string CACHE_PATH = "Assets/Resources/Prefabs/Shared/";
	public const string CACHE_FILE_NAME = "WorldSceneDetails.asset";
	public const string CACHE_FULL_PATH = CACHE_PATH + CACHE_FILE_NAME;

	[Tooltip("Apply this tag to any object in your starting scenes to turn them into initial spawn locations.")]
	public string InitialSpawnTag = "InitialSpawnPosition";
	[Tooltip("Apply this tag to any object in your scene you would like to behave as a respawn location.")]
	public string RespawnTag = "RespawnPosition";
	[Tooltip("Apply this tag to any object in your scene that you would like to act as a teleporter.")]
	public string TeleporterTag = "Teleporter";
	[Tooltip("Apply this tag to any object in your scene that you would like to act as a teleporter destination.")]
	public string TeleporterDestinationTag = "TeleporterDestination";

	public WorldSceneDetailsDictionary Scenes = new WorldSceneDetailsDictionary();

	public bool Rebuild()
	{
#if UNITY_EDITOR
		if (EditorSceneManager.GetActiveScene().path.Contains(WORLD_SCENE_PATH))
		{
			Debug.Log("WorldSceneDetails: Unable to rebuild scene details while a WorldScene is open. Load a bootstrap scene instead! TODO: Automate");
			return false;
		}
		Debug.Log("WorldSceneDetails: Rebuilding");

		// Keep track of teleporter sprites
		Dictionary<string, Dictionary<string, Sprite>> teleporterSpriteCache = new Dictionary<string, Dictionary<string, Sprite>>();
		foreach (var worldSceneEntry in Scenes)
		{
			foreach (var teleporterEntry in worldSceneEntry.Value.Teleporters)
			{
				if (teleporterEntry.Value.SceneTransitionImage == null) continue;
				
				if(teleporterSpriteCache.TryGetValue(worldSceneEntry.Key, out Dictionary<string, Sprite> spriteCache) == false)
				{
					spriteCache = new Dictionary<string, Sprite>();
					teleporterSpriteCache.Add(worldSceneEntry.Key, spriteCache);
				}

				spriteCache.Add(teleporterEntry.Key, teleporterEntry.Value.SceneTransitionImage);
			}
		}

		Scenes.Clear();
		Scenes = new WorldSceneDetailsDictionary();

		Dictionary<string, Dictionary<string, SceneTeleporterDetails>> teleporterCache = new Dictionary<string, Dictionary<string, SceneTeleporterDetails>>();
		Dictionary<string, TeleporterDestinationDetails> teleporterDestinationCache = new Dictionary<string, TeleporterDestinationDetails>();

		foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
		{
			if (!scene.enabled)
				continue;

			// ensure the scene is a world scene
			if (!scene.path.Contains(WORLD_SCENE_PATH))
				continue;

			// load the scene
			Scene s = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Additive);
			if (s.IsValid())
			{
				if (!Scenes.ContainsKey(s.name))
				{
					Debug.Log("WorldSceneDetails: Scene Loaded[" + s.name + "]");

					// add the scene to our world scenes list
					WorldSceneDetails sceneDetails = new WorldSceneDetails();
					Scenes.Add(s.name, sceneDetails);

					// search for initialSpawnPositions
					GameObject[] initialSpawns = GameObject.FindGameObjectsWithTag(InitialSpawnTag);
					foreach (GameObject obj in initialSpawns)
					{
						Debug.Log("WorldSceneDetails: Found new InitialSpawnPosition[" + obj.name + " Pos:" + obj.transform.position + " Rot:" + obj.transform.rotation + "]");

						sceneDetails.InitialSpawnPositions.Add(obj.name, new CharacterInitialSpawnPosition()
						{
							SpawnerName = obj.name,
							SceneName = s.name,
							Position = obj.transform.position,
							Rotation = obj.transform.rotation,
						});
					}

					// search for respawnPositions
					GameObject[] respawnPositions = GameObject.FindGameObjectsWithTag(RespawnTag);
					foreach (GameObject obj in respawnPositions)
					{
						Debug.Log("WorldSceneDetails: Found new RespawnPosition[" + obj.name + " " + obj.transform.position + "]");

						sceneDetails.RespawnPositions.Add(obj.name, obj.transform.position);
					}

					// Search for world bounds (bounds activate when outside of all of them)
					IBoundary[] sceneBoundaries = GameObject.FindObjectsOfType<IBoundary>();
					foreach (IBoundary obj in sceneBoundaries)
					{
						Debug.Log($"WorldSceneDetails: Found new Boundary[Name: {obj.name}, Center: {obj.GetBoundaryOffset()}, Size: {obj.GetBoundarySize()}]");

						sceneDetails.Boundaries.Add(obj.name, new SceneBoundaryDetails()
						{
							BoundaryOrigin = obj.GetBoundaryOffset(),
							BoundarySize = obj.GetBoundarySize()
						});
					}

					// search for teleporters
					SceneTeleporter[] teleports = GameObject.FindObjectsOfType<SceneTeleporter>();
					foreach (SceneTeleporter obj in teleports)
					{
						obj.name = obj.name.Trim();

						SceneTeleporterDetails newDetails = new SceneTeleporterDetails()
						{
							From = obj.name, // used for validation
							// we still need to set toScene and toPosition later
						};

						if (!teleporterCache.TryGetValue(s.name, out Dictionary<string, SceneTeleporterDetails> teleporters))
						{
							teleporterCache.Add(s.name, teleporters = new Dictionary<string, SceneTeleporterDetails>());
						}
						teleporters.Add(obj.name, newDetails);
					}

					// search for teleporter destinations
					GameObject[] teleportDestinations = GameObject.FindGameObjectsWithTag(TeleporterDestinationTag);
					foreach (GameObject obj in teleportDestinations)
					{
						string teleporterDestinationName = obj.name.Trim();

						Debug.Log("WorldSceneDetails: Found new TeleporterDestination[Destination:" + teleporterDestinationName + " " + obj.transform.position + "]");

						teleporterDestinationCache.Add(teleporterDestinationName, new TeleporterDestinationDetails()
						{
							Scene = s.name,
							Position = obj.transform.position,
						});
					}

					Debug.Log("WorldSceneDetails Scene Unloaded[" + s.name + "]");
				}
			}
			// unload the scene
			EditorSceneManager.CloseScene(s, true);
		}

		Debug.Log("WorldSceneDetails: Connecting teleporters...");

		// assign teleporter destination positions
		foreach (KeyValuePair<string, Dictionary<string, SceneTeleporterDetails>> teleporterDetailsPair in teleporterCache)
		{
			foreach (KeyValuePair<string, SceneTeleporterDetails> pair in teleporterDetailsPair.Value)
			{
				if (teleporterDestinationCache.TryGetValue("From" + pair.Value.From, out TeleporterDestinationDetails destination))
				{
					if (Scenes.TryGetValue(teleporterDetailsPair.Key, out WorldSceneDetails sceneDetails))
					{
						pair.Value.ToScene = destination.Scene;
						pair.Value.ToPosition = destination.Position;
						pair.Value.SceneTransitionImage = null;

						if (teleporterSpriteCache.TryGetValue(teleporterDetailsPair.Key, out Dictionary<string, Sprite> spriteCache))
						{
							if(spriteCache.TryGetValue(pair.Key, out Sprite sprite))
							{
								pair.Value.SceneTransitionImage = sprite;
							}
						}

						Debug.Log("WorldSceneDetails: Teleporter[" + pair.Key + "] connected to: Scene[" + destination.Scene + " " + pair.Value.ToPosition + "]");

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