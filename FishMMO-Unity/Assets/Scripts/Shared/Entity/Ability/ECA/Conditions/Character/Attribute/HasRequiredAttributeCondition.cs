using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	[CreateAssetMenu(fileName = "HasRequiredAttribute", menuName = "FishMMO/Triggers/Conditions/Attribute/Has Required Attribute", order = 0)]
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
				Log.Error("HasRequiredCharacterAttribute", $"Attribute Name is not set for '{name}'. Condition failed.");
				return false;
			}

			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}

			if (!characterToCheck.TryGet(out ICharacterAttributeController attributeController))
			{
				Log.Warning("HasRequiredCharacterAttribute", $"Character '{characterToCheck?.Name}' does not have an ICharacterAttributeController. Condition failed.");
				return false;
			}

			if (!attributeController.TryGetAttribute(Template, out CharacterAttribute characterAttribute))
			{
				Log.Warning("HasRequiredCharacterAttribute", $"Character '{characterToCheck?.Name}' does not have the specified Character Attribute. Condition failed.");
				return false;
			}

			bool meetsRequirement = characterAttribute.FinalValue >= RequiredValue;
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
	}
}