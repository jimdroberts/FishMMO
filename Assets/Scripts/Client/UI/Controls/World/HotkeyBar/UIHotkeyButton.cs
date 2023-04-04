namespace Client
{
	public class UIHotkeyButton : UIReferenceButton
	{
		public string keyMap = "";

		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject) && dragObject.visible)
			{
				hotkeyType = dragObject.hotkeyType;
				referenceID = dragObject.referenceID;
				icon.texture = dragObject.icon.texture;

				// clear the drag object no matter what
				dragObject.Clear();
			}
			else
			{
				Activate();
			}
		}

		public override void OnRightClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject) && !string.IsNullOrEmpty(referenceID))
			{
				dragObject.SetReference(icon.texture, referenceID, hotkeyType);
				Clear();
			}
		}

		public void Activate()
		{
			Character character = Character.localCharacter;
			if (character != null && !string.IsNullOrEmpty(keyMap))
			{
				switch (hotkeyType)
				{
					case HotkeyType.None:
						break;
					case HotkeyType.Any:
						break;
					case HotkeyType.Inventory:
						if (int.TryParse(referenceID, out int inventoryIndex))
						{
							character.InventoryController.Activate(inventoryIndex);
						}
						break;
					case HotkeyType.Equipment:
						if (int.TryParse(referenceID, out int equipmentIndex))
						{
							character.EquipmentController.Activate(equipmentIndex);
						}
						break;
					case HotkeyType.Ability:
						character.AbilityController.Activate(referenceID, InputManager.GetKeyCode(keyMap));
						break;
					default:
						return;
				};
			}
		}
	}
}