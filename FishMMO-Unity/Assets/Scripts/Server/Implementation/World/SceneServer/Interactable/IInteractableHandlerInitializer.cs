namespace FishMMO.Server.Implementation.SceneServer
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
		void RegisterHandlers(Server server);
	}
}