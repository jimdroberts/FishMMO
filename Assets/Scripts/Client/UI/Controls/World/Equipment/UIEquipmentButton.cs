namespace Client
{
	public class UIEquipmentButton : UIReferenceButton
	{
		public ItemSlot itemSlotType = ItemSlot.Head;

		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject))
			{
				Character character = Character.localCharacter;
				if (character != null)
				{
					if (dragObject.visible)
					{
						// we check the hotkey type because we can only equip items from the inventory
						if (dragObject.hotkeyType == HotkeyType.Inventory && int.TryParse(dragObject.referenceID, out int dragIndex))
						{
							// get the item from the Inventory
							Item item = character.InventoryController.items[dragIndex];
							if (item != null)
							{
								if (character.EquipmentController.Equip(item, dragIndex, itemSlotType))
								{
									// clear the drag object if we succeed in equipping our item
									dragObject.Clear();
								}
							}
							else
							{
								// clear the drag object if our item is null
								dragObject.Clear();
							}
						}
					}
					else if (!character.EquipmentController.IsSlotEmpty((int)itemSlotType))
					{
						dragObject.SetReference(icon.texture, referenceID, hotkeyType);
					}
				}
			}
		}

		public override void OnRightClick()
		{
			Character character = Character.localCharacter;
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject) && dragObject.visible)
			{
				dragObject.Clear();
			}
			if (character != null && hotkeyType == HotkeyType.Equipment)
			{
				if (icon != null) icon.texture = null;
				//inventorySlots[i].cooldownText = character.CooldownController.IsOnCooldown();
				if (amountText != null) amountText.text = "";

				character.EquipmentController.Unequip((int)itemSlotType);
			}
		}

		public override void Clear()
		{
			if (icon != null) icon.texture = null;
			if (cooldownText != null) cooldownText.text = "";
			if (amountText != null) amountText.text = "";
		}
	}
}