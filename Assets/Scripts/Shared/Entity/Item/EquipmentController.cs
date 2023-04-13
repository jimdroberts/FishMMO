using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class EquipmentController : ItemContainer
{
	public Character character;

	public override void OnStartClient()
	{
		base.OnStartClient();

		AddSlots(null, System.Enum.GetNames(typeof(ItemSlot)).Length); // equipment size = itemslot size

		if (character == null || !base.IsOwner)
		{
			enabled = false;
			return;
		}

		ClientManager.RegisterBroadcast<EquipmentEquipItemBroadcast>(OnClientEquipmentEquipItemBroadcastReceived);
		ClientManager.RegisterBroadcast<EquipmentUnequipItemBroadcast>(OnClientEquipmentUnequipItemBroadcastReceived);
	}

	public override void OnStopClient()
	{
		base.OnStopClient();

		if (base.IsOwner)
		{
			ClientManager.UnregisterBroadcast<EquipmentEquipItemBroadcast>(OnClientEquipmentEquipItemBroadcastReceived);
			ClientManager.UnregisterBroadcast<EquipmentUnequipItemBroadcast>(OnClientEquipmentUnequipItemBroadcastReceived);
		}
	}

	public override bool CanManipulate()
	{
		if (!base.CanManipulate() ||
			character == null)
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
		if (IsValidItem(index))
		{
			Debug.Log("EquipmentController: using item in slot[" + index + "]");
			//items[index].OnUseItem();
		}
	}

	public bool Equip(Item item, int inventoryIndex, ItemSlot slot)
	{
		if (item == null || !CanManipulate()) return false;

		EquippableItemTemplate equippable = item.Template as EquippableItemTemplate;
		// make sure the slot type matches so we aren't equipping things in weird places
		if (equippable == null || slot != equippable.Slot)
		{
			return false;
		}

		byte slotIndex = (byte)slot;
		Item prevItem = items[slotIndex];
		if (prevItem != null && prevItem.stackSize > 0)
		{
			prevItem.Unequip();

			// swap the items
			if (character.InventoryController != null)
			{
				character.InventoryController.SetItemSlot(prevItem, inventoryIndex);
			}
		}
		else
		{
			// remove the item from the inventory
			if (character.InventoryController != null)
			{
				character.InventoryController.RemoveItem(inventoryIndex);
			}
		}

		// put the new item in the correct slot
		if (!SetItemSlot(item, slotIndex))
		{
			return false;
		}

		// equip the item to the character (adds attributes.. etc..)
		item.Equip(character);
		return true;
	}

	/// <summary>
	/// Unequips the item and puts it in the inventory.
	/// </summary>
	public bool Unequip(byte slot)
	{
		if (!CanManipulate() || !IsValidItem(slot))
		{
			return false;
		}
		Item item = items[slot];

		if (character.InventoryController != null &&
			!character.InventoryController.CanAddItem(item))
		{
			return false;
		}

		// remove the equipped item
		SetItemSlot(null, slot);

		// unequip the item
		item.Unequip();

		// add the item back to the inventory
		if (character.InventoryController != null)
		{
			character.InventoryController.TryAddItem(item, out List<Item> modifiedItems);
		}

		return true;
	}

	/// <summary>
	/// Server sent a set item broadcast. Item slot is set to the received item details.
	/// </summary>
	private void OnClientEquipmentEquipItemBroadcastReceived(EquipmentEquipItemBroadcast msg)
	{
		InventoryController inventory = character.InventoryController;
		if (inventory != null)
		{
			if (inventory.IsValidItem(msg.inventoryIndex))
			{
				Equip(inventory.items[msg.inventoryIndex], msg.inventoryIndex, (ItemSlot)msg.slot);
			}
		}
	}

	/// <summary>
	/// Server sent a remove item from slot broadcast. Item is removed from the received slot with server authority.
	/// </summary>
	private void OnClientEquipmentUnequipItemBroadcastReceived(EquipmentUnequipItemBroadcast msg)
	{
		Unequip(msg.slot);
	}


	public void SendEquipRequest(int inventoryIndex, byte slot)
	{
		ClientManager.Broadcast(new EquipmentEquipItemBroadcast()
		{
			inventoryIndex = inventoryIndex,
			slot = slot,
		});
	}

	public void SendUnequipRequest(byte slot)
	{
		ClientManager.Broadcast(new EquipmentUnequipItemBroadcast()
		{
			slot = slot,
		});
	}
}