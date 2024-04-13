using TMPro;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public abstract class UIResourceBar : UICharacterControl
	{
		public Slider slider;
		public TMP_Text resourceValue;

		public CharacterAttributeTemplate Template;

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

		public void CharacterAttribute_OnAttributeUpdated(CharacterAttribute attribute)
		{
			if (Character != null &&
				Character.TryGet(out ICharacterAttributeController attributeController) && 
				attributeController.TryGetResourceAttribute(Template, out CharacterResourceAttribute resource))
			{
				float value = (float)resource.CurrentValue / resource.FinalValueAsFloat;
				if (slider != null) slider.value = value;
				if (resourceValue != null) resourceValue.text = resource.CurrentValue + "/" + resource.FinalValue;
			}
		}
	}
}