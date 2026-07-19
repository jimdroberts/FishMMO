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
		/// Asynchronously queries the database for active world servers and sends the list to the client.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		private async Task ProcessServerListRequestAsync(NetworkConnection conn)
		{
			try
			{
				if (!TryGetDbService(out IWorldServerService worldServerService))
				{
					await Log.Warning("ServerSelectSystem", "WorldServerService unavailable for server list request.");
					SendEmptyServerList(conn);
					return;
				}

				DatabaseResult<List<WorldServerData>> dbResult = await worldServerService.FetchActiveAsync(idleTimeout);

				if (!dbResult.IsSuccess || dbResult.Data == null)
				{
					await Log.Warning("ServerSelectSystem", $"Failed to fetch active servers: {dbResult.ErrorMessage}");
					SendEmptyServerList(conn);
					return;
				}

				// Map database DTOs to network broadcast type
				WorldServerDetails[] worldServerList = new WorldServerDetails[dbResult.Data.Count];
				for (int i = 0; i < dbResult.Data.Count; i++)
				{
					WorldServerData data = dbResult.Data[i];
					worldServerList[i] = new WorldServerDetails()
					{
						Name = data.Name,
						LastPulse = new DateTimeOffset(data.LastPulse, TimeSpan.Zero),
						Port = data.Port,
						CharacterCount = data.CharacterCount,
						Locked = data.Locked,
					};
				}

				// Marshal response back to main thread - FishNet Broadcast is not thread-safe
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
			}
			finally
			{
				EndServerListRequest(conn);
			}
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
				DateTime nowUtc = DateTime.UtcNow;
				if (runtimeData.NextAllowedRequestUtcByClientId.TryGetValue(conn.ClientId, out DateTime nextAllowed) && nowUtc < nextAllowed)
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
				runtimeData.NextAllowedRequestUtcByClientId[conn.ClientId] = DateTime.UtcNow.AddMilliseconds(serverListCooldownMilliseconds);
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
				runtimeData.NextAllowedRequestUtcByClientId.TryRemove(conn.ClientId, out _);
			}
		}
	}
}