using System.Collections.Generic;
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
		IServerComponentRegistry<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>
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