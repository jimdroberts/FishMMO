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
	public abstract class ServerBehaviour : MonoBehaviour, IServerBehaviour<INetworkManagerWrapper, ServerManager, NetworkConnection, IServerBehaviour>
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
		internal void InternalInitializeOnce(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server, ServerManager serverManager)
		{
			if (Initialized)
				return;

			Server = server;
			ServerManager = serverManager;
			Initialized = true;

			InitializeOnce();

			Log.Debug("ServerBehaviour", "Initialized[" + this.GetType().Name + "]");
		}

		/// <summary>
		/// Called once to initialize the behaviour. Must be implemented by derived classes.
		/// </summary>
		public abstract void InitializeOnce();

		/// <summary>
		/// Called when the behaviour is being destroyed. Must be implemented by derived classes.
		/// </summary>
		public abstract void Destroying();

		/// <summary>
		/// Unity OnDestroy callback. Calls Destroying and unregisters this behaviour instance.
		/// </summary>
		private void OnDestroy()
		{
			Destroying();
			Server?.BehaviourRegistry?.Unregister(this);
		}

		/// <summary>
		/// Unity OnApplicationQuit callback. Calls Destroying and unregisters this behaviour instance.
		/// </summary>
		private void OnApplicationQuit()
		{
			Destroying();
			Server?.BehaviourRegistry?.Unregister(this);
		}
	}
}