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
						// we check the hotkey type because we can only equip items from the inventory
						if (dragObject.Type == ReferenceButtonType.Inventory)
						{
							// get the item from the Inventory
							Item item = Character.InventoryController.Items[dragObject.ReferenceID];
							if (item != null)
							{
								Character.EquipmentController.SendEquipRequest(dragObject.ReferenceID, (byte)ItemSlotType, InventoryType.Inventory);
							}
						}
						// taking an item from the bank and putting it in this equipment slot
						else if (dragObject.Type == ReferenceButtonType.Bank)
						{
							// get the item from the Inventory
							Item item = Character.BankController.Items[dragObject.ReferenceID];
							if (item != null)
							{
								Character.EquipmentController.SendEquipRequest(dragObject.ReferenceID, (byte)ItemSlotType, InventoryType.Bank);
							}
						}

						// clear the drag object no matter what
						dragObject.Clear();
					}
					else if (!Character.EquipmentController.IsSlotEmpty((byte)ItemSlotType))
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
			if (Character != null && Type == ReferenceButtonType.Equipment)
			{
				Clear();

				// right clicking an item will attempt to send it to the inventory
				Character.EquipmentController.SendUnequipRequest((byte)ItemSlotType, InventoryType.Inventory);
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