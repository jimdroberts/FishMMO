using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
    [CreateAssetMenu(fileName = "New CanUseItemCondition", menuName = "FishMMO/Triggers/Conditions/Inventory/Can Use Item", order = 1)]
    public class CanUseItemCondition : BaseCondition
    {
        public BaseItemTemplate RequiredItem;

        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            ICharacter characterToCheck = initiator;
            if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
            {
                characterToCheck = charTargetEventData.Target;
            }
            if (characterToCheck == null)
            {
                Log.Warning("CanUseItemCondition", "Character does not exist.");
                return false;
            }
            if (RequiredItem == null)
            {
                Log.Warning("CanUseItemCondition", "RequiredItem is not assigned.");
                return false;
            }
            if (!characterToCheck.TryGet(out IInventoryController inventoryController))
            {
                Log.Warning("CanUseItemCondition", "Character does not have an IInventoryController.");
                return false;
            }
            // FIXME : Add a check for item usage conditions, such as cooldowns or requirements.
            // For now, we will just check if the item exists in the inventory.
            return inventoryController.ContainsItem(RequiredItem);
        }

        public override string GetFormattedDescription()
        {
            string itemName = RequiredItem != null ? RequiredItem.Name : "[Unassigned Item]";
            return $"Requires the ability to use item: {itemName}.";
        }
    }
}
