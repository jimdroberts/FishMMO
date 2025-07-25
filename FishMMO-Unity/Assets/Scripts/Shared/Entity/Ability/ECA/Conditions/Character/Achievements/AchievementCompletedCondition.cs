
using UnityEngine;

namespace FishMMO.Shared
{
    [CreateAssetMenu(fileName = "New Achievement Completed Condition", menuName = "FishMMO/Triggers/Conditions/Achievement/Achievement Completed")]
    public class AchievementCompletedCondition : BaseCondition
    {
        [Tooltip("The achievement template to check.")]
        public AchievementTemplate AchievementTemplate;

        [Tooltip("The required tier to consider the achievement completed. Leave 0 to require the last tier.")]
        public byte RequiredTier = 0;

        [Tooltip("The required value to consider the achievement completed. Leave 0 to require the next tier value or any value if RequiredTier is 0.")]
        public uint RequiredValue = 0;

        public override bool Evaluate(ICharacter initiator, EventData eventData = null)
        {
            ICharacter characterToCheck = initiator;
            if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
            {
                characterToCheck = charTargetEventData.Target;
            }

            if (characterToCheck == null)
                return false;

            if (AchievementTemplate == null)
                return false;

            if (characterToCheck.TryGet(out IAchievementController achievementController))
            {
                if (achievementController.TryGetAchievement(AchievementTemplate.ID, out Achievement achievement) && achievement != null)
                {
                    byte tierToCheck = RequiredTier;
                    uint valueToCheck = RequiredValue;
                    if (tierToCheck == 0 && achievement.Template != null && achievement.Template.Tiers != null)
                    {
                        tierToCheck = (byte)achievement.Template.Tiers.Count;
                    }
                    if (valueToCheck == 0)
                    {
                        // If RequiredValue is not set, use the next tier value if available, otherwise just check tier
                        if (tierToCheck > 0 && achievement.Template != null && achievement.Template.Tiers != null && achievement.Template.Tiers.Count >= tierToCheck)
                        {
                            valueToCheck = achievement.Template.Tiers[tierToCheck - 1].Value;
                        }
                    }
                    bool tierMet = achievement.CurrentTier >= tierToCheck && tierToCheck > 0;
                    bool valueMet = valueToCheck == 0 || achievement.CurrentValue >= valueToCheck;
                    return tierMet && valueMet;
                }
            }
            return false;
        }
    }
}
