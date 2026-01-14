using FishNet.Connection;
using FishMMO.Server.DatabaseServices;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.WorldServer;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Handles world server registration and heartbeat (pulse) updates in the database.
	/// Periodically updates the world server's status and character count.
	/// </summary>
	[CreateAssetMenu(fileName = "WorldServerSystem", menuName = "FishMMO/Server/WorldServer/World Server System", order = 1)]
	[RequiresDataContainer(typeof(WorldServerSystemRuntimeData))]
	public class WorldServerSystem : ServerBehaviour, IWorldServerSystem
	{
		/// <summary>
		/// Interval (in seconds) between heartbeat pulses to the database.
		/// </summary>
		public float PulseRate = 5.0f;

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("WorldServerSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				Log.Error("WorldServerSystem", "InitializeOnce: Failed to create database context");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			// Register the world server in the database if all required systems are available.
			if (Server.AddressProvider.TryGetServerIPAddress(out ServerAddress server) &&
				Server.BehaviourRegistry.TryGet(out IWorldSceneSystem worldSceneSystem))
			{
				int characterCount = Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var sceneData) ? sceneData.ConnectionCount : 0;

				Register(server.Address, server.Port, characterCount);
			}

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(PulseRate, OnPeriodicPulse);
			}

			Log.Debug("WorldServerSystem", $"Initialized (PulseRate={PulseRate}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Called when the system is being destroyed. No custom logic implemented.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("WorldServerSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicPulse);
			}
		}

		/// <summary>
		/// Registers the world server in the database. Public wrapper used by the core-facing interface.
		/// This duplicates the initialization-time registration logic when called directly.
		/// </summary>
		/// <param name="serverAddress">Address string.</param>
		/// <param name="port">Port number.</param>
		/// <param name="characterCount">Character count to register.</param>
		public void Register(string serverAddress, ushort port, int characterCount)
		{
			if (!Server.DataContainerRegistry.TryGet(out IWorldServerSystemRuntimeData data))
			{
				throw new UnityException("Failed to get IWorldServerSystemRuntimeData.");
			}
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				throw new UnityException("Failed to get dbContext.");
			}

			if (Server.Configuration.TryGetString("ServerName", out string name))
			{
				WorldServerService.Add(dbContext, name, serverAddress, port, characterCount, data.IsLocked, out long id);
				data.ID = id;
			}
		}

		/// <summary>
		/// Sends a heartbeat/pulse update with the current character count.
		/// </summary>
		/// <param name="characterCount">Current character count.</param>
		public void Pulse(int characterCount)
		{
			if (!Server.DataContainerRegistry.TryGet(out IWorldServerSystemRuntimeData data))
			{
				return;
			}
			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			WorldServerService.Pulse(dbContext, data.ID, characterCount);
		}

		/// <summary>
		/// Periodic callback that sends a heartbeat pulse to the database.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicPulse(float deltaTime)
		{
			if (Server.ServerState == ConnectionState.Started &&
				Initialized &&
				Server.BehaviourRegistry.TryGet(out IWorldSceneSystem worldSceneSystem))
			{
				// Send a heartbeat pulse to the database with the current character count using the interface method.
				int characterCount = Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var sceneData) ? sceneData.ConnectionCount : 0;
				Pulse(characterCount);
			}
		}
	}
}