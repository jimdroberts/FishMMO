using TMPro;
using UnityEngine.UI;

namespace Client
{
	public abstract class UIResourceBar : UIControl
	{
		public Slider slider;
		public TMP_Text resourceValue;

		public CharacterAttributeTemplate template;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		void Update()
		{
			Character character = Character.localCharacter;
			if (character == null) return;

			if (character.AttributeController.TryGetResourceAttribute(template, out CharacterResourceAttribute resource))
			{
				float value = resource.CurrentValue / resource.FinalValueAsFloat;
				if (slider != null) slider.value = value;
				if (resourceValue != null) resourceValue.text = resource.CurrentValue + "/" + resource.FinalValueAsFloat;
			}
		}
	}
}