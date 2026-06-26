using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Scene server subsystem that handles channel selection for open world scenes.
	/// Provides channel listing (aggregating scene instances across all scene servers on the
	/// same world server via database queries) and same-server channel switching.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Channels are multiple instances of the same scene on the same world server, potentially
	/// hosted across different scene servers. The channel list is fetched from the database
	/// using <c>ISceneService.FetchAvailableAsync</c> and <c>ISceneServerService.FetchAsync</c>
	/// to include instances beyond the local scene server.
	/// </para>
	/// <para>
	/// DoS protections:
	/// <list type="bullet">
	///   <item><description><see cref="IngressGuard"/> per-connection, per-operation rate limiting with debounce and in-flight gating.</description></item>
	///   <item><description>Periodic sweep of stale ingress entries to bound memory growth.</description></item>
	///   <item><description>Main-thread queue for marshalling async DB results safely.</description></item>
	///   <item><description>Connection validation on every broadcast handler entry point.</description></item>
	///   <item><description>Per-connection channel switch cooldown enforced server-side.</description></item>
	///   <item><description>Bounded channel switch cooldown dictionary with periodic cleanup.</description></item>
	/// </list>
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "SceneChannelSystem", menuName = "FishMMO/Server/SceneServer/Scene Channel System", order = 2)]
	[RequiresDataContainer(typeof(SceneChannelSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(SceneChannelSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class SceneChannelSystem : ServerBehaviour, ISceneChannelSystem
	{
		/// <summary>Maximum number of queued main-thread actions processed per frame.</summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max queued channel-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 50;

		/// <summary>Milliseconds of per-connection, per-operation debounce for ingress guard.</summary>
		[Header("Ingress Protection")]
		[Tooltip("Per-connection, per-operation debounce in milliseconds")]
		[SerializeField] private int ingressDebounceMilliseconds = 500;

		/// <summary>Seconds between ingress guard stale-entry sweep passes.</summary>
		[Tooltip("Seconds between ingress guard stale-entry sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 10.0f;

		/// <summary>Maximum age in seconds for ingress entries before they become eligible for sweep.</summary>
		[Tooltip("Ingress entry TTL in seconds before sweep eligibility")]
		[SerializeField] private float ingressEntryTtlSeconds = 60.0f;

		/// <summary>Maximum ingress entries removed per sweep pass.</summary>
		[Tooltip("Max ingress entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		/// <summary>Minimum seconds between channel switch attempts per connection.</summary>
		[Header("Channel Switching")]
		[Tooltip("Minimum seconds between channel switch attempts per connection")]
		[SerializeField] private float channelSwitchCooldownSeconds = 10.0f;

		/// <summary>Seconds between stale cooldown dictionary cleanup sweeps.</summary>
		[Tooltip("Seconds between stale cooldown dictionary cleanup sweeps")]
		[SerializeField] private float cooldownCleanupIntervalSeconds = 30.0f;

		/// <summary>Maximum cooldown entries removed per cleanup sweep.</summary>
		[Tooltip("Max cooldown entries removed per cleanup sweep")]
		[SerializeField] private int cooldownCleanupMaxRemovals = 128;

		/// <summary>Maximum entries allowed in the channel switch cooldown dictionary.</summary>
		[Tooltip("Maximum entries in channel switch cooldown dictionary")]
		[SerializeField] private int maxCooldownEntries = 5000;

		[Header("Scene Instance Cache")]
		[Tooltip("Seconds before cached scene-instance query results expire and are re-fetched from the database. Set to 0 to disable caching.")]
		[SerializeField] private float sceneInstanceCacheTtlSeconds = 5.0f;

		[Tooltip("Seconds before cached scene-server address results expire and are re-fetched from the database. Set to 0 to disable caching.")]
		[SerializeField] private float sceneServerCacheTtlSeconds = 10.0f;

		/// <summary>Ingress operations for the IngressGuard per-operation gating.</summary>
		private enum IngressOperation : byte
		{
			/// <summary>Channel list request operation.</summary>
			RequestList = 1,
			/// <summary>Channel selection operation.</summary>
			SelectChannel = 2,
		}

		#region Initialization

		/// <summary>
		/// Initializes the channel system. Validates all dependencies, registers broadcast handlers,
		/// subscribes to character load and connection events, and clamps serialized fields.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("SceneChannelSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.NetworkWrapper == null)
			{
				Log.Error("SceneChannelSystem", "InitializeOnce: NetworkWrapper is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneChannelSystemRuntimeData>(out _))
			{
				Log.Error("SceneChannelSystem", "InitializeOnce: ISceneChannelSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneChannelSystemMainThreadQueueData>(out _))
			{
				Log.Error("SceneChannelSystem", "InitializeOnce: ISceneChannelSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out _))
			{
				Log.Error("SceneChannelSystem", "InitializeOnce: ISceneInstanceMappingData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out _))
			{
				Log.Error("SceneChannelSystem", "InitializeOnce: ICharacterMappingData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> _))
			{
				Log.Error("SceneChannelSystem", "InitializeOnce: ISceneServerSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.Database?.ServiceRegistry == null)
			{
				Log.Error("SceneChannelSystem", "InitializeOnce: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			// Register broadcast handlers (requireAuthentication = true)
			Server.NetworkWrapper.RegisterBroadcast<RequestSceneChannelListBroadcast>(OnRequestChannelList, true);
			Server.NetworkWrapper.RegisterBroadcast<SceneChannelSelectBroadcast>(OnChannelSelect, true);

			// Subscribe to character load events to send initial channel list
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnAfterLoadCharacter += OnCharacterLoaded;
			}

			// Subscribe to connection events for cleanup on disconnect
			SubscribeToConnectionEvents();

			// Clamp serialized fields
			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			ingressDebounceMilliseconds = Mathf.Max(50, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(1.0f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(5.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);
			channelSwitchCooldownSeconds = Mathf.Max(1.0f, channelSwitchCooldownSeconds);
			cooldownCleanupIntervalSeconds = Mathf.Max(5.0f, cooldownCleanupIntervalSeconds);
			cooldownCleanupMaxRemovals = Mathf.Max(1, cooldownCleanupMaxRemovals);
			maxCooldownEntries = Mathf.Max(100, maxCooldownEntries);
			sceneInstanceCacheTtlSeconds = Mathf.Max(0f, sceneInstanceCacheTtlSeconds);
			sceneServerCacheTtlSeconds = Mathf.Max(0f, sceneServerCacheTtlSeconds);

			Log.Debug("SceneChannelSystem", $"Initialized (Debounce={ingressDebounceMilliseconds}ms, SwitchCooldown={channelSwitchCooldownSeconds}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Deinitializes the channel system. Unregisters broadcast handlers, unsubscribes from events,
		/// and drains any remaining main-thread queue actions.
		/// </summary>
		public override void OnDeinitialize()
		{
			// Drain remaining main-thread actions
			DrainMainThreadQueue(drainAll: true);

			if (Server?.DataContainerRegistry.TryGet<SceneChannelSystemRuntimeData>(out var runtimeData) == true)
			{
				runtimeData.AvailableSceneCache?.Clear();
				runtimeData.SceneServerAddressCache?.Clear();
			}

			if (Server?.NetworkWrapper != null)
			{
				Server.NetworkWrapper.UnregisterBroadcast<RequestSceneChannelListBroadcast>(OnRequestChannelList);
				Server.NetworkWrapper.UnregisterBroadcast<SceneChannelSelectBroadcast>(OnChannelSelect);
			}

			if (Server?.BehaviourRegistry != null &&
				Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnAfterLoadCharacter -= OnCharacterLoaded;
			}

			UnsubscribeFromConnectionEvents();
		}

		#endregion

		#region Main Thread Queue

		/// <summary>
		/// Drains queued main-thread actions from the channel system queue.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<ISceneChannelSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread via the channel system queue.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		/// <returns><c>true</c> if successfully enqueued.</returns>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<ISceneChannelSystemMainThreadQueueData>(action);
		}

		#endregion

		#region Ingress Guard

		/// <summary>
		/// Attempts to acquire the ingress guard for the given connection and operation.
		/// </summary>
		/// <param name="connectionId">The client connection ID.</param>
		/// <param name="operation">The ingress operation type.</param>
		/// <param name="guardKey">Output guard key for releasing after async work completes.</param>
		/// <returns><c>true</c> if the guard was acquired.</returns>
		private bool TryBeginIngressGuard(int connectionId, IngressOperation operation, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<ISceneChannelSystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, (byte)operation, ingressDebounceMilliseconds, out guardKey);
		}

		/// <summary>
		/// Releases the ingress guard for the given guard key.
		/// </summary>
		/// <param name="guardKey">The guard key returned by <see cref="TryBeginIngressGuard"/>.</param>
		private void EndIngressGuard(long guardKey)
		{
			if (Server?.DataContainerRegistry.TryGet<ISceneChannelSystemRuntimeData>(out var runtimeData) == true)
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Enqueues async work that automatically releases the ingress guard on completion or failure.
		/// </summary>
		private bool TryEnqueueIngressWork(Func<Task> work, long guardKey, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			return TryEnqueueGuardedAsyncWork(work, EndIngressGuard, guardKey, entityKey, callerName);
		}

		/// <summary>
		/// Sweeps expired ingress guard entries to prevent unbounded memory growth.
		/// </summary>
		private void SweepIngressGuard()
		{
			if (Server.DataContainerRegistry.TryGet<ISceneChannelSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}
		}

		#endregion

		#region Update & Cleanup

		/// <summary>
		/// Called each frame. Drains the main-thread queue, sweeps ingress entries,
		/// and cleans up stale cooldown dictionary entries.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
			SweepIngressGuard();

			if (!Server.DataContainerRegistry.TryGet<ISceneChannelSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			runtimeData.NextCooldownCleanup -= deltaTime;
			if (runtimeData.NextCooldownCleanup <= 0f)
			{
				runtimeData.NextCooldownCleanup = cooldownCleanupIntervalSeconds;
				CleanupExpiredCooldownEntries(runtimeData);
				SweepSceneCaches();
			}
		}

		/// <summary>
		/// Cleans up cooldown and ingress tracking when a remote connection disconnects.
		/// </summary>
		/// <param name="conn">The disconnected network connection.</param>
		protected override void OnRemoteConnectionStopped(NetworkConnection conn)
		{
			if (Server?.DataContainerRegistry.TryGet<ISceneChannelSystemRuntimeData>(out var runtimeData) == true)
			{
				runtimeData.ChannelSwitchCooldownByClientId?.Remove(conn.ClientId);
			}
		}

		/// <summary>
		/// Removes expired cooldown entries to bound dictionary growth.
		/// Only removes entries whose cooldown has fully expired.
		/// </summary>
		private void CleanupExpiredCooldownEntries(ISceneChannelSystemRuntimeData runtimeData)
		{
			var cooldowns = runtimeData.ChannelSwitchCooldownByClientId;
			if (cooldowns == null || cooldowns.Count == 0)
			{
				return;
			}

			DateTime expiredBefore = DateTime.UtcNow.AddSeconds(-channelSwitchCooldownSeconds);
			List<int> expiredKeys = null;
			int removed = 0;

			foreach (var kvp in cooldowns)
			{
				if (removed >= cooldownCleanupMaxRemovals)
				{
					break;
				}

				if (kvp.Value <= expiredBefore)
				{
					if (expiredKeys == null)
					{
						expiredKeys = new List<int>(cooldownCleanupMaxRemovals);
					}
					expiredKeys.Add(kvp.Key);
					removed++;
				}
			}

			if (expiredKeys != null)
			{
				for (int i = 0; i < expiredKeys.Count; ++i)
				{
					cooldowns.Remove(expiredKeys[i]);
				}
			}
		}

		#endregion

		#region Channel List

		/// <summary>
		/// Sends the initial channel list when a character loads into an open world scene.
		/// Instanced scenes do not support channels.
		/// </summary>
		/// <param name="conn">The character's network connection.</param>
		/// <param name="character">The loaded player character.</param>
		private void OnCharacterLoaded(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null || character.IsInInstance())
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.RequestList, out long guardKey))
			{
				return;
			}

			string sceneName = character.SceneName;
			long worldServerID = character.WorldServerID;
			long characterID = character.ID;

			if (string.IsNullOrEmpty(sceneName))
			{
				EndIngressGuard(guardKey);
				return;
			}

			int maxClients = GetMaxClients(sceneName);

			if (!TryEnqueueIngressWork(
				() => FetchAndSendChannelListAsync(conn, sceneName, worldServerID, maxClients),
				guardKey, characterID))
			{
				EndIngressGuard(guardKey);
				SendServerBusy(conn);
			}
		}

		/// <summary>
		/// Handles client requests for the channel list broadcast.
		/// Validates the connection, acquires the ingress guard, and enqueues an async DB fetch.
		/// </summary>
		/// <param name="conn">The requesting client's network connection.</param>
		/// <param name="msg">The request broadcast (empty payload).</param>
		/// <param name="channel">The network transport channel.</param>
		private void OnRequestChannelList(NetworkConnection conn, RequestSceneChannelListBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null || !conn.IsActive || conn.FirstObject == null)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping) ||
				!charMapping.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				return;
			}

			if (character.IsInInstance())
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.RequestList, out long guardKey))
			{
				return;
			}

			string sceneName = character.SceneName;
			long worldServerID = character.WorldServerID;
			long characterID = character.ID;

			if (string.IsNullOrEmpty(sceneName))
			{
				EndIngressGuard(guardKey);
				return;
			}

			int maxClients = GetMaxClients(sceneName);

			if (!TryEnqueueIngressWork(
				() => FetchAndSendChannelListAsync(conn, sceneName, worldServerID, maxClients),
				guardKey, characterID))
			{
				EndIngressGuard(guardKey);
				SendServerBusy(conn);
			}
		}

		/// <summary>
		/// Asynchronously fetches all available scene instances for the given scene name and world server
		/// from the database (includes instances on other scene servers), resolves each scene server's
		/// address, and sends the channel list to the client on the main thread.
		/// </summary>
		/// <param name="conn">The client's network connection.</param>
		/// <param name="sceneName">The scene name to fetch channels for.</param>
		/// <param name="worldServerID">The world server ID.</param>
		/// <param name="maxClients">Maximum clients per instance (for the FetchAvailableAsync query).</param>
		private async Task FetchAndSendChannelListAsync(NetworkConnection conn, string sceneName, long worldServerID, int maxClients)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}

				if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService) ||
					!Server.Database.ServiceRegistry.TryGet<ISceneServerService>(out var sceneServerService))
				{
					return;
				}

				if (!Server.DataContainerRegistry.TryGet<SceneChannelSystemRuntimeData>(out var runtimeData))
				{
					return;
				}

				// Fetch available instances (cache-aware)
				var availableScenes = await FetchAvailableScenesAsync(sceneService, runtimeData, worldServerID, sceneName, maxClients);
				if (availableScenes == null || availableScenes.Count < 1)
				{
					return;
				}

				List<ChannelAddress> addresses = new List<ChannelAddress>(availableScenes.Count);

				foreach (SceneData sceneData in availableScenes)
				{
					// Only include OpenWorld scenes
					if ((SceneType)sceneData.SceneType != SceneType.OpenWorld)
					{
						continue;
					}

					// Resolve the scene server's address (cache-aware)
					var serverAddr = await FetchSceneServerAddressAsync(sceneServerService, runtimeData, sceneData.SceneServerID);
					if (!serverAddr.HasValue)
					{
						continue;
					}

					addresses.Add(new ChannelAddress
					{
						Address = serverAddr.Value.Address,
						Port = serverAddr.Value.Port,
						SceneHandle = sceneData.SceneHandle,
						SceneName = sceneData.SceneName,
						CharacterCount = sceneData.CharacterCount,
					});
				}

				if (addresses.Count < 1)
				{
					return;
				}

				// Marshal the broadcast back to the main thread
				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive)
					{
						return;
					}

					Server.NetworkWrapper.Broadcast(conn, new SceneChannelListBroadcast
					{
						Addresses = addresses,
					});
				});
			}
			catch (Exception ex)
			{
				await Log.Error("SceneChannelSystem", $"Error fetching channel list (Scene={sceneName}, World={worldServerID}): {ex}");
			}
		}

		#endregion

		#region Channel Selection

		/// <summary>
		/// Handles client channel selection requests. Validates the connection and target channel,
		/// acquires the ingress guard, enforces cooldown, and enqueues an async DB validation.
		/// On success, the character's <see cref="IPlayerCharacter.SceneHandle"/> is updated and
		/// the client is disconnected so it reconnects through the world server's load balancing.
		/// </summary>
		/// <param name="conn">The client's network connection.</param>
		/// <param name="msg">The channel selection broadcast containing the target channel.</param>
		/// <param name="channel">The network transport channel.</param>
		private void OnChannelSelect(NetworkConnection conn, SceneChannelSelectBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null || !conn.IsActive || conn.FirstObject == null)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping) ||
				!charMapping.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				return;
			}

			// Only open world scenes support channel switching
			if (character.IsInInstance())
			{
				return;
			}

			int currentHandle = character.SceneHandle;
			int targetHandle = msg.Channel.SceneHandle;

			// Already on the target channel
			if (currentHandle == targetHandle)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneChannelSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			var cooldowns = runtimeData.ChannelSwitchCooldownByClientId;

			// Enforce per-connection cooldown (bounded dictionary)
			DateTime now = DateTime.UtcNow;
			if (cooldowns != null)
			{
				if (cooldowns.TryGetValue(conn.ClientId, out DateTime lastSwitch) &&
					(now - lastSwitch).TotalSeconds < channelSwitchCooldownSeconds)
				{
					return;
				}

				// Reject if cooldown dictionary is saturated (DoS defense)
				if (!cooldowns.ContainsKey(conn.ClientId) &&
					cooldowns.Count >= maxCooldownEntries)
				{
					return;
				}
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.SelectChannel, out long guardKey))
			{
				return;
			}

			// Record cooldown immediately to prevent rapid re-entry during async work
			if (cooldowns != null)
			{
				cooldowns[conn.ClientId] = now;
			}

			string sceneName = character.SceneName;
			long worldServerID = character.WorldServerID;
			long characterID = character.ID;
			int maxClients = GetMaxClients(sceneName);

			// Always validate via DB and redirect through the world server.
			// This ensures the channel switch goes through the proper character
			// save/load pipeline and world server load balancing.
			if (!TryEnqueueIngressWork(
				() => ValidateAndSwitchChannelAsync(conn, sceneName, worldServerID, targetHandle, maxClients),
				guardKey, characterID))
			{
				EndIngressGuard(guardKey);
				SendServerBusy(conn);
			}
		}

		/// <summary>
		/// Asynchronously validates that the target channel handle exists in the database
		/// (is <see cref="SceneType.OpenWorld"/> and has capacity), then marshals back to
		/// the main thread to update the character's <see cref="IPlayerCharacter.SceneHandle"/>
		/// and disconnect the client.
		/// <para>
		/// The disconnect triggers <c>CharacterSystem.OnRemoteConnectionStopped</c> which runs
		/// the standard save/despawn/session-release pipeline. <c>BuildCharacterData</c> captures
		/// the updated <c>SceneHandle</c> so the character is persisted with the target channel.
		/// The client then automatically reconnects to the world server, which re-routes through
		/// load balancing to the scene server hosting that channel.
		/// </para>
		/// </summary>
		/// <param name="conn">The client's network connection.</param>
		/// <param name="sceneName">The scene name to validate the target channel against.</param>
		/// <param name="worldServerID">The world server ID.</param>
		/// <param name="targetHandle">The target scene handle (channel) requested by the client.</param>
		/// <param name="maxClients">Maximum clients per instance for the availability query.</param>
		private async Task ValidateAndSwitchChannelAsync(
			NetworkConnection conn,
			string sceneName,
			long worldServerID,
			int targetHandle,
			int maxClients)
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

				if (!Server.DataContainerRegistry.TryGet<SceneChannelSystemRuntimeData>(out var runtimeData))
				{
					return;
				}

				// Validate the target handle exists in the database, is OpenWorld, and has capacity (cache-aware)
				var availableScenes = await FetchAvailableScenesAsync(sceneService, runtimeData, worldServerID, sceneName, maxClients);
				if (availableScenes == null)
				{
					return;
				}

				bool targetValid = false;
				foreach (SceneData sd in availableScenes)
				{
					if (sd.CharacterCount >= maxClients)
					{
						continue;
					}
					if (sd.SceneHandle == targetHandle && (SceneType)sd.SceneType == SceneType.OpenWorld)
					{
						targetValid = true;
						break;
					}
				}

				if (!targetValid)
				{
					return;
				}

				// Marshal back to the main thread to update character state and disconnect
				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive)
					{
						return;
					}

					// Re-validate the character is still mapped to this connection
					if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping) ||
						!charMapping.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter character))
					{
						return;
					}

					// Update SceneHandle to the target channel BEFORE disconnect.
					// BuildCharacterData (called during the disconnect pipeline) captures
					// this value so the character is persisted with the correct channel.
					character.SceneHandle = targetHandle;

					// Prevent gameplay actions during the transition
					character.DisableFlags(CharacterFlags.IsLoaded);

					// Disconnect the client. This triggers:
					//   CharacterSystem.OnRemoteConnectionStopped
					//     → RemoveCharacterConnectionMapping
					//       → SaveAndDespawnCharacter (saves with updated SceneHandle, releases session)
					// The client then auto-reconnects to the world server, which re-routes
					// through load balancing to the scene server hosting the target channel.
					conn.Disconnect(false);

					Log.Debug("SceneChannelSystem", $"Channel switch: Client {conn.ClientId} → target handle {targetHandle} in {sceneName} (disconnecting for world server re-route)");
				});
			}
			catch (Exception ex)
			{
				await Log.Error("SceneChannelSystem", $"Error validating channel switch (Scene={sceneName}, Handle={targetHandle}): {ex}");
			}
		}

		#endregion

		#region Helpers

		/// <summary>
		/// Gets the maximum number of clients allowed for a given scene, using the scene server system's cache.
		/// Falls back to a sensible default if the cache is unavailable.
		/// </summary>
		/// <param name="sceneName">Name of the scene.</param>
		/// <returns>Maximum number of clients for the scene.</returns>
		private int GetMaxClients(string sceneName)
		{
			const int DEFAULT_MAX_CLIENTS = 500;

			if (Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem) &&
				sceneServerSystem.WorldSceneDetailsCache?.Scenes?.TryGetValue(sceneName, out var details) == true)
			{
				return Mathf.Clamp(details.MaxClients, 1, DEFAULT_MAX_CLIENTS);
			}
			return DEFAULT_MAX_CLIENTS;
		}

		#endregion

		#region Scene Instance Cache

		/// <summary>
		/// Fetches available scene instances for a scene name, using the cache when valid.
		/// Falls through to <c>ISceneService.FetchAvailableAsync</c> on cache miss or when
		/// caching is disabled (<see cref="sceneInstanceCacheTtlSeconds"/> = 0).
		/// </summary>
		/// <returns>The list of available <see cref="SceneData"/>, or <c>null</c> on failure.</returns>
		private async Task<IReadOnlyList<SceneData>> FetchAvailableScenesAsync(
			ISceneService sceneService,
			SceneChannelSystemRuntimeData runtimeData,
			long worldServerID,
			string sceneName,
			int maxClients)
		{
			TimeSpan ttl = TimeSpan.FromSeconds(sceneInstanceCacheTtlSeconds);
			if (ttl > TimeSpan.Zero &&
				runtimeData.AvailableSceneCache.TryGet(sceneName, ttl, out var cached))
			{
				return cached;
			}

			var result = await sceneService.FetchAvailableAsync(worldServerID, sceneName, maxClients);
			if (!result.IsSuccess || result.Data == null || result.Data.Count < 1)
			{
				return null;
			}

			if (ttl > TimeSpan.Zero)
			{
				runtimeData.AvailableSceneCache.Set(sceneName, result.Data);
			}
			return result.Data;
		}

		/// <summary>
		/// Fetches a scene server's address, using the cache when valid.
		/// Falls through to <c>ISceneServerService.FetchAsync</c> on cache miss or when
		/// caching is disabled (<see cref="sceneServerCacheTtlSeconds"/> = 0).
		/// </summary>
		/// <returns>The scene server address and port, or <c>null</c> on failure.</returns>
		private async Task<(string Address, ushort Port)?> FetchSceneServerAddressAsync(
			ISceneServerService sceneServerService,
			SceneChannelSystemRuntimeData runtimeData,
			long sceneServerID)
		{
			TimeSpan ttl = TimeSpan.FromSeconds(sceneServerCacheTtlSeconds);
			if (ttl > TimeSpan.Zero &&
				runtimeData.SceneServerAddressCache.TryGet(sceneServerID, ttl, out var cached))
			{
				return cached;
			}

			var result = await sceneServerService.FetchAsync(sceneServerID);
			if (!result.IsSuccess)
			{
				// Invalidate any stale cached entry so subsequent calls re-fetch immediately
				runtimeData.SceneServerAddressCache?.Invalidate(sceneServerID);
				return null;
			}

			var addr = (result.Data.Address, result.Data.Port);
			if (ttl > TimeSpan.Zero)
			{
				runtimeData.SceneServerAddressCache.Set(sceneServerID, addr);
			}
			return addr;
		}

		/// <summary>
		/// Sweeps expired entries from both scene instance and scene server address caches.
		/// Called during the existing cooldown cleanup cycle to avoid an extra timer.
		/// </summary>
		private void SweepSceneCaches()
		{
			if (!Server.DataContainerRegistry.TryGet<SceneChannelSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			DateTime now = DateTime.UtcNow;
			runtimeData.AvailableSceneCache?.SweepExpired(
				now, TimeSpan.FromSeconds(sceneInstanceCacheTtlSeconds), 64, 32);
			runtimeData.SceneServerAddressCache?.SweepExpired(
				now, TimeSpan.FromSeconds(sceneServerCacheTtlSeconds), 64, 32);
		}

		#endregion
	}
}