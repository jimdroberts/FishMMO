using FishMMO.Shared;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishNet.Connection;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles interactions with teleporter objects, allowing players to teleport to target locations or named destinations.
	/// </summary>
	[HandlesInteractable(typeof(Teleporter))]
	public class TeleporterHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public TeleporterHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

		/// <summary>
		/// Handles the interaction between a player character and a teleporter.
		/// If a target transform is set, moves the character to the target position and rotation. Otherwise, triggers teleport by name.
		/// </summary>
		/// <param name="interactable">The interactable object (should be a Teleporter).</param>
		/// <param name="character">The player character interacting with the teleporter.</param>
		/// <param name="sceneObject">The scene object associated with the interaction.</param>
		/// <param name="interactableSystem">The interactable system managing interactables.</param>
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem)
		{
			if (character.IsTeleporting)
			{
				return;
			}

			ITeleporter teleporter = interactable as ITeleporter;
			if (teleporter == null)
			{
				return;
			}

			if (teleporter.Target != null)
			{
				// move the character
				character.Motor.SetPositionAndRotationAndVelocity(teleporter.Target.position, teleporter.Target.rotation, Vector3.zero);
			}
			else
			{
				character.Teleport(sceneObject.GameObject.name);
			}

			// Increment achievement
			if (teleporter.AchievementTemplate != null &&
				character.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(teleporter.AchievementTemplate, 1);
			}
		}
	}
}