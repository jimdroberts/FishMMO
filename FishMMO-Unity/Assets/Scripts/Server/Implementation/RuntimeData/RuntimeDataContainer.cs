using FishNet.Connection;
using FishNet.Managing.Server;
using FishMMO.Logging;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Base class for all runtime data containers in the FishMMO server architecture.
	/// Provides registration, initialization, and lifecycle management for server data storage.
	/// Data containers store mutable state separate from ServerBehaviour logic.
	/// </summary>
	public abstract class RuntimeDataContainer : IRuntimeDataContainer<INetworkManagerWrapper, ServerManager, NetworkConnection, IRuntimeDataContainer>
	{
		/// <summary>
		/// Indicates whether this data container has been initialized.
		/// </summary>
		public bool Initialized { get; private set; }

		/// <summary>
		/// Reference to the server instance associated with this container.
		/// </summary>
		public IServer<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer> Server { get; private set; }

		/// <summary>
		/// Reference to the server manager instance associated with this container.
		/// </summary>
		public ServerManager ServerManager { get; private set; }

		/// <summary>
		/// Initializes the container with server and manager references.
		/// Called by the registry during server startup.
		/// </summary>
		/// <param name="server">The server instance.</param>
		/// <param name="serverManager">The server manager instance.</param>
		/// <returns>The initialization status.</returns>
		public ServerComponentInitializationStatus Initialize(IServer<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer> server, ServerManager serverManager)
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

			Log.Debug("RuntimeDataContainer", "[" + this.GetType().Name + "] Initialization Status: " + initializationStatus);
			return initializationStatus;
		}

		/// <summary>
		/// Called once to initialize the data container. Must be implemented by derived classes.
		/// </summary>
		public abstract ServerComponentInitializationStatus InitializeOnce();

		/// <summary>
		/// Deinitializes the data container and resets base lifecycle fields.
		/// Derived classes should override <see cref="OnDeinitialize"/> for custom cleanup.
		/// </summary>
		public void Deinitialize()
		{
			OnDeinitialize();
			Initialized = false;
			Server = null;
			ServerManager = null;
		}

		/// <summary>
		/// Called when the data container is being deinitialized. Must be implemented by derived classes.
		/// </summary>
		protected abstract void OnDeinitialize();

		/// <summary>
		/// Clears all runtime data in this container, resetting it to initial state.
		/// Must be implemented by derived classes.
		/// </summary>
		public abstract void Clear();
	}
}