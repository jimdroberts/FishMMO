using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server
{
	/// <summary>
	/// Handles interactions with teleporter objects, allowing players to teleport to target locations or named destinations.
	/// </summary>
	public class TeleporterHandler : IInteractableHandler
	{
		/// <summary>
		/// Handles the interaction between a player character and a teleporter.
		/// If a target transform is set, moves the character to the target position and rotation. Otherwise, triggers teleport by name.
		/// </summary>
		/// <param name="interactable">The interactable object (should be a Teleporter).</param>
		/// <param name="character">The player character interacting with the teleporter.</param>
		/// <param name="sceneObject">The scene object associated with the interaction.</param>
		/// <param name="serverInstance">The server instance managing interactables.</param>
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance)
		{
			if (character.IsTeleporting)
			{
				return;
			}

			Teleporter teleporter = interactable as Teleporter;
			if (teleporter == null)
			{
				return;
			}

			if (teleporter.Target != null)
			{
				// move the character
				character.Motor.SetPositionAndRotationAndVelocity(teleporter.Target.position, teleporter.Target.rotation, Vector3.zero);
				return;
			}

			character.Teleport(sceneObject.GameObject.name);
		}
	}
}