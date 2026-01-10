namespace FishMMO.Server.Core
{
	/// <summary>
	/// Non-generic marker interface for server behaviours.
	/// Use this when declaring engine-agnostic server behaviour interfaces so the implementation
	/// registry can discover and register behaviours by their interface types.
	/// Extends IServerComponent to participate in the unified component registry system.
	/// </summary>
	public interface IServerBehaviour : IServerComponent { }

	/// <summary>
	/// Public interface representing the public API surface of server-side behaviours.
	/// Typical implementations are ScriptableObject-derived types that register with the server
	/// and perform server-side logic (for example <see cref="FishMMO.Server.Implementation.ServerBehaviour"/>).
	/// Extends IServerComponent to participate in the unified component initialization lifecycle.
	/// </summary>
	/// <typeparam name="TNetworkManager">The concrete network manager or wrapper type exposed by the server (for example <see cref="FishNet.Managing.NetworkManager"/> for FishNet).</typeparam>
	/// <typeparam name="TServerManager">The concrete server manager type exposed by the server (for example <see cref="FishNet.Managing.Server.ServerManager"/> for FishNet).</typeparam>
	/// <typeparam name="TConnection">The transport's connection representation (for example <see cref="FishNet.Connection.NetworkConnection"/> for FishNet).</typeparam>
	/// <typeparam name="TServerBehaviour">The concrete server behaviour type (for example <see cref="FishMMO.Server.Implementation.ServerBehaviour"/>).</typeparam>
	/// <remarks>
	/// Implementations should expose the server instance, server manager and lifecycle hooks used by the server runtime
	/// to initialize and tear down server behaviours in a controlled manner.
	/// </remarks>
	public interface IServerBehaviour<TNetworkManager, TServerManager, TConnection, TServerBehaviour> : 
		IServerBehaviour, 
		IServerComponent<TNetworkManager, TServerManager, TConnection, IServerBehaviour>
		where TServerBehaviour : IServerBehaviour
	{
		// Inherited from IServerComponent:
		// - bool Initialized { get; }
		// - IServer<TNetworkManager, TConnection, IServerBehaviour> Server { get; }
		// - TServerManager ServerManager { get; }
		// - ServerComponentInitializationStatus InitializeOnce();
		// - void Deinitialize();
	}
}