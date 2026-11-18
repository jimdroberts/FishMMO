namespace FishMMO.Server.Core
{
	/// <summary>
	/// Engine-agnostic interface for the runtime data container registry.
	/// Defines the contract for registering, retrieving, and managing runtime data containers.
	/// Extends IServerComponentRegistry for unified component management.
	/// </summary>
	/// <typeparam name="TNetworkManager">The network manager type.</typeparam>
	/// <typeparam name="TConnection">The connection type.</typeparam>
	/// <typeparam name="TDataContainer">The base type of data container to be managed.</typeparam>
	public interface IRuntimeDataContainerRegistry<TNetworkManager, TConnection, TDataContainer> :
		IServerComponentRegistry<TNetworkManager, TConnection, TDataContainer>
		where TDataContainer : IRuntimeDataContainer
	{
		// Inherits all methods from IServerComponentRegistry:
		// - void Register<T>(T container)
		// - void Unregister<T>(T container)
		// - bool TryGet<T>(out T container)
		// - T Get<T>()
		// - void InitializeAll(IServer<TNetworkManager, TConnection, TDataContainer> server)
		// - void DeinitializeAll()
	}
}