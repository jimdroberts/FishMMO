using FishMMO.Shared;

namespace FishMMO.Server
{
	/// <summary>
	/// Interface for handling interactions between player characters and interactable objects in the FishMMO server.
	/// </summary>
	public interface IInteractableHandler
	{
		/// <summary>
		/// Handles the interaction between a player character and an interactable object.
		/// </summary>
		/// <param name="interactable">The interactable object being interacted with.</param>
		/// <param name="character">The player character performing the interaction.</param>
		/// <param name="sceneObject">The scene object associated with the interaction.</param>
		/// <param name="serverInstance">The server instance managing interactables.</param>
		void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance);
	}
}