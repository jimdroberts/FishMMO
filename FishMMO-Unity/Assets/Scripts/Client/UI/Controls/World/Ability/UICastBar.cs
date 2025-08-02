using TMPro;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UICastBar : UICharacterControl
	{
		/// <summary>
		/// The slider UI element representing the cast progress.
		/// </summary>
		public Slider slider;
		/// <summary>
		/// The text UI element displaying the cast label.
		/// </summary>
		public TMP_Text castText;

		/// <summary>
		/// Called after the character is set. Subscribes to ability controller events.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IAbilityController abilityController))
			{
				// Subscribe to ability update and cancel events.
				abilityController.OnUpdate += OnUpdate;
				abilityController.OnCancel += OnCancel;
			}
		}

		/// <summary>
		/// Called before the character is unset. Unsubscribes from ability controller events and hides the cast bar.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character.TryGet(out IAbilityController abilityController))
			{
				// Unsubscribe from ability update and cancel events.
				abilityController.OnUpdate -= OnUpdate;
				abilityController.OnCancel -= OnCancel;
			}

			Hide();
		}

		/// <summary>
		/// Event handler for ability update. Updates the cast bar UI based on remaining and total time.
		/// </summary>
		/// <param name="label">The label for the cast bar.</param>
		/// <param name="remainingTime">The remaining time for the cast.</param>
		/// <param name="totalTime">The total time for the cast.</param>
		public void OnUpdate(string label, float remainingTime, float totalTime)
		{
			// If the cast is finished, hide the cast bar.
			if (remainingTime <= 0.001f)
			{
				Hide();
				return;
			}

			// Show the cast bar if it is not already visible.
			if (!Visible)
			{
				Show();
			}

			// Update the cast label text.
			if (castText != null) castText.text = label;

			// Update the slider value to reflect cast progress.
			if (slider != null)
			{
				slider.value = 1.0f - ((totalTime - remainingTime) / totalTime);

				// If the slider value is near zero, hide the cast bar.
				if (slider.value <= 0.001f)
				{
					Hide();
				}
			}
		}

		/// <summary>
		/// Event handler for ability cancel. Hides the cast bar.
		/// </summary>
		public void OnCancel()
		{
			Hide();
		}
	}
}