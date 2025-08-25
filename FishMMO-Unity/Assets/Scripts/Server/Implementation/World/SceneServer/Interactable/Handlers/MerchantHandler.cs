using FishMMO.Shared;
using FishMMO.Server.Core;
using FishNet.Transporting;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.SceneServer
{
	/// <summary>
	/// Handles interactions with merchant objects, allowing players to open merchant interfaces and interact with NPC merchants.
	/// </summary>
	public class MerchantHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public MerchantHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

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
			server.NetworkWrapper.Broadcast(character.Owner, new MerchantBroadcast()
			{
				InteractableID = sceneObject.ID,
				TemplateID = merchant.Template.ID,
			}, true, Channel.Reliable);

			serverInstance.OnInteractNPC(character, interactable);
		}
	}
}