using FishMMO.Server.Core;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Interface for initializing and registering interactable handlers in the FishMMO server.
	/// </summary>
	public interface IInteractableHandlerInitializer
	{
		/// <summary>
		/// Registers all interactable handlers with the system, providing the Server instance for dependency injection.
		/// </summary>
		/// <param name="server">The Server instance to provide to handlers.</param>
		/// <param name="system">The InteractableSystem receiving handler registrations.</param>
		void RegisterHandlers(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server, InteractableSystem system);
	}
}