namespace FishMMO.Client
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
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject) && referenceID != NULL_REFERENCE_ID)
			{
				dragObject.SetReference(icon.texture, referenceID, hotkeyType);
				Clear();
			}
		}

		public void Activate()
		{
			Character character = Character.localCharacter;
			if (character != null && !string.IsNullOrWhiteSpace(keyMap))
			{
				switch (hotkeyType)
				{
					case HotkeyType.None:
						break;
					case HotkeyType.Any:
						break;
					case HotkeyType.Inventory:
						character.InventoryController.Activate(referenceID);
						break;
					case HotkeyType.Equipment:
						character.EquipmentController.Activate(referenceID);
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