using FishMMO.Shared;

namespace FishMMO.Server
{
	public class InteractableEventData : EventData
	{
		public IInteractable Interactable { get; }
		public ISceneObject SceneObject { get; }
		public InteractableSystem ServerInstance { get; }

		public InteractableEventData(ICharacter initiator, IInteractable interactable, ISceneObject sceneObject, InteractableSystem serverInstance)
			: base(initiator)
		{
			Interactable = interactable;
			SceneObject = sceneObject;
			ServerInstance = serverInstance;
		}
	}
}