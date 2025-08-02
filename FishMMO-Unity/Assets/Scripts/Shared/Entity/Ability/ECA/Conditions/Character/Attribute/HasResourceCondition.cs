using UnityEngine;
namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character has at least a required amount of a specified resource attribute (e.g., Mana, Health).
	/// </summary>
	[CreateAssetMenu(fileName = "HasResourceCondition", menuName = "FishMMO/Triggers/Conditions/Attribute/Has Resource", order = 0)]
	public class HasResourceCondition : BaseCondition
	{
		/// <summary>
		/// The resource attribute template to check (e.g., Mana, Health).
		/// </summary>
		public CharacterAttributeTemplate Template;

		/// <summary>
		/// The minimum amount of the resource required to pass the condition.
		/// </summary>
		public float RequiredAmount;
		/// <summary>
		/// Evaluates whether the character (or event target) has at least the required amount of the specified resource.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character has at least the required amount of the resource; otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			// If the character or attribute controller is missing, fail the condition.
			if (characterToCheck == null || !characterToCheck.TryGet(out ICharacterAttributeController attributeController))
				return false;
			// If the resource template is not set, fail the condition.
			if (Template == null)
				return false;
			// Try to get the resource attribute from the controller.
			if (attributeController.TryGetResourceAttribute(Template, out var resource))
			{
				// Check if the current value meets or exceeds the required amount.
				return resource.CurrentValue >= RequiredAmount;
			}
			return false;
		}
		/// <summary>
		/// Returns a formatted description of the resource condition for UI display.
		/// </summary>
		/// <returns>A string describing the required resource and amount.</returns>
		public override string GetFormattedDescription()
		{
			string resourceName = Template != null ? Template.Name : "[Unassigned Resource]";
			return $"Requires at least {RequiredAmount} {resourceName}.";
		}
	}
}