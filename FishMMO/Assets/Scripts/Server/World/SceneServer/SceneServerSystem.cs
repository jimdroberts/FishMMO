using FishNet.Transporting;
using FishNet.Managing.Scened;
using UnityEngine;
using System.Collections.Generic;
using FishNet.Connection;
using FishMMO.Server.Services;
using FishMMO_DB.Entities;

namespace FishMMO.Server
{
	// Scene Manager handles the node services and heartbeat to World Server
	public class SceneServerSystem : ServerBehaviour
	{
		public SceneManager SceneManager;

		private LocalConnectionState serverState;

		public WorldSceneDetailsCache WorldSceneDetailsCache;

		private long id;
		private bool locked = false;
		private float pulseRate = 5.0f;
		private float nextPulse = 0.0f;

		public long ID { get { return id; } }

		// <worldID, <sceneName, <sceneHandle, details>>>
		public Dictionary<long, Dictionary<string, Dictionary<int, SceneInstanceDetails>>> worldScenes = new Dictionary<long, Dictionary<string, Dictionary<int, SceneInstanceDetails>>>();

		public override void InitializeOnce()
		{
			if (ServerManager != null &&
				SceneManager != null)
			{
				SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
			using var dbContext = Server.DbContextFactory.CreateDbContext();

			if (args.ConnectionState == LocalConnectionState.Started)
			{
				if (Server.TryGetServerIPAddress(out ServerAddress server))
				{
					int characterCount = Server.CharacterSystem.ConnectionCharacters.Count;

					if (Server.Configuration.TryGetString("ServerName", out string name))
					{
						SceneServerService.Add(dbContext, server.address, server.port, characterCount, locked, out id);
						Debug.Log("Scene Server System: Added Scene Server to Database: [" + id + "] " + name + ":" + server.address + ":" + server.port);
					}
				}
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				if (Server.Configuration.TryGetString("ServerName", out string name))
				{
					Debug.Log("Scene Server System: Removing Scene Server: " + id);
					SceneServerService.Delete(dbContext, id);
					LoadedSceneService.Delete(dbContext, id);
					dbContext.SaveChanges();
				}
			}
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started)
			{
				if (nextPulse < 0)
				{
					nextPulse = pulseRate;

					// TODO: maybe this one should exist....how expensive will this be to run on update?
					using var dbContext = Server.DbContextFactory.CreateDbContext();
					Debug.Log("Scene Server System: Pulse");
					int characterCount = Server.CharacterSystem.ConnectionCharacters.Count;
					SceneServerService.Pulse(dbContext, id, characterCount);

					// process loaded scene pulse update
					if (worldScenes != null)
					{
						foreach (Dictionary<string, Dictionary<int, SceneInstanceDetails>> sceneGroup in worldScenes.Values)
						{
							foreach (Dictionary<int, SceneInstanceDetails> scene in sceneGroup.Values)
							{
								foreach (KeyValuePair<int, SceneInstanceDetails> sceneDetails in scene)
								{
									Debug.Log("Scene Server System: " + sceneDetails.Value.Name + ":" + sceneDetails.Value.WorldServerID + ":" + sceneDetails.Value.Handle + " Pulse");
									LoadedSceneService.Pulse(dbContext, sceneDetails.Key, sceneDetails.Value.CharacterCount);
								}
							}
						}
					}

					// process pending scenes
					PendingSceneEntity pending = PendingSceneService.Dequeue(dbContext);
					if (pending != null)
					{
						dbContext.SaveChanges();
						Debug.Log("Scene Server System: Dequeued Pending Scene Load request World:" + pending.WorldServerID + " Scene:" + pending.SceneName);
						ProcessSceneLoadRequest(pending.WorldServerID, pending.SceneName);
					}
				}
				nextPulse -= Time.deltaTime;
			}
		}

		private void OnApplicationQuit()
		{
			if (Server != null && Server.DbContextFactory != null &&
				serverState != LocalConnectionState.Stopped)
			{
				using var dbContext = Server.DbContextFactory.CreateDbContext();
				Debug.Log("Scene Server System: Removing Scene Server: " + id);
				SceneServerService.Delete(dbContext, id);
				LoadedSceneService.Delete(dbContext, id);
				dbContext.SaveChanges();
			}
		}

		/// <summary>
		/// Process a single scene load request from the database.
		/// </summary>
		private void ProcessSceneLoadRequest(long worldServerID, string sceneName)
		{
			if (WorldSceneDetailsCache == null ||
				!WorldSceneDetailsCache.Scenes.Contains(sceneName))
			{
				Debug.Log("Scene Server System: Scene is missing from the cache. Unable to load the scene.");
				return;
			}

			SceneLoadData sld = new SceneLoadData(sceneName);
			sld.ReplaceScenes = ReplaceOption.None;
			sld.Options.AllowStacking = true;
			sld.Options.LocalPhysics = UnityEngine.SceneManagement.LocalPhysicsMode.Physics3D;
			// scene unloading should be controlled by the scene server
			sld.Options.AutomaticallyUnload = false;
			sld.Params.ServerParams = new object[]
			{
				worldServerID
			};
			SceneManager.LoadConnectionScenes(sld);
		}

		// we only track scene handles here for scene stacking, the SceneManager has the real Scene reference
		private void SceneManager_OnLoadEnd(SceneLoadEndEventArgs args)
		{
			// we only operate on newly loaded scenes here
			if (args.LoadedScenes == null ||
				args.LoadedScenes.Length < 1)
			{
				return;
			}

			UnityEngine.SceneManagement.Scene scene = args.LoadedScenes[0];

			// note there should only ever be one world id. we load one at a time
			long worldServerID = -1;
			if (args.QueueData.SceneLoadData.Params.ServerParams != null &&
				args.QueueData.SceneLoadData.Params.ServerParams.Length > 0)
			{
				worldServerID = (long)args.QueueData.SceneLoadData.Params.ServerParams[0];
			}
			// if the world scene is < 0 it is a local scene
			if (worldServerID < 0)
			{
				return;
			}

			// configure the mapping for this specific world scene
			if (!worldScenes.TryGetValue(worldServerID, out Dictionary<string, Dictionary<int, SceneInstanceDetails>> scenes))
			{
				worldScenes.Add(worldServerID, scenes = new Dictionary<string, Dictionary<int, SceneInstanceDetails>>());
			}
			if (!scenes.TryGetValue(scene.name, out Dictionary<int, SceneInstanceDetails> handles))
			{
				scenes.Add(scene.name, handles = new Dictionary<int, SceneInstanceDetails>());
			}
			if (!handles.ContainsKey(scene.handle))
			{
				// configure the scene physics ticker
				GameObject gob = new GameObject("PhysicsTicker");
				PhysicsTicker physicsTicker = gob.AddComponent<PhysicsTicker>();
				physicsTicker.InitializeOnce(scene.GetPhysicsScene(), Server.NetworkManager.TimeManager);
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gob, scene);

				// cache the newly loaded scene
				handles.Add(scene.handle, new SceneInstanceDetails()
				{
					WorldServerID = worldServerID,
					Name = scene.name,
					Handle = scene.handle,
					CharacterCount = 0,
				});

				// save the loaded scene information to the database
				using var dbContext = Server.DbContextFactory.CreateDbContext();
				Debug.Log("Scene Server System: Loaded Scene " + scene.name + ":" + scene.handle);
				LoadedSceneService.Add(dbContext, id, worldServerID, scene.name, scene.handle);
				dbContext.SaveChanges();
			}
			else
			{
				throw new UnityException("Scene Server System: Duplicate scene handles!!");
			}
		}

		public bool TryGetSceneInstanceDetails(long worldServerID, string sceneName, int sceneHandle, out SceneInstanceDetails instanceDetails)
		{
			instanceDetails = default;

			if (worldScenes.TryGetValue(worldServerID, out Dictionary<string, Dictionary<int, SceneInstanceDetails>> scenes))
			{
				if (scenes.TryGetValue(sceneName, out Dictionary<int, SceneInstanceDetails> instances))
				{
					if (instances.TryGetValue(sceneHandle, out instanceDetails))
					{
						return true;
					}
				}
			}
			return false;
		}

		public bool TryLoadSceneForConnection(NetworkConnection conn, SceneInstanceDetails instance)
		{
			UnityEngine.SceneManagement.Scene scene = SceneManager.GetScene(instance.Handle);
			if (scene != null && scene.IsValid() && scene.isLoaded)
			{
				SceneLoadData sld = new SceneLoadData(instance.Handle);
				sld.ReplaceScenes = ReplaceOption.None;
				// will this prevent server from loading the scene again? we only want the client to load the scene here..
				sld.Options.AllowStacking = false;
				// scene unloading is controlled by the server
				sld.Options.AutomaticallyUnload = false;
				SceneManager.LoadConnectionScenes(conn, new SceneLoadData(scene));
				return true;
			}
			return false;
		}

		public void AssignPhysicsScene(Character character)
		{
			UnityEngine.SceneManagement.Scene scene = SceneManager.GetScene(character.SceneHandle);
			if (scene != null && scene.IsValid() && scene.isLoaded)
			{
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(character.gameObject, scene);
				character.Motor.SetPhysicsScene(scene.GetPhysicsScene());
			}
		}
	}
}