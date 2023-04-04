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

		int slotIndex = (int)slot;
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
	public bool Unequip(int slot)
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
}