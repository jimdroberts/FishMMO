using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishNet.Connection;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Handles registration, lookup, and initialization of <see cref="IServerBehaviour"/> instances.
	/// Provides global access and lifecycle management for server-side behaviours.
	/// Extends the generic ServerComponentRegistry to provide behaviour-specific initialization.
	/// </summary>
	public class ServerBehaviourRegistry : 
		ServerComponentRegistry<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>,
		IServerBehaviourRegistry<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>
	{
		/// <summary>
		/// Registry name for logging purposes.
		/// </summary>
		protected override string RegistryName => "ServerBehaviourRegistry";

		/// <summary>
		/// Initializes all registered <see cref="IServerBehaviour"/>s with the provided server and server manager.
		/// </summary>
		/// <param name="server">The server instance.</param>
		public override void InitializeAll(IServer server)
		{
			if (components == null || components.Count == 0)
				return;

			Log.Debug(RegistryName, "Initializing all behaviours");

			// Cast to the specific server type needed for behaviour initialization
			var typedServer = server as IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>;
			if (typedServer == null)
			{
				Log.Error(RegistryName, "Server does not implement IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>");
				return;
			}

			foreach (var component in components.Values)
			{
				if (component is ServerBehaviour behaviour)
				{
					ServerComponentInitializationStatus initializationStatus = 
						behaviour.InternalInitializeOnce(typedServer, typedServer.NetworkWrapper.NetworkManager.ServerManager);

					if (initializationStatus != ServerComponentInitializationStatus.Initialized)
					{
						Log.Warning(RegistryName, $"Behaviour '{behaviour.name}' failed to initialize: {initializationStatus}");
					}
				}
			}

			Log.Debug(RegistryName, "Initialization Complete");
		}

		/// <summary>
		/// Initializes all registered behaviours without blocking the Unity main thread.
		/// Behaviours are initialized one at a time in registration order, so a behaviour may
		/// depend on state published by an earlier one (LoginServerSystem's registered server ID,
		/// for example).
		/// </summary>
		/// <param name="server">The server instance.</param>
		/// <param name="cancellationToken">Cancelled when the server shuts down mid-startup.</param>
		/// <returns>
		/// The behaviours that failed, with their status. Empty when every behaviour initialized.
		/// </returns>
		/// <remarks>
		/// Runs on the Unity main thread and awaits without <c>ConfigureAwait(false)</c>, so each
		/// behaviour resumes on the main thread after its I/O completes.
		/// </remarks>
		public async Task<IReadOnlyList<(string Name, ServerComponentInitializationStatus Status)>> InitializeAllAsync(
			IServer server,
			CancellationToken cancellationToken)
		{
			var failures = new List<(string, ServerComponentInitializationStatus)>();

			if (components == null || components.Count == 0)
				return failures;

			_ = Log.Debug(RegistryName, "Initializing all behaviours");

			var typedServer = server as IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>;
			if (typedServer == null)
			{
				_ = Log.Error(RegistryName, "Server does not implement IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>");
				failures.Add((RegistryName, ServerComponentInitializationStatus.FailedToFindServer));
				return failures;
			}

			// Components are registered under their concrete type AND every interface they
			// implement, so components.Values repeats instances. Deduplicate as DeinitializeAll
			// does — otherwise the same behaviour is visited several times and every repeat
			// reports AlreadyInitialized, which would look like a failure to the retry loop.
			var initialized = new HashSet<IServerBehaviour>();

			foreach (var component in components.Values)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (!(component is ServerBehaviour behaviour) || !initialized.Add(component))
				{
					continue;
				}

				ServerComponentInitializationStatus initializationStatus =
					await behaviour.InternalInitializeOnceAsync(
						typedServer,
						typedServer.NetworkWrapper.NetworkManager.ServerManager,
						cancellationToken);

				if (initializationStatus != ServerComponentInitializationStatus.Initialized &&
					initializationStatus != ServerComponentInitializationStatus.AlreadyInitialized)
				{
					_ = Log.Warning(RegistryName, $"Behaviour '{behaviour.name}' failed to initialize: {initializationStatus}");
					failures.Add((behaviour.name, initializationStatus));
				}
			}

			_ = Log.Debug(RegistryName, failures.Count == 0
				? "Initialization Complete"
				: $"Initialization finished with {failures.Count} failed behaviour(s)");

			return failures;
		}

		/// <summary>
		/// Deinitializes all registered behaviours with deduplication.
		/// </summary>
		public override void DeinitializeAll()
		{
			if (components == null || components.Count == 0)
				return;

			Log.Debug(RegistryName, "Deinitializing all behaviours");

			// Use a HashSet to avoid calling Deinitialize multiple times
			// on the same instance registered under multiple keys.
			var deinitialized = new HashSet<IServerBehaviour>();

			foreach (var component in components.Values)
			{
				if (component is ServerBehaviour behaviour && deinitialized.Add(component))
				{
					behaviour.Deinitialize();
				}
			}

			components.Clear();

			Log.Debug(RegistryName, "All behaviours deinitialized");
		}
	}
}