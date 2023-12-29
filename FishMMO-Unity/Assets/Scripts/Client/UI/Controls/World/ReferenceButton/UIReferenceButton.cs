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

		private UITooltip currentUITooltip;

		/// <summary>
		/// ReferenceID is equal to the inventory slot, equipment slot, or ability id based on Reference Type.
		/// </summary>
		public int ReferenceID = NULL_REFERENCE_ID;
		public ReferenceButtonType AllowedType = ReferenceButtonType.Any;
		public ReferenceButtonType Type = ReferenceButtonType.None;
		[SerializeField]
		public Image Icon;
		[SerializeField]
		public TMP_Text CooldownText;
		[SerializeField]
		public TMP_Text AmountText;

		public Character Character;

		protected override void OnDisable()
		{
			base.OnDisable();

			ClearTooltip();
		}

		public virtual void OnLeftClick() { }
		public virtual void OnRightClick() { }

		public override void OnPointerEnter(PointerEventData eventData)
		{
			base.OnPointerEnter(eventData);

			if (Character != null && ReferenceID > -1)
			{
				switch (Type)
				{
					case ReferenceButtonType.None:
						break;
					case ReferenceButtonType.Any:
						break;
					case ReferenceButtonType.Inventory:
						if (Character.InventoryController != null &&
							Character.InventoryController.TryGetItem(ReferenceID, out Item inventoryItem) &&
							UIManager.TryGet("UITooltip", out currentUITooltip))
						{
							currentUITooltip.Open(inventoryItem.Tooltip());
						}
						break;
					case ReferenceButtonType.Equipment:
						if (Character.EquipmentController != null &&
							Character.EquipmentController.TryGetItem(ReferenceID, out Item equippedItem) &&
							UIManager.TryGet("UITooltip", out currentUITooltip))
						{
							currentUITooltip.Open(equippedItem.Tooltip());
						}
						break;
					case ReferenceButtonType.Ability:
						if (Character.AbilityController.KnownAbilities != null &&
							Character.AbilityController.KnownAbilities.TryGetValue(ReferenceID, out Ability ability) &&
							UIManager.TryGet("UITooltip", out currentUITooltip))
						{
							currentUITooltip.Open(ability.Tooltip());
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

			ClearTooltip();
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

		private void ClearTooltip()
		{
			if (currentUITooltip != null)
			{
				currentUITooltip.Hide();
				currentUITooltip = null;
			}
		}

		public virtual void Clear()
		{
			ReferenceID = NULL_REFERENCE_ID;
			Type = ReferenceButtonType.None;
			if (Icon != null) Icon.sprite = null;
			if (CooldownText != null) CooldownText.text = "";
			if (AmountText != null) AmountText.text = "";
			ClearTooltip();
		}
	}
}