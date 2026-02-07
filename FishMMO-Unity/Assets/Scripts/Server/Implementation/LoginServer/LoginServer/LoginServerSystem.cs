using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.LoginServer;
using FishMMO.Server.Implementation;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Manages login server lifecycle, including database registration and heartbeat updates for the login service.
	/// </summary>
	[CreateAssetMenu(fileName = "LoginServerSystem", menuName = "FishMMO/Server/LoginServer/Login Server System", order = 1)]
	[RequiresDataContainer(typeof(LoginServerRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
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
			if (Server == null)
			{
				Log.Error("LoginServerSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ILoginServerRuntimeData>(out var runtimeData))
			{
				Log.Error("LoginServerSystem", "Failed to initialize: ILoginServerRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.AddressProvider.TryGetServerIPAddress(out ServerAddress server))
			{
				Log.Error("LoginServerSystem", "Failed to initialize: Could not get server IP address");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.Configuration.TryGetString("ServerName", out string name))
			{
				Log.Error("LoginServerSystem", "Failed to initialize: ServerName not configured");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Register login server in database
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ILoginServerService>(out var loginServerService))
			{
				Log.Error("LoginServerSystem", "Failed to resolve ILoginServerService from database service registry");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			// Register login server in database (Task.Run avoids deadlock from
			// Unity's SynchronizationContext when blocking on async during init)
			DatabaseResult<LoginServerData> dbResult = Task.Run(() =>
				loginServerService.PersistAsync(name, server.Address, server.Port))
				.GetAwaiter().GetResult();

			if (!dbResult.IsSuccess)
			{
				Log.Error("LoginServerSystem", $"Failed to register login server in database: {dbResult.ErrorMessage}");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			runtimeData.ID = dbResult.Data.ID;

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(PulseRate, OnPeriodicPulse);
			}

			Log.Debug("LoginServerSystem", $"Initialized (ServerID={runtimeData.ID}, Address={server.Address}:{server.Port}, PulseRate={PulseRate}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the login server system.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("LoginServerSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Periodic callbacks
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
			if (Server.ServerState == ConnectionState.Started &&
				Server.DataContainerRegistry.TryGet<ILoginServerRuntimeData>(out var runtimeData))
			{
				EnqueueAsyncWork(() => PulseAsync(runtimeData.ID));
			}
		}

		/// <summary>
		/// Asynchronously sends a heartbeat pulse to the database.
		/// </summary>
		/// <param name="serverId">The login server's database ID.</param>
		private async Task PulseAsync(long serverId)
		{
			try
			{
				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ILoginServerService>(out var loginServerService))
				{
					return;
				}

				DatabaseResult dbResult = await loginServerService.PulseAsync(serverId);

				if (!dbResult.IsSuccess)
				{
					await Log.Warning("LoginServerSystem", $"Pulse failed: {dbResult.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("LoginServerSystem", $"Error during pulse: {ex}");
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