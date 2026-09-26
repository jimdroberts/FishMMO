using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.LoginServer;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Manages server selection for clients, providing the list of available world servers from the database.
	/// </summary>
	[CreateAssetMenu(fileName = "ServerSelectSystem", menuName = "FishMMO/Server/LoginServer/Server Select System", order = 1)]
	[RequiresDataContainer(typeof(ServerSelectSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(ServerSelectSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class ServerSelectSystem : ServerBehaviour, IServerSelectSystem
	{
		/// <summary>
		/// Maximum number of queued main-thread response actions processed per frame.
		/// This time-slices response dispatch to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max server-select responses drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadResponsesPerFrame = 100;

		/// <summary>
		/// Idle timeout in seconds for world servers to be considered active.
		/// </summary>
		[Tooltip("Idle timeout in seconds for world servers to be considered active")]
		[SerializeField][Min(1f)] private float idleTimeout = 60;

		/// <summary>
		/// Cooldown in milliseconds between server-list requests per connection.
		/// Prevents sequential spam even after the in-flight guard releases.
		/// </summary>
		[Tooltip("Cooldown in milliseconds between server-list requests per connection")]
		[SerializeField] private int serverListCooldownMilliseconds = 1000;

		/// <summary>
		/// How long one read of the world-server list is served to every client, in seconds.
		/// </summary>
		/// <remarks>
		/// Every request used to run its own identical query, limited only by the per-connection
		/// cooldown, so a login wave of N clients a second was N world_server reads a second. The
		/// list changes on a world server's pulse (every few seconds) and on registration, so a
		/// read or two a second answers everyone just as well. Freshness counts from when the read
		/// started; requests that arrive while a read is running share it. Zero keeps the
		/// sharing of in-flight reads but serves no completed read to a later request.
		/// </remarks>
		[Tooltip("Seconds one read of the world-server list is served to every client")]
		[SerializeField][Min(0f)] private float serverListCacheSeconds = 2f;

		/// <summary>The only key in <see cref="ServerSelectSystemRuntimeData.ServerList"/>: the list is the same for everyone.</summary>
		private const byte ServerListKey = 0;

		/// <summary>
		/// Initializes the server select system, registering broadcast handlers for server list requests.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("ServerSelectSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Verify required data containers
			if (!Server.DataContainerRegistry.TryGet<IServerSelectSystemMainThreadQueueData>(out _))
			{
				Log.Error("ServerSelectSystem", "Failed to initialize: IServerSelectSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<ServerSelectSystemRuntimeData>(out _))
			{
				Log.Error("ServerSelectSystem", "Failed to initialize: ServerSelectSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<RequestServerListBroadcast>(OnServerRequestServerListBroadcastReceived, true);
			SubscribeToConnectionEvents();

			maxMainThreadResponsesPerFrame = Mathf.Max(1, maxMainThreadResponsesPerFrame);

			Log.Debug("ServerSelectSystem", $"Initialized (idleTimeout={idleTimeout}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the server select system, unregistering broadcast handlers for server list requests.
		/// Drains remaining main-thread responses so clients get their final messages.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("ServerSelectSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain remaining responses so clients get their final messages.
			DrainMainThreadQueue(drainAll: true);
			if (Server.DataContainerRegistry.TryGet<ServerSelectSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.InFlightRequests.Clear();
			}

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<RequestServerListBroadcast>(OnServerRequestServerListBroadcastReceived);
			UnsubscribeFromConnectionEvents();
		}

		/// <summary>
		/// Handles broadcast to request the list of available world servers.
		/// Delegates to async processing to avoid blocking the network thread.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">RequestServerListBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerRequestServerListBroadcastReceived(NetworkConnection conn, RequestServerListBroadcast msg, Channel channel)
		{
			if (!conn.IsActive)
			{
				return;
			}

			// M5: Verify the connection is authenticated before processing server list requests
			if (!Server.AccountManager.GetAccountNameByConnection(conn, out _))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			if (!TryBeginServerListRequest(conn))
			{
				SendEmptyServerList(conn);
				return;
			}

			if (!TryEnqueueAsyncWork(() => ProcessServerListRequestAsync(conn)))
			{
				EndServerListRequest(conn);
				SendServerBusy(conn);
			}
		}

		/// <summary>
		/// Answers a client's server-list request from the shared list, reading it from the
		/// database only when no fresh read exists or is running.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		private async Task ProcessServerListRequestAsync(NetworkConnection conn)
		{
			try
			{
				if (!Server.DataContainerRegistry.TryGet<ServerSelectSystemRuntimeData>(out var runtimeData) ||
					runtimeData.ServerList == null)
				{
					SendEmptyServerList(conn);
					return;
				}

				TimeSpan cacheLifetime = TimeSpan.FromSeconds(Mathf.Max(0f, serverListCacheSeconds));

				// Reads start one queue node each; reclaim the lapsed ones here, off the main
				// thread, so the cache stays bounded without a per-frame sweep.
				runtimeData.ServerList.SweepExpired(cacheLifetime, 8, 8);

				// A failed read yields null, which is returned to the requests that shared it but
				// never kept: the next request reads again.
				WorldServerDetails[] worldServerList = await runtimeData.ServerList.GetOrFetchAsync(
					ServerListKey, cacheLifetime, FetchServerListAsync, list => list != null);

				if (worldServerList == null)
				{
					SendEmptyServerList(conn);
					return;
				}

				// Marshal response back to main thread - FishNet Broadcast is not thread-safe.
				// The array is shared by every request served from this read; nothing mutates it
				// after the read, and each broadcast serializes it when sent.
				TryEnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive)
					{
						Server.NetworkWrapper.Broadcast(conn, new ServerListBroadcast()
						{
							Servers = worldServerList,
						}, true, Channel.Reliable);
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("ServerSelectSystem", $"Error processing server list request: {ex}");
				// A read shared by many requests can fault for all of them at once; answer each
				// rather than leave its client waiting on a list that will not come.
				SendEmptyServerList(conn);
			}
			finally
			{
				EndServerListRequest(conn);
			}
		}

		/// <summary>
		/// One read of the active world-server list, mapped to the broadcast type.
		/// </summary>
		/// <returns>The list, or null when it could not be read (logged once per read, not per requester).</returns>
		private async Task<WorldServerDetails[]> FetchServerListAsync()
		{
			if (!TryGetDbService(out IWorldServerService worldServerService))
			{
				await Log.Warning("ServerSelectSystem", "WorldServerService unavailable for server list request.");
				return null;
			}

			DatabaseResult<List<WorldServerData>> dbResult = await worldServerService.FetchActiveAsync(idleTimeout).ConfigureAwait(false);

			if (!dbResult.IsSuccess || dbResult.Data == null)
			{
				await Log.Warning("ServerSelectSystem", $"Failed to fetch active servers: [{dbResult.ErrorCode}] {dbResult.ErrorMessage}");
				return null;
			}

			// Map database DTOs to network broadcast type
			WorldServerDetails[] worldServerList = new WorldServerDetails[dbResult.Data.Count];
			for (int i = 0; i < dbResult.Data.Count; i++)
			{
				WorldServerData data = dbResult.Data[i];
				worldServerList[i] = new WorldServerDetails()
				{
					Name = data.Name,
					Port = (ushort)data.Port,
					CharacterCount = data.CharacterCount,
					Locked = data.Locked,
				};
			}
			return worldServerList;
		}

		/// <summary>
		/// Drains the main-thread response queue each frame.
		/// All network operations from async workers are marshalled through this queue
		/// to ensure they execute on the main Unity thread.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
		}

		/// <summary>
		/// Drains the main-thread queue via the RuntimeDataContainer.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<IServerSelectSystemMainThreadQueueData>(maxMainThreadResponsesPerFrame, drainAll);
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread
		/// via the RuntimeDataContainer.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IServerSelectSystemMainThreadQueueData>(action);
		}

		/// <summary>
		/// Sends an empty server list to the client when the fetch operation fails.
		/// Prevents the client from hanging indefinitely waiting for a response.
		/// </summary>
		/// <param name="conn">Network connection to send the empty list to.</param>
		private void SendEmptyServerList(NetworkConnection conn)
		{
			TryEnqueueMainThread(() =>
			{
				if (conn != null && conn.IsActive)
				{
					Server.NetworkWrapper.Broadcast(conn, new ServerListBroadcast()
					{
						Servers = Array.Empty<WorldServerDetails>(),
					}, true, Channel.Reliable);
				}
			});
		}

		/// <summary>
		/// Attempts to acquire a per-connection in-flight server-list slot.
		/// </summary>
		/// <param name="conn">Requesting connection.</param>
		/// <returns><c>true</c> if the slot was acquired; otherwise <c>false</c>.</returns>
		private bool TryBeginServerListRequest(NetworkConnection conn)
		{
			if (conn == null) return false;

			// Debounce and add in-flight slot using generic helper
			return TryBeginInFlightRequest<ServerSelectSystemRuntimeData>(conn, runtimeData =>
			{
				double now = MonotonicClock.NowSeconds;
				if (runtimeData.NextAllowedRequestSecondsByClientId.TryGetValue(conn.ClientId, out double nextAllowed) && now < nextAllowed)
				{
					return false;
				}
				return runtimeData.InFlightRequests.TryAdd(conn.ClientId, 0);
			});
		}

		/// <summary>
		/// Releases the per-connection in-flight server-list slot.
		/// </summary>
		/// <param name="conn">Connection to release.</param>
		private void EndServerListRequest(NetworkConnection conn)
		{
			if (conn == null) return;

			EndInFlightRequest<ServerSelectSystemRuntimeData>(conn, runtimeData =>
			{
				runtimeData.InFlightRequests.TryRemove(conn.ClientId, out _);
				runtimeData.NextAllowedRequestSecondsByClientId[conn.ClientId] = MonotonicClock.NowSeconds + serverListCooldownMilliseconds / 1000.0;
			});
		}

		/// <summary>
		/// Releases per-connection in-flight server-list state when a client disconnects.
		/// </summary>
		protected override void OnRemoteConnectionStopped(NetworkConnection conn)
		{
			if (Server.DataContainerRegistry.TryGet<ServerSelectSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.InFlightRequests.TryRemove(conn.ClientId, out _);
				runtimeData.NextAllowedRequestSecondsByClientId.TryRemove(conn.ClientId, out _);
			}
		}
	}
}