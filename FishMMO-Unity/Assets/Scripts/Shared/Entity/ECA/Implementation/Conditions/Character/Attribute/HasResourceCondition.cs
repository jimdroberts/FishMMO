using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character has at least a required amount of a specified resource attribute (e.g., Mana, Health).
	/// Also implements <see cref="IResourceCost"/> for ability resource cost aggregation and <see cref="ITooltipContributor"/> for tooltip display.
	/// </summary>
	[Serializable]
	public class HasResourceCondition : BaseCondition, IResourceCost, ITooltipContributor
	{
		/// <summary>
		/// The resource attribute template to check (e.g., Mana, Health).
		/// </summary>
		public CharacterAttributeTemplate Template;

		/// <summary>
		/// The minimum amount of the resource required to pass the condition.
		/// </summary>
		public int RequiredAmount;

		/// <summary>
		/// The resource attribute template, as required by <see cref="IResourceCost"/>.
		/// </summary>
		CharacterAttributeTemplate IResourceCost.ResourceTemplate => Template;

		/// <summary>
		/// The resource amount, as required by <see cref="IResourceCost"/>.
		/// </summary>
		int IResourceCost.ResourceAmount => RequiredAmount;
		/// <summary>
		/// Evaluates whether the character (or event target) has at least the required amount of the specified resource.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character has at least the required amount of the resource; otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = ResolveTarget(initiator, eventData);
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
		/// Returns the tooltip contribution showing the resource cost.
		/// </summary>
		public string GetTooltipContribution()
		{
			if (Template != null && RequiredAmount > 0)
			{
				return RichText.Format(Template.Name, RequiredAmount, true, "f5ad6eFF", "", "", "120%");
			}
			return null;
		}
	}
}