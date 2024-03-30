using FishNet.Transporting;
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
						if (Character.TryGet(out IEquipmentController equipmentController))
						{
							int referenceID = (int)dragObject.ReferenceID;

							// we check the hotkey type because we can only equip items from the inventory
							if (dragObject.Type == ReferenceButtonType.Inventory &&
								Character.TryGet(out IInventoryController inventoryController))
							{
								// get the item from the Inventory
								Item item = inventoryController.Items[referenceID];
								if (item != null)
								{
									Client.Broadcast(new EquipmentEquipItemBroadcast()
									{
										inventoryIndex = referenceID,
										slot = (byte)ItemSlotType,
										fromInventory = InventoryType.Inventory,
									}, Channel.Reliable);
								}
							}
							// taking an item from the bank and putting it in this equipment slot
							else if (dragObject.Type == ReferenceButtonType.Bank &&
									 Character.TryGet(out IBankController bankController))
							{
								// get the item from the Inventory
								Item item = bankController.Items[referenceID];
								if (item != null)
								{
									Client.Broadcast(new EquipmentEquipItemBroadcast()
									{
										inventoryIndex = referenceID,
										slot = (byte)ItemSlotType,
										fromInventory = InventoryType.Bank,
									}, Channel.Reliable);
								}
							}
						}

						// clear the drag object no matter what
						dragObject.Clear();
					}
					else if (Character.TryGet(out IEquipmentController equipmentController) &&
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
				Character.TryGet(out IEquipmentController equipmentController))
			{
				Clear();

				// right clicking an item will attempt to send it to the inventory
				Client.Broadcast(new EquipmentUnequipItemBroadcast()
				{
					slot = (byte)ItemSlotType,
					toInventory = InventoryType.Inventory,
				}, Channel.Reliable);
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