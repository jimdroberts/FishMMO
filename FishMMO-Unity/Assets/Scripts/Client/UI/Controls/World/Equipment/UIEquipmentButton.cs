using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIEquipmentButton : UIReferenceButton
	{
		public ItemSlot ItemSlotType = ItemSlot.Head;

		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject))
			{
				if (Character != null)
				{
					if (dragObject.Visible)
					{
						int referenceID = (int)dragObject.ReferenceID;

						// we check the hotkey type because we can only equip items from the inventory
						if (dragObject.Type == ReferenceButtonType.Inventory &&
							Character.TryGet(out InventoryController inventoryController))
						{
							// get the item from the Inventory
							Item item = inventoryController.Items[referenceID];
							if (item != null &&
								Character.TryGet(out EquipmentController equipmentController))
							{
								equipmentController.SendEquipRequest(referenceID, (byte)ItemSlotType, InventoryType.Inventory);
							}
						}
						// taking an item from the bank and putting it in this equipment slot
						else if (dragObject.Type == ReferenceButtonType.Bank &&
								 Character.TryGet(out BankController bankController))
						{
							// get the item from the Inventory
							Item item = bankController.Items[referenceID];
							if (item != null &&
								Character.TryGet(out EquipmentController equipmentController))
							{
								equipmentController.SendEquipRequest(referenceID, (byte)ItemSlotType, InventoryType.Bank);
							}
						}

						// clear the drag object no matter what
						dragObject.Clear();
					}
					else if (Character.TryGet(out EquipmentController equipmentController) &&
							 !equipmentController.IsSlotEmpty((byte)ItemSlotType))
					{
						dragObject.SetReference(Icon.sprite, ReferenceID, Type);
					}
				}
			}
		}

		public override void OnRightClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject) && dragObject.Visible)
			{
				dragObject.Clear();
			}
			if (Character != null &&
				Type == ReferenceButtonType.Equipment &&
				Character.TryGet(out EquipmentController equipmentController))
			{
				Clear();

				// right clicking an item will attempt to send it to the inventory
				equipmentController.SendUnequipRequest((byte)ItemSlotType, InventoryType.Inventory);
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