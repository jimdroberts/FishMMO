using System;
using UnityEngine;
using FishMMO.Logging;

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
		public CharacterAttributeTemplate AttributeTemplate;

		/// <summary>
		/// The multiplier applied to the attribute's final value.
		/// </summary>
		[Tooltip("The multiplier applied to the attribute's final value.")]
		public float ScaleFactor = 1.0f;

		/// <summary>
		/// If true, reads the attribute from the event target instead of the initiator.
		/// </summary>
		[Tooltip("If true, reads the attribute from the event target instead of the initiator.")]
		public bool UseTarget;

		/// <inheritdoc/>
		public float GetValue(ICharacter initiator, EventData eventData)
		{
			if (AttributeTemplate == null)
			{
				Log.Warning("StatScaledFloatValue", "AttributeTemplate is null. Returning 0.");
				return 0f;
			}

			// Determine which character to read the attribute from.
			ICharacter source = initiator;
			if (UseTarget &&
				eventData != null &&
				eventData.TryGet(out CharacterHitEventData hitData) &&
				hitData.Target != null)
			{
				source = hitData.Target;
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

			if (attributeController.TryGetAttribute(AttributeTemplate, out CharacterAttribute attribute))
			{
				return attribute.FinalValue * ScaleFactor;
			}

			Log.Warning("StatScaledFloatValue", $"Character '{source.Name}' does not have attribute '{AttributeTemplate.Name}'. Returning 0.");
			return 0f;
		}
	}
}