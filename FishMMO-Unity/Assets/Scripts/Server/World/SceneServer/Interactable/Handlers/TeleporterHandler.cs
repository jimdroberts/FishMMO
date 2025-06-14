using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server
{
	public class TeleporterHandler : IInteractableHandler
	{
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