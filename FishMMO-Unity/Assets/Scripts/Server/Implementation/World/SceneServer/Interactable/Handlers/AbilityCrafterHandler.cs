using FishMMO.Shared;
using FishMMO.Server.Core;
using FishNet.Transporting;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.SceneServer
{
	/// <summary>
	/// Handles interactions with ability crafter objects, allowing players to open the ability crafting interface and triggers NPC interaction logic.
	/// </summary>
	public class AbilityCrafterHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public AbilityCrafterHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

		/// <summary>
		/// Handles the interaction between a player character and an ability crafter.
		/// Broadcasts ability crafter data to the client and triggers NPC look-at logic.
		/// </summary>
		/// <param name="interactable">The interactable object (should be an ability crafter).</param>
		/// <param name="character">The player character interacting with the ability crafter.</param>
		/// <param name="sceneObject">The scene object associated with the interaction.</param>
		/// <param name="serverInstance">The server instance managing interactables.</param>
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance)
		{
			server.NetworkWrapper.Broadcast(character.Owner, new AbilityCrafterBroadcast()
			{
				InteractableID = sceneObject.ID,
			}, true, Channel.Reliable);

			// Tell the NPC to look at the interacting character
			serverInstance.OnInteractNPC(character, interactable);
		}
	}
}