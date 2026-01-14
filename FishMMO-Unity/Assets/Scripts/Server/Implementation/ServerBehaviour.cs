using FishNet.Connection;
using FishNet.Managing.Server;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Server.Core;

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
		/// Called once to initialize the behaviour. Must be implemented by derived classes.
		/// </summary>
		public abstract ServerComponentInitializationStatus InitializeOnce();

		/// <summary>
		/// Called when the behaviour is being deinitialized. Must be implemented by derived classes.
		/// </summary>
		public abstract void OnDeinitialize();

		/// <summary>
		/// Called by the Server's LateUpdate to provide mutable data and perform per-frame logic.
		/// Override this method in derived classes that need per-frame updates.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		public virtual void OnLateUpdate(float deltaTime) { }

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