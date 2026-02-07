using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.WorldServer;
using FishMMO.Server.Implementation;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Handles world server registration and heartbeat (pulse) updates in the database.
	/// Periodically updates the world server's status and character count.
	/// Database operations are async to avoid blocking the main thread.
	/// </summary>
	[CreateAssetMenu(fileName = "WorldServerSystem", menuName = "FishMMO/Server/WorldServer/World Server System", order = 1)]
	[RequiresDataContainer(typeof(WorldServerSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
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

			if (Server.Database?.ServiceRegistry == null)
			{
				Log.Error("WorldServerSystem", "InitializeOnce: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			if (!Server.Database.ServiceRegistry.TryGet<IWorldServerService>(out _))
			{
				Log.Error("WorldServerSystem", "InitializeOnce: IWorldServerService not found");
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
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IWorldServerService>(out var worldServerService))
			{
				throw new UnityException("Failed to resolve IWorldServerService from database service registry.");
			}

			if (Server.Configuration.TryGetString("ServerName", out string name))
			{
				// Task.Run avoids deadlock from Unity's SynchronizationContext when blocking on async during init
				DatabaseResult<(long ServerId, WorldServerData ServerData)> result = Task.Run(() =>
					worldServerService.PersistAsync(name, serverAddress, port, characterCount, data.IsLocked))
					.GetAwaiter().GetResult();

				if (!result.IsSuccess)
				{
					throw new UnityException($"Failed to register world server: {result.ErrorMessage}");
				}

				data.ID = result.Data.ServerId;
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

			// Fire-and-forget async DB pulse
			EnqueueAsyncWork(() => PulseAsync(data.ID, characterCount));
		}

		/// <summary>
		/// Asynchronously sends a heartbeat pulse to the database.
		/// </summary>
		private async Task PulseAsync(long serverId, int characterCount)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IWorldServerService>(out var worldServerService))
				{
					return;
				}
				await worldServerService.PulseAsync(serverId, characterCount);
			}
			catch (Exception ex)
			{
				await Log.Error("WorldServerSystem", $"Error during pulse (ServerID={serverId}): {ex}");
			}
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

		/// <summary>
		/// Enqueues an async work item to the centralized async worker for controlled execution.
		/// </summary>
		private void EnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
					asyncWorker.Enqueue(work, entityKey, callerName);
				else
					asyncWorker.Enqueue(work, callerName);
			}
		}
	}
}