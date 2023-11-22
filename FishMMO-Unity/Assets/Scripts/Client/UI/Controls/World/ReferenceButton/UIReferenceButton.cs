using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Advanced button class used by the inventory, equipment, and hotkey buttons.
	/// </summary>
	public class UIReferenceButton : Button
	{
		public const int NULL_REFERENCE_ID = -1;

		/// <summary>
		/// ReferenceID is equal to the inventory slot, equipment slot, or ability id based on Reference Type.
		/// </summary>
		public int ReferenceID = NULL_REFERENCE_ID;
		public ReferenceButtonType AllowedType = ReferenceButtonType.Any;
		public ReferenceButtonType Type = ReferenceButtonType.None;
		[SerializeField]
		public RawImage Icon;
		[SerializeField]
		public TMP_Text CooldownText;
		[SerializeField]
		public TMP_Text AmountText;

		public Character Character;

		public virtual void OnLeftClick() { }
		public virtual void OnRightClick() { }

		public override void OnPointerEnter(PointerEventData eventData)
		{
			base.OnPointerEnter(eventData);

			if (Character != null && ReferenceID > -1)
			{
				UITooltip tooltip;
				switch (Type)
				{
					case ReferenceButtonType.None:
						break;
					case ReferenceButtonType.Any:
						break;
					case ReferenceButtonType.Inventory:
						if (Character.InventoryController != null &&
							Character.InventoryController.TryGetItem(ReferenceID, out Item inventoryItem) &&
							UIManager.TryGet("UITooltip", out tooltip))
						{
							tooltip.Open(inventoryItem.Tooltip());
						}
						break;
					case ReferenceButtonType.Equipment:
						if (Character.EquipmentController != null &&
							Character.EquipmentController.TryGetItem(ReferenceID, out Item equippedItem) &&
							UIManager.TryGet("UITooltip", out tooltip))
						{
							tooltip.Open(equippedItem.Tooltip());
						}
						break;
					case ReferenceButtonType.Ability:
						if (Character.AbilityController.KnownAbilities != null &&
							Character.AbilityController.KnownAbilities.TryGetValue(ReferenceID, out Ability ability) &&
							UIManager.TryGet("UITooltip", out tooltip))
						{
							tooltip.Open(ability.Tooltip());
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
				tooltip.Hide();
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
			Type = ReferenceButtonType.None;
			if (Icon != null) Icon.texture = null;
			if (CooldownText != null) CooldownText.text = "";
			if (AmountText != null) AmountText.text = "";
		}
	}
}