using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Server.DatabaseServices;
using UnityEngine;

namespace FishMMO.Server
{
	public class KickRequestSystem : ServerBehaviour
	{
		private LocalConnectionState serverState;
		private DateTime lastFetchTime = DateTime.UtcNow;
		private long lastPosition = 0;
		private float nextPump = 0.0f;

		[Tooltip("The server kick request update pump rate limit in seconds.")]
		public float UpdatePumpRate = 5.0f;
		public int UpdateFetchCount = 100;

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

		public override void Destroying()
		{
			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState -= ServerManager_OnServerConnectionState;
				ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
		}

		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped &&
				AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				if (dbContext != null)
				{
					KickRequestService.Delete(dbContext, accountName);
				}
			}
		}

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

		private List<KickRequestEntity> FetchKickRequests()
		{
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			// fetch guild updates from the database
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

		// Process updates from the database
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
				if (Server != null && Server.NpgsqlDbContextFactory != null)
				{
					using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

					//Debug.Log($"Processing kick request for {kickRequest.AccountName}");

					// Immediately set all characters for the account to offline. Kick will be processed on scene servers.
					CharacterService.SetOnlineState(dbContext, kickRequest.AccountName, false);

					if (AccountService.TryGetLastLogin(dbContext, kickRequest.AccountName, out DateTime lastLogin))
					{
						if (lastLogin >= kickRequest.TimeCreated)
						{
							//Debug.Log($"{kickRequest.AccountName} is recently connected.");
							return;
						}
					}
				}

				if (AccountManager.GetConnectionByAccountName(kickRequest.AccountName, out NetworkConnection conn))
				{
					//Debug.Log($"Kicking {kickRequest.AccountName}");

					// Kick the connection
					conn.Kick(FishNet.Managing.Server.KickReason.UnexpectedProblem);
				}
			}
		}
	}
}
