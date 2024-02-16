using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIInventoryButton : UIReferenceButton
	{
		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject))
			{
				if (Character != null)
				{
					if (dragObject.Visible)
					{
						if (Character.TryGet(out InventoryController inventoryController))
						{
							InventoryType inventoryType = dragObject.Type == ReferenceButtonType.Bank ? InventoryType.Bank :
														  dragObject.Type == ReferenceButtonType.Inventory ? InventoryType.Inventory :
														  InventoryType.Equipment;

							// we check the hotkey type because we can swap items in the inventory
							if (inventoryType != InventoryType.Equipment)
							{
								int from = (int)dragObject.ReferenceID;
								int to = (int)ReferenceID;

								if (inventoryController.CanSwapItemSlots(from, to, inventoryType))
								{
									// swap item slots in the inventory
									Client.Broadcast(new InventorySwapItemSlotsBroadcast()
									{
										from = from,
										to = to,
										fromInventory = inventoryType,
									}, Channel.Reliable);
								}
							}
							// we can also unequip items
							else if (dragObject.ReferenceID >= byte.MinValue && // Equipment slot index is a byte, validate here
									 dragObject.ReferenceID <= byte.MaxValue)
							{
								// unequip the item
								Client.Broadcast(new EquipmentUnequipItemBroadcast()
								{
									slot = (byte)dragObject.ReferenceID,
									toInventory = InventoryType.Inventory,
								}, Channel.Reliable);
							}
						}

						// clear the drag object
						dragObject.Clear();
					}
					else if (Character.TryGet(out InventoryController inventoryController) &&
							!inventoryController.IsSlotEmpty((int)ReferenceID))
					{
						dragObject.SetReference(Icon.sprite, ReferenceID, Type);
					}
				}
			}
		}

		public override void OnRightClick()
		{
			if (Character != null &&
				Type == ReferenceButtonType.Inventory &&
				Character.TryGet(out InventoryController inventoryController))
			{
				inventoryController.Activate((int)ReferenceID);
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