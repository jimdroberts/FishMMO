using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Advanced button class used for the inventory, equipment, and hotkey UI.
	/// ReferenceID is the inventory slot, equipment slot, or ability name.
	/// </summary>
	public class UIReferenceButton : Button
	{
		public const int NULL_REFERENCE_ID = -1;

		public int index = -1;
		public int referenceID = NULL_REFERENCE_ID;
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
			if (character != null && referenceID > -1)
			{
				UITooltip tooltip;
				switch (hotkeyType)
				{
					case HotkeyType.None:
						break;
					case HotkeyType.Any:
						break;
					case HotkeyType.Inventory:
						if (character.InventoryController.TryGetItem(referenceID, out Item inventoryItem) && UIManager.TryGet("UITooltip", out tooltip))
						{
							tooltip.SetText(inventoryItem.Tooltip(), true);
						}
						break;
					case HotkeyType.Equipment:
						if (character.EquipmentController.TryGetItem(referenceID, out Item equippedItem) && UIManager.TryGet("UITooltip", out tooltip))
						{
							tooltip.SetText(equippedItem.Tooltip(), true);
						}
						break;
					case HotkeyType.Ability:
						if (character.AbilityController.knownAbilities.TryGetValue(referenceID, out Ability ability) && UIManager.TryGet("UITooltip", out tooltip))
						{
							tooltip.SetText(ability.Tooltip(), true);
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
			referenceID = NULL_REFERENCE_ID;
			hotkeyType = HotkeyType.None;
			if (icon != null) icon.texture = null;
			if (cooldownText != null) cooldownText.text = "";
			if (amountText != null) amountText.text = "";
		}
	}
}