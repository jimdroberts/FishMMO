using FishMMO.Shared;
using TMPro;

namespace FishMMO.Client
{
	public class UIAbilityButton : UIReferenceButton
	{
		public TMP_Text DescriptionLabel;

		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject))
			{
				if (Character != null)
				{
					if (dragObject.Visible)
					{
						dragObject.Clear();
					}
					else if (Character.AbilityController.KnownAbilities.ContainsKey(ReferenceID))
					{
						dragObject.SetReference(Icon.sprite, ReferenceID, Type);
					}
				}
			}
		}

		public override void ShowTooltip(Character character, long referenceID)
		{
			// do nothing
		}

		public override void Clear()
		{
			if (Icon != null) Icon.sprite = null;
			if (CooldownText != null) CooldownText.text = "";
			if (AmountText != null) AmountText.text = "";
			if (DescriptionLabel != null) DescriptionLabel.text = "";
		}
	}
}