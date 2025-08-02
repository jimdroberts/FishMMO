using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIBankButton : UIReferenceButton
	{
		/// <summary>
		/// Handles left-click interactions for the bank button, including swapping items and unequipping equipment.
		/// </summary>
		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject))
			{
				if (Character != null)
				{
					// If the drag object is visible, handle item swap or unequip logic.
					if (dragObject.Visible)
					{
						if (Character.TryGet(out IBankController bankController))
						{
							// Determine the inventory type based on the drag object's reference type.
							InventoryType inventoryType = dragObject.Type == ReferenceButtonType.Bank ? InventoryType.Bank :
														  dragObject.Type == ReferenceButtonType.Inventory ? InventoryType.Inventory :
														  InventoryType.Equipment;

							// Check the inventory type; only allow swapping for non-equipment types.
							if (inventoryType != InventoryType.Equipment)
							{
								// Get the source and destination slot indices for the swap.
								int from = (int)dragObject.ReferenceID;
								int to = (int)ReferenceID;

								if (bankController.CanSwapItemSlots(from, to, inventoryType))
								{
									// Swap item slots in the bank by broadcasting the swap event to the server.
									Client.Broadcast(new BankSwapItemSlotsBroadcast()
									{
										From = from,
										To = to,
										FromInventory = inventoryType,
									}, Channel.Reliable);
								}
							}
							// If the drag object is an equipment slot, unequip the item to the bank.
							else if (dragObject.ReferenceID >= byte.MinValue && // Equipment slot index is a byte, validate here
									 dragObject.ReferenceID <= byte.MaxValue)
							{
								// Unequip the item and broadcast the event to the server.
								Client.Broadcast(new EquipmentUnequipItemBroadcast()
								{
									Slot = (byte)dragObject.ReferenceID,
									ToInventory = InventoryType.Bank,
								}, Channel.Reliable);
							}
						}

						// Clear the drag object after the operation is complete.
						dragObject.Clear();
					}
					// If the drag object is not visible and the bank slot is not empty, set the drag reference.
					else if (Character.TryGet(out IBankController bankController) &&
							 !bankController.IsSlotEmpty((int)ReferenceID))
					{
						dragObject.SetReference(Icon.sprite, ReferenceID, Type);
					}
				}
			}
		}

		/// <summary>
		/// Clears the bank button UI, resetting icon and text fields.
		/// </summary>
		public override void Clear()
		{
			if (Icon != null) Icon.sprite = null;
			if (CooldownText != null) CooldownText.text = "";
			if (AmountText != null) AmountText.text = "";
		}
	}
}