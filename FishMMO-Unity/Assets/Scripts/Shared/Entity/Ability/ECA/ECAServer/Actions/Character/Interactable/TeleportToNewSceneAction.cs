using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server
{
    [CreateAssetMenu(fileName = "TeleportToNewSceneAction", menuName = "FishMMO/Actions/Teleporter/To New Scene Teleport", order = 0)]
    public class TeleportToNewSceneAction : BaseAction
    {
        public override void Execute(ICharacter initiator, EventData eventData)
        {
            if (!eventData.TryGet(out InteractableEventData interactableEventData))
            {
                Log.Error("TeleportToNewSceneAction: Missing InteractableEventData.");
                return;
            }

            IPlayerCharacter character = initiator as IPlayerCharacter;
            // The 'teleporter' here is the object from which we get the target scene name.
            // In the original code, `sceneObject.GameObject.name` was used, which implies the Teleporter itself is named after the target scene.
            // If the Teleporter has a dedicated 'targetSceneName' field, use that instead.
            // For now, let's stick to the original logic assuming sceneObject.GameObject.name is the target scene.
            
            if (character == null || interactableEventData.SceneObject == null || interactableEventData.SceneObject.GameObject == null)
            {
                Log.Warning("TeleportToNewSceneAction: Invalid character or scene object for scene teleport.");
                return;
            }
            
            string targetSceneName = interactableEventData.SceneObject.GameObject.name; // Based on original logic
            
            // Perform the scene change teleport
            character.Teleport(targetSceneName);
            Log.Debug($"Character {character.Name} initiated teleport to new scene: {targetSceneName}.");
        }
    }
}