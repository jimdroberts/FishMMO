namespace FishMMO.Server
{
	/// <summary>
	/// Interface for initializing and registering interactable handlers in the FishMMO server.
	/// </summary>
	public interface IInteractableHandlerInitializer
	{
		/// <summary>
		/// Registers all interactable handlers with the system.
		/// </summary>
		void RegisterHandlers();
	}
}