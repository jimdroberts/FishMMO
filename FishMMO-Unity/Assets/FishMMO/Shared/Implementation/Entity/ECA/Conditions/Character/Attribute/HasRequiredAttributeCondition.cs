using System;
using FishMMO.Logging;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character has a required value for a specified attribute, with optional inversion.
	/// </summary>
	[Serializable]
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

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (Template == null)
			{
				Log.Error("HasRequiredCharacterAttribute", $"Attribute Name is not set for '{GetType().Name}'. Condition failed.");
				return false;
			}

			ICharacter characterToCheck = (eventData?.TargetCharacter ?? initiator);

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
			if (!meetsRequirement)
			{
				Log.Debug("HasRequiredCharacterAttribute", $"Character '{characterToCheck?.Name}' failed stat check for '{Template.Name}'. Current: {characterAttribute.FinalValue}, Required: {RequiredValue}.");
			}
			return meetsRequirement;
		}

		/// <inheritdoc />
		public override string GetTooltipContribution()
		{
			if (Template != null && RequiredValue > 0)
			{
				return $"{Template.Name}: {RequiredValue}";
			}
			return null;
		}
	}
}