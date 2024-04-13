using TMPro;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UICastBar : UICharacterControl
	{
		public Slider slider;
		public TMP_Text castText;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnUpdate += OnUpdate;
				abilityController.OnCancel += OnCancel;
			}
		}

		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnUpdate -= OnUpdate;
				abilityController.OnCancel -= OnCancel;
			}
		}

		public void OnUpdate(string label, float remainingTime, float totalTime)
		{
			if (remainingTime <= 0.001f)
			{
				Hide();

				return;
			}

			if (!Visible)
			{
				Show();
			}

			if (castText != null) castText.text = label;

			if (slider != null)
			{
				slider.value = 1.0f - ((totalTime - remainingTime) / totalTime);

				if (slider.value <= 0.001f)
				{
					Hide();
				}
			}
		}

		public void OnCancel()
		{
			Hide();
		}
	}
}