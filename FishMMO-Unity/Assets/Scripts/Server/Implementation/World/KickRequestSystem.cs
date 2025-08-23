using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Server.DatabaseServices;
using UnityEngine;

namespace FishMMO.Server.Implementation.WorldServer
{
	/// <summary>
	/// System for processing kick requests from the database and disconnecting accounts as needed.
	/// Periodically polls the database for new kick requests and processes them.
	/// </summary>
	public class KickRequestSystem : ServerBehaviour
	{
		/// <summary>
		/// Current connection state of the server.
		/// </summary>
		private LocalConnectionState serverState;
		/// <summary>
		/// Timestamp of the last successful fetch from the database.
		/// </summary>
		private DateTime lastFetchTime = DateTime.UtcNow;
		/// <summary>
		/// Last processed position (ID) in the kick request table.
		/// </summary>
		private long lastPosition = 0;
		/// <summary>
		/// Time remaining until the next database poll for kick requests.
		/// </summary>
		private float nextPump = 0.0f;

		/// <summary>
		/// The server kick request update pump rate limit in seconds.
		/// </summary>
		[Tooltip("The server kick request update pump rate limit in seconds.")]
		public float UpdatePumpRate = 5.0f;
		/// <summary>
		/// Maximum number of kick requests to fetch per poll.
		/// </summary>
		public int UpdateFetchCount = 100;

		/// <summary>
		/// Called once to initialize the system. Subscribes to server connection state events.
		/// </summary>
		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
				ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		/// <summary>
		/// Called when the system is being destroyed. Unsubscribes from server connection state events.
		/// </summary>
		public override void Destroying()
		{
			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState -= ServerManager_OnServerConnectionState;
				ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
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
		/// Unity LateUpdate callback. Polls the database for kick requests at the specified rate and processes them.
		/// </summary>
		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started)
			{
				if (nextPump < 0)
				{
					nextPump = UpdatePumpRate;

					List<KickRequestEntity> updates = FetchKickRequests();
					ProcessKickRequests(updates);

				}
				nextPump -= Time.deltaTime;
			}
		}

		/// <summary>
		/// Fetches new kick requests from the database since the last fetch.
		/// Updates lastFetchTime and lastPosition for incremental polling.
		/// </summary>
		/// <returns>List of new kick request entities.</returns>
		private List<KickRequestEntity> FetchKickRequests()
		{
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();

			// Fetch kick requests from the database
			List<KickRequestEntity> updates = KickRequestService.Fetch(dbContext, lastFetchTime, lastPosition, UpdateFetchCount);
			if (updates != null && updates.Count > 0)
			{
				KickRequestEntity latest = updates[updates.Count - 1];
				if (latest != null)
				{
					lastFetchTime = latest.TimeCreated;
					lastPosition = latest.ID;
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
