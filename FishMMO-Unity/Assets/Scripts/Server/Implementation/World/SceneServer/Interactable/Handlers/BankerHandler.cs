using FishMMO.Shared;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishNet.Transporting;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles interactions with banker objects, allowing players to access their bank and triggers NPC interaction logic.
	/// </summary>
	[HandlesInteractable(typeof(Banker))]
	public class BankerHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public BankerHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
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
		/// <param name="interactableSystem">The interactable system managing interactables.</param>
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem)
		{
			if (character.TryGet(out IBankController bankController))
			{
				bankController.LastInteractableID = sceneObject.ID;

				server.NetworkWrapper.Broadcast(character.Owner, new BankerBroadcast(), true, Channel.Reliable);

				// Tell the NPC to look at the interacting character
				interactableSystem.OnInteractNPC(character, interactable);

				// Increment achievement
				IBanker banker = interactable as IBanker;
				if (banker != null &&
					banker.AchievementTemplate != null &&
					character.TryGet(out IAchievementController achievementController))
				{
					achievementController.Increment(banker.AchievementTemplate, 1);
				}
			}
		}
	}
}