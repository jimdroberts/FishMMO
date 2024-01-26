using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIInventoryButton : UIReferenceButton
	{
		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject))
			{
				if (Character != null)
				{
					if (dragObject.Visible)
					{
						if (Character.TryGet(out InventoryController inventoryController))
						{
							// we check the hotkey type because we can swap items in the inventory
							if (dragObject.Type == ReferenceButtonType.Inventory)
							{
								// swap item slots in the inventory
								inventoryController.SendSwapItemSlotsRequest((int)dragObject.ReferenceID, (int)ReferenceID, InventoryType.Inventory);
							}
							// taking an item from the bank and putting it in this inventory slot
							else if (dragObject.Type == ReferenceButtonType.Bank)
							{
								inventoryController.SendSwapItemSlotsRequest((int)dragObject.ReferenceID, (int)ReferenceID, InventoryType.Bank);
							}
						}
						// we can also unequip items
						else if (dragObject.Type == ReferenceButtonType.Equipment &&
								 dragObject.ReferenceID >= byte.MinValue && // Equipment slot index is a byte, validate here
								 dragObject.ReferenceID <= byte.MaxValue &&
								 Character.TryGet(out EquipmentController equipmentController))
						{
							// unequip the item
							equipmentController.SendUnequipRequest((byte)dragObject.ReferenceID, InventoryType.Inventory);
						}

						// clear the drag object
						dragObject.Clear();
					}
					else
					{
						if (Character.TryGet(out InventoryController inventoryController) &&
							!inventoryController.IsSlotEmpty((int)ReferenceID))
						{
							dragObject.SetReference(Icon.sprite, ReferenceID, Type);
						}
					}
				}
			}
		}

		public override void OnRightClick()
		{
			if (Character != null &&
				Type == ReferenceButtonType.Inventory &&
				Character.TryGet(out InventoryController inventoryController))
			{
				inventoryController.Activate((int)ReferenceID);
			}
		}

		public override void Clear()
		{
			if (Icon != null) Icon.sprite = null;
			if (CooldownText != null) CooldownText.text = "";
			if (AmountText != null) AmountText.text = "";
		}
	}
}