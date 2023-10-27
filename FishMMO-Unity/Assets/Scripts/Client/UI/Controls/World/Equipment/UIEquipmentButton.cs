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
				Character character = Character.localCharacter;
				if (character != null)
				{
					if (dragObject.Visible)
					{
						// we check the hotkey type because we can only equip items from the inventory
						if (dragObject.HotkeyType == HotkeyType.Inventory)
						{
							// get the item from the Inventory
							Item item = character.InventoryController.Items[ReferenceID];
							if (item != null)
							{
								character.EquipmentController.SendEquipRequest(ReferenceID, (byte)ItemSlotType);
								// clear the drag object if we succeed in equipping our item
								dragObject.Clear();
							}
							else
							{
								// clear the drag object if our item is null
								dragObject.Clear();
							}
						}
					}
					else if (!character.EquipmentController.IsSlotEmpty((byte)ItemSlotType))
					{
						dragObject.SetReference(Icon.texture, ReferenceID, HotkeyType);
					}
				}
			}
		}

		public override void OnRightClick()
		{
			Character character = Character.localCharacter;
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject) && dragObject.Visible)
			{
				dragObject.Clear();
			}
			if (character != null && HotkeyType == HotkeyType.Equipment)
			{
				Clear();

				character.EquipmentController.SendUnequipRequest((byte)ItemSlotType);
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