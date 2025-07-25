using UnityEngine;
namespace FishMMO.Shared
{
    [CreateAssetMenu(fileName = "HasResourceCondition", menuName = "FishMMO/Triggers/Conditions/Attribute/Has Resource", order = 0)]
    public class HasResourceCondition : BaseCondition
    {
        public CharacterAttributeTemplate Template;
        public float RequiredAmount;
        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            ICharacter characterToCheck = initiator;
            if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
            {
                characterToCheck = charTargetEventData.Target;
            }
            if (characterToCheck == null || !characterToCheck.TryGet(out ICharacterAttributeController attributeController))
                return false;
            // Use TryGetResourceAttribute with the template
            if (Template == null)
                return false;
            if (attributeController.TryGetResourceAttribute(Template, out var resource))
            {
                return resource.CurrentValue >= RequiredAmount;
            }
            return false;
        }
    }
}