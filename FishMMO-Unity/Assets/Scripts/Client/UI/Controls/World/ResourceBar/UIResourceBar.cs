using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Abstract class representing a UI resource bar for a character, such as health or stamina.
	/// </summary>
	public abstract class UIResourceBar : UICharacterControl
	{
		/// <summary>
		/// The slider UI element representing the resource value visually.
		/// </summary>
		public Slider slider;
		/// <summary>
		/// The text UI element displaying the current and maximum resource value.
		/// </summary>
		public TMP_Text resourceValue;

		/// <summary>
		/// The attribute template used to identify which resource this bar represents (e.g., health, stamina).
		/// </summary>
		public CharacterAttributeTemplate Template;

		/// <summary>
		/// Called before the character is set. Unsubscribes from attribute update events and updates the bar.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			if (Character != null &&
				Character.TryGet(out ICharacterAttributeController attributeController) &&
				attributeController.TryGetResourceAttribute(Template, out CharacterResourceAttribute attribute))
			{
				attribute.OnAttributeUpdated -= CharacterAttribute_OnAttributeUpdated;

				CharacterAttribute_OnAttributeUpdated(attribute);
			}
		}

		/// <summary>
		/// Called after the character is set. Subscribes to attribute update events and updates the bar.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character != null &&
				Character.TryGet(out ICharacterAttributeController attributeController) &&
				attributeController.TryGetResourceAttribute(Template, out CharacterResourceAttribute attribute))
			{
				attribute.OnAttributeUpdated += CharacterAttribute_OnAttributeUpdated;

				CharacterAttribute_OnAttributeUpdated(attribute);
			}
		}

		/// <summary>
		/// Called when the resource attribute is updated. Updates the slider and text to reflect the new value.
		/// </summary>
		/// <param name="attribute">The updated character attribute.</param>
		public void CharacterAttribute_OnAttributeUpdated(CharacterAttribute attribute)
		{
			if (Character != null &&
				Character.TryGet(out ICharacterAttributeController attributeController) &&
				attributeController.TryGetResourceAttribute(Template, out CharacterResourceAttribute resource))
			{
				float value = resource.CurrentValue / resource.FinalValueAsFloat;
				if (slider != null) slider.value = value;
				if (resourceValue != null) resourceValue.text = Mathf.RoundToInt(resource.CurrentValue) + "/" + resource.FinalValue;
			}
		}
	}
}