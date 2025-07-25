using UnityEngine;
namespace FishMMO.Shared
{
    [CreateAssetMenu(fileName = "HasBankItemCondition", menuName = "FishMMO/Triggers/Conditions/Bank/Has Bank Item", order = 0)]
    public class HasBankItemCondition : BaseCondition
    {
        public int ItemTemplateID;
        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            ICharacter characterToCheck = initiator;
            if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
            {
                characterToCheck = charTargetEventData.Target;
            }
            if (characterToCheck == null || !characterToCheck.TryGet(out IBankController bankController))
                return false;
            var template = BaseItemTemplate.Get<BaseItemTemplate>(ItemTemplateID);
            if (template == null) return false;
            return bankController.ContainsItem(template);
        }
    }
}