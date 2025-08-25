namespace FishMMO.Server.Core
{
	/// <summary>
	/// Engine-agnostic interface for the server behaviour registry.
	/// Defines the contract for registering, retrieving, and managing server-side behaviours.
	/// </summary>
	/// <typeparam name="TBehaviour">The base type of server behaviour to be managed (unused by registration methods).</typeparam>
	public interface IServerBehaviourRegistry<TNetworkManager, TConnection, TBehaviour>
	{
		/// <summary>
		/// Registers a behaviour instance.
		/// </summary>
		/// <typeparam name="T">The concrete type of the behaviour, must derive from TBehaviour.</typeparam>
		/// <param name="behaviour">The behaviour instance to register.</param>
		void Register<T>(T behaviour) where T : class, IServerBehaviour;

		/// <summary>
		/// Unregisters a behaviour instance.
		/// </summary>
		/// <typeparam name="T">The concrete type of the behaviour, must derive from TBehaviour.</typeparam>
		/// <param name="behaviour">The behaviour instance to unregister.</param>
		void Unregister<T>(T behaviour) where T : class, IServerBehaviour;

		/// <summary>
		/// Attempts to get a registered behaviour instance.
		/// </summary>
		/// <typeparam name="T">The concrete type of the behaviour, must derive from TBehaviour.</typeparam>
		/// <param name="control">The output behaviour instance if found.</param>
		/// <returns>True if the behaviour was found, otherwise false.</returns>
		bool TryGet<T>(out T control) where T : class, IServerBehaviour;

		/// <summary>
		/// Gets a registered behaviour instance.
		/// </summary>
		/// <typeparam name="T">The concrete type of the behaviour, must derive from TBehaviour.</typeparam>
		/// <returns>The behaviour instance if found, otherwise null.</returns>
		T Get<T>() where T : class, IServerBehaviour;

		/// <summary>
		/// Initializes all registered behaviours.
		/// </summary>
		/// <param name="server">The server instance that owns this registry.</param>
		void InitializeOnceInternal(IServer<TNetworkManager, TConnection, TBehaviour> server);

		/// <summary>
		/// Destroys all registered behaviours.
		/// </summary>
		void DestroyAll();
	}
}