using FishMMO.Logging;
using FishMMO.Server.Core;
using FishNet.Connection;
using FishNet.Managing.Server;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Handles registration, lookup, and initialization of <see cref="IRuntimeDataContainer"/> instances.
	/// Provides global access and lifecycle management for runtime data containers.
	/// Extends the generic ServerComponentRegistry to provide container-specific initialization.
	/// </summary>
	public class RuntimeDataContainerRegistry :
		ServerComponentRegistry<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer>,
		IServerComponentRegistry<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer>
	{
		/// <summary>
		/// Registry name for logging purposes.
		/// </summary>
		protected override string RegistryName => "RuntimeDataContainerRegistry";

		/// <summary>
		/// Initializes all registered <see cref="IRuntimeDataContainer"/>s with the provided server and server manager.
		/// </summary>
		/// <param name="server">The server instance.</param>
		public override void InitializeAll(IServer server)
		{
			if (components == null || components.Count == 0)
				return;

			Log.Debug(RegistryName, "Initializing all data containers");

			// Cast to the specific server type needed for container initialization
			var typedServer = server as IServer<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer>;
			if (typedServer == null)
			{
				Log.Error(RegistryName, "Server does not implement IServer<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer>");
				return;
			}

			foreach (var component in components.Values)
			{
				if (component is IRuntimeDataContainer<INetworkManagerWrapper, ServerManager, NetworkConnection, IRuntimeDataContainer> container)
				{
					ServerComponentInitializationStatus initializationStatus =
						container.Initialize(typedServer, typedServer.NetworkWrapper.NetworkManager.ServerManager);
				}
			}

			Log.Debug(RegistryName, "Initialization Complete");
		}

		/// <summary>
		/// Deinitializes all registered data containers and clears their data.
		/// </summary>
		public override void DeinitializeAll()
		{
			if (components == null || components.Count == 0)
				return;

			Log.Debug(RegistryName, "Deinitializing all data containers");

			foreach (var component in components.Values)
			{
				if (component is IRuntimeDataContainer<INetworkManagerWrapper, ServerManager, NetworkConnection, IRuntimeDataContainer> container)
				{
					container.Clear();
					container.Deinitialize();
				}
			}

			components.Clear();

			Log.Debug(RegistryName, "All data containers deinitialized");
		}
	}
}