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
				Debug.Log("Character not found!");
				return;
			}

			// Validate same scene
			if (character.SceneName != sceneObject.GameObject.scene.name)
			{
				Debug.Log("Character is not in the same scene as the bindstone!");
				return;
			}

			if (!ServerBehaviour.TryGet(out SceneServerSystem sceneServerSystem))
			{
				Debug.Log("SceneServerSystem not found!");
				return;
			}

			using var dbContext = sceneServerSystem.Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				Debug.Log("Could not get database context.");
				return;
			}

			character.BindPosition = character.Motor.Transform.position;
			character.BindScene = character.SceneName;
		}
	}
}