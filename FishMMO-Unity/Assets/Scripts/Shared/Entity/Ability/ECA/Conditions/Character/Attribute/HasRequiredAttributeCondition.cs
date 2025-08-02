using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	/// <summary>
	/// Condition that checks if a character has a required value for a specified attribute, with optional inversion.
	/// </summary>
	[CreateAssetMenu(fileName = "HasRequiredAttribute", menuName = "FishMMO/Triggers/Conditions/Attribute/Has Required Attribute", order = 0)]
	public class HasRequiredAttribute : BaseCondition
	{
		[Header("Stat Requirements")]
		/// <summary>
		/// The attribute template to check (e.g., 'Strength', 'Health', 'Mana').
		/// </summary>
		[Tooltip("The name of the attribute (e.g., 'Strength', 'Health', 'Mana').")]
		public CharacterAttributeTemplate Template;

		/// <summary>
		/// The minimum value the character's attribute must have to pass the condition.
		/// </summary>
		[Tooltip("The minimum FinalValue the character's attribute must have.")]
		public int RequiredValue;

		/// <summary>
		/// If true, the condition passes if the character does NOT meet the stat requirement (inverts the result).
		/// </summary>
		[Tooltip("If true, the condition passes if the character does NOT meet the stat requirement.")]
		public bool InvertResult = false;

		/// <summary>
		/// Evaluates whether the character (or event target) meets the required attribute value, with optional inversion.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the condition is met (or not met, if inverted); otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Check if the attribute template is set.
			if (Template == null)
			{
				Log.Error("HasRequiredCharacterAttribute", $"Attribute Name is not set for '{name}'. Condition failed.");
				return false;
			}

			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}

			// Try to get the attribute controller from the character.
			if (!characterToCheck.TryGet(out ICharacterAttributeController attributeController))
			{
				Log.Warning("HasRequiredCharacterAttribute", $"Character '{characterToCheck?.Name}' does not have an ICharacterAttributeController. Condition failed.");
				return false;
			}

			// Try to get the specific attribute from the controller.
			if (!attributeController.TryGetAttribute(Template, out CharacterAttribute characterAttribute))
			{
				Log.Warning("HasRequiredCharacterAttribute", $"Character '{characterToCheck?.Name}' does not have the specified Character Attribute. Condition failed.");
				return false;
			}

			// Check if the attribute meets the required value.
			bool meetsRequirement = characterAttribute.FinalValue >= RequiredValue;
			// Optionally invert the result.
			bool finalResult = InvertResult ? !meetsRequirement : meetsRequirement;

			if (!finalResult)
			{
				string status = meetsRequirement ?
					$"has {characterAttribute.FinalValue} (meets requirement)" :
					$"has {characterAttribute.FinalValue} (does NOT meet requirement)";

				Log.Debug("HasRequiredCharacterAttribute", $"Character '{characterToCheck?.Name}' failed stat check for '{Template.Name}'. Current: {characterAttribute.FinalValue}, Required: {RequiredValue}. Inverted: {InvertResult}.");
			}

			return finalResult;
		}

		/// <summary>
		/// Returns a formatted description of the required attribute condition for UI display.
		/// </summary>
		/// <returns>A string describing the required attribute and value, and whether the result is inverted.</returns>
		public override string GetFormattedDescription()
		{
			string attrName = Template != null ? Template.Name : "[Unassigned Attribute]";
			string invert = InvertResult ? "not " : string.Empty;
			return $"Requires the character to {invert} have at least {RequiredValue} {attrName}.";
		}
	}
}