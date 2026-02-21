using FishNet.Connection;
using FishNet.Managing.Scened;
using SceneManager = FishNet.Managing.Scened.SceneManager;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages scene server node services, scene loading/unloading, and heartbeat updates to the world server.
	/// Tracks scene instances, handles connection events, and synchronizes scene state with the database.
	/// Game logic and Broadcasts run synchronously on the main thread.
	/// Database operations are async to avoid blocking the main thread.
	/// Results from async DB queries that require main-thread state changes are marshalled
	/// via ISceneServerSystemMainThreadQueueData.
	/// </summary>
	[CreateAssetMenu(fileName = "SceneServerSystem", menuName = "FishMMO/Server/SceneServer/Scene Server System", order = 1)]
	[RequiresDataContainer(typeof(SceneInstanceMappingData))]
	[RequiresDataContainer(typeof(SceneServerRuntimeData))]
	[RequiresDataContainer(typeof(SceneServerSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class SceneServerSystem : ServerBehaviour, ISceneServerSystem<NetworkConnection>
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max queued scene-server actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Maximum pending scene load age in seconds before failing and removing the request.
		/// </summary>
		[Header("Pending Scene Protection")]
		[Tooltip("Maximum pending scene load age in seconds before failing the request")]
		[SerializeField] private float pendingSceneTimeoutSeconds = 60.0f;

		/// <summary>
		/// Interval in seconds between bounded pending-scene cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded pending-scene cleanup sweeps")]
		[SerializeField] private float pendingSceneSweepIntervalSeconds = 2.0f;

		/// <summary>
		/// Maximum number of expired pending scenes removed per sweep pass.
		/// </summary>
		[Tooltip("Maximum expired pending scenes removed per sweep")]
		[SerializeField] private int pendingSceneSweepMaxRemovals = 64;

		/// <summary>
		/// Interval (in seconds) between heartbeat pulses to the database.
		/// </summary>
		[SerializeField]
		private float pulseRate = 5.0f;

		/// <summary>
		/// Gets or sets the pulse rate for heartbeat updates.
		/// </summary>
		public float PulseRate { get { return pulseRate; } set { pulseRate = value; } }
		/// <summary>
		/// Cache of world scene details, including max clients per scene.
		/// </summary>
		[SerializeField]
		private WorldSceneDetailsCache worldSceneDetailsCache;

		/// <summary>
		/// Cache of world scene details, including max clients per scene.
		/// </summary>
		public WorldSceneDetailsCache WorldSceneDetailsCache { get { return worldSceneDetailsCache; } }

		/// <summary>
		/// Called once to initialize the scene server system. Registers the server in the database and subscribes to connection and scene events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("SceneServerSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneServerSystemMainThreadQueueData>(out _))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: ISceneServerSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: ISceneInstanceMappingData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				Log.Error("SceneServerSystem", "InitializeOnce: ISceneServerRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.NetworkWrapper.NetworkManager.SceneManager == null)
			{
				Log.Error("SceneServerSystem", "Failed to initialize: SceneManager not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.AddressProvider.TryGetServerIPAddress(out ServerAddress server))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: Could not get server IP address");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: ICharacterSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: ICharacterMappingData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.Database?.ServiceRegistry == null)
			{
				Log.Error("SceneServerSystem", "Failed to initialize: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			if (!Server.Database.ServiceRegistry.TryGet<ISceneServerService>(out var sceneServerService))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: ISceneServerService not found");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: ISceneService not found");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			// Scene manager events
			Server.NetworkWrapper.NetworkManager.SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;
			Server.NetworkWrapper.NetworkManager.SceneManager.OnUnloadEnd += SceneManager_OnUnloadEnd;

			// Character system events
			characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;
			characterSystem.OnAfterLoadCharacter += CharacterSystem_OnAfterLoadCharacter;

			// Register scene server in database (Task.Run avoids deadlock from
			// Unity's SynchronizationContext when blocking on async during init)
			int characterCount = characterMappingData.ConnectionCharacters.Count;
			DatabaseResult<(long ServerId, SceneServerData ServerData)> persistResult = Task.Run(() =>
				sceneServerService.PersistAsync(name, server.Address, server.Port, characterCount, runtimeData.IsLocked))
				.GetAwaiter().GetResult();

			if (!persistResult.IsSuccess)
			{
				Log.Error("SceneServerSystem", $"Failed to register scene server in database: {persistResult.ErrorMessage}");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			long id = persistResult.Data.ServerId;
			runtimeData.ID = id;

			// Delete any stale scenes for this server
			Task.Run(() => sceneService.DeleteBySceneServerAsync(id)).GetAwaiter().GetResult();

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(PulseRate, OnPeriodicPulse);
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			pendingSceneTimeoutSeconds = Mathf.Max(5.0f, pendingSceneTimeoutSeconds);
			pendingSceneSweepIntervalSeconds = Mathf.Max(0.25f, pendingSceneSweepIntervalSeconds);
			pendingSceneSweepMaxRemovals = Mathf.Max(1, pendingSceneSweepMaxRemovals);
			runtimeData.PulseInFlight = 0;
			runtimeData.NextPendingSceneSweepUtc = DateTime.UtcNow;

			Log.Debug("SceneServerSystem", $"Initialized (ServerID={id}, Address={server.Address}:{server.Port}, CharacterCount={characterCount})");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Called when the system is being destroyed. Unsubscribes from events and deletes scene data from the database.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("SceneServerSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue(drainAll: true);

			// Delete scene server data from database
			if (Server.Configuration.TryGetString("ServerName", out string name) &&
				Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				Log.Debug("SceneServerSystem", $"Deinitializing: Removing Scene Server scenes (ServerID={runtimeData.ID})");

				if (Server.Database?.ServiceRegistry != null &&
					Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					// Blocking call during shutdown to ensure cleanup completes
					Task.Run(() => sceneService.DeleteBySceneServerAsync(runtimeData.ID)).GetAwaiter().GetResult();
				}
			}

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicPulse);
			}

			// Character system events
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnDisconnect -= CharacterSystem_OnDisconnect;
				characterSystem.OnAfterLoadCharacter -= CharacterSystem_OnAfterLoadCharacter;
			}

			// Scene manager events
			Server.NetworkWrapper.NetworkManager.SceneManager.OnLoadEnd -= SceneManager_OnLoadEnd;
			Server.NetworkWrapper.NetworkManager.SceneManager.OnUnloadEnd -= SceneManager_OnUnloadEnd;

			if (Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeState))
			{
				runtimeState.PendingSceneEnqueueUtcBySceneId.Clear();
				runtimeState.PulseInFlight = 0;
			}
		}

		/// <summary>
		/// Drains queued main-thread actions from the ISceneServerSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			if (Server?.DataContainerRegistry.TryGet<ISceneServerSystemMainThreadQueueData>(out var queueData) == true)
			{
				if (drainAll)
				{
					queueData.Drain();
				}
				else
				{
					queueData.Drain(maxMainThreadActionsPerFrame);
				}
			}
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private void EnqueueMainThread(Action action)
		{
			if (Server?.DataContainerRegistry.TryGet<ISceneServerSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Enqueue(action);
			}
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		public override void OnLateUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
		}

		/// <summary>
		/// Handles character disconnect events, adjusting scene character counts.
		/// </summary>
		/// <param name="conn">Network connection.</param>
		/// <param name="character">Player character that disconnected.</param>
		private void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}

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
			if (character == null)
			{
				return;
			}

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
		/// Periodic callback that sends heartbeat pulses and processes scene state.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicPulse(float deltaTime)
		{
			if (Server.ServerState == ConnectionState.Started)
			{
				if (Server != null &&
					Server.BehaviourRegistry != null &&
				Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData) &&
				Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData) &&
				Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
				{
					SweepExpiredPendingScenes(mappingData);

					int characterCount = characterMappingData.ConnectionCharacters.Count;

					// Collect scene pulse data on the main thread before async work
					List<(int Handle, int CharacterCount, bool StalePulse, double TimeSinceLastExit)> scenePulseData =
						new List<(int, int, bool, double)>();
					List<int> scenesToUnload = new List<int>();

					if (mappingData.WorldScenes != null)
					{
						foreach (var sceneGroup in mappingData.WorldScenes.Values)
						{
							foreach (var scenes in new List<IReadOnlyDictionary<int, ISceneInstanceDetails>>(sceneGroup.Values))
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
											scenePulseData.Add((sceneDetails.Handle, sceneDetails.CharacterCount, true, timeSinceLastExit));
										}
										else
										{
											// Mark for unload on main thread
											scenesToUnload.Add(sceneDetails.Handle);
										}
									}
									else
									{
										scenePulseData.Add((sceneDetails.Handle, sceneDetails.CharacterCount, false, 0));
									}
								}
							}
						}
					}

					// Unload stale scenes immediately on main thread
					foreach (int handle in scenesToUnload)
					{
						UnloadScene(handle);
					}

					// Fire-and-forget async DB operations
					lock (runtimeData)
					{
						if (runtimeData.PulseInFlight != 0)
						{
							return;
						}

						runtimeData.PulseInFlight = 1;
					}

					if (!TryEnqueueAsyncWork(() => PeriodicPulseAsync(runtimeData.ID, characterCount, runtimeData.IsLocked, scenePulseData), runtimeData.ID))
					{
						lock (runtimeData)
						{
							runtimeData.PulseInFlight = 0;
						}
					}
				}
			}
		}

		/// <summary>
		/// Performs a bounded TTL sweep of expired pending scene load requests.
		/// Expired requests are failed in the database and removed from local tracking.
		/// </summary>
		private void SweepExpiredPendingScenes(ISceneInstanceMappingData mappingData)
		{
			if (mappingData == null || mappingData.PendingScenes == null)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;
			if (nowUtc < runtimeData.NextPendingSceneSweepUtc)
			{
				return;
			}

			runtimeData.NextPendingSceneSweepUtc = nowUtc.AddSeconds(pendingSceneSweepIntervalSeconds);
			DateTime staleBeforeUtc = nowUtc.AddSeconds(-pendingSceneTimeoutSeconds);

			List<long> expiredSceneIds = new List<long>();
			int removed = 0;
			foreach (var kvp in mappingData.PendingScenes)
			{
				if (removed >= pendingSceneSweepMaxRemovals)
				{
					break;
				}

				if (!runtimeData.PendingSceneEnqueueUtcBySceneId.TryGetValue(kvp.Key, out DateTime enqueuedUtc))
				{
					runtimeData.PendingSceneEnqueueUtcBySceneId[kvp.Key] = nowUtc;
					continue;
				}

				if (enqueuedUtc <= staleBeforeUtc)
				{
					expiredSceneIds.Add(kvp.Key);
					removed++;
				}
			}

			for (int i = 0; i < expiredSceneIds.Count; ++i)
			{
				long sceneId = expiredSceneIds[i];
				mappingData.PendingScenes.Remove(sceneId);
				runtimeData.PendingSceneEnqueueUtcBySceneId.Remove(sceneId);

				Log.Warning("SceneServerSystem", $"Pending scene request timed out and was failed: SceneID={sceneId}");
				TryEnqueueAsyncWork(() => UpdateSceneStatusAsync(sceneId, SceneStatus.Failed), sceneId);
			}
		}

		/// <summary>
		/// Asynchronously sends heartbeat pulses to the database and processes pending scene requests.
		/// Scene pulse data and dequeued scene requests are processed on the main thread via EnqueueMainThread.
		/// </summary>
		/// <param name="serverID">Scene server identifier.</param>
		/// <param name="characterCount">Current connected character count.</param>
		/// <param name="isLocked">Whether the server is currently locked.</param>
		/// <param name="scenePulseData">Collected pulse payload for tracked scenes.</param>
		/// <returns>Asynchronous pulse processing task.</returns>
		private async Task PeriodicPulseAsync(long serverID, int characterCount, bool isLocked,
			List<(int Handle, int CharacterCount, bool StalePulse, double TimeSinceLastExit)> scenePulseData)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}

				if (!Server.Database.ServiceRegistry.TryGet<ISceneServerService>(out var sceneServerService) ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					return;
				}

				// Send server heartbeat pulse
				await sceneServerService.PulseAsync(serverID, characterCount, isLocked);

				// Send scene heartbeat pulses
				foreach (var (Handle, CharCount, StalePulse, TimeSinceLastExit) in scenePulseData)
				{
					await sceneService.PulseAsync(Handle, CharCount);
				}

				// Process pending scenes
				DatabaseResult<SceneData> dequeueResult = await sceneService.DequeueAsync();
				if (dequeueResult.IsSuccess)
				{
					SceneData pending = dequeueResult.Data;
					EnqueueMainThread(() =>
					{
						Log.Debug("SceneServerSystem", $"Scene Server System: Dequeued Pending Scene Load request World:{pending.WorldServerID} Scene:{pending.SceneName}");
						ProcessSceneLoadRequest(pending);
					});
				}
			}
			catch (Exception ex)
			{
				await Log.Error("SceneServerSystem", $"Error during periodic pulse: {ex}");
			}
			finally
			{
				if (Server != null &&
					Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
				{
					lock (runtimeData)
					{
						runtimeData.PulseInFlight = 0;
					}
				}
			}
		}

		/// <summary>
		/// Processes a single scene load request from the database, pre-caching and loading the scene.
		/// </summary>
		/// <param name="sceneEntity">Scene entity to process.</param>
		private void ProcessSceneLoadRequest(SceneData sceneData)
		{
			if (WorldSceneDetailsCache == null ||
				!WorldSceneDetailsCache.Scenes.Contains(sceneData.SceneName))
			{
				Log.Debug("SceneServerSystem", "Scene Server System: Scene is missing from the cache. Unable to load the scene.");
				TryEnqueueAsyncWork(() => UpdateSceneStatusAsync(sceneData.ID, SceneStatus.Failed), sceneData.ID);
				// TODO: kick players waiting for this scene otherwise they get stuck
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData))
			{
				return;
			}
			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				return;
			}

			mappingData.PendingScenes[sceneData.ID] = sceneData;
			runtimeData.PendingSceneEnqueueUtcBySceneId[sceneData.ID] = DateTime.UtcNow;

			// Pre-cache the scene on the server
			SceneLookupData lookupData = new SceneLookupData(sceneData.SceneName);
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
						sceneData.ID,
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

			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData) ||
				!mappingData.PendingScenes.TryGetValue((long)args.QueueData.SceneLoadData.Params.ServerParams[0], out SceneData sceneData))
			{
				Log.Warning("SceneServerSystem", "Pending Scene does not exist!");
				return;
			}
			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				Log.Warning("SceneServerSystem", "Runtime data missing while processing scene load end.");
				return;
			}

			mappingData.PendingScenes.Remove(sceneData.ID);
			runtimeData.PendingSceneEnqueueUtcBySceneId.Remove(sceneData.ID);

			if (sceneData.WorldServerID == UNKNOWN_WORLD_ID)
			{
				Log.Warning("SceneServerSystem", "Failed to get World Server ID.");
				TryEnqueueAsyncWork(() => UpdateSceneStatusAsync(sceneData.ID, SceneStatus.Failed), sceneData.ID);
				return;
			}

			SceneType sceneType = (SceneType)sceneData.SceneType;
			if (sceneType == SceneType.Unknown)
			{
				Log.Warning("SceneServerSystem", "Unknown scene type.");
				TryEnqueueAsyncWork(() => UpdateSceneStatusAsync(sceneData.ID, SceneStatus.Failed), sceneData.ID);
				return;
			}

			// If the load was unsuccessful, args.LoadedScenes will be empty.
			if (args.LoadedScenes == null || args.LoadedScenes.Length < 1)
			{
				Log.Debug("SceneServerSystem", $"Failed to load Database Scene[{sceneData.ID}].");
				TryEnqueueAsyncWork(() => UpdateSceneStatusAsync(sceneData.ID, SceneStatus.Failed), sceneData.ID);
			}
			else
			{
				Scene scene = args.LoadedScenes[0];

				// Process the scene by adding it to the world dictionary mappings.
				ProcessScene(scene, sceneType, sceneData.WorldServerID);

				Log.Debug("SceneServerSystem", $"Saved {sceneType} scene {scene.name}:{scene.handle} to the database.");
				TryEnqueueAsyncWork(() => SetSceneReadyAsync(runtimeData.ID, sceneData.WorldServerID, scene.name, scene.handle), runtimeData.ID);
			}
		}

		/// <summary>
		/// Asynchronously updates a scene's status in the database.
		/// </summary>
		/// <param name="sceneId">Database scene identifier.</param>
		/// <param name="status">Status value to apply.</param>
		/// <returns>Asynchronous update task.</returns>
		private async Task UpdateSceneStatusAsync(long sceneId, SceneStatus status)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					return;
				}
				// Cast from FishMMO.Shared.SceneStatus to FishMMO.Database.Data.Enums.SceneStatus (same int values)
				await sceneService.UpdateStatusAsync(sceneId, (FishMMO.Database.Data.Enums.SceneStatus)(int)status);
			}
			catch (Exception ex)
			{
				await Log.Error("SceneServerSystem", $"Error updating scene status (SceneID={sceneId}): {ex}");
			}
		}

		/// <summary>
		/// Asynchronously sets a scene to ready status in the database.
		/// </summary>
		/// <param name="sceneServerId">Owning scene server identifier.</param>
		/// <param name="worldServerId">Owning world server identifier.</param>
		/// <param name="sceneName">Scene name to mark ready.</param>
		/// <param name="sceneHandle">Scene handle to mark ready.</param>
		/// <returns>Asynchronous update task.</returns>
		private async Task SetSceneReadyAsync(long sceneServerId, long worldServerId, string sceneName, int sceneHandle)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					return;
				}
				await sceneService.SetReadyAsync(sceneServerId, worldServerId, sceneName, sceneHandle);
			}
			catch (Exception ex)
			{
				await Log.Error("SceneServerSystem", $"Error setting scene ready (SceneServerID={sceneServerId}, Scene={sceneName}:{sceneHandle}): {ex}");
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
			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData))
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				return;
			}

			// Configure the mapping for this specific world scene
			if (!mappingData.WorldScenes.TryGetValue(worldServerID, out Dictionary<string, Dictionary<int, ISceneInstanceDetails>> scenes))
			{
				mappingData.WorldScenes.Add(worldServerID, scenes = new Dictionary<string, Dictionary<int, ISceneInstanceDetails>>());
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
					SceneServerID = runtimeData.ID,
					Name = scene.name,
					SceneType = sceneType,
					Handle = scene.handle,
					CharacterCount = 0,
					LastExit = DateTime.UtcNow,
				});

				Log.Debug("SceneServerSystem", $"New scene handle added for {worldServerID}:{scene.name}:{scene.handle}");

				mappingData.SceneNameByHandle.Add(scene.handle, scene.name);
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
			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData) ||
				mappingData.WorldScenes == null)
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

				foreach (Dictionary<string, Dictionary<int, ISceneInstanceDetails>> sceneGroup in mappingData.WorldScenes.Values)
				{
					foreach (Dictionary<int, ISceneInstanceDetails> scene in sceneGroup.Values)
					{
						if (scene.ContainsKey(unloaded.Handle))
						{
							// Remove the scene
							scene.Remove(unloaded.Handle);
							mappingData.SceneNameByHandle.Remove(unloaded.Handle);

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

			if (Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData) &&
				mappingData.WorldScenes != null &&
				mappingData.WorldScenes.TryGetValue(worldServerID, out var scenes))
			{
				if (scenes != null &&
					!string.IsNullOrEmpty(sceneName) &&
					scenes.TryGetValue(sceneName, out var instances))
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
			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData))
			{
				Log.Warning("SceneServerSystem", "Failed to get ISceneInstanceMappingData during Scene Unload.");
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				Log.Warning("SceneServerSystem", "Failed to get ISceneServerRuntimeData during Scene Unload.");
				return;
			}

			// Remove the scene details from the database immediately upon an Unload request
			// to prevent new clients from connecting to it.
			long serverId = runtimeData.ID;
			TryEnqueueAsyncWork(() => DeleteSceneByHandleAsync(serverId, handle), serverId);

			SceneUnloadData sud = new SceneUnloadData()
			{
				SceneLookupDatas = new SceneLookupData[]
				{
					new SceneLookupData(handle),
				},
			};
			Server.NetworkWrapper.NetworkManager.SceneManager.UnloadConnectionScenes(sud);
		}

		/// <summary>
		/// Asynchronously deletes a specific scene by server ID and handle from the database.
		/// </summary>
		/// <param name="sceneServerId">Owning scene server identifier.</param>
		/// <param name="sceneHandle">Scene handle to delete.</param>
		/// <returns>Asynchronous delete task.</returns>
		private async Task DeleteSceneByHandleAsync(long sceneServerId, int sceneHandle)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					return;
				}
				await sceneService.DeleteByHandleAsync(sceneServerId, sceneHandle);
			}
			catch (Exception ex)
			{
				await Log.Error("SceneServerSystem", $"Error deleting scene by handle (ServerID={sceneServerId}, Handle={sceneHandle}): {ex}");
			}
		}

		/// <summary>
		/// Enqueues an async work item to the centralized async worker for controlled execution.
		/// Returns false when the queue is unavailable or rejected due to backpressure.
		/// </summary>
		/// <param name="work">Asynchronous work delegate to queue.</param>
		/// <param name="entityKey">Optional entity key for ordered execution.</param>
		/// <param name="callerName">Optional caller name used for diagnostics.</param>
		/// <returns>True if work was accepted by the queue; otherwise false.</returns>
		private bool TryEnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
				{
					if (asyncWorker.Enqueue(work, entityKey, callerName))
					{
						return true;
					}

					Log.Warning("SceneServerSystem", $"{callerName}: Async worker queue rejected work (entityKey={entityKey}).");
					return false;
				}
				else
				{
					if (asyncWorker.Enqueue(work, callerName))
					{
						return true;
					}

					Log.Warning("SceneServerSystem", $"{callerName}: Async worker queue rejected work.");
					return false;
				}
			}

			Log.Warning("SceneServerSystem", $"{callerName}: IAsyncWorkerData unavailable; work was not enqueued.");
			return false;
		}
	}
}