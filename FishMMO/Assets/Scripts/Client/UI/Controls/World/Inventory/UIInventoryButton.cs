namespace FishMMO.Client
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
					if (dragObject.Visible)
					{
						// we check the hotkey type because we can swap items in the inventory
						if (dragObject.HotkeyType == HotkeyType.Inventory)
						{
								// swap item slots in the inventory
								character.InventoryController.SendSwapItemSlotsRequest(Index, dragObject.ReferenceID);
						}
						// we can also unequip items
						else if (dragObject.HotkeyType == HotkeyType.Equipment &&
								 dragObject.ReferenceID >= byte.MinValue && // Equipment slot index is a byte, validate here
								 dragObject.ReferenceID <= byte.MaxValue)
						{
							// unequip the item
							character.EquipmentController.SendUnequipRequest((byte)dragObject.ReferenceID);
						}
						else
						{
							// clear the drag object no matter what
							dragObject.Clear();
						}
					}
					else
					{
						if (!character.InventoryController.IsSlotEmpty(Index))
						{
							dragObject.SetReference(Icon.texture, ReferenceID, HotkeyType);
						}
					}
				}
			}
		}

		public override void OnRightClick()
		{
			Character character = Character.localCharacter;
			if (character != null && HotkeyType == HotkeyType.Inventory)
			{
				character.InventoryController.Activate(Index);
			}
		}

		public override void Clear()
		{
			if (Icon != null) Icon.texture = null;
			if (CooldownText != null) CooldownText.text = "";
			if (AmountText != null) AmountText.text = "";
		}
	}
}