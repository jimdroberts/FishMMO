using FishMMO.Shared;
using FishNet.Transporting;

namespace FishMMO.Server.Implementation.SceneServer
{
	/// <summary>
	/// Handles interactions with banker objects, allowing players to access their bank and triggers NPC interaction logic.
	/// </summary>
	   public class BankerHandler : IInteractableHandler
	   {
		   private readonly Server server;

		   public BankerHandler(Server server)
		   {
			   this.server = server;
		   }

		   /// <summary>
		   /// Handles the interaction between a player character and a banker.
		   /// Sets the last interactable ID, broadcasts bank access to the client, and triggers NPC look-at logic.
		   /// </summary>
		   /// <param name="interactable">The interactable object (should be a banker).</param>
		   /// <param name="character">The player character interacting with the banker.</param>
		   /// <param name="sceneObject">The scene object associated with the interaction.</param>
		   /// <param name="serverInstance">The server instance managing interactables.</param>
		   public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance)
		   {
			   if (character.TryGet(out IBankController bankController))
			   {
				   bankController.LastInteractableID = sceneObject.ID;

				   server.NetworkWrapper.Broadcast(character.Owner, new BankerBroadcast(), true, Channel.Reliable);

				   // Tell the NPC to look at the interacting character
				   serverInstance.OnInteractNPC(character, interactable);
			   }
		   }
	   }
}