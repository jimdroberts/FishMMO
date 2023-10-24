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

		public int Index = -1;
		public int ReferenceID = NULL_REFERENCE_ID;
		public HotkeyType AllowedHotkeyType = HotkeyType.Any;
		public HotkeyType HotkeyType = HotkeyType.None;
		[SerializeField]
		public RawImage Icon;
		[SerializeField]
		public TMP_Text CooldownText;
		[SerializeField]
		public TMP_Text AmountText;

		public virtual void OnLeftClick() { }
		public virtual void OnRightClick() { }

		public override void OnPointerEnter(PointerEventData eventData)
		{
			base.OnPointerEnter(eventData);

			Character character = Character.localCharacter;
			if (character != null && ReferenceID > -1)
			{
				UITooltip tooltip;
				switch (HotkeyType)
				{
					case HotkeyType.None:
						break;
					case HotkeyType.Any:
						break;
					case HotkeyType.Inventory:
						if (character.InventoryController.TryGetItem(ReferenceID, out Item inventoryItem) && UIManager.TryGet("UITooltip", out tooltip))
						{
							tooltip.SetText(inventoryItem.Tooltip(), true);
						}
						break;
					case HotkeyType.Equipment:
						if (character.EquipmentController.TryGetItem(ReferenceID, out Item equippedItem) && UIManager.TryGet("UITooltip", out tooltip))
						{
							tooltip.SetText(equippedItem.Tooltip(), true);
						}
						break;
					case HotkeyType.Ability:
						if (character.AbilityController.KnownAbilities.TryGetValue(ReferenceID, out Ability ability) && UIManager.TryGet("UITooltip", out tooltip))
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
			ReferenceID = NULL_REFERENCE_ID;
			HotkeyType = HotkeyType.None;
			if (Icon != null) Icon.texture = null;
			if (CooldownText != null) CooldownText.text = "";
			if (AmountText != null) AmountText.text = "";
		}
	}
}