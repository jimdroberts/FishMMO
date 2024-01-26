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
				Character.TryGet(out CharacterAttributeController attributeController) &&
				attributeController.TryGetResourceAttribute(Template, out CharacterResourceAttribute attribute))
			{
				attribute.OnAttributeUpdated -= CharacterAttribute_OnAttributeUpdated;
			}
		}

		public override void SetCharacter(Character character)
		{
			base.SetCharacter(character);

			if (Character != null &&
				Character.TryGet(out CharacterAttributeController attributeController) &&
				attributeController.TryGetResourceAttribute(Template, out CharacterResourceAttribute attribute))
			{
				attribute.OnAttributeUpdated += CharacterAttribute_OnAttributeUpdated;
			}
		}

		public void CharacterAttribute_OnAttributeUpdated(CharacterAttribute attribute)
		{
			if (Character != null &&
				Character.TryGet(out CharacterAttributeController attributeController) && 
				attributeController.TryGetResourceAttribute(Template, out CharacterResourceAttribute resource))
			{
				float value = resource.CurrentValue / resource.FinalValue;
				if (slider != null) slider.value = value;
				if (resourceValue != null) resourceValue.text = resource.CurrentValue + "/" + resource.FinalValue;
			}
		}
	}
}