using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server
{
    [CreateAssetMenu(fileName = "New Same Scene Condition", menuName = "FishMMO/Conditions/Interactable/Same Scene", order = 0)]
    public class SameSceneCondition : BaseCondition
    {
        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            if (!eventData.TryGet(out InteractableEventData interactableEventData))
            {
                Log.Error("IsInSameSceneAsInteractableCondition: Missing InteractableEventData.");
                return false;
            }

            // Ensure character and scene object are available
            if (initiator == null || interactableEventData.SceneObject == null || interactableEventData.SceneObject.GameObject == null)
            {
                Log.Warning("IsInSameSceneAsInteractableCondition: Invalid character or scene object data.");
                return false;
            }

            // Perform the scene name check
            if (initiator.GameObject.scene.name != interactableEventData.SceneObject.GameObject.scene.name)
            {
                Log.Debug("IsInSameSceneAsInteractableCondition: Character is not in the same scene as the interactable.");
                return false;
            }
            return true;
        }
    }
}