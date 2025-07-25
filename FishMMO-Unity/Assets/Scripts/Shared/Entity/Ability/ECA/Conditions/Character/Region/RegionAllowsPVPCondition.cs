/*using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
    [CreateAssetMenu(fileName = "New RegionAllowsPVPCondition", menuName = "FishMMO/Triggers/Conditions/Region/Region Allows PVP", order = 1)]
    public class RegionAllowsPVPCondition : BaseCondition
    {
        public Region RequiredRegion;
        public bool MustAllowPVP = true;

        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            ICharacter characterToCheck = initiator;
            if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
            {
                characterToCheck = charTargetEventData.Target;
            }
            if (characterToCheck == null)
            {
                Log.Warning("RegionAllowsPVPCondition", "Character does not exist.");
                return false;
            }
            if (RequiredRegion == null)
            {
                Log.Warning("RegionAllowsPVPCondition", "RequiredRegion is not assigned.");
                return false;
            }
            if (!characterToCheck.TryGet(out IRegionController regionController))
            {
                Log.Warning("RegionAllowsPVPCondition", "Character does not have an IRegionController.");
                return false;
            }
            if (regionController.CurrentRegion != RequiredRegion)
                return false;
            return RequiredRegion.AllowsPVP == MustAllowPVP;
        }
    }
}
*/