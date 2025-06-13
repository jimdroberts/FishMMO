using UnityEngine;

namespace FishMMO.Shared
{
    [CreateAssetMenu(fileName = "New Faction Condition", menuName = "FishMMO/Conditions/Faction Condition", order = 1)]
    public class FactionCondition : BaseCondition
    {
        [Tooltip("The specific faction this condition pertains to.")]
        public FactionTemplate TargetFaction;

        [Tooltip("The required alliance level with the Target Faction.")]
        public FactionAllianceLevel RequiredAllianceLevel;

        [Tooltip("Optional: If checking a specific reputation value, e.g., 'at least Friendly' (value >= 25).")]
        public int MinimumReputationValue;

        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            if (initiator == null)
            {
                Debug.LogWarning($"FactionCondition: Player character does not exist.");
                return false;
            }

            if (TargetFaction == null)
            {
                Debug.LogWarning($"FactionCondition: TargetFaction is not assigned for {this.name}.");
                return false;
            }

            if (!initiator.TryGet(out IFactionController playerFactionController))
            {
                Debug.LogWarning($"FactionCondition: Player character does not have an IFactionController.");
                return false;
            }

            // Get the player's current relationship with the target faction
            // This assumes your IFactionController has a method to get the player's reputation value
            // or directly gives access to the Factions dictionary.
            int playerFactionValue = 0;
            if (playerFactionController.Factions.TryGetValue(TargetFaction.ID, out Faction currentFaction))
            {
                playerFactionValue = currentFaction.Value;
            }
            // If the faction isn't explicitly tracked, assume it's neutral (value 0)
            // or whatever your default non-explicitly-set faction value is.

            // Determine the player's current Alliance Level based on their faction value
            // You might have a helper method in FactionTemplate or FactionController
            // to map values to FactionAllianceLevel.
            FactionAllianceLevel currentAllianceLevel = FactionAllianceLevel.Neutral;

            // This mapping logic should ideally live in your FactionTemplate or a FactionUtility class.
            // For example purposes, let's put some basic logic here:
            if (playerFactionValue >= FactionTemplate.Maximum / 2)
            {
                currentAllianceLevel = FactionAllianceLevel.Ally;
            }
            else if (playerFactionValue <= FactionTemplate.Minimum / 2)
            {
                currentAllianceLevel = FactionAllianceLevel.Enemy;
            }
            else
            {
                currentAllianceLevel = FactionAllianceLevel.Neutral;
            }


            // First, check the required alliance level
            bool allianceLevelMet = currentAllianceLevel == RequiredAllianceLevel;

            // If a minimum reputation value is also specified, check that as well
            if (allianceLevelMet && MinimumReputationValue > FactionTemplate.Minimum) // Check if MinimumReputationValue is actually being used
            {
                // This check applies only if the current alliance level matches the required one
                // AND the specific minimum value is also required.
                return playerFactionValue >= MinimumReputationValue;
            }

            return allianceLevelMet;
        }
    }
}