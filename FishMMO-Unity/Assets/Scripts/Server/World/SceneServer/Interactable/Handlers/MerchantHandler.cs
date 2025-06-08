using FishMMO.Shared;
using FishNet.Transporting;

namespace FishMMO.Server
{
	public class MerchantHandler : IInteractableHandler
	{
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance)
		{
			Merchant merchant = interactable as Merchant;
			if (merchant == null)
			{
				return;
			}
			Server.Broadcast(character.Owner, new MerchantBroadcast()
			{
				InteractableID = sceneObject.ID,
				TemplateID = merchant.Template.ID,
			}, true, Channel.Reliable);

			serverInstance.OnInteractNPC(character, interactable);
		}
	}
}