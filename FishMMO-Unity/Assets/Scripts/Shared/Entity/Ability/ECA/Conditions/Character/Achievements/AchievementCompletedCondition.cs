
using UnityEngine;

namespace FishMMO.Shared
{
    /// <summary>
    /// Condition that checks if a character has completed a specified achievement, optionally at a required tier and value.
    /// </summary>
    [CreateAssetMenu(fileName = "New Achievement Completed Condition", menuName = "FishMMO/Triggers/Conditions/Achievement/Achievement Completed")]
    public class AchievementCompletedCondition : BaseCondition
    {
        /// <summary>
        /// The achievement template to check for completion.
        /// </summary>
        [Tooltip("The achievement template to check.")]
        public AchievementTemplate AchievementTemplate;

        /// <summary>
        /// The required tier to consider the achievement completed. Leave 0 to require the last tier.
        /// </summary>
        [Tooltip("The required tier to consider the achievement completed. Leave 0 to require the last tier.")]
        public byte RequiredTier = 0;

        /// <summary>
        /// The required value to consider the achievement completed. Leave 0 to require the next tier value or any value if RequiredTier is 0.
        /// </summary>
        [Tooltip("The required value to consider the achievement completed. Leave 0 to require the next tier value or any value if RequiredTier is 0.")]
        public uint RequiredValue = 0;

        /// <summary>
        /// Evaluates whether the specified character (or event target) has completed the achievement at the required tier and value.
        /// </summary>
        /// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
        /// <param name="eventData">Optional event data that may provide a different character to check.</param>
        /// <returns>True if the achievement is completed at the required tier and value; otherwise, false.</returns>
        public override bool Evaluate(ICharacter initiator, EventData eventData = null)
        {
            // Determine which character to check: use the event target if available, otherwise use the initiator.
            ICharacter characterToCheck = initiator;
            if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
            {
                characterToCheck = charTargetEventData.Target;
            }

            if (characterToCheck == null)
                return false;

            if (AchievementTemplate == null)
                return false;

            // Try to get the achievement controller from the character.
            if (characterToCheck.TryGet(out IAchievementController achievementController))
            {
                // Try to get the achievement instance for the specified template.
                if (achievementController.TryGetAchievement(AchievementTemplate.ID, out Achievement achievement) && achievement != null)
                {
                    byte tierToCheck = RequiredTier;
                    uint valueToCheck = RequiredValue;
                    // If no specific tier is required, use the last tier from the template.
                    if (tierToCheck == 0 && achievement.Template != null && achievement.Template.Tiers != null)
                    {
                        tierToCheck = (byte)achievement.Template.Tiers.Count;
                    }
                    // If no specific value is required, use the value for the required tier if available.
                    if (valueToCheck == 0)
                    {
                        // If RequiredValue is not set, use the next tier value if available, otherwise just check tier
                        if (tierToCheck > 0 && achievement.Template != null && achievement.Template.Tiers != null && achievement.Template.Tiers.Count >= tierToCheck)
                        {
                            valueToCheck = achievement.Template.Tiers[tierToCheck - 1].Value;
                        }
                    }
                    // Check if the achievement meets the required tier and value.
                    bool tierMet = achievement.CurrentTier >= tierToCheck && tierToCheck > 0;
                    bool valueMet = valueToCheck == 0 || achievement.CurrentValue >= valueToCheck;
                    return tierMet && valueMet;
                }
            }
            return false;
        }
    }
}