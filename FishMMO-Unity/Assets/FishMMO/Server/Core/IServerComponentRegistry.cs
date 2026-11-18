namespace FishMMO.Server.Core
{
	/// <summary>
	/// Generic interface for server component registries.
	/// Defines the contract for registering, retrieving, and managing server components
	/// (behaviours, data containers, etc.).
	/// </summary>
	/// <typeparam name="TNetworkManager">The network manager type.</typeparam>
	/// <typeparam name="TConnection">The connection type.</typeparam>
	/// <typeparam name="TServerComponent">The base component type being managed.</typeparam>
	public interface IServerComponentRegistry<TNetworkManager, TConnection, TServerComponent>
		where TServerComponent : IServerComponent
	{
		/// <summary>
		/// Registers a component instance for global access.
		/// The component will be registered under every interface it implements that derives from TServerComponent,
		/// as well as under its concrete type.
		/// </summary>
		/// <typeparam name="T">The concrete type of the component.</typeparam>
		/// <param name="component">The component instance to register.</param>
		void Register<T>(T component) where T : class, TServerComponent;

		/// <summary>
		/// Unregisters a component instance from global access.
		/// Removes any entries keyed by the concrete type and by any interfaces it implemented.
		/// </summary>
		/// <typeparam name="T">The concrete type of the component.</typeparam>
		/// <param name="component">The component instance to unregister.</param>
		void Unregister<T>(T component) where T : class, TServerComponent;

		/// <summary>
		/// Attempts to get a registered component instance.
		/// </summary>
		/// <typeparam name="T">The type of the component to retrieve.</typeparam>
		/// <param name="component">The output component instance if found.</param>
		/// <returns>True if the component was found, otherwise false.</returns>
		bool TryGet<T>(out T component) where T : class, TServerComponent;

		/// <summary>
		/// Gets a registered component instance, or null if not found.
		/// </summary>
		/// <typeparam name="T">The type of the component to retrieve.</typeparam>
		/// <returns>The component instance if found, otherwise null.</returns>
		T Get<T>() where T : class, TServerComponent;

		/// <summary>
		/// Initializes all registered components with the provided server context.
		/// </summary>
		/// <param name="server">The server instance that owns this registry.</param>
		void InitializeAll(IServer server);

		/// <summary>
		/// Deinitializes and cleans up all registered components.
		/// </summary>
		void DeinitializeAll();
	}
}