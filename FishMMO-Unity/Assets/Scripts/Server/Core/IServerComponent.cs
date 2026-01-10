namespace FishMMO.Server.Core
{
	/// <summary>
	/// Base marker interface for all server components (behaviours, data containers, etc.).
	/// Use this when declaring engine-agnostic component interfaces so the implementation
	/// registry can discover and register components by their interface types.
	/// </summary>
	public interface IServerComponent { }

	/// <summary>
	/// Generic interface for server components that require initialization and lifecycle management.
	/// This is the base interface for all server components including behaviours and data containers.
	/// </summary>
	/// <typeparam name="TNetworkManager">The concrete network manager or wrapper type exposed by the server.</typeparam>
	/// <typeparam name="TServerManager">The concrete server manager type exposed by the server.</typeparam>
	/// <typeparam name="TConnection">The transport's connection representation.</typeparam>
	/// <typeparam name="TServerComponent">The concrete server component type (for type safety).</typeparam>
	public interface IServerComponent<TNetworkManager, TServerManager, TConnection, TServerComponent> : IServerComponent
		where TServerComponent : IServerComponent
	{
		/// <summary>
		/// Indicates whether this component has been initialized by the server runtime.
		/// </summary>
		bool Initialized { get; }

		/// <summary>
		/// Reference to the server instance associated with this component.
		/// </summary>
		IServer<TNetworkManager, TConnection, TServerComponent> Server { get; }

		/// <summary>
		/// Reference to the server manager instance associated with this component.
		/// </summary>
		TServerManager ServerManager { get; }

		/// <summary>
		/// Called once by the server runtime to initialize the component. Implementers should perform
		/// one-time setup here. This method is guaranteed to be invoked at most once per component instance.
		/// </summary>
		ServerComponentInitializationStatus InitializeOnce();

		/// <summary>
		/// Called when the component is being deinitialized.
		/// Implementers should release resources and unregister any external callbacks here.
		/// </summary>
		void Deinitialize();
	}
}