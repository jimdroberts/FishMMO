using FishMMO.Shared;
using FishNet.Transporting;

namespace FishMMO.Server
{
	/// <summary>
	/// Handles interactions with merchant objects, allowing players to open merchant interfaces and interact with NPC merchants.
	/// </summary>
	public class MerchantHandler : IInteractableHandler
	{
		/// <summary>
		/// Handles the interaction between a player character and a merchant.
		/// Broadcasts merchant data to the client and triggers NPC interaction logic.
		/// </summary>
		/// <param name="interactable">The interactable object (should be a Merchant).</param>
		/// <param name="character">The player character interacting with the merchant.</param>
		/// <param name="sceneObject">The scene object associated with the interaction.</param>
		/// <param name="serverInstance">The server instance managing interactables.</param>
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