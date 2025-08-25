using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.SceneServer
{
	/// <summary>
	/// Handles interactions with bindstone objects, allowing players to set their respawn location to the current scene and position.
	/// </summary>
	public class BindstoneHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public BindstoneHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

		/// <summary>
		/// Handles the interaction between a player character and a bindstone.
		/// Validates character and scene, then sets the character's bind position and scene for respawn.
		/// </summary>
		/// <param name="interactable">The interactable object (should be a bindstone).</param>
		/// <param name="character">The player character interacting with the bindstone.</param>
		/// <param name="sceneObject">The scene object associated with the interaction.</param>
		/// <param name="serverInstance">The server instance managing interactables.</param>
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance)
		{
			if (character == null)
			{
				Log.Debug("BindstoneHandler", "Character not found!");
				return;
			}

			// Validate same scene
			if (character.SceneName != sceneObject.GameObject.scene.name)
			{
				Log.Debug("BindstoneHandler", "Character is not in the same scene as the bindstone!");
				return;
			}

			if (!server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				Log.Debug("BindstoneHandler", "SceneServerSystem not found!");
				return;
			}

			character.BindPosition = character.Motor.Transform.position;
			character.BindScene = character.SceneName;
		}
	}
}