using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World;
using FishMMO.Server.DatabaseServices;
using UnityEngine;

namespace FishMMO.Server.Implementation.World
{
	/// <summary>
	/// System for processing kick requests from the database and disconnecting accounts as needed.
	/// Periodically polls the database for new kick requests and processes them.
	/// </summary>
	[CreateAssetMenu(fileName = "KickRequestSystem", menuName = "FishMMO/Server/WorldServer/Kick Request System", order = 1)]
	public class KickRequestSystem : ServerBehaviour, IKickRequestSystem
	{
		/// <summary>
		/// The server kick request update pump rate limit in seconds.
		/// </summary>
		[Tooltip("The server kick request update pump rate limit in seconds.")]
		[SerializeField]
		public float updatePumpRate = 5.0f;
		/// <summary>
		/// Maximum number of kick requests to fetch per poll.
		/// </summary>
		[SerializeField]
		public int updateFetchCount = 100;

		/// <summary>
		/// The server kick request update pump rate limit in seconds.
		/// </summary>
		public float UpdatePumpRate { get { return updatePumpRate; } set { updatePumpRate = value; } }
		/// <summary>
		/// Maximum number of kick requests to fetch per poll.
		/// </summary>
		public int UpdateFetchCount { get { return updateFetchCount; } set { updateFetchCount = value; } }

		/// <summary>
		/// Called once to initialize the system. Subscribes to server connection state events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

			// Register periodic callback
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(updatePumpRate, OnPeriodicUpdate);
			}

			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Called when the system is being destroyed. Unsubscribes from server connection state events.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (ServerManager != null)
			{
				ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
			}

			// Unregister periodic callback
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicUpdate);
			}
		}

		/// <summary>
		/// Handles remote connection state changes. Deletes kick requests for accounts that disconnect.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="args">Remote connection state arguments.</param>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped &&
				Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
				if (dbContext != null)
				{
					KickRequestService.Delete(dbContext, accountName);
				}
			}
		}

		/// <summary>
		/// Called by the server's LateUpdate. Polls the database for kick requests at the specified rate and processes them.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		public override void OnLateUpdate(float deltaTime)
		{
		}

		/// <summary>
		/// Periodic callback that fetches and processes kick requests from the database.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicUpdate(float deltaTime)
		{
			if (Server.ServerState == LocalConnectionState.Started)
			{
				List<KickRequestEntity> updates = FetchKickRequests();
				ProcessKickRequests(updates);
			}
		}

		/// <summary>
		/// Fetches new kick requests from the database since the last fetch.
		/// Updates lastFetchTime and lastPosition for incremental polling.
		/// </summary>
		/// <returns>List of new kick request entities.</returns>
		private List<KickRequestEntity> FetchKickRequests()
		{
			if (!Server.DataContainerRegistry.TryGet(out IKickRequestQueueData data))
			{
				return null;
			}
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();

			// Fetch kick requests from the database
			List<KickRequestEntity> updates = KickRequestService.Fetch(dbContext, data.LastFetchTime, data.LastPosition, UpdateFetchCount);
			if (updates != null && updates.Count > 0)
			{
				KickRequestEntity latest = updates[updates.Count - 1];
				if (latest != null)
				{
					data.LastFetchTime = latest.TimeCreated;
					data.LastPosition = latest.ID;
				}
			}
			return updates;
		}

		/// <summary>
		/// Processes a list of kick requests, setting accounts offline and kicking connections as needed.
		/// </summary>
		/// <param name="requests">List of kick request entities to process.</param>
		private void ProcessKickRequests(List<KickRequestEntity> requests)
		{
			if (requests == null || requests.Count < 1)
			{
				return;
			}

			for (int i = 0; i < requests.Count; ++i)
			{
				KickRequestEntity kickRequest = requests[i];
				if (kickRequest == null)
				{
					continue;
				}

				// Check if the last successful login happened after the kick request.
				if (Server != null && Server.CoreServer.NpgsqlDbContextFactory != null)
				{
					using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();

					// Immediately set all characters for the account to offline. Kick will be processed on scene servers.
					CharacterService.SetOnlineState(dbContext, kickRequest.AccountName, false);

					if (AccountService.TryGetLastLogin(dbContext, kickRequest.AccountName, out DateTime lastLogin))
					{
						if (lastLogin >= kickRequest.TimeCreated)
						{
							// Account is recently connected, skip kicking.
							return;
						}
					}
				}

				if (Server.AccountManager.GetConnectionByAccountName(kickRequest.AccountName, out NetworkConnection conn))
				{
					// Kick the connection for the account.
					conn.Kick(FishNet.Managing.Server.KickReason.UnexpectedProblem);
				}
			}
		}
	}
}