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
		/// <summary>
		/// Constant representing a null reference ID for buttons.
		/// </summary>
		public const long NULL_REFERENCE_ID = -1;

		/// <summary>
		/// Reference to the currently displayed tooltip instance.
		/// </summary>
		protected UITooltip currentUITooltip;

		/// <summary>
		/// ReferenceID is equal to the inventory slot, equipment slot, or ability id based on Reference Type.
		/// </summary>
		public long ReferenceID = NULL_REFERENCE_ID;
		/// <summary>
		/// The type of reference button (inventory, equipment, bank, ability, etc.).
		/// </summary>
		public ReferenceButtonType Type = ReferenceButtonType.None;
		/// <summary>
		/// The icon image displayed on the button.
		/// </summary>
		[SerializeField]
		public Image Icon;
		/// <summary>
		/// The default icon sprite to use when no item is present.
		/// </summary>
		[SerializeField]
		public Sprite DefaultIconSprite;
		/// <summary>
		/// The text displaying cooldown information.
		/// </summary>
		[SerializeField]
		public TMP_Text CooldownText;
		/// <summary>
		/// The text displaying item amount or stack size.
		/// </summary>
		[SerializeField]
		public TMP_Text AmountText;

		/// <summary>
		/// The player character associated with this button.
		/// </summary>
		public IPlayerCharacter Character;

		/// <summary>
		/// Called when the button is disabled. Hides any active tooltip.
		/// </summary>
		protected override void OnDisable()
		{
			base.OnDisable();

			ClearTooltip();
		}

		/// <summary>
		/// Called when the left mouse button is clicked. Override for custom logic.
		/// </summary>
		public virtual void OnLeftClick() { }
		/// <summary>
		/// Called when the right mouse button is clicked. Override for custom logic.
		/// </summary>
		public virtual void OnRightClick() { }

		/// <summary>
		/// Handles pointer enter event to show tooltip for the referenced item or ability.
		/// </summary>
		/// <param name="eventData">Pointer event data.</param>
		public override void OnPointerEnter(PointerEventData eventData)
		{
			base.OnPointerEnter(eventData);

			ShowTooltip(ReferenceID);
		}

		/// <summary>
		/// Shows the tooltip for the referenced item, equipment, bank item, or ability based on button type.
		/// </summary>
		/// <param name="referenceID">Reference ID to look up tooltip data.</param>
		public virtual void ShowTooltip(long referenceID)
		{
			if (Character == null ||
				referenceID < 0)
			{
				return;
			}
			// Show tooltip based on button type
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
			}
		}

		/// <summary>
		/// Handles pointer exit event to hide tooltip.
		/// </summary>
		/// <param name="eventData">Pointer event data.</param>
		public override void OnPointerExit(PointerEventData eventData)
		{
			base.OnPointerExit(eventData);

			ClearTooltip();
		}

		/// <summary>
		/// Handles pointer click event, invoking left or right click logic.
		/// </summary>
		/// <param name="eventData">Pointer event data.</param>
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

		/// <summary>
		/// Hides the currently displayed tooltip, if any.
		/// </summary>
		private void ClearTooltip()
		{
			if (currentUITooltip != null)
			{
				currentUITooltip.Hide();
				currentUITooltip = null;
			}
		}

		/// <summary>
		/// Clears the button state, resets icon and text, and hides tooltip.
		/// </summary>
		public virtual void Clear()
		{
			ReferenceID = NULL_REFERENCE_ID;
			Type = ReferenceButtonType.None;
			if (Icon != null) Icon.sprite = DefaultIconSprite;
			if (CooldownText != null) CooldownText.text = "";
			if (AmountText != null) AmountText.text = "";
			ClearTooltip();
		}
	}
}