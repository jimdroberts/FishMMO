using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Server.Core;
using FishMMO.Shared;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Base class for all server-side behaviours in the FishMMO server architecture.
	/// Provides registration, initialization, and lifecycle management for server behaviours.
	/// </summary>
	public abstract class ServerBehaviour : ScriptableObject, IServerBehaviour<INetworkManagerWrapper, ServerManager, NetworkConnection, IServerBehaviour>
	{
		/// <summary>
		/// Indicates whether this behaviour has been initialized.
		/// </summary>
		public bool Initialized { get; private set; }
		/// <summary>
		/// Reference to the server instance associated with this behaviour.
		/// </summary>
		public IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> Server { get; private set; }
		/// <summary>
		/// Reference to the server manager instance associated with this behaviour.
		/// </summary>
		public ServerManager ServerManager { get; private set; }

		/// <summary>
		/// Internal initialization logic for this behaviour. Sets server and manager references and calls InitializeOnce.
		/// </summary>
		/// <param name="server">The server instance.</param>
		/// <param name="serverManager">The server manager instance.</param>
		internal ServerComponentInitializationStatus InternalInitializeOnce(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server, ServerManager serverManager)
		{
			if (Initialized)
			{
				return ServerComponentInitializationStatus.AlreadyInitialized;
			}

			if (server == null)
			{
				return ServerComponentInitializationStatus.FailedToFindServer;
			}

			if (serverManager == null)
			{
				return ServerComponentInitializationStatus.FailedToFindServerManager;
			}

			Server = server;
			ServerManager = serverManager;
			ServerComponentInitializationStatus initializationStatus = InitializeOnce();

			if (initializationStatus == ServerComponentInitializationStatus.Initialized)
			{
				Initialized = true;
			}
			return initializationStatus;
		}

		/// <summary>
		/// Asynchronous initialization entry point used by the server startup chain.
		/// Sets server and manager references, then awaits <see cref="InitializeOnceAsync"/>.
		/// </summary>
		/// <param name="server">The server instance.</param>
		/// <param name="serverManager">The server manager instance.</param>
		/// <param name="cancellationToken">Cancelled when the server shuts down mid-startup.</param>
		internal async Task<ServerComponentInitializationStatus> InternalInitializeOnceAsync(
			IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server,
			ServerManager serverManager,
			CancellationToken cancellationToken)
		{
			if (Initialized)
			{
				return ServerComponentInitializationStatus.AlreadyInitialized;
			}

			if (server == null)
			{
				return ServerComponentInitializationStatus.FailedToFindServer;
			}

			if (serverManager == null)
			{
				return ServerComponentInitializationStatus.FailedToFindServerManager;
			}

			Server = server;
			ServerManager = serverManager;

			// No ConfigureAwait(false): this is started from the Unity main thread, so the
			// continuation is posted back to Unity's SynchronizationContext and resumes on the
			// main thread. That is what makes Unity API access legal after an await here — and
			// it only works because the main thread is never blocked waiting on this task.
			ServerComponentInitializationStatus initializationStatus = await InitializeOnceAsync(cancellationToken);

			if (initializationStatus == ServerComponentInitializationStatus.Initialized)
			{
				Initialized = true;
			}
			return initializationStatus;
		}

		/// <summary>
		/// Called once to initialize the behaviour. Must be implemented by derived classes.
		/// </summary>
		/// <remarks>
		/// Behaviours that need to await I/O (database registration, for example) must override
		/// <see cref="InitializeOnceAsync"/> instead of blocking in here. The Unity main thread
		/// must never block on I/O: it is the thread that drains async continuations, so
		/// blocking it can deadlock startup before the transport ever binds.
		/// </remarks>
		public abstract ServerComponentInitializationStatus InitializeOnce();

		/// <summary>
		/// Asynchronous initialization hook. The default implementation simply runs the
		/// synchronous <see cref="InitializeOnce"/>, so behaviours with no I/O need not change.
		/// </summary>
		/// <param name="cancellationToken">Cancelled when the server shuts down mid-startup.</param>
		/// <returns>The initialization status.</returns>
		/// <remarks>
		/// Overrides are invoked on the Unity main thread. Awaiting without
		/// <c>ConfigureAwait(false)</c> resumes on the main thread, so Unity APIs remain safe to
		/// touch after an await.
		/// </remarks>
		public virtual Task<ServerComponentInitializationStatus> InitializeOnceAsync(CancellationToken cancellationToken)
		{
			return Task.FromResult(InitializeOnce());
		}

		/// <summary>
		/// Called when the behaviour is being deinitialized. Must be implemented by derived classes.
		/// </summary>
		public abstract void OnDeinitialize();

		/// <summary>
		/// Called by the Server's LateUpdate to provide mutable data and perform per-frame logic.
		/// Performs a guard check to ensure the behaviour is initialized and the server reference is valid,
		/// then delegates to <see cref="OnUpdate"/> for subclass-specific logic.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		public void OnLateUpdate(float deltaTime)
		{
			if (!Initialized || Server == null)
			{
				return;
			}
			OnUpdate(deltaTime);
		}

		/// <summary>
		/// Drain the specified main-thread queue for this server behaviour.
		/// </summary>
		protected void DrainMainThreadQueue<TQueue>(int maxActions, bool drainAll)
			where TQueue : class, IMainThreadQueueData
		{
			MainThreadQueueHelper.Drain<TQueue>(Server, maxActions, drainAll);
		}

		/// <summary>
		/// Enqueue an action to the specified main-thread queue.
		/// </summary>
		protected bool TryEnqueueMainThread<TQueue>(Action action)
			where TQueue : class, IMainThreadQueueData
		{
			return MainThreadQueueHelper.TryEnqueue<TQueue>(Server, action);
		}

		/// <summary>
		/// Attempts to resolve a database service from the server's database service registry.
		/// Encapsulates the null-check on Server.Database.ServiceRegistry and the TryGet call.
		/// </summary>
		protected bool TryGetDbService<T>(out T service) where T : class
		{
			service = default;
			var registry = Server?.Database?.ServiceRegistry;
			return registry != null && registry.TryGet(out service);
		}

		/// <summary>
		/// Sends a <see cref="ServerBusyBroadcast"/> to the connection, informing the client
		/// that the server cannot process the request right now.
		/// Uses Unreliable channel to avoid amplifying server-side send-queue pressure.
		/// </summary>
		protected void SendServerBusy(NetworkConnection conn)
		{
			if (conn == null || !conn.IsActive)
				return;

			Server?.NetworkWrapper?.Broadcast(conn, new ServerBusyBroadcast(), true, Channel.Unreliable);
		}

		/// <summary>
		/// Enqueue a unit of asynchronous work to the centralized AsyncWorker. Returns false when rejected.
		/// Logs warnings using the concrete behaviour name for diagnostics.
		/// </summary>
		protected bool TryEnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			string tag = GetType().Name;
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
				{
					if (asyncWorker.Enqueue(work, entityKey, callerName))
						return true;

					Log.Warning(tag, $"{callerName}: Async worker queue rejected work (entityKey={entityKey}).");
					return false;
				}

				if (asyncWorker.Enqueue(work, callerName))
					return true;
				
				Log.Warning(tag, $"{callerName}: Async worker queue rejected work.");
				return false;
			}

			Log.Warning(tag, $"{callerName}: IAsyncWorkerData unavailable; work was not enqueued.");
			return false;
		}

		/// <summary>
		/// Enqueues async work and sends <see cref="ServerBusyBroadcast"/> to the client on failure.
		/// Use this for broadcast handlers where the client expects a response.
		/// </summary>
		/// <param name="work">The async work to enqueue.</param>
		/// <param name="conn">The client connection to notify on failure.</param>
		/// <param name="entityKey">Optional entity key for consistent hashing.</param>
		/// <param name="callerName">Caller name for diagnostics.</param>
		/// <returns>True if the work was enqueued successfully.</returns>
		protected bool TryEnqueueAsyncWork(Func<Task> work, NetworkConnection conn, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (TryEnqueueAsyncWork(work, entityKey, callerName))
				return true;

			SendServerBusy(conn);
			return false;
		}

		/// <summary>
		/// Enqueues persistence work through the async worker. If the bounded channel is full,
		/// runs the work directly on the thread pool as a fallback to prevent data loss.
		/// <para>
		/// Use this instead of <see cref="TryEnqueueAsyncWork"/> for post-processing persistence
		/// where in-memory state has already been committed and the database write must not be silently dropped.
		/// </para>
		/// </summary>
		/// <returns><c>true</c> if enqueued normally; <c>false</c> if the fallback path was used.</returns>
		protected bool EnqueuePersistence(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (TryEnqueueAsyncWork(work, entityKey, callerName))
				return true;

			string tag = GetType().Name;
			Log.Error(tag, $"{callerName}: Async worker full — persistence running via direct fallback (entityKey={entityKey}).");

			_ = Task.Run(async () =>
			{
				try
				{
					await work();
				}
				catch (Exception ex)
				{
					await Log.Error(tag, $"{callerName}: Direct fallback persistence failed (entityKey={entityKey}): {ex}");
				}
			});

			return false;
		}

		/// <summary>
		/// Generic helper to attempt acquiring an in-flight slot from a runtime data container.
		/// Caller supplies a lambda that performs the actual add (e.g. runtimeData.InFlightRequests.TryAdd(conn.ClientId, 0)).
		/// </summary>
		protected bool TryBeginInFlightRequest<TRuntime>(NetworkConnection conn, Func<TRuntime, bool> tryAdd)
			where TRuntime : class, FishMMO.Server.Core.IRuntimeDataContainer
		{
			if (conn == null || tryAdd == null)
				return false;

			if (!Server.DataContainerRegistry.TryGet<TRuntime>(out var runtimeData))
				return false;

			try
			{
				return tryAdd(runtimeData);
			}
			catch (Exception ex)
			{
				Log.Error(GetType().Name, $"TryBeginInFlightRequest exception: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Generic helper to release an in-flight slot from a runtime data container.
		/// Caller supplies an action that performs the remove/cleanup on the runtime data.
		/// </summary>
		protected void EndInFlightRequest<TRuntime>(NetworkConnection conn, Action<TRuntime> onEnd)
			where TRuntime : class, FishMMO.Server.Core.IRuntimeDataContainer
		{
			if (conn == null || onEnd == null)
				return;

			if (!Server.DataContainerRegistry.TryGet<TRuntime>(out var runtimeData))
				return;

			try
			{
				onEnd(runtimeData);
			}
			catch (Exception ex)
			{
				Log.Error(GetType().Name, $"EndInFlightRequest exception: {ex.Message}");
			}
		}

		/// <summary>
		/// Enqueues async work that guarantees an ingress guard is released (via <paramref name="releaseGuard"/>)
		/// when the work completes, even if it throws.
		/// </summary>
		protected bool TryEnqueueGuardedAsyncWork(Func<Task> work, Action<long> releaseGuard, long guardKey, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			return TryEnqueueAsyncWork(async () =>
			{
				try
				{
					await work();
				}
				finally
				{
					releaseGuard(guardKey);
				}
			}, entityKey, callerName);
		}

		/// <summary>
		/// Called when a remote connection stops. Override in derived classes to perform
		/// per-connection cleanup (e.g. clearing in-flight requests, caches, queue entries).
		/// Systems must call <see cref="SubscribeToConnectionEvents"/> in <see cref="InitializeOnce"/>
		/// and <see cref="UnsubscribeFromConnectionEvents"/> in <see cref="OnDeinitialize"/> to use this.
		/// </summary>
		protected virtual void OnRemoteConnectionStopped(NetworkConnection conn) { }

		/// <summary>
		/// Subscribes to <see cref="ServerManager.OnRemoteConnectionState"/> and dispatches
		/// disconnect events to <see cref="OnRemoteConnectionStopped"/>.
		/// </summary>
		protected void SubscribeToConnectionEvents()
		{
			if (ServerManager != null)
			{
				ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;
			}
		}

		/// <summary>
		/// Unsubscribes from <see cref="ServerManager.OnRemoteConnectionState"/>.
		/// </summary>
		protected void UnsubscribeFromConnectionEvents()
		{
			if (ServerManager != null)
			{
				ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;
			}
		}

		/// <summary>
		/// Handles remote connection state changes from FishNet and dispatches disconnect
		/// events to <see cref="OnRemoteConnectionStopped"/>.
		/// </summary>
		private void HandleRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped && conn != null)
			{
				OnRemoteConnectionStopped(conn);
			}
		}

		/// <summary>
		/// Override this method in derived classes that need per-frame updates.
		/// Guaranteed to run only when the behaviour is initialized and <see cref="Server"/> is non-null.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		protected virtual void OnUpdate(float deltaTime) { }

		/// <summary>
		/// Deinitializes this behaviour, calling OnDeinitialize and clearing references.
		/// </summary>
		public void Deinitialize()
		{
			OnDeinitialize();

			Initialized = false;
			Server = null;
			ServerManager = null;
		}
	}
}