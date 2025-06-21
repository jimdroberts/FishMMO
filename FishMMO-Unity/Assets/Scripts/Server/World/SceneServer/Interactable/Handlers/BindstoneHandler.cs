using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server
{
	public class BindstoneHandler : IInteractableHandler
	{
		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, InteractableSystem serverInstance)
		{
			if (character == null)
			{
				Log.Debug("Character not found!");
				return;
			}

			// Validate same scene
			if (character.SceneName != sceneObject.GameObject.scene.name)
			{
				Log.Debug("Character is not in the same scene as the bindstone!");
				return;
			}

			if (!ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				Log.Debug("SceneServerSystem not found!");
				return;
			}

			character.BindPosition = character.Motor.Transform.position;
			character.BindScene = character.SceneName;
		}
	}
}