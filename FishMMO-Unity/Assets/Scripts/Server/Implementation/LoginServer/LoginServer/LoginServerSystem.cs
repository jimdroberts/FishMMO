using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.LoginServer;
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
			// Unity's SynchronizationContext when blocking on async during init).
			// Timeout prevents hanging indefinitely if the DB is unreachable.
			const int dbRegistrationTimeoutMs = 30_000;
			Task<DatabaseResult<LoginServerData>> registerTask = Task.Run(() =>
				loginServerService.PersistAsync(name, server.Address, server.Port));

			if (!registerTask.Wait(dbRegistrationTimeoutMs))
			{
				Log.Error("LoginServerSystem", $"Login server DB registration timed out after {dbRegistrationTimeoutMs}ms");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			DatabaseResult<LoginServerData> dbResult = registerTask.GetAwaiter().GetResult();

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

			// Deregister login server from database on shutdown
			if (Server.DataContainerRegistry.TryGet<ILoginServerRuntimeData>(out var runtimeData) &&
				runtimeData.ID > 0 &&
				Server.Database?.ServiceRegistry != null &&
				Server.Database.ServiceRegistry.TryGet<ILoginServerService>(out var loginServerService))
			{
				try
				{
					Task.Run(() => loginServerService.DeleteAsync(runtimeData.ID)).Wait(5000);
				}
				catch (Exception ex)
				{
					Log.Error("LoginServerSystem", $"Failed to deregister login server from DB: {ex.Message}");
				}
			}
		}



		/// <summary>
		/// Periodic callback that sends a heartbeat pulse to the database.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicPulse(float deltaTime)
		{
			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			if (Server.DataContainerRegistry.TryGet<ILoginServerRuntimeData>(out var runtimeData))
			{
				if (!TryEnqueueAsyncWork(() => PulseAsync(runtimeData.ID)))
				{
					Log.Warning("LoginServerSystem", "Failed to enqueue pulse work item.");
				}
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
		/// <param name="work">The async work delegate to enqueue.</param>
		/// <param name="entityKey">Optional entity key for consistent worker routing.</param>
		/// <param name="callerName">Caller member name used for diagnostics.</param>
		/// <returns><c>true</c> if the work item was enqueued; otherwise, <c>false</c>.</returns>
		private bool TryEnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
					return asyncWorker.Enqueue(work, entityKey, callerName);
				else
					return asyncWorker.Enqueue(work, callerName);
			}

			return false;
		}
	}
}