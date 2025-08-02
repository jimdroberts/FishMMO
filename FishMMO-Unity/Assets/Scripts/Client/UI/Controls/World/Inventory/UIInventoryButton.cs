using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI button representing an inventory slot, handling drag-and-drop and item activation logic.
	/// </summary>
	public class UIInventoryButton : UIReferenceButton
	{
		/// <summary>
		/// Handles left mouse click events for inventory buttons.
		/// Supports swapping items, unequipping equipment, and drag-and-drop logic.
		/// </summary>
		public override void OnLeftClick()
		{
			// Attempt to get the drag object UI component
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject))
			{
				// Ensure the character reference is valid
				if (Character != null)
				{
					// If the drag object is visible, handle drag-and-drop logic
					if (dragObject.Visible)
					{
						// Try to get the inventory controller from the character
						if (Character.TryGet(out IInventoryController inventoryController))
						{
							// Determine the inventory type based on the drag object's button type
							InventoryType inventoryType = dragObject.Type == ReferenceButtonType.Bank ? InventoryType.Bank :
														  dragObject.Type == ReferenceButtonType.Inventory ? InventoryType.Inventory :
														  InventoryType.Equipment;

							// Only allow swapping items for non-equipment inventory types
							if (inventoryType != InventoryType.Equipment)
							{
								// Source and destination slot indices for swap
								int from = (int)dragObject.ReferenceID;
								int to = (int)ReferenceID;

								// Check if the swap is allowed by the controller
								if (inventoryController.CanSwapItemSlots(from, to, inventoryType))
								{
									// Broadcast a message to swap item slots in the inventory
									Client.Broadcast(new InventorySwapItemSlotsBroadcast()
									{
										From = from,
										To = to,
										FromInventory = inventoryType,
									}, Channel.Reliable);
								}
							}
							// If the drag object is an equipment slot, unequip the item
							else if (dragObject.ReferenceID >= byte.MinValue && // Equipment slot index is a byte, validate here
									 dragObject.ReferenceID <= byte.MaxValue)
							{
								// Broadcast a message to unequip the item
								Client.Broadcast(new EquipmentUnequipItemBroadcast()
								{
									Slot = (byte)dragObject.ReferenceID,
									ToInventory = InventoryType.Inventory,
								}, Channel.Reliable);
							}
						}

						// Clear the drag object after operation
						dragObject.Clear();
					}
					// If drag object is not visible, start dragging if slot is not empty
					else if (Character.TryGet(out IInventoryController inventoryController) &&
							!inventoryController.IsSlotEmpty((int)ReferenceID))
					{
						dragObject.SetReference(Icon.sprite, ReferenceID, Type);
					}
				}
			}
		}

		/// <summary>
		/// Handles right mouse click events for inventory buttons.
		/// Activates the item in the inventory slot.
		/// </summary>
		public override void OnRightClick()
		{
			if (Character != null &&
				Type == ReferenceButtonType.Inventory &&
				Character.TryGet(out IInventoryController inventoryController))
			{
				inventoryController.Activate((int)ReferenceID);
			}
		}

		/// <summary>
		/// Clears the UI elements for this inventory button.
		/// </summary>
		public override void Clear()
		{
			if (Icon != null) Icon.sprite = null;
			if (CooldownText != null) CooldownText.text = "";
			if (AmountText != null) AmountText.text = "";
		}
	}
}