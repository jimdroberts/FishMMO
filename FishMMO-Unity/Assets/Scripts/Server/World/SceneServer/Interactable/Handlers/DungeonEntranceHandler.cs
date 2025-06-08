using FishMMO.Shared;
using FishNet.Transporting;

namespace FishMMO.Server
{
	public class DungeonEntranceHandler : IInteractableHandler
	{
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance)
		{
			Server.Broadcast(character.Owner, new DungeonFinderBroadcast()
			{
				InteractableID = sceneObject.ID,
			}, true, Channel.Reliable);
		}
	}
}