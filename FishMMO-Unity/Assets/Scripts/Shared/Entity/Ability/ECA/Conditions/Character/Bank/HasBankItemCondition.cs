using UnityEngine;
namespace FishMMO.Shared
{
    /// <summary>
    /// Condition that checks if a character's bank contains a specific item template.
    /// </summary>
    [CreateAssetMenu(fileName = "HasBankItemCondition", menuName = "FishMMO/Triggers/Conditions/Bank/Has Bank Item", order = 0)]
    public class HasBankItemCondition : BaseCondition
    {
        /// <summary>
        /// The ID of the item template to check for in the character's bank.
        /// </summary>
        public int ItemTemplateID;
        /// <summary>
        /// Evaluates whether the character (or event target) has the specified item in their bank.
        /// </summary>
        /// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
        /// <param name="eventData">Optional event data that may provide a different character to check.</param>
        /// <returns>True if the character's bank contains the item; otherwise, false.</returns>
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
            // Get the item template by ID.
            var template = BaseItemTemplate.Get<BaseItemTemplate>(ItemTemplateID);
            if (template == null) return false;
            // Check if the bank contains the item template.
            return bankController.ContainsItem(template);
        }
    }
}