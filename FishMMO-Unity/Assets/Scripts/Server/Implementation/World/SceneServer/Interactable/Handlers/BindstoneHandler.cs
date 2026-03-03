using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles interactions with bindstone objects, allowing players to set their respawn location to the current scene and position.
	/// </summary>
	[HandlesInteractable(typeof(Bindstone))]
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
		/// <param name="interactableSystem">The interactable system managing interactables.</param>
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem)
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

			// Increment achievement
			IBindstone bindstone = interactable as IBindstone;
			if (bindstone != null &&
				bindstone.AchievementTemplate != null &&
				character.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(bindstone.AchievementTemplate, 1);
			}
		}
	}
}