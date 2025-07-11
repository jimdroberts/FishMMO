using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server
{
	public class BindstoneHandler : IInteractableHandler
	{
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

			if (!ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				Log.Debug("BindstoneHandler", "SceneServerSystem not found!");
				return;
			}

			character.BindPosition = character.Motor.Transform.position;
			character.BindScene = character.SceneName;
		}
	}
}