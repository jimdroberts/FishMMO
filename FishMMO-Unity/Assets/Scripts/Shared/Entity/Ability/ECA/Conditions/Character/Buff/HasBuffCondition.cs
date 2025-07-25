using UnityEngine;
namespace FishMMO.Shared
{
    [CreateAssetMenu(fileName = "HasBuffCondition", menuName = "FishMMO/Triggers/Conditions/Buff/Has Buff", order = 0)]
    public class HasBuffCondition : BaseCondition
    {
        public BaseBuffTemplate BuffTemplate;
        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            ICharacter characterToCheck = initiator;
            if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
            {
                characterToCheck = charTargetEventData.Target;
            }
            if (characterToCheck == null || !characterToCheck.TryGet(out IBuffController buffController))
                return false;
            if (BuffTemplate == null)
                return false;
            return buffController.Buffs != null && buffController.Buffs.ContainsKey(BuffTemplate.ID);
        }
    }
}