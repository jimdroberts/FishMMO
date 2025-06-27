using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	[CreateAssetMenu(fileName = "HasRequiredAttribute", menuName = "FishMMO/Conditions/Character/Has Required Attribute", order = 0)]
	public class HasRequiredAttribute : BaseCondition
	{
		[Header("Stat Requirements")]
		[Tooltip("The name of the attribute (e.g., 'Strength', 'Health', 'Mana').")]
		public CharacterAttributeTemplate Template;

		[Tooltip("The minimum FinalValue the character's attribute must have.")]
		public int RequiredValue;

		[Tooltip("If true, the condition passes if the character does NOT meet the stat requirement.")]
		public bool InvertResult = false;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (Template == null)
			{
				Log.Error($"HasRequiredCharacterAttribute: Attribute Name is not set for '{name}'. Condition failed.");
				return false;
			}

			// Try to get the ICharacterAttributeController from the initiator.
			if (!initiator.TryGet(out ICharacterAttributeController attributeController))
			{
				Log.Warning($"HasRequiredCharacterAttribute: Initiator '{initiator?.Name}' does not have an ICharacterAttributeController. Condition failed.");
				return false;
			}

			// Get the specific CharacterAttribute instance from the controller.
			if (!attributeController.TryGetAttribute(Template, out CharacterAttribute characterAttribute))
			{
				Log.Warning($"HasRequiredCharacterAttribute: Initiator '{initiator?.Name}' does not have the specified Character Attribute. Condition failed.");
				return false;
			}

			// Check if the attribute's FinalValue meets the required value.
			bool meetsRequirement = characterAttribute.FinalValue >= RequiredValue;

			// Apply inversion logic
			bool finalResult = InvertResult ? !meetsRequirement : meetsRequirement;

			// Optional: Detailed logging for debugging
			if (!finalResult)
			{
				string status = meetsRequirement ?
					$"has {characterAttribute.FinalValue} (meets requirement)" :
					$"has {characterAttribute.FinalValue} (does NOT meet requirement)";

				Log.Debug($"HasRequiredCharacterAttribute: Character '{initiator?.Name}' failed stat check for '{Template.Name}'. Current: {characterAttribute.FinalValue}, Required: {RequiredValue}. Inverted: {InvertResult}.");
			}

			return finalResult;
		}
	}
}