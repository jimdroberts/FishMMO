using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI button representing an equipment slot, handling drag-and-drop and equip/unequip logic.
	/// </summary>
	public class UIEquipmentButton : UIReferenceButton
	{
		/// <summary>
		/// The type of equipment slot this button represents (e.g., Head, Chest).
		/// </summary>
		public ItemSlot ItemSlotType = ItemSlot.Head;

		/// <summary>
		/// Handles left mouse click events for equipment buttons.
		/// Supports equipping items from inventory or bank, and drag-and-drop logic.
		/// </summary>
		public override void OnLeftClick()
		{
			if (UIManager.TryGet("UIDragObject", out UIDragObject dragObject))
			{
				if (Character != null)
				{
					// If the drag object is visible, handle drag-and-drop logic
					if (dragObject.Visible)
					{
						if (Character.TryGet(out IEquipmentController equipmentController))
						{
							int referenceID = (int)dragObject.ReferenceID;

							// Only allow equipping items from inventory
							if (dragObject.Type == ReferenceButtonType.Inventory &&
								Character.TryGet(out IInventoryController inventoryController))
							{
								// Get the item from the inventory
								Item item = inventoryController.Items[referenceID];
								if (item != null)
								{
									// Broadcast equip item from inventory
									Client.Broadcast(new EquipmentEquipItemBroadcast()
									{
										InventoryIndex = referenceID,
										Slot = (byte)ItemSlotType,
										FromInventory = InventoryType.Inventory,
									}, Channel.Reliable);
								}
							}
							// Allow equipping items from bank
							else if (dragObject.Type == ReferenceButtonType.Bank &&
									 Character.TryGet(out IBankController bankController))
							{
								// Get the item from the bank
								Item item = bankController.Items[referenceID];
								if (item != null)
								{
									// Broadcast equip item from bank
									Client.Broadcast(new EquipmentEquipItemBroadcast()
									{
										InventoryIndex = referenceID,
										Slot = (byte)ItemSlotType,
										FromInventory = InventoryType.Bank,
									}, Channel.Reliable);
								}
							}
						}

						// Clear the drag object after operation
						dragObject.Clear();
					}
					// If drag object is not visible, start dragging if slot is not empty
					else if (Character.TryGet(out IEquipmentController equipmentController) &&
							 !equipmentController.IsSlotEmpty((byte)ItemSlotType))
					{
						dragObject.SetReference(Icon.sprite, ReferenceID, Type);
					}
				}
			}
		}

		/// <summary>
		/// Handles right mouse click events for equipment buttons.
		/// Unequips the item and sends it to the inventory.
		/// </summary>
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

				// Right clicking an item will attempt to send it to the inventory
				Client.Broadcast(new EquipmentUnequipItemBroadcast()
				{
					Slot = (byte)ItemSlotType,
					ToInventory = InventoryType.Inventory,
				}, Channel.Reliable);
			}
		}

		/// <summary>
		/// Clears the UI elements for this equipment button.
		/// </summary>
		public override void Clear()
		{
			if (Icon != null) Icon.sprite = null;
			if (CooldownText != null) CooldownText.text = "";
			if (AmountText != null) AmountText.text = "";
		}
	}
}