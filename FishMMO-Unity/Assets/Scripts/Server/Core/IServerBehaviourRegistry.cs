namespace FishMMO.Server.Core
{
	/// <summary>
	/// Engine-agnostic interface for the server behaviour registry.
	/// Defines the contract for registering, retrieving, and managing server-side behaviours.
	/// Extends IServerComponentRegistry for unified component management.
	/// </summary>
	/// <typeparam name="TNetworkManager">The network manager type.</typeparam>
	/// <typeparam name="TConnection">The connection type.</typeparam>
	/// <typeparam name="TBehaviour">The base type of server behaviour to be managed.</typeparam>
	public interface IServerBehaviourRegistry<TNetworkManager, TConnection, TBehaviour> :
		IServerComponentRegistry<TNetworkManager, TConnection, TBehaviour>
		where TBehaviour : IServerBehaviour
	{
		// Inherits all methods from IServerComponentRegistry:
		// - void Register<T>(T behaviour)
		// - void Unregister<T>(T behaviour)
		// - bool TryGet<T>(out T control)
		// - T Get<T>()
		// - void InitializeAll(IServer<TNetworkManager, TConnection, TBehaviour> server)
		// - void DeinitializeAll()
	}
}