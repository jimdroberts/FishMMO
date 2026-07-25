using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishNet.Connection;
// HashSet used to de-dupe behaviours registered under multiple keys

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

			Log.Info(RegistryName, $"Initializing {components.Count} registered behaviour key(s)...");

			// Cast to the specific server type needed for behaviour initialization
			var typedServer = server as IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>;
			if (typedServer == null)
			{
				Log.Error(RegistryName, "Server does not implement IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>");
				return;
			}

			int ok = 0;
			int fail = 0;
			var seen = new HashSet<IServerBehaviour>();
			foreach (var component in components.Values)
			{
				if (component is ServerBehaviour behaviour && seen.Add(behaviour))
				{
					ServerComponentInitializationStatus initializationStatus =
						behaviour.InternalInitializeOnce(typedServer, typedServer.NetworkWrapper.NetworkManager.ServerManager);

					if (initializationStatus != ServerComponentInitializationStatus.Initialized)
					{
						fail++;
						// Ship-blocker for Login: missing AccountCreation / LoginServerSystem must be loud.
						Log.Error(RegistryName,
							$"Behaviour '{behaviour.name}' ({behaviour.GetType().Name}) failed to initialize: {initializationStatus}");
					}
					else
					{
						ok++;
						Log.Info(RegistryName,
							$"Behaviour '{behaviour.name}' ({behaviour.GetType().Name}) Initialized");
					}
				}
			}

			Log.Info(RegistryName, $"Initialization Complete — {ok} ok, {fail} failed");
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