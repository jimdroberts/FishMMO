using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Handler for a specific interactable type. Implementations perform the interaction logic.
	/// </summary>
	public interface IInteractableHandler
	{
		void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem);
	}
}