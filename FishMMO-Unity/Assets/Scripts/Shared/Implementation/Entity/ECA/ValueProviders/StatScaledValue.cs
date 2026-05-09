using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Value provider that scales a character attribute's final value by a configurable factor.
	/// Reads the initiator's (or event target's) attribute and returns <c>(int)(Attribute.FinalValue * ScaleFactor)</c>.
	/// </summary>
	[Serializable]
	public sealed class StatScaledValue : IIntValueProvider
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
		/// Selector that determines which character's attribute to read. When unset, reads from the
		/// current event target (or initiator if no target). Uses the first selected target that has
		/// an <see cref="ICharacter"/> component.
		/// </summary>
		[Tooltip("Selector that picks the source character whose attribute is read. When unset, uses the current event target or initiator.")]
		[SerializeReference, SubclassSelector]
		public TargetSelector SourceSelector;

		/// <inheritdoc/>
		public int GetValue(ICharacter initiator, EventData eventData)
		{
			if (AttributeTemplateID == 0)
			{
				Log.Warning("StatScaledValue", "AttributeTemplateID is not set. Returning 0.");
				return 0;
			}

			// Determine which character to read the attribute from.
			ICharacter source = eventData?.TargetCharacter ?? initiator;
			if (SourceSelector != null && eventData != null)
			{
				foreach (GameObject go in SourceSelector.SelectTargets(eventData))
				{
					if (go == null) continue;
					if (go.TryGetComponent(out ICharacter c) && c != null)
					{
						source = c;
						break;
					}
				}
			}

			if (source == null)
			{
				Log.Warning("StatScaledValue", "Source character is null. Returning 0.");
				return 0;
			}

			if (!source.TryGet(out ICharacterAttributeController attributeController))
			{
				Log.Warning("StatScaledValue", $"Character '{source.Name}' has no ICharacterAttributeController. Returning 0.");
				return 0;
			}

			if (attributeController.TryGetAttribute(AttributeTemplateID, out CharacterAttribute attribute))
			{
				return (int)(attribute.FinalValue * ScaleFactor);
			}

			Log.Warning("StatScaledValue", $"Character '{source.Name}' does not have attribute with ID '{AttributeTemplateID}'. Returning 0.");
			return 0;
		}
	}
}