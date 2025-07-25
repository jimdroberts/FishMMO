/*using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
    [CreateAssetMenu(fileName = "New IsCharacterInRegionCondition", menuName = "FishMMO/Triggers/Conditions/Region/Is Character In Region", order = 1)]
    public class IsCharacterInRegionCondition : BaseCondition
    {
        public RegionTemplate RequiredRegion;

        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            ICharacter characterToCheck = initiator;
            if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
            {
                characterToCheck = charTargetEventData.Target;
            }
            if (characterToCheck == null)
            {
                Log.Warning("IsCharacterInRegionCondition", "Character does not exist.");
                return false;
            }
            if (RequiredRegion == null)
            {
                Log.Warning("IsCharacterInRegionCondition", "RequiredRegion is not assigned.");
                return false;
            }
            if (!characterToCheck.TryGet(out IRegionController regionController))
            {
                Log.Warning("IsCharacterInRegionCondition", "Character does not have an IRegionController.");
                return false;
            }
            return regionController.CurrentRegion == RequiredRegion;
        }

        public override string GetFormattedDescription()
        {
            string regionName = RequiredRegion != null ? RequiredRegion.Name : "[Unassigned Region]";
            return $"Requires the character to be in region: {regionName}.";
        }
    }
}*/