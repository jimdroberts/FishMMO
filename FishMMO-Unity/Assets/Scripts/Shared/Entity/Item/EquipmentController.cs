using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(Character))]
	public class EquipmentController : ItemContainer
	{
		public Character Character;

		private void Awake()
		{
			AddSlots(null, System.Enum.GetNames(typeof(ItemSlot)).Length); // equipment size = itemslot size
		}

#if !UNITY_SERVER
		public override void OnStartClient()
		{
			base.OnStartClient();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<EquipmentSetItemBroadcast>(OnClientEquipmentSetItemBroadcastReceived);
			ClientManager.RegisterBroadcast<EquipmentSetMultipleItemsBroadcast>(OnClientEquipmentSetMultipleItemsBroadcastReceived);
			ClientManager.RegisterBroadcast<EquipmentEquipItemBroadcast>(OnClientEquipmentEquipItemBroadcastReceived);
			ClientManager.RegisterBroadcast<EquipmentUnequipItemBroadcast>(OnClientEquipmentUnequipItemBroadcastReceived);
		}

		public override void OnStopClient()
		{
			base.OnStopClient();

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
		private void OnClientEquipmentSetItemBroadcastReceived(EquipmentSetItemBroadcast msg)
		{
			Item newItem = new Item(msg.instanceID, msg.seed, msg.templateID, msg.stackSize);
			Equip(newItem, -1, null, (ItemSlot)msg.slot);
		}

		/// <summary>
		/// Server sent a multiple set item broadcast. Item slot is set to the received item details.
		/// </summary>
		private void OnClientEquipmentSetMultipleItemsBroadcastReceived(EquipmentSetMultipleItemsBroadcast msg)
		{
			foreach (EquipmentSetItemBroadcast subMsg in msg.items)
			{
				Item newItem = new Item(subMsg.instanceID, subMsg.seed, subMsg.templateID, subMsg.stackSize);
				Equip(newItem, -1, null, (ItemSlot)subMsg.slot);
			}
		}

		/// <summary>
		/// Server sent an equip item broadcast.
		/// </summary>
		private void OnClientEquipmentEquipItemBroadcastReceived(EquipmentEquipItemBroadcast msg)
		{
			switch (msg.fromInventory)
			{
				case InventoryType.Inventory:
					if (Character.InventoryController.TryGetItem(msg.inventoryIndex, out Item inventoryItem))
					{
						Equip(inventoryItem, msg.inventoryIndex, Character.InventoryController, (ItemSlot)msg.slot);
					}
					break;
				case InventoryType.Equipment:
					break;
				case InventoryType.Bank:
					if (Character.BankController.TryGetItem(msg.inventoryIndex, out Item bankItem))
					{
						Equip(bankItem, msg.inventoryIndex, Character.BankController, (ItemSlot)msg.slot);
					}
					break;
				default: return;
			}
		}

		/// <summary>
		/// Server sent an unequip item broadcast.
		/// </summary>
		private void OnClientEquipmentUnequipItemBroadcastReceived(EquipmentUnequipItemBroadcast msg)
		{
			switch (msg.toInventory)
			{
				case InventoryType.Inventory:
					if (Character.InventoryController != null)
					{
						Unequip(Character.InventoryController, msg.slot);
					}
					break;
				case InventoryType.Equipment:
					break;
				case InventoryType.Bank:
					if (Character.BankController != null)
					{
						Unequip(Character.BankController, msg.slot);
					}
					break;
				default: return;
			}
		}
#endif

		public void SendEquipRequest(int inventoryIndex, byte slot, InventoryType fromInventory)
		{
			ClientManager.Broadcast(new EquipmentEquipItemBroadcast()
			{
				inventoryIndex = inventoryIndex,
				slot = slot,
				fromInventory = fromInventory,
			}, Channel.Reliable);
		}

		public void SendUnequipRequest(byte slot, InventoryType toInventory)
		{
			ClientManager.Broadcast(new EquipmentUnequipItemBroadcast()
			{
				slot = slot,
				toInventory = toInventory,
			}, Channel.Reliable);
		}

		public override bool CanManipulate()
		{
			if (!base.CanManipulate())
			{
				return false;
			}

			/*if ((character.State == CharacterState.Idle ||
				  character.State == CharacterState.Moving) &&
				  character.State != CharacterState.UsingObject &&
				  character.State != CharacterState.IsFrozen &&
				  character.State != CharacterState.IsStunned &&
				  character.State != CharacterState.IsMesmerized) return true;
			*/
			return true;
		}

		public void Activate(int index)
		{
			if (TryGetItem(index, out Item item))
			{
				Debug.Log("EquipmentController: using item in slot[" + index + "]");
				//items[index].OnUseItem();
			}
		}

		public bool Equip(Item item, int inventoryIndex, ItemContainer container, ItemSlot toSlot)
		{
			if (item == null ||
				!item.IsEquippable ||
				!CanManipulate())
			{
				return false;
			}

			EquippableItemTemplate Equippable = item.Template as EquippableItemTemplate;
			// make sure the slot type matches so we aren't equipping things in weird places
			if (Equippable == null || toSlot != Equippable.Slot)
			{
				return false;
			}

			byte slotIndex = (byte)toSlot;

			if (container != null)
			{
				Item prevItem = Items[slotIndex];
				if (prevItem != null &&
					prevItem.Equippable != null)
				{
					prevItem.Equippable.Unequip();

					// swap the items
					if (!container.SetItemSlot(prevItem, inventoryIndex))
					{
						return false;
					}
				}
				else
				{
					// remove the item from the inventory
					container.RemoveItem(inventoryIndex);
				}
			}

			// put the new item in the correct slot
			if (!SetItemSlot(item, slotIndex))
			{
				return false;
			}

			// equip the item to the character (adds attributes.. etc..)
			if (item.Equippable != null)
			{
				item.Equippable.Equip(Character);
			}
			return true;
		}

		/// <summary>
		/// Unequips the item and puts it in the inventory.
		/// </summary>
		public bool Unequip(ItemContainer container, byte slot)
		{
			if (!CanManipulate() ||
				!TryGetItem(slot, out Item item) ||
				container == null ||
				!container.CanAddItem(item))
			{
				return false;
			}

			// try to add the item back to the inventory before we remove it from the slot
			if (!container.TryAddItem(item, out List<Item> modifiedItems))
			{
				return false;
			}

			// remove the equipped item
			SetItemSlot(null, slot);

			// unequip the item
			if (item.Equippable != null)
			{
				item.Equippable.Unequip();
			}
			return true;
		}
	}
}