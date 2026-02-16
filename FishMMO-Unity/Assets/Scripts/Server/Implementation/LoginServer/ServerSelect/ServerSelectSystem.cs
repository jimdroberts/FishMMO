using FishNet.Connection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
		/// Per-connection in-flight gate for server-list requests.
		/// Prevents request spam from creating many concurrent async tasks per connection.
		/// </summary>
		private readonly ConcurrentDictionary<int, byte> inFlightServerListRequests = new ConcurrentDictionary<int, byte>();

		/// <summary>
		/// Idle timeout in seconds for world servers to be considered active.
		/// </summary>
		public float IdleTimeout = 60;

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

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<RequestServerListBroadcast>(OnServerRequestServerListBroadcastReceived, true);

			maxMainThreadResponsesPerFrame = Mathf.Max(1, maxMainThreadResponsesPerFrame);

			Log.Debug("ServerSelectSystem", $"Initialized (IdleTimeout={IdleTimeout}s)");
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
			inFlightServerListRequests.Clear();

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<RequestServerListBroadcast>(OnServerRequestServerListBroadcastReceived);
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

			if (!TryBeginServerListRequest(conn))
			{
				return;
			}

			if (!TryEnqueueAsyncWork(() => ProcessServerListRequestAsync(conn)))
			{
				EndServerListRequest(conn);
				Log.Warning("ServerSelectSystem", "Failed to enqueue server list request.");
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
				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IWorldServerService>(out var worldServerService))
				{
					return;
				}

				DatabaseResult<List<WorldServerData>> dbResult = await worldServerService.FetchActiveAsync(IdleTimeout);

				if (!dbResult.IsSuccess || dbResult.Data == null)
				{
					await Log.Warning("ServerSelectSystem", $"Failed to fetch active servers: {dbResult.ErrorMessage}");
					return;
				}

				// Map database DTOs to network broadcast type
				var worldServerList = new List<WorldServerDetails>(dbResult.Data.Count);
				foreach (WorldServerData data in dbResult.Data)
				{
					worldServerList.Add(new WorldServerDetails()
					{
						Name = data.Name,
						LastPulse = data.LastPulse,
						Address = data.Address,
						Port = data.Port,
						CharacterCount = data.CharacterCount,
						Locked = data.Locked,
					});
				}

				// Marshal response back to main thread - FishNet Broadcast is not thread-safe
				EnqueueMainThread(() =>
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
		public override void OnLateUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
		}

		/// <summary>
		/// Drains the main-thread queue via the RuntimeDataContainer.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			if (Server?.DataContainerRegistry.TryGet<IServerSelectSystemMainThreadQueueData>(out var queueData) == true)
			{
				if (drainAll)
				{
					queueData.Drain();
				}
				else
				{
					queueData.Drain(maxMainThreadResponsesPerFrame);
				}
			}
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread
		/// via the RuntimeDataContainer.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		private void EnqueueMainThread(Action action)
		{
			if (Server?.DataContainerRegistry.TryGet<IServerSelectSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Enqueue(action);
			}
		}

		/// <summary>
		/// Enqueues an async work item to the centralized async worker for controlled execution.
		/// </summary>
		/// <param name="work">The async work delegate to enqueue.</param>
		/// <param name="entityKey">Optional entity key for consistent worker routing.</param>
		/// <param name="callerName">Caller member name used for diagnostics.</param>
		/// <returns><c>true</c> if the work item was enqueued; otherwise, <c>false</c>.</returns>
		private bool TryEnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
					return asyncWorker.Enqueue(work, entityKey, callerName);
				else
					return asyncWorker.Enqueue(work, callerName);
			}

			return false;
		}

		private bool TryBeginServerListRequest(NetworkConnection conn)
		{
			if (conn == null)
			{
				return false;
			}

			return inFlightServerListRequests.TryAdd(conn.ClientId, 0);
		}

		private void EndServerListRequest(NetworkConnection conn)
		{
			if (conn != null)
			{
				inFlightServerListRequests.TryRemove(conn.ClientId, out _);
			}
		}
	}
}