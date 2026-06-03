using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit cast bar bound to the character's ability controller. Mirrors the legacy
	/// UGUI <see cref="UICastBar"/>: displays casting progress and the cast label, and hides
	/// itself when the cast completes or is cancelled. The fill width is driven as a percentage
	/// in place of the legacy UGUI Slider value.
	/// </summary>
	public class UITKCastBar : UITKCharacterControl
	{
		/// <summary>Name of the progress fill element inside the cast-bar UXML.</summary>
		private const string FILL_NAME = "castbar-fill";

		/// <summary>Name of the cast label element inside the cast-bar UXML.</summary>
		private const string LABEL_NAME = "castbar-label";

		/// <summary>The progress fill element whose width represents cast progress.</summary>
		private VisualElement fill;

		/// <summary>The label element displaying the current cast name.</summary>
		private Label castLabel;

		/// <summary>
		/// Queries the fill and label elements from the document root.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			fill = root.Q(FILL_NAME);
			castLabel = root.Q<Label>(LABEL_NAME);
		}

		/// <summary>
		/// Subscribes to ability controller cast update and cancel events for the new character.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnUpdate += AbilityController_OnUpdate;
				abilityController.OnCancel += AbilityController_OnCancel;
			}
		}

		/// <summary>
		/// Unsubscribes from ability controller events and hides the cast bar before the character changes.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character != null &&
				Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnUpdate -= AbilityController_OnUpdate;
				abilityController.OnCancel -= AbilityController_OnCancel;
			}

			Hide();
		}

		/// <summary>
		/// Updates the cast bar fill and label based on the remaining and total cast time.
		/// </summary>
		/// <param name="label">The cast label to display.</param>
		/// <param name="remainingTime">The remaining cast time.</param>
		/// <param name="totalTime">The total cast time.</param>
		public void AbilityController_OnUpdate(string label, float remainingTime, float totalTime)
		{
			// If the cast is finished, hide the cast bar.
			if (remainingTime <= 0.001f || totalTime <= 0.0f)
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
			if (castLabel != null)
			{
				castLabel.text = label;
			}

			// Mirror the legacy slider value: remainingTime / totalTime.
			float fraction = 1.0f - ((totalTime - remainingTime) / totalTime);

			// If the fill is near zero, hide the cast bar.
			if (fraction <= 0.001f)
			{
				Hide();
				return;
			}

			if (fill != null)
			{
				fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100.0f);
			}
		}

		/// <summary>
		/// Handles ability cancel by hiding the cast bar.
		/// </summary>
		public void AbilityController_OnCancel()
		{
			Hide();
		}
	}
}
