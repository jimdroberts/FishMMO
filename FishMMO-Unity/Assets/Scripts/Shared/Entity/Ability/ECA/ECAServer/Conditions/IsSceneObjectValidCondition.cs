using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server
{
    [CreateAssetMenu(fileName = "IsSceneObjectValidCondition", menuName = "FishMMO/Conditions/Interactable/Is SceneObject Valid", order = 0)]
    public class IsSceneObjectValidCondition : BaseCondition
    {
        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            if (!eventData.TryGet(out InteractableEventData interactableEventData))
            {
                Log.Error("IsSceneObjectValidCondition: Missing InteractableEventData.");
                return false;
            }

            if (interactableEventData.SceneObject == null ||
				interactableEventData.SceneObject.GameObject == null)
            {
                Log.Warning($"IsSceneObjectValidCondition: SceneObject is missing or invalid.");
                return false;
            }
            return true;
        }
    }
}