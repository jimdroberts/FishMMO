using FishMMO.Shared;
using FishNet.Transporting;

namespace FishMMO.Server
{
	public class AbilityCrafterHandler : IInteractableHandler
	{
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance)
		{
			Server.Broadcast(character.Owner, new AbilityCrafterBroadcast()
			{
				InteractableID = sceneObject.ID,
			}, true, Channel.Reliable);

			// Tell the NPC to look at the interacting character
			serverInstance.OnInteractNPC(character, interactable);
		}
	}
}