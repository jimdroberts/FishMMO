using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Float value provider that scales a character attribute's final value by a configurable factor.
	/// Reads the initiator's (or event target's) attribute and returns <c>Attribute.FinalValue * ScaleFactor</c>.
	/// </summary>
	[Serializable]
	public sealed class StatScaledFloatValue : IFloatValueProvider
	{
		/// <summary>
		/// The character attribute to read the value from (e.g., Strength, Intelligence).
		/// </summary>
		[Tooltip("The character attribute to scale from (e.g., Strength, Intelligence).")]
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int AttributeTemplateID;

		/// <summary>
		/// The multiplier applied to the attribute's final value.
		/// </summary>
		[Tooltip("The multiplier applied to the attribute's final value.")]
		public float ScaleFactor = 1.0f;

		/// <summary>
		/// Provider that determines which character's attribute to read. When unset, reads from the initiator.
		/// </summary>
		[Tooltip("Provider that determines which character's attribute to read. When unset, reads from the initiator.")]
		[SerializeReference, SubclassSelector]
		public ICharacterProvider SourceProvider;

		/// <inheritdoc/>
		public float GetValue(ICharacter initiator, EventData eventData)
		{
			if (AttributeTemplateID == 0)
			{
				Log.Warning("StatScaledFloatValue", "AttributeTemplateID is not set. Returning 0.");
				return 0f;
			}

			// Determine which character to read the attribute from.
			ICharacter source = initiator;
			if (SourceProvider != null)
			{
				source = SourceProvider.GetCharacter(initiator, eventData) ?? initiator;
			}

			if (source == null)
			{
				Log.Warning("StatScaledFloatValue", "Source character is null. Returning 0.");
				return 0f;
			}

			if (!source.TryGet(out ICharacterAttributeController attributeController))
			{
				Log.Warning("StatScaledFloatValue", $"Character '{source.Name}' has no ICharacterAttributeController. Returning 0.");
				return 0f;
			}

			if (attributeController.TryGetAttribute(AttributeTemplateID, out CharacterAttribute attribute))
			{
				return attribute.FinalValue * ScaleFactor;
			}

			Log.Warning("StatScaledFloatValue", $"Character '{source.Name}' does not have attribute with ID '{AttributeTemplateID}'. Returning 0.");
			return 0f;
		}
	}
}