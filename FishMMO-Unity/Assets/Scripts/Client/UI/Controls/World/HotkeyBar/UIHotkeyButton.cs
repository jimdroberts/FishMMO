using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIHotkeyButton : UIReferenceButton
	{
		public string KeyMap = "";

		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject) &&
				dragObject.Visible)
			{
				if (dragObject.Type != ReferenceButtonType.Bank)
				{
					Type = dragObject.Type;
					ReferenceID = dragObject.ReferenceID;
					if (Icon != null)
					{
						Icon.sprite = dragObject.Icon.sprite;
					}
				}

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
				dragObject.SetReference(Icon.sprite, ReferenceID, Type);
				Clear();
			}
		}

		public void Activate()
		{
			if (Character != null && !string.IsNullOrWhiteSpace(KeyMap))
			{
				switch (Type)
				{
					case ReferenceButtonType.None:
						break;
					case ReferenceButtonType.Inventory:
						if (Character.TryGet(out InventoryController inventoryController))
						{
							inventoryController.Activate((int)ReferenceID);
						}
						break;
					case ReferenceButtonType.Equipment:
						if (Character.TryGet(out EquipmentController equipmentController))
						{
							equipmentController.Activate((int)ReferenceID);
						}
						break;
					case ReferenceButtonType.Bank:
						break;
					case ReferenceButtonType.Ability:
						if (Character.TryGet(out AbilityController abilityController))
						{
							abilityController.Activate(ReferenceID, InputManager.GetKeyCode(KeyMap));
						}
						break;
					default:
						return;
				};
			}
		}
	}
}