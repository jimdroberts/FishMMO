using FishMMO.Shared;
using FishNet.Transporting;

namespace FishMMO.Server
{
	public class BankerHandler : IInteractableHandler
	{
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance)
		{
			if (character.TryGet(out IBankController bankController))
			{
				bankController.LastInteractableID = sceneObject.ID;

				Server.Broadcast(character.Owner, new BankerBroadcast(), true, Channel.Reliable);

				// Tell the NPC to look at the interacting character
				serverInstance.OnInteractNPC(character, interactable);
			}
		}
	}
}