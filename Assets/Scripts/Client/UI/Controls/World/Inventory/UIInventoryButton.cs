namespace Client
{
	public class UIInventoryButton : UIReferenceButton
	{
		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject))
			{
				Character character = Character.localCharacter;
				if (character != null)
				{
					if (dragObject.visible)
					{
						if (int.TryParse(dragObject.referenceID, out int dragIndex))
						{
							// we check the hotkey type because we can swap items in the inventory
							if (dragObject.hotkeyType == HotkeyType.Inventory)
							{
								// swap item slots in the inventory
								character.InventoryController.SendSwapItemSlotsRequest(index, dragIndex);
							}
							// we can also unequip items
							else if (dragObject.hotkeyType == HotkeyType.Equipment)
							{
								// unequip the item
								character.EquipmentController.Unequip(dragIndex);
							}
						}
						// clear the drag object no matter what
						dragObject.Clear();
					}
					else
					{
						if (!character.InventoryController.IsSlotEmpty(index))
						{
							dragObject.SetReference(icon.texture, referenceID, hotkeyType);
						}
					}
				}
			}
		}

		public override void OnRightClick()
		{
			Character character = Character.localCharacter;
			if (character != null && hotkeyType == HotkeyType.Inventory)
			{
				character.InventoryController.Activate(index);
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