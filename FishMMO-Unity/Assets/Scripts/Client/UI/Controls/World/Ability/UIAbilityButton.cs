using FishMMO.Shared;
using TMPro;

namespace FishMMO.Client
{
	public class UIAbilityButton : UIReferenceButton
	{
		/// <summary>
		/// The label displaying the ability description.
		/// </summary>
		public TMP_Text DescriptionLabel;

		/// <summary>
		/// Handles left-click interactions for the ability button, including drag-and-drop logic.
		/// </summary>
		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject))
			{
				if (Character != null)
				{
					// If the drag object is visible, clear it. Otherwise, set the drag reference if the ability is known.
					if (dragObject.Visible)
					{
						dragObject.Clear();
					}
					else if (Character.TryGet(out IAbilityController abilityController) &&
							 abilityController.KnownAbilities.ContainsKey(ReferenceID))
					{
						dragObject.SetReference(Icon.sprite, ReferenceID, Type);
					}
				}
			}
		}

		/// <summary>
		/// Shows the tooltip for the ability button. (No implementation)
		/// </summary>
		/// <param name="referenceID">The reference ID for the tooltip.</param>
		public override void ShowTooltip(long referenceID)
		{
			// No tooltip implementation for ability button.
		}

		/// <summary>
		/// Clears the ability button UI, resetting icon, text, and description fields.
		/// </summary>
		public override void Clear()
		{
			if (Icon != null) Icon.sprite = null;
			if (CooldownText != null) CooldownText.text = "";
			if (AmountText != null) AmountText.text = "";
			if (DescriptionLabel != null) DescriptionLabel.text = "";
		}
	}
}