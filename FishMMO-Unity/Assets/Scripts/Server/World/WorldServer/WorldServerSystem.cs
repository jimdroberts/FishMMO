using FishNet.Managing.Server;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Server
{
	// World Server System handles the database heartbeat
	public class WorldServerSystem : ServerBehaviour
	{
		private LocalConnectionState serverState;

		private long id;
		private bool locked = false;
		private float pulseRate = 5.0f;
		private float nextPulse = 0.0f;

		public long ID { get { return id; } }

		public override void InitializeOnce()
		{
			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				throw new UnityException("Failed to get dbContext.");
			}

			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;

				if (Server != null &&
					Server.TryGetServerIPAddress(out ServerAddress server) &&
					ServerBehaviour.TryGet(out WorldSceneSystem worldSceneSystem))
				{
					int characterCount = worldSceneSystem.ConnectionCount;

					if (Constants.Configuration.Settings.TryGetString("ServerName", out string name))
					{
						WorldServerService.Add(dbContext, name, server.address, server.port, characterCount, locked, out id);
					}
				}
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started &&
				ServerBehaviour.TryGet(out WorldSceneSystem worldSceneSystem))
			{
				if (nextPulse < 0)
				{
					nextPulse = pulseRate;

					// TODO: maybe this one should exist....how expensive will this be to run on update?
					using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
					//Debug.Log("World Server System: Pulse");
					int characterCount = worldSceneSystem.ConnectionCount;
					WorldServerService.Pulse(dbContext, id, characterCount);
				}
				nextPulse -= Time.deltaTime;
			}
		}
	}
}
