using FishMMO.Shared;

namespace FishMMO.Server
{
	public interface IInteractableHandler
	{
		void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance);
	}
}