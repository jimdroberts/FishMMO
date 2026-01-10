using FishNet.Managing.Server;
using FishNet.Transporting;
using FishMMO.Server.Core;
using FishMMO.Server.Core.LoginServer;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Manages login server lifecycle, including database registration and heartbeat updates for the login service.
	/// </summary>
	[CreateAssetMenu(fileName = "LoginServerSystem", menuName = "FishMMO/Server/LoginServer/Login Server System", order = 1)]
	public class LoginServerSystem : ServerBehaviour, ILoginServerSystem
	{
		/// <summary>
		/// Interval in seconds between database heartbeat pulses.
		/// </summary>
		public float PulseRate = 5.0f;

		/// <summary>
		/// Initializes the login server system, registers event handlers, and adds the server to the database.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (!Server.DataContainerRegistry.TryGet<ILoginServerRuntimeData>(out var runtimeData))
			{
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			if (Server.AddressProvider.TryGetServerIPAddress(out ServerAddress server) &&
				Server.Configuration.TryGetString("ServerName", out string name))
			{
				LoginServerService.Add(dbContext, name, server.Address, server.Port, out long serverId);
				runtimeData.ID = serverId;
			}

			// Register periodic callback
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(PulseRate, OnPeriodicPulse);
			}

			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the login server system. (No-op)
		/// </summary>
		public override void OnDeinitialize()
		{
			// Unregister periodic callback
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicPulse);
			}
		}

		/// <summary>
		/// Called by the server's LateUpdate. Periodically sends heartbeat pulses to the database to indicate server activity.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		public override void OnLateUpdate(float deltaTime)
		{
		}

		/// <summary>
		/// Periodic callback that sends a heartbeat pulse to the database.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicPulse(float deltaTime)
		{
			if (Server.ServerState == LocalConnectionState.Started &&
				Server.DataContainerRegistry.TryGet<ILoginServerRuntimeData>(out var runtimeData))
			{
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();

				//Log.Debug("Login Server System: Pulse");
				LoginServerService.Pulse(dbContext, runtimeData.ID);
			}
		}
	}
}