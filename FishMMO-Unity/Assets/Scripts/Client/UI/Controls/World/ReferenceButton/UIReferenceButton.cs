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
		public const long NULL_REFERENCE_ID = -1;

		protected UITooltip currentUITooltip;

		/// <summary>
		/// ReferenceID is equal to the inventory slot, equipment slot, or ability id based on Reference Type.
		/// </summary>
		public long ReferenceID = NULL_REFERENCE_ID;
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

			ShowTooltip(Character, ReferenceID);
		}

		public virtual void ShowTooltip(Character character, long referenceID)
		{
			if (character == null ||
				referenceID < 0)
			{
				return;
			}
			switch (Type)
			{
				case ReferenceButtonType.None:
					break;
				case ReferenceButtonType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController) &&
						inventoryController.TryGetItem((int)referenceID, out Item inventoryItem) &&
						UIManager.TryGet("UITooltip", out currentUITooltip))
					{
						currentUITooltip.Open(inventoryItem.Tooltip());
					}
					break;
				case ReferenceButtonType.Equipment:
					if (Character.TryGet(out IEquipmentController equipmentController) &&
						equipmentController.TryGetItem((int)referenceID, out Item equippedItem) &&
						UIManager.TryGet("UITooltip", out currentUITooltip))
					{
						currentUITooltip.Open(equippedItem.Tooltip());
					}
					break;
				case ReferenceButtonType.Bank:
					if (Character.TryGet(out IBankController bankController) &&
						bankController.TryGetItem((int)referenceID, out Item bankItem) &&
						UIManager.TryGet("UITooltip", out currentUITooltip))
					{
						currentUITooltip.Open(bankItem.Tooltip());
					}
					break;
				case ReferenceButtonType.Ability:
					if (Character.TryGet(out IAbilityController abilityController) &&
						abilityController.KnownAbilities.TryGetValue(referenceID, out Ability ability) &&
						UIManager.TryGet("UITooltip", out currentUITooltip))
					{
						currentUITooltip.Open(ability.Tooltip());
					}
					break;
				default:
					return;
			};
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