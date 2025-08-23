using FishMMO.Shared;
using FishMMO.Server.Core;
using FishNet.Transporting;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.SceneServer
{
	/// <summary>
	/// Handles interactions with dungeon entrance objects, allowing players to open the dungeon finder interface.
	/// </summary>
	public class DungeonEntranceHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, ServerBehaviour> server;

		public DungeonEntranceHandler(IServer<INetworkManagerWrapper, NetworkConnection, ServerBehaviour> server)
		{
			this.server = server;
		}

		/// <summary>
		/// Handles the interaction between a player character and a dungeon entrance.
		/// Broadcasts dungeon finder data to the client.
		/// </summary>
		/// <param name="interactable">The interactable object (should be a dungeon entrance).</param>
		/// <param name="character">The player character interacting with the dungeon entrance.</param>
		/// <param name="sceneObject">The scene object associated with the interaction.</param>
		/// <param name="serverInstance">The server instance managing interactables.</param>
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance)
		{
			server.NetworkWrapper.Broadcast(character.Owner, new DungeonFinderBroadcast()
			{
				InteractableID = sceneObject.ID,
			}, true, Channel.Reliable);
		}
	}
}