using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
    [CreateAssetMenu(fileName = "New Race Condition", menuName = "FishMMO/Triggers/Conditions/Race/Is Race Condition", order = 1)]
    public class IsRaceCondition : BaseCondition
    {
        public RaceTemplate RequiredRace;

        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            ICharacter characterToCheck = initiator;
            if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
            {
                characterToCheck = charTargetEventData.Target;
            }
            if (characterToCheck == null)
            {
                Log.Warning("IsRaceCondition", "Character does not exist.");
                return false;
            }
            if (RequiredRace == null)
            {
                Log.Warning("IsRaceCondition", "RequiredRace is not assigned.");
                return false;
            }
            if (characterToCheck is IPlayerCharacter playerCharacter)
            {
                return playerCharacter.RaceID == RequiredRace.ID;
            }
            Log.Warning("IsRaceCondition", "Character is not a player character.");
            return false;
        }
    }
}
