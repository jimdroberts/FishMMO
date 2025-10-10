using FishNet.Connection;
using FishNet.Managing.Scened;
using SceneManager = FishNet.Managing.Scened.SceneManager;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Database.Npgsql.Entities;
using System.Runtime.CompilerServices;

namespace FishMMO.Server.Implementation.SceneServer
{
	/// <summary>
	/// Manages scene server node services, scene loading/unloading, and heartbeat updates to the world server.
	/// Tracks scene instances, handles connection events, and synchronizes scene state with the database.
	/// </summary>
	public class SceneServerSystem : ServerBehaviour, ISceneServerSystem<NetworkConnection>
	{
		/// <summary>
		/// Current connection state of the server.
		/// </summary>
		private LocalConnectionState serverState;

		/// <summary>
		/// Database ID for this scene server instance.
		/// </summary>
		private long id;
		/// <summary>
		/// Indicates whether the scene server is locked (not accepting new connections).
		/// </summary>
		private bool locked = false;
		/// <summary>
		/// Time remaining until the next heartbeat pulse.
		/// </summary>
		private float nextPulse = 0.0f;
		/// <summary>
		/// Interval (in seconds) between heartbeat pulses to the database.
		/// </summary>
		[SerializeField]
		private float pulseRate = 5.0f;
		/// <summary>
		/// Cache of world scene details, including max clients per scene.
		/// </summary>
		[SerializeField]
		private WorldSceneDetailsCache worldSceneDetailsCache;

		/// <summary>
		/// Gets the database ID for this scene server instance.
		/// </summary>
		public long ID { get { return id; } }
		/// <summary>
		/// When true, the scene server is locked and will refuse new load/unload requests.
		/// </summary>
		public bool IsLocked { get { return locked; } }
		/// <summary>
		/// Interval (in seconds) between heartbeat pulses to the database.
		/// </summary>
		public float PulseRate { get { return pulseRate; } }
		/// <summary>
		/// Cache of world scene details, including max clients per scene.
		/// </summary>
		public WorldSceneDetailsCache WorldSceneDetailsCache { get { return worldSceneDetailsCache; } }

		/// <summary>
		/// Maps world server IDs to scene names and handles, tracking all loaded scene instances.
		/// </summary>
		public Dictionary<long, Dictionary<string, Dictionary<int, ISceneInstanceDetails>>> WorldScenes { get; } = new Dictionary<long, Dictionary<string, Dictionary<int, ISceneInstanceDetails>>>();
		/// <summary>
		/// Maps scene handles to scene names for quick lookup.
		/// </summary>
		public Dictionary<int, string> SceneNameByHandle { get; } = new Dictionary<int, string>();

		/// <summary>
		/// Tracks pending scene load requests by scene ID.
		/// </summary>
		private Dictionary<long, SceneEntity> pendingScenes = new Dictionary<long, SceneEntity>();

		/// <summary>
		/// Called once to initialize the scene server system. Registers the server in the database and subscribes to connection and scene events.
		/// </summary>
		public override void InitializeOnce()
		{
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				throw new UnityException("Failed to get dbContext.");
			}

			if (ServerManager != null &&
				Server != null &&
				Server.NetworkWrapper.NetworkManager.SceneManager != null)
			{
				Server.NetworkWrapper.NetworkManager.SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;
				Server.NetworkWrapper.NetworkManager.SceneManager.OnUnloadEnd += SceneManager_OnUnloadEnd;
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;

				if (Server.AddressProvider.TryGetServerIPAddress(out ServerAddress server) &&
					Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
				{
					int characterCount = characterSystem.ConnectionCharacters.Count;

					if (Server.Configuration.TryGetString("ServerName", out string name))
					{
						SceneServerService.Add(dbContext, name, server.Address, server.Port, characterCount, locked, out id);
						SceneService.Delete(dbContext, id);
					}

					characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;
					characterSystem.OnAfterLoadCharacter += CharacterSystem_OnAfterLoadCharacter;
				}
			}
			else
			{
				enabled = false;
			}
		}

		/// <summary>
		/// Called when the system is being destroyed. Unsubscribes from events and deletes scene data from the database.
		/// </summary>
		public override void Destroying()
		{
			using var dbContext = Server?.CoreServer?.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				throw new UnityException("Failed to get dbContext.");
			}

			if (ServerManager != null)
			{
				if (Server != null &&
					Server.Configuration.TryGetString("ServerName", out string name))
				{
					Log.Debug("SceneServerSystem", "Scene Server System: Removing Scene Server scenes: " + id);
					SceneService.Delete(dbContext, id);
				}

				Server.NetworkWrapper.NetworkManager.SceneManager.OnLoadEnd -= SceneManager_OnLoadEnd;
				Server.NetworkWrapper.NetworkManager.SceneManager.OnUnloadEnd -= SceneManager_OnUnloadEnd;
				ServerManager.OnServerConnectionState -= ServerManager_OnServerConnectionState;
			}

			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnDisconnect -= CharacterSystem_OnDisconnect;
				characterSystem.OnAfterLoadCharacter -= CharacterSystem_OnAfterLoadCharacter;
			}
		}

		/// <summary>
		/// Handles changes in the server's connection state.
		/// </summary>
		/// <param name="args">Connection state arguments.</param>
		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
		}

		/// <summary>
		/// Handles character disconnect events, adjusting scene character counts.
		/// </summary>
		/// <param name="conn">Network connection.</param>
		/// <param name="character">Player character that disconnected.</param>
		private void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character.IsInInstance())
			{
				AdjustSceneCharacterCount(character.WorldServerID, character.InstanceSceneName, character.InstanceSceneHandle, -1);
			}
			else
			{
				AdjustSceneCharacterCount(character.WorldServerID, character.SceneName, character.SceneHandle, -1);
			}
		}

		/// <summary>
		/// Handles character load events, adjusting scene character counts.
		/// </summary>
		/// <param name="conn">Network connection.</param>
		/// <param name="character">Player character that loaded.</param>
		private void CharacterSystem_OnAfterLoadCharacter(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character.IsInInstance())
			{
				AdjustSceneCharacterCount(character.WorldServerID, character.InstanceSceneName, character.InstanceSceneHandle, 1);
			}
			else
			{
				AdjustSceneCharacterCount(character.WorldServerID, character.SceneName, character.SceneHandle, 1);
			}
		}

		/// <summary>
		/// Adjusts the character count for a specific scene instance.
		/// </summary>
		/// <param name="worldServerID">World server ID.</param>
		/// <param name="sceneName">Scene name.</param>
		/// <param name="sceneHandle">Scene handle.</param>
		/// <param name="amount">Amount to adjust by (+1 or -1).</param>
		private void AdjustSceneCharacterCount(long worldServerID, string sceneName, int sceneHandle, int amount)
		{
			// update scene instance details
			if (TryGetSceneInstanceDetails(worldServerID,
											sceneName,
											sceneHandle,
											out ISceneInstanceDetails instance))
			{
				instance.AddCharacterCount(amount);
			}
		}

		/// <summary>
		/// Unity LateUpdate callback. Periodically sends heartbeat pulses to the database and processes scene state.
		/// </summary>
		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started)
			{
				if (nextPulse < 0)
				{
					nextPulse = PulseRate;

					if (Server != null &&
						Server.BehaviourRegistry != null &&
						Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
					{
						// Send heartbeat pulse to the database with current character count.
						using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
						int characterCount = characterSystem.ConnectionCharacters.Count;
						SceneServerService.Pulse(dbContext, id, characterCount, locked);

						// Process loaded scene pulse update
						if (WorldScenes != null)
						{
							foreach (Dictionary<string, Dictionary<int, ISceneInstanceDetails>> sceneGroup in WorldScenes.Values)
							{
								foreach (Dictionary<int, ISceneInstanceDetails> scenes in new List<Dictionary<int, ISceneInstanceDetails>>(sceneGroup.Values))
								{
									foreach (ISceneInstanceDetails sceneDetails in new List<ISceneInstanceDetails>(scenes.Values))
									{
										if (sceneDetails.StalePulse)
										{
											double timeSinceLastExit = DateTime.UtcNow.Subtract(sceneDetails.LastExit).TotalMinutes;
											if (Server.Configuration.TryGetInt("StaleSceneTimeout", out int result) &&
												timeSinceLastExit < result)
											{
												Log.Debug("SceneServerSystem", $"{sceneDetails.Name}:{sceneDetails.WorldServerID}{sceneDetails.Handle}:{sceneDetails.CharacterCount} Stale Pulse");
												SceneService.Pulse(dbContext, sceneDetails.Handle, sceneDetails.CharacterCount);
												continue;
											}

											// Unload the scene on the server if it is stale.
											UnloadScene(sceneDetails.Handle);
										}
										else
										{
											SceneService.Pulse(dbContext, sceneDetails.Handle, sceneDetails.CharacterCount);
										}
									}
								}
							}
						}

						// Process pending scenes
						SceneEntity pending = SceneService.Dequeue(dbContext);
						if (pending != null)
						{
							Log.Debug("SceneServerSystem", $"Scene Server System: Dequeued Pending Scene Load request World:{pending.WorldServerID} Scene:{pending.SceneName}");
							ProcessSceneLoadRequest(pending);
						}
					}
				}
				nextPulse -= Time.deltaTime;
			}
		}

		/// <summary>
		/// Processes a single scene load request from the database, pre-caching and loading the scene.
		/// </summary>
		/// <param name="sceneEntity">Scene entity to process.</param>
		private void ProcessSceneLoadRequest(SceneEntity sceneEntity)
		{
			if (WorldSceneDetailsCache == null ||
				!WorldSceneDetailsCache.Scenes.Contains(sceneEntity.SceneName))
			{
				Log.Debug("SceneServerSystem", "Scene Server System: Scene is missing from the cache. Unable to load the scene.");
				// TODO: kick players waiting for this scene otherwise they get stuck
				return;
			}

			pendingScenes[sceneEntity.ID] = sceneEntity;

			// Pre-cache the scene on the server
			SceneLookupData lookupData = new SceneLookupData(sceneEntity.SceneName);
			SceneLoadData sld = new SceneLoadData(lookupData)
			{
				ReplaceScenes = ReplaceOption.None,
				Options = new LoadOptions
				{
					AllowStacking = true,
					AutomaticallyUnload = false,
					LocalPhysics = LocalPhysicsMode.Physics3D,
				},
				Params = new LoadParams()
				{
					ServerParams = new object[]
					{
						sceneEntity.ID,
					},
				},
			};
			Server.NetworkWrapper.NetworkManager.SceneManager.LoadConnectionScenes(sld);
		}

		/// <summary>
		/// Handles scene load completion events, updating mappings and database state.
		/// </summary>
		/// <param name="args">Scene load end event arguments.</param>
		private void SceneManager_OnLoadEnd(SceneLoadEndEventArgs args)
		{
			const int UNKNOWN_WORLD_ID = -1;

			// If ServerParams are missing or there are no elements we should ignore processing this scene load.
			if (args.QueueData.SceneLoadData.Params.ServerParams == null)
			{
				Log.Warning("SceneServerSystem", "Failed to process scene. Invalid Server Parameters.");
				return;
			}

			if (args.QueueData.SceneLoadData.Params.ServerParams.Length < 1)
			{
				return;
			}

			if (!pendingScenes.TryGetValue((long)args.QueueData.SceneLoadData.Params.ServerParams[0], out SceneEntity sceneEntity))
			{
				Log.Warning("SceneServerSystem", "Pending Scene does not exist!");
				return;
			}

			pendingScenes.Remove(sceneEntity.ID);

			if (sceneEntity.WorldServerID == UNKNOWN_WORLD_ID)
			{
				Log.Warning("SceneServerSystem", "Failed to get World Server ID.");
				return;
			}

			SceneType sceneType = (SceneType)sceneEntity.SceneType;
			if (sceneType == SceneType.Unknown)
			{
				Log.Warning("SceneServerSystem", "Unknown scene type.");
				return;
			}

			// If the load was unsuccessful, args.LoadedScenes will be empty.
			if (args.LoadedScenes == null || args.LoadedScenes.Length < 1)
			{
				// Save the loaded scene information to the database
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
				Log.Debug("SceneServerSystem", $"Failed to load Database Scene[{sceneEntity.ID}].");
				SceneService.UpdateStatus(dbContext, sceneEntity.ID, SceneStatus.Failed);
			}
			else
			{
				Scene scene = args.LoadedScenes[0];

				// Process the scene by adding it to the world dictionary mappings.
				ProcessScene(scene, sceneType, sceneEntity.WorldServerID);

				// Save the loaded scene information to the database
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
				Log.Debug("SceneServerSystem", $"Saved {sceneType} scene {scene.name}:{scene.handle} to the database.");
				SceneService.SetReady(dbContext, id, sceneEntity.WorldServerID, scene.name, scene.handle);
			}
		}

		/// <summary>
		/// Adds a loaded scene to the world scene mappings and sets up physics ticking.
		/// </summary>
		/// <param name="scene">The loaded Unity scene.</param>
		/// <param name="sceneType">Type of the scene.</param>
		/// <param name="worldServerID">World server ID.</param>
		private void ProcessScene(Scene scene, SceneType sceneType, long worldServerID)
		{
			// Configure the mapping for this specific world scene
			if (!WorldScenes.TryGetValue(worldServerID, out Dictionary<string, Dictionary<int, ISceneInstanceDetails>> scenes))
			{
				WorldScenes.Add(worldServerID, scenes = new Dictionary<string, Dictionary<int, ISceneInstanceDetails>>());
			}
			if (!scenes.TryGetValue(scene.name, out Dictionary<int, ISceneInstanceDetails> handles))
			{
				scenes.Add(scene.name, handles = new Dictionary<int, ISceneInstanceDetails>());
			}
			if (!handles.ContainsKey(scene.handle))
			{
				// Ensure the scene has a physics ticker
				GameObject gob = new GameObject("PhysicsTicker");
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gob, scene);
				PhysicsTicker physicsTicker = gob.AddComponent<PhysicsTicker>();
				physicsTicker.InitializeOnce(scene.GetPhysicsScene(), Server.NetworkWrapper.NetworkManager.TimeManager);

				// Cache the newly loaded scene
				handles.Add(scene.handle, new SceneInstanceDetails()
				{
					WorldServerID = worldServerID,
					SceneServerID = id,
					Name = scene.name,
					SceneType = sceneType,
					Handle = scene.handle,
					CharacterCount = 0,
					LastExit = DateTime.UtcNow,
				});

				Log.Debug("SceneServerSystem", $"New scene handle added for {worldServerID}:{scene.name}:{scene.handle}");

				SceneNameByHandle.Add(scene.handle, scene.name);
			}
			else
			{
				throw new UnityException("SceneServerSystem: Duplicate scene handles!!");
			}
		}

		/// <summary>
		/// Handles scene unload completion events, removing scene mappings and cleaning up.
		/// </summary>
		/// <param name="args">Scene unload end event arguments.</param>
		public void SceneManager_OnUnloadEnd(SceneUnloadEndEventArgs args)
		{
			if (WorldScenes == null)
			{
				Log.Warning("SceneServerSystem", "No World Scenes found.");
				return;
			}

			if (args.UnloadedScenesV2.Count < 1)
			{
				Log.Warning("SceneServerSystem", "UnloadedScenesV2 failed to unload any scenes.");
				return;
			}

			for (int i = 0; i < args.UnloadedScenesV2.Count; ++i)
			{
				UnloadedScene unloaded = args.UnloadedScenesV2[i];

				foreach (Dictionary<string, Dictionary<int, ISceneInstanceDetails>> sceneGroup in WorldScenes.Values)
				{
					foreach (Dictionary<int, ISceneInstanceDetails> scene in sceneGroup.Values)
					{
						if (scene.ContainsKey(unloaded.Handle))
						{
							// Remove the scene
							scene.Remove(unloaded.Handle);
							SceneNameByHandle.Remove(unloaded.Handle);

							Log.Debug("SceneServerSystem", $"Unloaded scene handle: {unloaded.Handle}");

							break;
						}
					}
				}
			}
		}

		/// <summary>
		/// Attempts to get scene instance details for a given world server, scene name, and handle.
		/// </summary>
		/// <param name="worldServerID">World server ID.</param>
		/// <param name="sceneName">Scene name.</param>
		/// <param name="sceneHandle">Scene handle.</param>
		/// <param name="instanceDetails">Output instance details.</param>
		/// <returns>True if found, false otherwise.</returns>
		public bool TryGetSceneInstanceDetails(long worldServerID, string sceneName, int sceneHandle, out ISceneInstanceDetails instanceDetails)
		{
			instanceDetails = default;

			if (WorldScenes != null &&
				WorldScenes.TryGetValue(worldServerID, out Dictionary<string, Dictionary<int, ISceneInstanceDetails>> scenes))
			{
				if (scenes != null &&
					!string.IsNullOrEmpty(sceneName) &&
					scenes.TryGetValue(sceneName, out Dictionary<int, ISceneInstanceDetails> instances))
				{
					if (instances != null &&
						instances.TryGetValue(sceneHandle, out instanceDetails))
					{
						return true;
					}
					else
					{
						Log.Warning("SceneServerSystem", $"Scene handle {sceneHandle} not found in '{sceneName}'. Available: {string.Join(", ", instances.Keys)}");
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Attempts to load a scene for a connection if it is valid and loaded.
		/// </summary>
		/// <param name="connection">Network connection.</param>
		/// <param name="instance">Scene instance details.</param>
		/// <returns>True if scene was loaded for the connection, false otherwise.</returns>
		public bool TryLoadSceneForConnection(NetworkConnection connection, ISceneInstanceDetails instance)
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
				Server.NetworkWrapper.NetworkManager.SceneManager.LoadConnectionScenes(connection, sld);
				return true;
			}
			else
			{
				Log.Debug("SceneServerSystem", $"Scene: {instance.Name}|{instance.Handle} not found in SceneManager.");
			}
			return false;
		}

		/// <summary>
		/// Unloads a scene for a connection by scene name.
		/// </summary>
		/// <param name="connection">Network connection.</param>
		/// <param name="sceneName">Name of the scene to unload.</param>
		public void UnloadSceneForConnection(NetworkConnection connection, string sceneName)
		{
			SceneUnloadData sud = new SceneUnloadData()
			{
				SceneLookupDatas = new SceneLookupData[]
				{
					new SceneLookupData(sceneName),
				},
				Options = new UnloadOptions()
				{
					Mode = UnloadOptions.ServerUnloadMode.KeepUnused
				}
			};
			Server.NetworkWrapper.NetworkManager.SceneManager.UnloadConnectionScenes(connection, sud);
		}

		/// <summary>
		/// Unloads a scene by handle and removes its details from the database and server.
		/// </summary>
		/// <param name="handle">Scene handle to unload.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UnloadScene(int handle)
		{
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				Log.Warning("SceneServerSystem", "Failed to create dbContext during Scene Unload.");
				return;
			}

			// Remove the scene details from the database immediately upon an Unload request to prevent new clients from connecting to it.
			SceneService.Delete(dbContext, id, handle);

			SceneUnloadData sud = new SceneUnloadData()
			{
				SceneLookupDatas = new SceneLookupData[]
				{
					new SceneLookupData(handle),
				},
			};
			Server.NetworkWrapper.NetworkManager.SceneManager.UnloadConnectionScenes(sud);
		}
	}
}