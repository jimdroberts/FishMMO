using FishNet.Managing.Server;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Server
{
	// Login Server System handles the database heartbeat for Login Service
	public class LoginServerSystem : ServerBehaviour
	{
		/// <summary>
		/// Current connection state of the server.
		/// </summary>
		private LocalConnectionState serverState;

		/// <summary>
		/// Unique ID of this login server instance.
		/// </summary>
		private long id;
		/// <summary>
		/// Interval in seconds between database heartbeat pulses.
		/// </summary>
		private float pulseRate = 5.0f;
		/// <summary>
		/// Time remaining until the next database heartbeat pulse.
		/// </summary>
		private float nextPulse = 0.0f;

		/// <summary>
		/// Gets the unique ID of this login server instance.
		/// </summary>
		public long ID { get { return id; } }

		/// <summary>
		/// Initializes the login server system, registers event handlers, and adds the server to the database.
		/// </summary>
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
					Configuration.GlobalSettings.TryGetString("ServerName", out string name))
				{
					LoginServerService.Add(dbContext, name, server.Address, server.Port, out id);
				}
			}
			else
			{
				enabled = false;
			}
		}

		/// <summary>
		/// Cleans up the login server system. (No-op)
		/// </summary>
		public override void Destroying()
		{
		}

		/// <summary>
		/// Handles changes in the server's connection state.
		/// </summary>
		/// <param name="args">Arguments containing the new connection state.</param>
		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;
		}

		/// <summary>
		/// Unity LateUpdate callback. Periodically sends heartbeat pulses to the database to indicate server activity.
		/// </summary>
		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started)
			{
				if (nextPulse < 0)
				{
					nextPulse = pulseRate;

					using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

					//Log.Debug("Login Server System: Pulse");
					LoginServerService.Pulse(dbContext, id);
				}
				nextPulse -= Time.deltaTime;
			}
		}
	}
}
