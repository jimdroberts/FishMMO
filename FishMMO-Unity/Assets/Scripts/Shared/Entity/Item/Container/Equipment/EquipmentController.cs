using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls the character's equipment slots, handling equip/unequip logic and network synchronization.
	/// Manages client-server broadcasts for equipment changes and slot management.
	/// </summary>
	public class EquipmentController : ItemContainer, IEquipmentController
	{
		/// <summary>
		/// Called when the equipment controller is initialized. Adds slots for each equipment type.
		/// </summary>
		public override void OnAwake()
		{
			AddSlots(null, System.Enum.GetNames(typeof(ItemSlot)).Length); // equipment size = itemslot size
		}

		/// <summary>
		/// Resets the state of the equipment controller, clearing all equipped items and calling base reset logic.
		/// </summary>
		/// <param name="asServer">True if called on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			Clear();
		}

		/// <summary>
		/// Reads equipment data from the network payload and sets item slots accordingly.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="reader">The network reader.</param>
		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			int itemCount = reader.ReadInt32();
			for (int i = 0; i < itemCount; ++i)
			{
				long id = reader.ReadInt64();
				int templateID = reader.ReadInt32();
				int slot = reader.ReadInt32();
				int seed = reader.ReadInt32();
				uint stackSize = reader.ReadUInt32();

				Item item = new Item(id, seed, templateID, stackSize);

				SetItemSlot(item, slot);
				if (item.IsEquippable)
				{
					item.Equippable.Equip(Character);
				}
			}
		}

		/// <summary>
		/// Writes equipment data to the network payload for synchronization.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="writer">The network writer.</param>
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			if (Items == null ||
				Items.Count < 1)
			{
				writer.WriteUInt32(0);
				return;
			}

			writer.WriteInt32(FilledSlots());
			foreach (Item item in Items)
			{
				if (item == null)
				{
					continue;
				}
				writer.WriteInt64(item.ID);
				writer.WriteInt32(item.Template.ID);
				writer.WriteInt32(item.Slot);
				writer.WriteInt32(item.IsGenerated ? item.Generator.Seed : 0);
				writer.WriteUInt32(item.IsStackable ? item.Stackable.Amount : 0);
			}
		}

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character starts. Sets owners for equippable items and registers client broadcast handlers for equipment operations if the local player owns this equipment.
		/// Disables the controller for non-owners.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			// Register client broadcast handlers for equipment item operations.
			ClientManager.RegisterBroadcast<EquipmentSetItemBroadcast>(OnClientEquipmentSetItemBroadcastReceived);
			ClientManager.RegisterBroadcast<EquipmentSetMultipleItemsBroadcast>(OnClientEquipmentSetMultipleItemsBroadcastReceived);
			ClientManager.RegisterBroadcast<EquipmentEquipItemBroadcast>(OnClientEquipmentEquipItemBroadcastReceived);
			ClientManager.RegisterBroadcast<EquipmentUnequipItemBroadcast>(OnClientEquipmentUnequipItemBroadcastReceived);
		}

		/// <summary>
		/// Called when the character stops. Unregisters client broadcast handlers for equipment operations if the local player owns this equipment.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<EquipmentSetItemBroadcast>(OnClientEquipmentSetItemBroadcastReceived);
				ClientManager.UnregisterBroadcast<EquipmentSetMultipleItemsBroadcast>(OnClientEquipmentSetMultipleItemsBroadcastReceived);
				ClientManager.UnregisterBroadcast<EquipmentEquipItemBroadcast>(OnClientEquipmentEquipItemBroadcastReceived);
				ClientManager.UnregisterBroadcast<EquipmentUnequipItemBroadcast>(OnClientEquipmentUnequipItemBroadcastReceived);
			}
		}

		/// <summary>
		/// Server sent a set item broadcast. Item slot is set to the received item details.
		/// </summary>
		/// <summary>
		/// Handles a broadcast from the server to set a single equipment item.
		/// Equips the received item in the specified slot.
		/// </summary>
		/// <param name="msg">The broadcast message containing item data.</param>
		/// <param name="channel">The network channel used for the broadcast.</param>
		private void OnClientEquipmentSetItemBroadcastReceived(EquipmentSetItemBroadcast msg, Channel channel)
		{
			Item newItem = new Item(msg.InstanceID, msg.Seed, msg.TemplateID, msg.StackSize);
			Equip(newItem, -1, null, (ItemSlot)msg.Slot);
		}

		/// <summary>
		/// Server sent a multiple set item broadcast. Item slot is set to the received item details.
		/// </summary>
		/// <summary>
		/// Handles a broadcast from the server to set multiple equipment items.
		/// Equips each received item in the specified slot.
		/// </summary>
		/// <param name="msg">The broadcast message containing multiple items.</param>
		/// <param name="channel">The network channel used for the broadcast.</param>
		private void OnClientEquipmentSetMultipleItemsBroadcastReceived(EquipmentSetMultipleItemsBroadcast msg, Channel channel)
		{
			foreach (EquipmentSetItemBroadcast subMsg in msg.Items)
			{
				Item newItem = new Item(subMsg.InstanceID, subMsg.Seed, subMsg.TemplateID, subMsg.StackSize);
				Equip(newItem, -1, null, (ItemSlot)subMsg.Slot);
			}
		}

		/// <summary>
		/// Server sent an equip item broadcast.
		/// </summary>
		/// <summary>
		/// Handles a broadcast from the server to equip an item from another inventory.
		/// Performs the equip operation based on the source inventory type.
		/// </summary>
		/// <param name="msg">The broadcast message containing equip details.</param>
		/// <param name="channel">The network channel used for the broadcast.</param>
		private void OnClientEquipmentEquipItemBroadcastReceived(EquipmentEquipItemBroadcast msg, Channel channel)
		{
			switch (msg.FromInventory)
			{
				case InventoryType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController) &&
						inventoryController.TryGetItem(msg.InventoryIndex, out Item inventoryItem))
					{
						Equip(inventoryItem, msg.InventoryIndex, inventoryController, (ItemSlot)msg.Slot);
					}
					break;
				case InventoryType.Equipment:
					// Equipment swaps are not handled here.
					break;
				case InventoryType.Bank:
					if (Character.TryGet(out IBankController bankController) &&
						bankController.TryGetItem(msg.InventoryIndex, out Item bankItem))
					{
						Equip(bankItem, msg.InventoryIndex, bankController, (ItemSlot)msg.Slot);
					}
					break;
				default: return;
			}
		}

		/// <summary>
		/// Server sent an unequip item broadcast.
		/// </summary>
		/// <summary>
		/// Handles a broadcast from the server to unequip an item to another inventory.
		/// Performs the unequip operation based on the destination inventory type.
		/// </summary>
		/// <param name="msg">The broadcast message containing unequip details.</param>
		/// <param name="channel">The network channel used for the broadcast.</param>
		private void OnClientEquipmentUnequipItemBroadcastReceived(EquipmentUnequipItemBroadcast msg, Channel channel)
		{
			switch (msg.ToInventory)
			{
				case InventoryType.Inventory:
					if (Character.TryGet(out IInventoryController inventoryController))
					{
						Unequip(inventoryController, msg.Slot, out List<Item> modifiedItems);
					}
					break;
				case InventoryType.Equipment:
					// Equipment swaps are not handled here.
					break;
				case InventoryType.Bank:
					if (Character.TryGet(out IBankController bankController))
					{
						Unequip(bankController, msg.Slot, out List<Item> modifiedItems);
					}
					break;
				default: return;
			}
		}
#endif

		/// <summary>
		/// Determines if the equipment can be manipulated (e.g., items moved or swapped).
		/// Always returns true unless base logic restricts manipulation.
		/// </summary>
		/// <returns>True if manipulation is allowed, false otherwise.</returns>
		public override bool CanManipulate()
		{
			if (!base.CanManipulate())
			{
				return false;
			}

			// Additional character state checks could be added here if needed.
			return true;
		}

		/// <summary>
		/// Activates the item in the specified equipment slot, typically triggering its use effect.
		/// Only activates if the character is alive and the item exists in the slot.
		/// </summary>
		/// <param name="index">The equipment slot index to activate.</param>
		public void Activate(int index)
		{
			if (!Character.TryGet(out ICharacterDamageController damageController) ||
				!damageController.IsAlive)
			{
				// Cannot activate equipment while dead.
				return;
			}
			if (TryGetItem(index, out Item item))
			{
				Log.Debug("EquipmentController", $"Using item in slot[" + index + "]");
				//items[index].OnUseItem();
			}
		}

		/// <summary>
		/// Equips the specified item into the given equipment slot, handling swaps and unequips as needed.
		/// Ensures slot compatibility and updates both equipment and source container.
		/// </summary>
		/// <param name="item">The item to equip.</param>
		/// <param name="inventoryIndex">The index in the source inventory.</param>
		/// <param name="container">The source item container (e.g., inventory or bank).</param>
		/// <param name="toSlot">The equipment slot to equip the item into.</param>
		/// <returns>True if the item was successfully equipped, false otherwise.</returns>
		public bool Equip(Item item, int inventoryIndex, IItemContainer container, ItemSlot toSlot)
		{
			if (item == null ||
				!item.IsEquippable ||
				!CanManipulate())
			{
				return false;
			}

			EquippableItemTemplate Equippable = item.Template as EquippableItemTemplate;
			// Make sure the slot type matches so we aren't equipping things in incorrect places.
			if (Equippable == null || toSlot != Equippable.Slot)
			{
				return false;
			}

			byte slotIndex = (byte)toSlot;

			if (container != null)
			{
				if (TryGetItem(slotIndex, out Item previousItem) &&
					previousItem.IsEquippable)
				{
					previousItem.Equippable.Unequip();

					// Swap the items.
					if (!container.SetItemSlot(previousItem, inventoryIndex))
					{
						return false;
					}
				}
				else
				{
					// Remove the item from the inventory.
					container.RemoveItem(inventoryIndex);
				}
			}

			// Put the new item in the correct slot.
			if (!SetItemSlot(item, slotIndex))
			{
				return false;
			}

			// Equip the item to the character (adds attributes, etc.).
			if (item.IsEquippable)
			{
				item.Equippable.Equip(Character);
			}
			return true;
		}

		/// <summary>
		/// Unequips the item from the specified slot and adds it to the given container (e.g., inventory or bank).
		/// Ensures the item can be added before removing it from the equipment slot.
		/// </summary>
		/// <param name="container">The destination item container.</param>
		/// <param name="slot">The equipment slot to unequip from.</param>
		/// <param name="modifiedItems">The list of items modified during the operation.</param>
		/// <returns>True if the item was successfully unequipped and added, false otherwise.</returns>
		public bool Unequip(IItemContainer container, byte slot, out List<Item> modifiedItems)
		{
			if (!CanManipulate() ||
				!TryGetItem(slot, out Item item) ||
				container == null ||
				!container.CanAddItem(item))
			{
				modifiedItems = null;
				return false;
			}

			// Try to add the item back to the inventory before removing it from the slot.
			if (!container.TryAddItem(item, out modifiedItems))
			{
				return false;
			}

			// Unequip the item.
			if (item.IsEquippable)
			{
				item.Equippable.Unequip();
			}

			// Remove the equipped item.
			SetItemSlot(null, slot);

			return true;
		}
	}
}