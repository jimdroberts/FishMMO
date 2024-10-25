using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIBankButton : UIReferenceButton
	{
		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject))
			{
				if (Character != null)
				{
					if (dragObject.Visible)
					{
						if (Character.TryGet(out IBankController bankController))
						{
							InventoryType inventoryType = dragObject.Type == ReferenceButtonType.Bank ? InventoryType.Bank :
														  dragObject.Type == ReferenceButtonType.Inventory ? InventoryType.Inventory :
														  InventoryType.Equipment;

							// we check the hotkey type because we can swap items in the bank
							if (inventoryType != InventoryType.Equipment)
							{
								int from = (int)dragObject.ReferenceID;
								int to = (int)ReferenceID;

								if (bankController.CanSwapItemSlots(from, to, inventoryType))
								{
									// swap item slots in the bank
									Client.Broadcast(new BankSwapItemSlotsBroadcast()
									{
										From = from,
										To = to,
										FromInventory = inventoryType,
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
									Slot = (byte)dragObject.ReferenceID,
									ToInventory = InventoryType.Bank,
								}, Channel.Reliable);
							}
						}

						// clear the drag object
						dragObject.Clear();
					}
					else if (Character.TryGet(out IBankController bankController) &&
							!bankController.IsSlotEmpty((int)ReferenceID))
					{
						dragObject.SetReference(Icon.sprite, ReferenceID, Type);
					}
				}
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