using FishNet.Connection;
using FishNet.Managing.Scened;
using SceneManager = FishNet.Managing.Scened.SceneManager;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server
{
	// Scene Manager handles the node services and heartbeat to World Server
	public class SceneServerSystem : ServerBehaviour
	{
		private LocalConnectionState serverState;

		public WorldSceneDetailsCache WorldSceneDetailsCache;

		private long id;
		private bool locked = false;
		private float nextPulse = 0.0f;

		public float PulseRate = 5.0f;

		public long ID { get { return id; } }

		// <worldID, <sceneName, <sceneHandle, details>>>
		public Dictionary<long, Dictionary<string, Dictionary<int, SceneInstanceDetails>>> worldScenes = new Dictionary<long, Dictionary<string, Dictionary<int, SceneInstanceDetails>>>();

		public override void InitializeOnce()
		{
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				throw new UnityException("Failed to get dbContext.");
			}

			if (ServerManager != null &&
				Server != null &&
				Server.NetworkManager.SceneManager != null)
			{
				Server.NetworkManager.SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;
				//Server.NetworkManager.SceneManager.OnUnloadEnd += SceneManager_OnUnloadEnd;
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;

				if (Server.TryGetServerIPAddress(out ServerAddress server) &&
					ServerBehaviour.TryGet(out CharacterSystem characterSystem))
				{
					int characterCount = characterSystem.ConnectionCharacters.Count;

					if (Server.Configuration.TryGetString("ServerName", out string name))
					{
						SceneServerService.Add(dbContext, server.address, server.port, characterCount, locked, out id);
						Debug.Log("Scene Server System: Added Scene Server to Database: [" + id + "] " + name + ":" + server.address + ":" + server.port);
					}
				}
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				throw new UnityException("Failed to get dbContext.");
			}

			if (ServerManager != null)
			{
				if (Server != null &&
					Server.Configuration.TryGetString("ServerName", out string name))
				{
					Debug.Log("Scene Server System: Removing Scene Server: " + id);
					SceneServerService.Delete(dbContext, id);
					LoadedSceneService.Delete(dbContext, id);
				}
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started)
			{
				if (nextPulse < 0)
				{
					nextPulse = PulseRate;

					if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
					{
						// TODO: maybe this one should exist....how expensive will this be to run on update?
						using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
						//Debug.Log("Scene Server System: Pulse");
						int characterCount = characterSystem.ConnectionCharacters.Count;
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
										//Debug.Log("Scene Server System: " + sceneDetails.Value.Name + ":" + sceneDetails.Value.WorldServerID + ":" + sceneDetails.Value.Handle + " Pulse");
										LoadedSceneService.Pulse(dbContext, sceneDetails.Key, sceneDetails.Value.CharacterCount);
									}
								}
							}
						}

						// process pending scenes
						PendingSceneEntity pending = PendingSceneService.Dequeue(dbContext);
						if (pending != null)
						{
							Debug.Log("Scene Server System: Dequeued Pending Scene Load request World:" + pending.WorldServerID + " Scene:" + pending.SceneName);
							ProcessSceneLoadRequest(pending.WorldServerID, pending.SceneName);
						}
					}
				}
				nextPulse -= Time.deltaTime;
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
				// TODO kick players waiting for this scene otherwise they get stuck
				return;
			}

			// pre cache the scene on the server
			SceneLookupData lookupData = new SceneLookupData(sceneName);
			SceneLoadData sld = new SceneLoadData(lookupData)
			{
				ReplaceScenes = ReplaceOption.None,
				Options = new LoadOptions
				{
					AllowStacking = true,
					AutomaticallyUnload = false,
					LocalPhysics = UnityEngine.SceneManagement.LocalPhysicsMode.Physics3D,
				},
				Params = new LoadParams()
				{
					ServerParams = new object[]
					{
						worldServerID
					}
				},
			};
			Server.NetworkManager.SceneManager.LoadConnectionScenes(sld);
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
				// ensure the scene has a physics ticker
				GameObject gob = new GameObject("PhysicsTicker");
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gob, scene);
				PhysicsTicker physicsTicker = gob.AddComponent<PhysicsTicker>();
				physicsTicker.InitializeOnce(scene.GetPhysicsScene(), Server.NetworkManager.TimeManager);

				// cache the newly loaded scene
				handles.Add(scene.handle, new SceneInstanceDetails()
				{
					WorldServerID = worldServerID,
					Name = scene.name,
					Handle = scene.handle,
					CharacterCount = 0,
				});

				// save the loaded scene information to the database
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				Debug.Log("Scene Server System: Loaded Scene " + scene.name + ":" + scene.handle);
				LoadedSceneService.Add(dbContext, id, worldServerID, scene.name, scene.handle);
			}
			else
			{
				throw new UnityException("Scene Server System: Duplicate scene handles!!");
			}
		}

		public bool TryGetSceneInstanceDetails(long worldServerID, string sceneName, int sceneHandle, out SceneInstanceDetails instanceDetails)
		{
			instanceDetails = default;

			if (worldScenes != null &&
				worldScenes.TryGetValue(worldServerID, out Dictionary<string, Dictionary<int, SceneInstanceDetails>> scenes))
			{
				if (scenes != null &&
					scenes.TryGetValue(sceneName, out Dictionary<int, SceneInstanceDetails> instances))
				{
					if (instances != null &&
						instances.TryGetValue(sceneHandle, out instanceDetails))
					{
						return true;
					}
				}
			}
			return false;
		}

		public bool TryLoadSceneForConnection(NetworkConnection connection, IPlayerCharacter character, SceneInstanceDetails instance)
		{
			Scene scene = SceneManager.GetScene(instance.Handle);
			if (scene != null && scene.IsValid() && scene.isLoaded)
			{
				SceneLookupData lookupData = new SceneLookupData(instance.Handle);
				SceneLoadData sld = new SceneLoadData(lookupData)
				{
					ReplaceScenes = ReplaceOption.None,
					Options = new LoadOptions
					{
						AutomaticallyUnload = false,
					},
					PreferredActiveScene = new PreferredScene(lookupData),
				};
				Server.NetworkManager.SceneManager.LoadConnectionScenes(connection, sld);
				return true;
			}
			return false;
		}
	}
}