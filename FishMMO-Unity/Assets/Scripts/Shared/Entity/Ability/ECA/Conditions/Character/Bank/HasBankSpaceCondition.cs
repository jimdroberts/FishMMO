using UnityEngine;
namespace FishMMO.Shared
{
    /// <summary>
    /// Condition that checks if a character's bank has at least a specified number of free slots.
    /// </summary>
    [CreateAssetMenu(fileName = "HasBankSpaceCondition", menuName = "FishMMO/Triggers/Conditions/Bank/Has Bank Space", order = 0)]
    public class HasBankSpaceCondition : BaseCondition
    {
        /// <summary>
        /// The minimum number of free slots required in the bank for the condition to pass.
        /// </summary>
        public int RequiredSpace = 1;

        /// <summary>
        /// Evaluates whether the character (or event target) has at least the required number of free bank slots.
        /// </summary>
        /// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
        /// <param name="eventData">Optional event data that may provide a different character to check.</param>
        /// <returns>True if the character's bank has enough free slots; otherwise, false.</returns>
        public override bool Evaluate(ICharacter initiator, EventData eventData)
        {
            // Determine which character to check: use the event target if available, otherwise use the initiator.
            ICharacter characterToCheck = initiator;
            if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
            {
                characterToCheck = charTargetEventData.Target;
            }
            // Check if the character and bank controller exist.
            if (characterToCheck == null || !characterToCheck.TryGet(out IBankController bankController))
                return false;
            // Check if the bank has the required free space.
            return bankController.FreeSlots() >= RequiredSpace;
        }
    }
}