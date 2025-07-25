using UnityEngine;
namespace FishMMO.Shared
{
    [CreateAssetMenu(fileName = "HasBankSpaceCondition", menuName = "FishMMO/Triggers/Conditions/Bank/Has Bank Space", order = 0)]
    public class HasBankSpaceCondition : BaseCondition
    {
        public int RequiredSpace = 1;
        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            ICharacter characterToCheck = initiator;
            if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
            {
                characterToCheck = charTargetEventData.Target;
            }
            if (characterToCheck == null || !characterToCheck.TryGet(out IBankController bankController))
                return false;
            return bankController.FreeSlots() >= RequiredSpace;
        }
    }
}