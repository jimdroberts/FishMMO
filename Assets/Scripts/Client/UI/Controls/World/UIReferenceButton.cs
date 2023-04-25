using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Client
{
	/// <summary>
	/// Advanced button class used for the inventory, equipment, and hotkey UI.
	/// ReferenceID is the inventory slot, equipment slot, or ability name.
	/// </summary>
	public class UIReferenceButton : Button
	{
		public int index = -1;
		public string referenceID = "";
		public HotkeyType allowedHotkeyType = HotkeyType.Any;
		public HotkeyType hotkeyType = HotkeyType.None;
		[SerializeField]
		public RawImage icon;
		[SerializeField]
		public TMP_Text cooldownText;
		[SerializeField]
		public TMP_Text amountText;

		public virtual void OnLeftClick() { }
		public virtual void OnRightClick() { }

		public override void OnPointerEnter(PointerEventData eventData)
		{
			base.OnPointerEnter(eventData);

			Character character = Character.localCharacter;
			if (character != null && !string.IsNullOrWhiteSpace(referenceID))
			{
				switch (hotkeyType)
				{
					case HotkeyType.None:
						break;
					case HotkeyType.Any:
						break;
					case HotkeyType.Inventory:
						if (int.TryParse(referenceID, out int inventoryIndex))
						{
							if (character.InventoryController.IsValidItem(inventoryIndex) && UIManager.TryGet("UITooltip", out UITooltip tooltip))
							{
								tooltip.SetText(character.InventoryController.items[inventoryIndex].Tooltip(), true);
							}
						}
						break;
					case HotkeyType.Equipment:
						if (int.TryParse(referenceID, out int equipmentIndex))
						{
							if (character.EquipmentController.IsValidItem(equipmentIndex) && UIManager.TryGet("UITooltip", out UITooltip tooltip))
							{
								tooltip.SetText(character.EquipmentController.items[equipmentIndex].Tooltip(), true);
							}
						}
						break;
					case HotkeyType.Ability:
						if (!string.IsNullOrWhiteSpace(referenceID))
						{
							/*if (character.AbilityController.abilities.TryGetValue(referenceID, out Ability ability) &&
								UIManager.TryGet("UITooltip", out UITooltip tooltip))
							{
								tooltip.SetText(ability.Tooltip(), true);
							}*/
						}

						break;
					default:
						return;
				};
			}
		}

		public override void OnPointerExit(PointerEventData eventData)
		{
			base.OnPointerExit(eventData);

			if (UIManager.TryGet("UITooltip", out UITooltip tooltip))
			{
				tooltip.OnHide();
			}
		}

		public override void OnPointerClick(PointerEventData eventData)
		{
			base.OnPointerClick(eventData);

			if (eventData.button == PointerEventData.InputButton.Left)
			{
				OnLeftClick();
			}
			else if (eventData.button == PointerEventData.InputButton.Right)
			{
				OnRightClick();
			}
		}

		public virtual void Clear()
		{
			referenceID = "";
			hotkeyType = HotkeyType.None;
			if (icon != null) icon.texture = null;
			if (cooldownText != null) cooldownText.text = "";
			if (amountText != null) amountText.text = "";
		}
	}
}