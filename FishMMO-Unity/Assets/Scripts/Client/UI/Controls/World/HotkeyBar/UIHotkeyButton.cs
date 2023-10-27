using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIHotkeyButton : UIReferenceButton
	{
		public string KeyMap = "";

		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject) && dragObject.Visible)
			{
				HotkeyType = dragObject.HotkeyType;
				ReferenceID = dragObject.ReferenceID;
				Icon.texture = dragObject.Icon.texture;

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
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject) && ReferenceID != NULL_REFERENCE_ID)
			{
				dragObject.SetReference(Icon.texture, ReferenceID, HotkeyType);
				Clear();
			}
		}

		public void Activate()
		{
			Character character = Character.localCharacter;
			if (character != null && !string.IsNullOrWhiteSpace(KeyMap))
			{
				switch (HotkeyType)
				{
					case HotkeyType.None:
						break;
					case HotkeyType.Any:
						break;
					case HotkeyType.Inventory:
						character.InventoryController.Activate(ReferenceID);
						break;
					case HotkeyType.Equipment:
						character.EquipmentController.Activate(ReferenceID);
						break;
					case HotkeyType.Ability:
						character.AbilityController.Activate(ReferenceID, InputManager.GetKeyCode(KeyMap));
						break;
					default:
						return;
				};
			}
		}
	}
}