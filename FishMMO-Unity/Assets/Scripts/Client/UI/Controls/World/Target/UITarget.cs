using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UITarget : UIControl
	{
		public TMP_Text NameLabel;
		public Slider HealthSlider;
		public CharacterAttributeTemplate HealthAttribute;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public void OnChangeTarget(GameObject obj)
		{
			if (obj == null)
			{
				// hide the UI
				Hide();
				return;
			}

			if (NameLabel != null)
			{
				NameLabel.text = obj.name;
			}
			CharacterAttributeController characterAttributeController = obj.GetComponent<CharacterAttributeController>();
			if (characterAttributeController != null)
			{
				if (characterAttributeController.TryGetResourceAttribute(HealthAttribute, out CharacterResourceAttribute health))
				{
					HealthSlider.value = health.CurrentValue / health.FinalValue;
				}
			}
			else
			{
				HealthSlider.value = 0;
			}

			// make the UI visible
			Show();
		}

		public void OnUpdateTarget(GameObject obj)
		{
			if (obj == null)
			{
				// hide the UI
				Hide();
				return;
			}

			// update the health slider
			CharacterAttributeController characterAttributeController = obj.GetComponent<CharacterAttributeController>();
			if (characterAttributeController != null)
			{
				if (characterAttributeController.TryGetResourceAttribute(HealthAttribute, out CharacterResourceAttribute health))
				{
					HealthSlider.value = health.CurrentValue / health.FinalValue;
				}
			}
		}
	}
}