using FishNet.Transporting;
using FishNet.Managing.Scened;
using FishNet.Managing.Client;
using UnityEngine;
using System;
using System.Collections.Generic;
using FishNet.Connection;

namespace Server
{
	// Scene Manager handles the node services and heartbeat to World Server
	public class SceneServerSystem : ServerBehaviour
	{
		public SceneManager SceneManager;

		private SceneServerAuthenticator loginAuthenticator;
		private LocalConnectionState serverState;

		public WorldSceneDetailsCache worldSceneDetailsCache;

		public bool locked = false;
		public float pulseRate = 10.0f;
		private float nextPulse = 0.0f;

		public event Action<string> OnSceneLoadComplete;

		// sceneName, <sceneHandle, details>
		public Dictionary<string, Dictionary<int, SceneInstanceDetails>> scenes = new Dictionary<string, Dictionary<int, SceneInstanceDetails>>();

		public override void InitializeOnce()
		{
			nextPulse = pulseRate;

			if (ServerManager != null &&
				ClientManager != null &&
				SceneManager != null)
			{
				ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
				ClientManager.OnRemoteConnectionState += ClientManager_OnRemoteConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started && Server.configuration.TryGetString("ServerName", out string name))
			{
				nextPulse -= Time.deltaTime;
				if (nextPulse < 0)
				{
					nextPulse = pulseRate;

					//Debug.Log("[" + DateTime.UtcNow + "] " + name + ": Pulse");

					// keeps the connection alive and updates character count on the world server
					ClientManager.Broadcast(new ScenePulseBroadcast()
					{
						name = name,
						sceneInstanceDetails = RebuildSceneInstanceDetails(),
					});
				}
			}
		}

		private void OnApplicationQuit()
		{
		}

		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			loginAuthenticator = FindObjectOfType<SceneServerAuthenticator>();
			if (loginAuthenticator == null)
				return;

			serverState = args.ConnectionState;

			if (args.ConnectionState == LocalConnectionState.Started)
			{
				loginAuthenticator.OnSceneServerAuthenticationResult += Authenticator_OnSceneServerAuthenticationResult;

				SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;

				ClientManager.RegisterBroadcast<SceneListBroadcast>(OnClientSceneListBroadcastReceived);
				ClientManager.RegisterBroadcast<SceneLoadBroadcast>(OnClientSceneLoadBroadcastReceived);
				ClientManager.RegisterBroadcast<SceneUnloadBroadcast>(OnClientSceneUnloadBroadcastReceived);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				loginAuthenticator.OnSceneServerAuthenticationResult -= Authenticator_OnSceneServerAuthenticationResult;

				SceneManager.OnLoadEnd -= SceneManager_OnLoadEnd;

				ClientManager.UnregisterBroadcast<SceneListBroadcast>(OnClientSceneListBroadcastReceived);
				ClientManager.UnregisterBroadcast<SceneLoadBroadcast>(OnClientSceneLoadBroadcastReceived);
				ClientManager.UnregisterBroadcast<SceneUnloadBroadcast>(OnClientSceneUnloadBroadcastReceived);
			}
		}

		public List<SceneInstanceDetails> RebuildSceneInstanceDetails()
		{
			List<SceneInstanceDetails> newDetails = new List<SceneInstanceDetails>();
			if (scenes != null && scenes.Count > 0)
			{
				foreach (Dictionary<int, SceneInstanceDetails> instances in scenes.Values)
				{
					foreach (SceneInstanceDetails instance in instances.Values)
					{
						newDetails.Add(instance);
					}
				}
			}
			return newDetails;
		}

		// we only track scene handles here for scene stacking, the SceneManager has the real Scene reference
		private void SceneManager_OnLoadEnd(SceneLoadEndEventArgs args)
		{
			foreach (UnityEngine.SceneManagement.Scene scene in args.LoadedScenes)
			{
				if (!scenes.TryGetValue(scene.name, out Dictionary<int, SceneInstanceDetails> handles))
				{
					handles = new Dictionary<int, SceneInstanceDetails>();
					scenes.Add(scene.name, handles);
				}

				if (!handles.ContainsKey(scene.handle))
				{
					GameObject gob = new GameObject("PhysicsTicker");
					PhysicsTicker physicsTicker = gob.AddComponent<PhysicsTicker>();
					physicsTicker.InitializeOnce(scene.GetPhysicsScene());
					UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gob, scene);

					// cache the newly loaded scene
					handles.Add(scene.handle, new SceneInstanceDetails()
					{
						name = scene.name,
						handle = scene.handle,
						clientCount = 0,
					});
				}

				// tell the world server we loaded the scene
				ClientManager.Broadcast(new SceneLoadBroadcast()
				{
					sceneName = scene.name,
					handle = scene.handle,
				});

				// tell the clients the scene is loaded
				OnSceneLoadComplete?.Invoke(scene.name);
			}
		}

		private void ClientManager_OnRemoteConnectionState(RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				List<string> scenesToUnload = new List<string>(scenes.Keys);
				SceneManager.UnloadConnectionScenes(new SceneUnloadData(scenesToUnload));
				scenes.Clear();
			}
		}

		private void Authenticator_OnSceneServerAuthenticationResult()
		{
			// tell the world server our information
			ClientManager.Broadcast(new SceneServerDetailsBroadcast()
			{
				address = Server.address,
				port = Server.port,
				sceneInstanceDetails = RebuildSceneInstanceDetails(),
			});
		}

		/// <summary>
		/// The world server requested an updated scene list from this server.
		/// </summary>
		private void OnClientSceneListBroadcastReceived(SceneListBroadcast msg)
		{
			// tell the world server what scenes we are hosting
			ClientManager.Broadcast(new SceneListBroadcast()
			{
				sceneInstanceDetails = RebuildSceneInstanceDetails(),
			});
		}

		/// <summary>
		/// The world server requested to load a scene.
		/// </summary>
		private void OnClientSceneLoadBroadcastReceived(SceneLoadBroadcast msg)
		{
			if (worldSceneDetailsCache == null ||
				!worldSceneDetailsCache.scenes.Contains(msg.sceneName))
			{
				Debug.Log("[" + DateTime.UtcNow + "] SceneServerManager: Scene is missing from the cache. Unable to load the scene.");
				return;
			}

			SceneLoadData sld = new SceneLoadData(msg.sceneName);
			sld.ReplaceScenes = ReplaceOption.None;
			sld.Options.AllowStacking = true;
			sld.Options.LocalPhysics = UnityEngine.SceneManagement.LocalPhysicsMode.Physics3D;
			// scene unloading is controlled by the server
			sld.Options.AutomaticallyUnload = false;

			SceneManager.LoadConnectionScenes(sld);
		}

		/// <summary>
		/// The world server requested to unload a scene.
		/// </summary>
		private void OnClientSceneUnloadBroadcastReceived(SceneUnloadBroadcast msg)
		{
			// TODO migrate players connected to this scene to another scene instance or scene server with the scene

			SceneUnloadData sud = new SceneUnloadData(new int[] { msg.handle });

			// unload a scene
			SceneManager.UnloadConnectionScenes(sud);

			// remove the scene from the scene list
			if (scenes.TryGetValue(msg.sceneName, out Dictionary<int, SceneInstanceDetails> instances))
			{
				instances.Remove(msg.handle);
			}

			// tell the world server we unloaded the scene
			ClientManager.Broadcast(msg);
		}

		public bool TryGetValidScene(string sceneName, out SceneInstanceDetails instanceDetails)
		{
			instanceDetails = default;

			if (scenes.TryGetValue(sceneName, out Dictionary<int, SceneInstanceDetails> instances))
			{
				bool found = false;
				foreach (SceneInstanceDetails instance in instances.Values)
				{
					if (!found)
					{
						instanceDetails = instance;
						found = true;
						continue;
					}
					if (instanceDetails.clientCount > instance.clientCount)
					{
						instanceDetails = instance;
					}
				}
				if (found) return true;
			}
			return false;
		}

		public bool TryLoadSceneForConnection(NetworkConnection conn, SceneInstanceDetails instance)
		{
			UnityEngine.SceneManagement.Scene scene = SceneManager.GetScene(instance.handle);
			if (scene != null && scene.IsValid() && scene.isLoaded)
			{
				SceneLoadData sld = new SceneLoadData(instance.handle);
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
			UnityEngine.SceneManagement.Scene scene = SceneManager.GetScene(character.sceneHandle);
			if (scene != null && scene.IsValid() && scene.isLoaded)
			{
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(character.gameObject, scene);
				character.Motor.SetPhysicsScene(scene.GetPhysicsScene());
			}
		}
	}
}