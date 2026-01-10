namespace FishMMO.Server.Core
{
	/// <summary>
	/// Non-generic marker interface for runtime data containers.
	/// Use this when declaring engine-agnostic data container interfaces so the implementation
	/// registry can discover and register containers by their interface types.
	/// Extends IServerComponent to participate in the unified component registry system.
	/// </summary>
	public interface IRuntimeDataContainer : IServerComponent { }

	/// <summary>
	/// Generic interface for runtime data containers that store mutable server state.
	/// Data containers are managed by Server.cs and provide type-safe access to runtime data
	/// without coupling data storage to behaviour logic.
	/// </summary>
	/// <typeparam name="TNetworkManager">The concrete network manager or wrapper type.</typeparam>
	/// <typeparam name="TServerManager">The concrete server manager type.</typeparam>
	/// <typeparam name="TConnection">The transport's connection representation.</typeparam>
	/// <typeparam name="TDataContainer">The concrete data container type.</typeparam>
	public interface IRuntimeDataContainer<TNetworkManager, TServerManager, TConnection, TDataContainer> :
		IRuntimeDataContainer,
		IServerComponent<TNetworkManager, TServerManager, TConnection, IRuntimeDataContainer>
		where TDataContainer : IRuntimeDataContainer
	{
		/// <summary>
		/// Clears all runtime data in this container, resetting it to initial state.
		/// Called during server shutdown or when data needs to be reset.
		/// </summary>
		void Clear();
	}
}