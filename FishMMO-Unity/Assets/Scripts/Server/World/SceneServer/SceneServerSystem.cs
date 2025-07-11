﻿using FishNet.Connection;
using FishNet.Managing.Scened;
using SceneManager = FishNet.Managing.Scened.SceneManager;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Database.Npgsql.Entities;
using System.Runtime.CompilerServices;

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
		public readonly Dictionary<long, Dictionary<string, Dictionary<int, SceneInstanceDetails>>> WorldScenes = new Dictionary<long, Dictionary<string, Dictionary<int, SceneInstanceDetails>>>();
		public readonly Dictionary<int, string> SceneNameByHandle = new Dictionary<int, string>();

		private Dictionary<long, SceneEntity> pendingScenes = new Dictionary<long, SceneEntity>();

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
				Server.NetworkManager.SceneManager.OnUnloadEnd += SceneManager_OnUnloadEnd;
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;

				if (Server.TryGetServerIPAddress(out ServerAddress server) &&
					ServerBehaviour.TryGet(out CharacterSystem characterSystem))
				{
					int characterCount = characterSystem.ConnectionCharacters.Count;

					if (Configuration.GlobalSettings.TryGetString("ServerName", out string name))
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
					Configuration.GlobalSettings.TryGetString("ServerName", out string name))
				{
					Log.Debug("SceneServerSystem", "Scene Server System: Removing Scene Server scenes: " + id);
					SceneService.Delete(dbContext, id);
				}

				Server.NetworkManager.SceneManager.OnLoadEnd -= SceneManager_OnLoadEnd;
				Server.NetworkManager.SceneManager.OnUnloadEnd -= SceneManager_OnUnloadEnd;
				ServerManager.OnServerConnectionState -= ServerManager_OnServerConnectionState;
			}

			if (ServerBehaviour.TryGet(out CharacterSystem characterSystem))
			{
				characterSystem.OnDisconnect -= CharacterSystem_OnDisconnect;
				characterSystem.OnAfterLoadCharacter -= CharacterSystem_OnAfterLoadCharacter;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
		}

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

		private void AdjustSceneCharacterCount(long worldServerID, string sceneName, int sceneHandle, int amount)
		{
			// update scene instance details
			if (TryGetSceneInstanceDetails(worldServerID,
											sceneName,
											sceneHandle,
											out SceneInstanceDetails instance))
			{
				instance.AddCharacterCount(amount);
			}
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started)
			{
				if (nextPulse < 0)
				{
					nextPulse = PulseRate;

					if (Server != null &&
						ServerBehaviour.TryGet(out CharacterSystem characterSystem))
					{
						// TODO: maybe this one should exist....how expensive will this be to run on update?
						using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
						//Log.Debug("SceneServerSystem", "Scene Server System: Pulse");
						int characterCount = characterSystem.ConnectionCharacters.Count;
						SceneServerService.Pulse(dbContext, id, characterCount, locked);

						// process loaded scene pulse update
						if (WorldScenes != null)
						{
							foreach (Dictionary<string, Dictionary<int, SceneInstanceDetails>> sceneGroup in WorldScenes.Values)
							{
								foreach (Dictionary<int, SceneInstanceDetails> scenes in new List<Dictionary<int, SceneInstanceDetails>>(sceneGroup.Values))
								{
									foreach (SceneInstanceDetails sceneDetails in new List<SceneInstanceDetails>(scenes.Values))
									{
										if (sceneDetails.StalePulse)
										{
											double timeSinceLastExit = DateTime.UtcNow.Subtract(sceneDetails.LastExit).TotalMinutes;
											if (Configuration.GlobalSettings.TryGetInt("StaleSceneTimeout", out int result) &&
												timeSinceLastExit < result)
											{
												Log.Debug("SceneServerSystem", $"{sceneDetails.Name}:{sceneDetails.WorldServerID}{sceneDetails.Handle}:{sceneDetails.CharacterCount} Stale Pulse");
												SceneService.Pulse(dbContext, sceneDetails.Handle, sceneDetails.CharacterCount);
												continue;
											}

											//Log.Debug("SceneServerSystem", $"{sceneDetails.Name}:{sceneDetails.WorldServerID}{sceneDetails.Handle}:{sceneDetails.CharacterCount} Closing Stale Scene");

											// Unload the scene on the server
											UnloadScene(sceneDetails.Handle);
										}
										else
										{
											//Log.Debug("SceneServerSystem", $"{sceneDetails.Name}:{sceneDetails.WorldServerID}{sceneDetails.Handle}:{sceneDetails.CharacterCount} Pulse");
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
							Log.Debug("SceneServerSystem", "Scene Server System: Dequeued Pending Scene Load request World:" + pending.WorldServerID + " Scene:" + pending.SceneName);
							ProcessSceneLoadRequest(pending);
						}
					}
				}
				nextPulse -= Time.deltaTime;
			}
		}

		/// <summary>
		/// Process a single scene load request from the database.
		/// </summary>
		private void ProcessSceneLoadRequest(SceneEntity sceneEntity)
		{
			if (WorldSceneDetailsCache == null ||
				!WorldSceneDetailsCache.Scenes.Contains(sceneEntity.SceneName))
			{
				Log.Debug("SceneServerSystem", "Scene Server System: Scene is missing from the cache. Unable to load the scene.");
				// TODO kick players waiting for this scene otherwise they get stuck
				return;
			}

			pendingScenes[sceneEntity.ID] = sceneEntity;

			// Pre cache the scene on the server
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
			Server.NetworkManager.SceneManager.LoadConnectionScenes(sld);
		}

		// We only track scene handles here for scene stacking, the SceneManager has the real Scene reference
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
				//Log.Warning("SceneServerSystem", $"Failed to process scene. Invalid Server Parameter Length. Length is {args.QueueData.SceneLoadData.Params.ServerParams.Length}");
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
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				Log.Debug("SceneServerSystem", $"Failed to load Database Scene[{sceneEntity.ID}].");
				SceneService.UpdateStatus(dbContext, sceneEntity.ID, SceneStatus.Failed);
			}
			else
			{
				Scene scene = args.LoadedScenes[0];

				// Process the scene by adding it to the world dictionary mappings.
				ProcessScene(scene, sceneType, sceneEntity.WorldServerID);

				// Save the loaded scene information to the database
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				Log.Debug("SceneServerSystem", $"Saved {sceneType} scene {scene.name}:{scene.handle} to the database.");
				SceneService.SetReady(dbContext, id, sceneEntity.WorldServerID, scene.name, scene.handle);
			}
		}

		private void ProcessScene(Scene scene, SceneType sceneType, long worldServerID)
		{
			// Configure the mapping for this specific world scene
			if (!WorldScenes.TryGetValue(worldServerID, out Dictionary<string, Dictionary<int, SceneInstanceDetails>> scenes))
			{
				WorldScenes.Add(worldServerID, scenes = new Dictionary<string, Dictionary<int, SceneInstanceDetails>>());
			}
			if (!scenes.TryGetValue(scene.name, out Dictionary<int, SceneInstanceDetails> handles))
			{
				scenes.Add(scene.name, handles = new Dictionary<int, SceneInstanceDetails>());
			}
			if (!handles.ContainsKey(scene.handle))
			{
				// Ensure the scene has a physics ticker
				GameObject gob = new GameObject("PhysicsTicker");
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gob, scene);
				PhysicsTicker physicsTicker = gob.AddComponent<PhysicsTicker>();
				physicsTicker.InitializeOnce(scene.GetPhysicsScene(), Server.NetworkManager.TimeManager);

				// Cache the newly loaded scene
				handles.Add(scene.handle, new SceneInstanceDetails()
				{
					WorldServerID = worldServerID,
					SceneServerID = id,
					Name = scene.name,
					SceneType = sceneType,
					Handle = scene.handle,
					CharacterCount = 0,
				});

				Log.Debug("SceneServerSystem", "$New scene handle added for {worldServerID}:{scene.name}:{scene.handle}");

				SceneNameByHandle.Add(scene.handle, scene.name);
			}
			else
			{
				throw new UnityException("SceneServerSystem: Duplicate scene handles!!");
			}
		}

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

				foreach (Dictionary<string, Dictionary<int, SceneInstanceDetails>> sceneGroup in WorldScenes.Values)
				{
					foreach (Dictionary<int, SceneInstanceDetails> scene in sceneGroup.Values)
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

		public bool TryGetSceneInstanceDetails(long worldServerID, string sceneName, int sceneHandle, out SceneInstanceDetails instanceDetails)
		{
			instanceDetails = default;

			if (WorldScenes != null &&
				WorldScenes.TryGetValue(worldServerID, out Dictionary<string, Dictionary<int, SceneInstanceDetails>> scenes))
			{
				if (scenes != null &&
					!string.IsNullOrEmpty(sceneName) &&
					scenes.TryGetValue(sceneName, out Dictionary<int, SceneInstanceDetails> instances))
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
				/*else
				{
					Log.Debug("SceneServerSystem", $"Failed to find scene by name: {sceneName}");
				}*/
			}
			/*else
			{
				Log.Debug("SceneServerSystem", $"Failed to find world scene: {worldServerID}");
			}*/
			return false;
		}

		public bool TryLoadSceneForConnection(NetworkConnection connection, SceneInstanceDetails instance)
		{
			Scene scene = SceneManager.GetScene(instance.Handle);
			if (scene != null && scene.IsValid() && scene.isLoaded)
			{
				//Log.Debug("SceneServerSystem", $"Scene: {instance.Name} - {instance.Handle} found in SceneManager.");

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
			else
			{
				Log.Debug("SceneServerSystem", $"Scene: {instance.Name}|{instance.Handle} not found in SceneManager.");
			}
			return false;
		}

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
			Server.NetworkManager.SceneManager.UnloadConnectionScenes(connection, sud);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UnloadScene(int handle)
		{
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				Log.Warning("SceneServerSystem", "Failed to create dbContext during Scene Unload.");
				return;
			}

			// Remove the scene details from the database, we do this immediately upon an Unload request to prevent new clients from connecting to it.
			SceneService.Delete(dbContext, id, handle);

			SceneUnloadData sud = new SceneUnloadData()
			{
				SceneLookupDatas = new SceneLookupData[]
				{
					new SceneLookupData(handle),
				},
			};
			Server.NetworkManager.SceneManager.UnloadConnectionScenes(sud);
		}
	}
}