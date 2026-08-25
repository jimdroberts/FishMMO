using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Database;
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

		/// <summary>
		/// Whether every character load pushes an unsolicited channel list to its client.
		/// </summary>
		/// <remarks>
		/// Off by default, because the client asks for one when the player opens the picker and a
		/// channel list is a population snapshot that is stale within seconds of being sent. Left
		/// on, every single login paid for two database queries — <c>FetchAvailableAsync</c> plus
		/// an address lookup per hosting scene server — to deliver a message that would be replaced
		/// by a fresh request before anyone could act on it.
		/// <para>
		/// Kept as a switch rather than deleted: a client that wants the list on screen the moment
		/// the world appears, without the round trip, only has to turn this on.
		/// </para>
		/// </remarks>
		[Header("Channel List Delivery")]
		[Tooltip("Push a channel list to every client as its character loads. Off by default; the client requests one when the player opens the picker.")]
		[SerializeField] private bool sendChannelListOnCharacterLoad = false;

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

			// Subscribe to character load events to send an initial channel list. Opt-in — see
			// sendChannelListOnCharacterLoad.
			if (sendChannelListOnCharacterLoad &&
				Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
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
			// A lingering combat-logout body is reattached through the ordinary load path, and
			// every event on that path is also raised for bodies that have no connection at all.
			// There is nobody to send a channel list to in that case.
			if (conn == null || !conn.IsActive || character == null || character.IsInInstance())
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
			long currentSceneHandle = character.SceneHandle;

			if (string.IsNullOrEmpty(sceneName))
			{
				EndIngressGuard(guardKey);
				return;
			}

			int maxClients = GetMaxClients(sceneName);

			if (!TryEnqueueIngressWork(
				() => FetchAndSendChannelListAsync(conn, sceneName, worldServerID, maxClients, currentSceneHandle),
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

			/* Instanced content has no channels, and the answer to that is an empty list rather
			 * than silence. The picker opens itself on the player's click and only closes again
			 * when a list arrives, so a request that is never answered leaves an empty window
			 * over the game with nothing to explain it. */
			if (character.IsInInstance())
			{
				SendChannelList(conn, Array.Empty<ChannelAddress>(), character.InstanceSceneHandle);
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.RequestList, out long guardKey))
			{
				// Debounced, or a list request is already in flight — that one will answer.
				return;
			}

			string sceneName = character.SceneName;
			long worldServerID = character.WorldServerID;
			long characterID = character.ID;
			long currentSceneHandle = character.SceneHandle;

			if (string.IsNullOrEmpty(sceneName))
			{
				EndIngressGuard(guardKey);
				return;
			}

			int maxClients = GetMaxClients(sceneName);

			if (!TryEnqueueIngressWork(
				() => FetchAndSendChannelListAsync(conn, sceneName, worldServerID, maxClients, currentSceneHandle),
				guardKey, characterID))
			{
				EndIngressGuard(guardKey);
				SendServerBusy(conn);
			}
		}

		/// <summary>
		/// Sends a channel list to a client. Main thread only.
		/// </summary>
		/// <remarks>
		/// Every request gets one of these, including the ones with nothing to offer. The picker
		/// is opened by the player's click and closes on the list arriving, so an unanswered
		/// request left an empty window sitting over the game — the same silent-failure shape
		/// the refusal broadcast exists to eliminate on the switch itself.
		/// </remarks>
		/// <param name="conn">Connection to answer.</param>
		/// <param name="addresses">Channels available, possibly none.</param>
		/// <param name="currentSceneHandle">Scene row the character is on now.</param>
		private void SendChannelList(NetworkConnection conn, ChannelAddress[] addresses, long currentSceneHandle)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server?.NetworkWrapper?.Broadcast(conn, new SceneChannelListBroadcast
			{
				Addresses = addresses ?? Array.Empty<ChannelAddress>(),
				CurrentSceneHandle = currentSceneHandle,
			}, true, FishNet.Transporting.Channel.Reliable);
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
		/// <param name="currentSceneHandle">
		/// Scene row the character is on now, echoed back so the client's picker can mark it.
		/// See <see cref="SceneChannelListBroadcast.CurrentSceneHandle"/>.
		/// </param>
		private async Task FetchAndSendChannelListAsync(NetworkConnection conn, string sceneName, long worldServerID, int maxClients, long currentSceneHandle)
		{
			/* Answer exactly once, whatever happens.
			 *
			 * Every failure below used to be a bare return, so a scene with no other instances —
			 * or a database that was briefly unavailable — produced no reply at all. The picker
			 * opens on the player's click and only fills in or closes when a list arrives, so
			 * those cases left an empty window over the game with nothing to say why. An empty
			 * list is a real answer: the client can present "no other channels" and close. */
			List<ChannelAddress> addresses = new List<ChannelAddress>();

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

				addresses.Capacity = availableScenes.Count;

				foreach (SceneData sceneData in availableScenes)
				{
					// Only include OpenWorld scenes
					if ((SceneType)sceneData.SceneType != SceneType.OpenWorld)
					{
						continue;
					}

					/* Resolve the hosting scene server, but only to prove it is there.
					 *
					 * A scene row outlives the process that served it — a crashed scene server
					 * deletes nothing — so a Ready row is not evidence that the channel behind it
					 * can be entered. Failing this lookup is what keeps a dead instance out of the
					 * list. Cache-aware, so it costs one query per scene server per TTL rather than
					 * one per channel per request. */
					ushort? serverPort = await FetchSceneServerAddressAsync(sceneServerService, runtimeData, sceneData.SceneServerID);
					if (!serverPort.HasValue)
					{
						continue;
					}

					addresses.Add(new ChannelAddress
					{
						/* Deliberately not sent.
						 *
						 * A channel switch is never a direct dial: the client is released here and
						 * comes back through the world server, which picks the destination from the
						 * character row and issues its own WorldSceneConnectBroadcast. The port is
						 * therefore of no use to a legitimate client — and putting it in the list
						 * handed every connected player a map of which scene servers are hosting
						 * which instances, for instances they are not being routed to. */
						Port = 0,
						// The channel's identity is its scene row, not the hosting process's
						// local handle for it — see ChannelAddress.SceneHandle.
						SceneHandle = sceneData.ID,
						SceneName = sceneData.SceneName,
						CharacterCount = sceneData.CharacterCount,
					});
				}
			}
			catch (Exception ex)
			{
				await Log.Error("SceneChannelSystem", $"Error fetching channel list (Scene={sceneName}, World={worldServerID}): {ex}");
				addresses.Clear();
			}
			finally
			{
				// Marshal the broadcast back to the main thread. Runs on every exit above,
				// including the failures — see the note at the top of this method.
				ChannelAddress[] payload = addresses.ToArray();
				TryEnqueueMainThread(() => SendChannelList(conn, payload, currentSceneHandle));
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

			/* Gate on the same predicate every other state-changing handler uses. CanActOrMove
			 * rejects a character that is dead, teleporting, frozen, not finished loading, or in
			 * combat — a channel switch is a voluntary move to another scene instance and none of
			 * those states may initiate one.
			 *
			 * Combat matters for the same reason CharacterSystem.IPlayerCharacter_OnTeleport
			 * refuses a teleport mid-fight: allowing it makes a channel switch a cleaner escape
			 * than a teleporter — instant, and it lands the player on an instance their attacker
			 * is not in.
			 *
			 * It is also actively corrupting rather than merely unfair. The switch works by
			 * rewriting SceneHandle to the target and then dropping the connection, which lands
			 * in CharacterSystem.OnRemoteConnectionStopped — the one path that passes
			 * allowCombatLinger: true. In combat that starts a combat-logout linger: the body
			 * stays on the OLD instance holding the character's session claim, while the row now
			 * says the character belongs to the TARGET handle. The world server then routes the
			 * reconnect to the target scene server, which has no body to reattach, so it tries a
			 * fresh claim, loses to the still-held one, and kicks the player — repeatedly, until
			 * the linger expires. AdjustLingeringSceneCount also books the body against the
			 * target handle, which this server does not host, so the scene population goes wrong
			 * and logs an error on every adjustment. */
			if (!CharacterStateValidation.CanActOrMove(character))
			{
				Log.Debug("SceneChannelSystem", $"Channel switch refused for {character.CharacterName}: not in an actionable state.");
				SendTransferRefused(conn, SceneTransferRefusalReason.CharacterStateChanged);
				return;
			}

			long currentHandle = character.SceneHandle;
			long targetHandle = msg.Channel.SceneHandle;

			/* Already on the target channel.
			 *
			 * Answered rather than ignored. The picker closes itself the moment it sends, so a
			 * silent return here is indistinguishable from the request being dropped — and the
			 * player's natural response, clicking again, is then swallowed by the cooldown
			 * below. It is reachable without a modified client too: the list the player clicked
			 * is a snapshot, and the world server can move a character between instances of the
			 * same scene while the picker is open. */
			if (currentHandle == targetHandle)
			{
				SendTransferRefused(conn, SceneTransferRefusalReason.AlreadyAtDestination);
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneChannelSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			var cooldowns = runtimeData.ChannelSwitchCooldownByClientId;

			/* Cheap first line only. This dictionary is keyed by connection id, and a successful
			 * switch ends the connection — so on its own it never limited switching at all, only
			 * repeated *attempts* within one session. The limit that actually holds is claimed
			 * against the character row in ValidateAndSwitchChannelAsync; this exists to keep a
			 * spamming client from reaching the database in the first place. */
			DateTime now = DateTime.UtcNow;
			if (cooldowns != null)
			{
				if (cooldowns.TryGetValue(conn.ClientId, out DateTime lastSwitch) &&
					(now - lastSwitch).TotalSeconds < channelSwitchCooldownSeconds)
				{
					SendTransferRefused(conn, SceneTransferRefusalReason.OnCooldown);
					return;
				}

				// Reject if cooldown dictionary is saturated (DoS defense)
				if (!cooldowns.ContainsKey(conn.ClientId) &&
					cooldowns.Count >= maxCooldownEntries)
				{
					SendTransferRefused(conn, SceneTransferRefusalReason.ServerError);
					return;
				}
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.SelectChannel, out long guardKey))
			{
				// Debounced or already in flight. The client's own UI is now closed, so say so
				// rather than leaving the player looking at a switch that never happens.
				SendTransferRefused(conn, SceneTransferRefusalReason.OnCooldown);
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
				() => ValidateAndSwitchChannelAsync(conn, characterID, sceneName, worldServerID, targetHandle, maxClients),
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
		/// <param name="characterID">Character performing the switch, for the persisted cooldown claim.</param>
		/// <param name="sceneName">The scene name to validate the target channel against.</param>
		/// <param name="worldServerID">The world server ID.</param>
		/// <param name="targetHandle">The target channel, identified by its scene row ID.</param>
		/// <param name="maxClients">Maximum clients per instance for the availability query.</param>
		private async Task ValidateAndSwitchChannelAsync(
			NetworkConnection conn,
			long characterID,
			string sceneName,
			long worldServerID,
			long targetHandle,
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
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
					return;
				}

				if (!Server.DataContainerRegistry.TryGet<SceneChannelSystemRuntimeData>(out var runtimeData))
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
					return;
				}

				// Validate the target handle exists in the database, is OpenWorld, and has capacity (cache-aware)
				var availableScenes = await FetchAvailableScenesAsync(sceneService, runtimeData, worldServerID, sceneName, maxClients);
				if (availableScenes == null)
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.DestinationUnavailable));
					return;
				}

				/* Distinguish "gone" from "full" so the client can say which. Both used to be a
				 * bare return, which the player experienced as the switch silently doing
				 * nothing — the one outcome a channel picker must never produce, because the
				 * obvious response is to click again, and the cooldown then swallows that too. */
				bool targetValid = false;
				foreach (SceneData sd in availableScenes)
				{
					if (sd.ID != targetHandle || (SceneType)sd.SceneType != SceneType.OpenWorld)
					{
						continue;
					}
					if (sd.CharacterCount < maxClients)
					{
						targetValid = true;
					}
					break;
				}

				if (!targetValid)
				{
					/* Absence from this list does not say why.
					 *
					 * FetchAvailableAsync already filters on capacity, so a channel that filled up
					 * between the list being drawn and the player clicking is simply missing from
					 * the result — indistinguishable, here, from one that was unloaded. Reporting
					 * both as "no longer available" told a player to refresh and try again when the
					 * honest answer was "that one is full, pick another", which is the difference
					 * between a useful message and a pointless retry.
					 *
					 * The row itself has the answer, and this is the refusal path — one extra read
					 * on a request that is already being declined. */
					SceneTransferRefusalReason reason = await ResolveUnavailableReasonAsync(
						sceneService, targetHandle, worldServerID, sceneName, maxClients);

					TryEnqueueMainThread(() => SendTransferRefused(conn, reason));
					return;
				}

				/* Claim the cooldown against the character row — the only cooldown that survives
				 * the switch.
				 *
				 * A switch releases the character and drops the connection, so the client returns
				 * through the world server on a fresh connection id, quite possibly to a
				 * different scene server. The per-connection dictionary in OnChannelSelect is
				 * therefore wiped by the very action it is supposed to rate-limit: channel
				 * hopping was completely uncapped, and the only thing the cooldown ever delayed
				 * was a retry after a *failed* switch.
				 *
				 * Claimed here, after the destination has been validated, so a player is not put
				 * on cooldown for asking about a channel that turned out to be full or gone. It
				 * is the last thing that can refuse the request; everything after this is the
				 * transfer itself. */
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
					return;
				}

				var cooldownClaim = await characterService.TryBeginChannelSwitchAsync(
					characterID, TimeSpan.FromSeconds(channelSwitchCooldownSeconds));

				if (!cooldownClaim.IsSuccess)
				{
					await Log.Warning("SceneChannelSystem",
						$"Could not claim the channel-switch cooldown for character {characterID}: " +
						$"{cooldownClaim.ErrorCode} - {cooldownClaim.ErrorMessage}. Refusing the switch.");
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
					return;
				}

				if (!cooldownClaim.Data.HasValue)
				{
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.OnCooldown));
					return;
				}

				/* The claim has to be taken before the transfer — it is the last thing that can
				 * refuse the request — but the transfer can still fail after it. Every such path
				 * below puts the claim back, so a player is never charged the cooldown for a switch
				 * they did not get: without this they were refused, and then their retry was
				 * refused again with "you are travelling too often". */
				DateTime cooldownPreviousUtc = cooldownClaim.Data.Value;

				// Marshal back to the main thread to update character state and disconnect
				if (!TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive)
					{
						ReleaseChannelSwitchClaim(characterID, cooldownPreviousUtc);
						return;
					}

					// Re-validate the character is still mapped to this connection
					if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMapping) ||
						!charMapping.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter character))
					{
						ReleaseChannelSwitchClaim(characterID, cooldownPreviousUtc);
						return;
					}

					/* Re-check on the authoritative side. The validation above went to the
					 * database, so the character's state may have changed while it ran — pulled
					 * into a fight, or killed. Applying the switch now would start the linger
					 * described in OnChannelSelect, or transfer a corpse. */
					if (!CharacterStateValidation.CanActOrMove(character))
					{
						Log.Debug("SceneChannelSystem", $"Channel switch aborted for {character.CharacterName}: state changed during validation.");
						ReleaseChannelSwitchClaim(characterID, cooldownPreviousUtc);
						SendTransferRefused(conn, SceneTransferRefusalReason.CharacterStateChanged);
						return;
					}

					/* Hand the whole transfer to the character system.
					 *
					 * This used to rewrite SceneHandle here and then drop the connection, letting
					 * the connection-stopped handler do the teardown. That inverted the ordering
					 * the teardown depends on: scene population is debited by the handle the
					 * character is carrying when OnDisconnect fires, so rewriting first debited
					 * the DESTINATION instance — one this server frequently does not even host —
					 * while the instance actually being left kept the resident forever. The
					 * source's phantom population then stopped it ever being seen as empty (so it
					 * was never unloaded), understated its free capacity, and eventually made it
					 * read as full, at which point players queued for a scene instance that was
					 * in fact deserted.
					 *
					 * BeginChannelTransfer performs the release in the right order and releases
					 * the character promptly rather than through the combat-logout path, closing
					 * the window in which a character pulled into combat between the check above
					 * and the disconnect would leave its body — and its session claim — behind. */
					if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, UnityEngine.SceneManagement.Scene> characterSystem) ||
						!characterSystem.BeginChannelTransfer(conn, targetHandle))
					{
						Log.Warning("SceneChannelSystem", $"Channel switch could not be started for client {conn.ClientId} → handle {targetHandle}.");
						ReleaseChannelSwitchClaim(characterID, cooldownPreviousUtc);
						SendTransferRefused(conn, SceneTransferRefusalReason.ServerError);
						return;
					}

					Log.Debug("SceneChannelSystem", $"Channel switch: Client {conn.ClientId} → target handle {targetHandle} in {sceneName} (disconnecting for world server re-route)");
				}))
				{
					// The switch cannot be applied off the main thread, and the client's picker
					// is already closed. Refusing explicitly is the only thing that gets the
					// player back to a state they can act from.
					await Log.Warning("SceneChannelSystem",
						$"Main-thread queue rejected the channel switch to handle {targetHandle}; refusing the request.");
					ReleaseChannelSwitchClaim(characterID, cooldownPreviousUtc);
					TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
				}
			}
			catch (Exception ex)
			{
				await Log.Error("SceneChannelSystem", $"Error validating channel switch (Scene={sceneName}, Handle={targetHandle}): {ex}");
				TryEnqueueMainThread(() => SendTransferRefused(conn, SceneTransferRefusalReason.ServerError));
			}
		}

		/// <summary>
		/// Puts back a channel-switch cooldown claimed for a switch that did not happen.
		/// </summary>
		/// <remarks>
		/// Fire-and-forget, and deliberately so: the player has already been told their switch was
		/// refused, and nothing they can do depends on this landing. A release that is dropped
		/// because the worker pool is saturated costs them one cooldown period, which is the
		/// behaviour that existed before this was here at all.
		/// <para>
		/// Safe from the main thread as well as from a worker — the database call itself is
		/// enqueued, not run here.
		/// </para>
		/// </remarks>
		/// <param name="characterID">Character whose claim is being released.</param>
		/// <param name="previousUtc">The timestamp the claim replaced, restored as-is.</param>
		private void ReleaseChannelSwitchClaim(long characterID, DateTime previousUtc)
		{
			if (characterID <= 0)
			{
				return;
			}

			if (!TryEnqueueAsyncWork(() => RollbackChannelSwitchAsync(characterID, previousUtc), characterID))
			{
				Log.Warning("SceneChannelSystem",
					$"Could not enqueue the channel-switch cooldown release for character {characterID}; " +
					"it will serve out the cooldown for a switch that was refused.");
			}
		}

		/// <summary>
		/// Performs the cooldown release. See <see cref="ReleaseChannelSwitchClaim"/>.
		/// </summary>
		private async Task RollbackChannelSwitchAsync(long characterID, DateTime previousUtc)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				DatabaseResult result = await characterService.RollbackChannelSwitchAsync(characterID, previousUtc);
				if (!result.IsSuccess)
				{
					await Log.Warning("SceneChannelSystem",
						$"Failed to release the channel-switch cooldown for character {characterID}: {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("SceneChannelSystem", $"Error releasing the channel-switch cooldown for character {characterID}: {ex}");
			}
		}

		/// <summary>
		/// Works out why a channel that is not in the available list cannot be entered.
		/// </summary>
		/// <remarks>
		/// <c>ISceneService.FetchAvailableAsync</c> selects on world, scene name, Ready status and
		/// remaining capacity, so every reason a channel is missing from it collapses into one
		/// result. Reading the row directly separates them again: still Ready and at capacity is
		/// <see cref="SceneTransferRefusalReason.DestinationFull"/>; anything else — deleted,
		/// unloaded, belonging to another world or another scene, or never an open-world channel
		/// at all — is <see cref="SceneTransferRefusalReason.DestinationUnavailable"/>.
		/// <para>
		/// Only ever called on the refusal path, so the extra read costs nothing on a switch that
		/// is going to succeed. A read that itself fails reports "unavailable", which is the
		/// conservative answer: it tells the player to refresh rather than to wait for room.
		/// </para>
		/// </remarks>
		/// <param name="sceneService">Scene service to read the row through.</param>
		/// <param name="targetHandle">Scene row ID the player asked for.</param>
		/// <param name="worldServerID">World the requesting character belongs to.</param>
		/// <param name="sceneName">Scene the requesting character is in.</param>
		/// <param name="maxClients">Capacity of one instance of that scene.</param>
		private async Task<SceneTransferRefusalReason> ResolveUnavailableReasonAsync(
			ISceneService sceneService,
			long targetHandle,
			long worldServerID,
			string sceneName,
			int maxClients)
		{
			try
			{
				var targetResult = await sceneService.FetchAsync(targetHandle);
				if (!targetResult.IsSuccess)
				{
					return SceneTransferRefusalReason.DestinationUnavailable;
				}

				SceneData target = targetResult.Data;
				bool sameChannelGroup = target.WorldServerID == worldServerID &&
										string.Equals(target.SceneName, sceneName, StringComparison.Ordinal) &&
										(SceneType)target.SceneType == SceneType.OpenWorld &&
										(SceneStatus)target.SceneStatus == SceneStatus.Ready;

				return sameChannelGroup && target.CharacterCount >= maxClients
					? SceneTransferRefusalReason.DestinationFull
					: SceneTransferRefusalReason.DestinationUnavailable;
			}
			catch (Exception ex)
			{
				await Log.Warning("SceneChannelSystem",
					$"Could not read scene row {targetHandle} to explain a refused channel switch: {ex}");
				return SceneTransferRefusalReason.DestinationUnavailable;
			}
		}

		#endregion

		#region Helpers

		/// <summary>
		/// Tells a client its scene transfer was declined, and why.
		/// </summary>
		/// <remarks>
		/// Reliable: this is a one-shot state transition that unblocks the client's UI. Losing it
		/// leaves the player exactly where the silent-return behaviour did.
		/// </remarks>
		/// <param name="conn">Connection to notify. Main thread only.</param>
		/// <param name="reason">Why the transfer was refused.</param>
		private void SendTransferRefused(NetworkConnection conn, SceneTransferRefusalReason reason)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server?.NetworkWrapper?.Broadcast(conn,
				new SceneTransferRefusedBroadcast { Reason = reason },
				true,
				FishNet.Transporting.Channel.Reliable);
		}

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
			string cacheKey = BuildAvailableSceneCacheKey(worldServerID, sceneName);

			TimeSpan ttl = TimeSpan.FromSeconds(sceneInstanceCacheTtlSeconds);
			if (ttl > TimeSpan.Zero &&
				runtimeData.AvailableSceneCache.TryGet(cacheKey, ttl, out var cached))
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
				runtimeData.AvailableSceneCache.Set(cacheKey, result.Data);
			}
			return result.Data;
		}

		/// <summary>
		/// Builds the cache key for a <c>FetchAvailableAsync</c> result.
		/// </summary>
		/// <remarks>
		/// The world server is part of the key, not just the scene name. A scene server is not
		/// owned by a world server — <c>ISceneService.DequeueAsync</c> hands out whichever pending
		/// scene row is oldest, whatever world it belongs to, which is why the instance registry
		/// is keyed by world id at all. Caching this query under the bare scene name therefore
		/// served one world's channel list to a character on another: wrong populations, other
		/// worlds' instances offered as destinations, and a switch validated against rows the
		/// player's own world server knows nothing about — so it disconnected them and then
		/// silently routed them somewhere else.
		/// </remarks>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static string BuildAvailableSceneCacheKey(long worldServerID, string sceneName)
		{
			return worldServerID.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + sceneName;
		}

		/// <summary>
		/// Fetches a scene server's address, using the cache when valid.
		/// Falls through to <c>ISceneServerService.FetchAsync</c> on cache miss or when
		/// caching is disabled (<see cref="sceneServerCacheTtlSeconds"/> = 0).
		/// </summary>
		/// <returns>The scene server address and port, or <c>null</c> on failure.</returns>
		private async Task<ushort?> FetchSceneServerAddressAsync(
			ISceneServerService sceneServerService,
			SceneChannelSystemRuntimeData runtimeData,
			long sceneServerID)
		{
			TimeSpan ttl = TimeSpan.FromSeconds(sceneServerCacheTtlSeconds);
			if (ttl > TimeSpan.Zero &&
				runtimeData.SceneServerAddressCache.TryGet(sceneServerID, ttl, out ushort cached))
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

			ushort port = (ushort)result.Data.Port;
			if (ttl > TimeSpan.Zero)
			{
				runtimeData.SceneServerAddressCache.Set(sceneServerID, port);
			}
			return port;
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