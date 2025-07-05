using FishMMO.Shared;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Server
{
    [CreateAssetMenu(fileName = "IsTeleporterValidCondition", menuName = "FishMMO/Conditions/Interactable/Is Teleporter Valid", order = 0)]
    public class IsTeleporterValidCondition : BaseCondition
    {
        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            if (!eventData.TryGet(out InteractableEventData interactableEventData))
            {
                Log.Error("IsTeleporterValidCondition", "Missing InteractableEventData.");
                return false;
            }

            Teleporter teleporter = interactableEventData.Interactable as Teleporter;
            if (teleporter == null)
            {
                Log.Warning("IsTeleporterValidCondition", $"Interactable {interactableEventData.Interactable?.GetType().Name} is not a Teleporter.");
                return false;
            }
            // Additional checks if necessary, e.g., teleporter has a target or scene name defined
            return true;
        }
    }
}