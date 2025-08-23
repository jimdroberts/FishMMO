namespace FishMMO.Server.Core
{
	/// <summary>
	/// Public interface representing the public API surface of server-side behaviours.
	/// Typical implementations are MonoBehaviour-derived types that register with the server
	/// and perform server-side logic (for example <see cref="FishMMO.Server.Implementation.ServerBehaviour"/>).
	/// </summary>
	/// <typeparam name="TNetworkManager">The concrete network manager or wrapper type exposed by the server (for example <see cref="FishNet.Managing.NetworkManager"/> for FishNet).</typeparam>
	/// <typeparam name="TServerManager">The concrete server manager type exposed by the server (for example <see cref="FishNet.Managing.Server.ServerManager"/> for FishNet).</typeparam>
	/// <typeparam name="TConnection">The transport's connection representation (for example <see cref="FishNet.Connection.NetworkConnection"/> for FishNet).</typeparam>
	/// <typeparam name="TServerBehaviour">The concrete server behaviour type (for example <see cref="FishMMO.Server.Implementation.ServerBehaviour"/>).</typeparam>
	/// <remarks>
	/// Implementations should expose the server instance, server manager and lifecycle hooks used by the server runtime
	/// to initialize and tear down server behaviours in a controlled manner.
	/// </remarks>
	public interface IServerBehaviour<TNetworkManager, TServerManager, TConnection, TServerBehaviour>
	{
		/// <summary>
		/// Indicates whether this behaviour has been initialized by the server runtime.
		/// </summary>
		bool Initialized { get; }

		/// <summary>
		/// Reference to the server instance associated with this behaviour.
		/// </summary>
		IServer<TNetworkManager, TConnection, TServerBehaviour> Server { get; }

		/// <summary>
		/// Reference to the FishNet <see cref="ServerManager"/> instance associated with this behaviour.
		/// </summary>
		TServerManager ServerManager { get; }

		/// <summary>
		/// Called once by the server runtime to initialize the behaviour. Implementers should perform
		/// one-time setup here. This method is guaranteed to be invoked at most once per behaviour instance.
		/// </summary>
		void InitializeOnce();

		/// <summary>
		/// Called when the behaviour is being destroyed or when the application is quitting.
		/// Implementers should release resources and unregister any external callbacks here.
		/// </summary>
		void Destroying();
	}
}