using FishNet.Managing.Server;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Server
{
	/// <summary>
	/// Handles world server registration and heartbeat (pulse) updates in the database.
	/// Periodically updates the world server's status and character count.
	/// </summary>
	public class WorldServerSystem : ServerBehaviour
	{
		/// <summary>
		/// Current connection state of the server.
		/// </summary>
		private LocalConnectionState serverState;

		/// <summary>
		/// Database ID for this world server instance.
		/// </summary>
		private long id;
		/// <summary>
		/// Indicates whether the world server is locked (not accepting new connections).
		/// </summary>
		private bool locked = false;
		/// <summary>
		/// Interval (in seconds) between heartbeat pulses to the database.
		/// </summary>
		private float pulseRate = 5.0f;
		/// <summary>
		/// Time remaining until the next heartbeat pulse.
		/// </summary>
		private float nextPulse = 0.0f;

		/// <summary>
		/// Gets the database ID for this world server instance.
		/// </summary>
		public long ID { get { return id; } }

		/// <summary>
		/// Called once to initialize the world server system. Registers the server in the database and subscribes to connection state events.
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

				// Register the world server in the database if all required systems are available.
				if (Server != null &&
					Server.TryGetServerIPAddress(out ServerAddress server) &&
					ServerBehaviour.TryGet(out WorldSceneSystem worldSceneSystem))
				{
					int characterCount = worldSceneSystem.ConnectionCount;

					if (Configuration.GlobalSettings.TryGetString("ServerName", out string name))
					{
						WorldServerService.Add(dbContext, name, server.Address, server.Port, characterCount, locked, out id);
					}
				}
			}
			else
			{
				enabled = false;
			}
		}

		/// <summary>
		/// Called when the system is being destroyed. No custom logic implemented.
		/// </summary>
		public override void Destroying()
		{
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
		/// Unity LateUpdate callback. Periodically sends a heartbeat pulse to the database with the current character count.
		/// </summary>
		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started &&
				ServerBehaviour.TryGet(out WorldSceneSystem worldSceneSystem))
			{
				if (nextPulse < 0)
				{
					nextPulse = pulseRate;

					// Send a heartbeat pulse to the database with the current character count.
					using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
					int characterCount = worldSceneSystem.ConnectionCount;
					WorldServerService.Pulse(dbContext, id, characterCount);
				}
				nextPulse -= Time.deltaTime;
			}
		}
	}
}
