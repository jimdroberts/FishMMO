using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for item containers, providing slot and item management for inventories, equipment, banks, etc.
	/// Implements IItemContainer and extends CharacterBehaviour for character association.
	/// </summary>
	public abstract class ItemContainer : CharacterBehaviour, IItemContainer
	{
		/// <summary>
		/// Internal list of items stored in this container.
		/// </summary>
		private readonly List<Item> items = new List<Item>();

		/// <summary>
		/// Event triggered when an item slot is updated (item added, removed, or changed).
		/// </summary>
		public event Action<IItemContainer, Item, int> OnSlotUpdated;

		/// <summary>
		/// Gets the list of items contained in this container.
		/// </summary>
		public List<Item> Items { get { return items; } }

		/// <summary>
		/// Called when the container is being destroyed. Clears event handlers.
		/// </summary>
		public override void OnDestroying()
		{
			OnSlotUpdated = null;
		}

		/// <summary>
		/// Determines if the container can be manipulated (e.g., items moved or swapped).
		/// Checks if the character is alive and the items list is not empty.
		/// </summary>
		/// <returns>True if manipulation is allowed, false otherwise.</returns>
		public virtual bool CanManipulate()
		{
			if (Character.TryGet(out ICharacterDamageController damageController))
			{
				if (!damageController.IsAlive)
				{
					return false;
				}
			}
			return Items.Count > 0;
		}

		/// <summary>
		/// Checks if the item slot exists (is within valid range).
		/// </summary>
		/// <param name="slot">The slot index to check.</param>
		/// <returns>True if the slot is valid, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsValidSlot(int slot)
		{
			return slot > -1 &&
				  slot < Items.Count;
		}

		/// <summary>
		/// Checks if the specified slot is empty (contains no item).
		/// </summary>
		/// <param name="slot">The slot index to check.</param>
		/// <returns>True if the slot is empty, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsSlotEmpty(int slot)
		{
			return IsValidSlot(slot) &&
				   Items[slot] == null;
		}

		/// <summary>
		/// Attempts to get the item in the specified slot. Returns false if the item doesn't exist.
		/// </summary>
		/// <param name="slot">The slot index to retrieve.</param>
		/// <param name="item">The item found in the slot, or null if not found.</param>
		/// <returns>True if an item was found, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryGetItem(int slot, out Item item)
		{
			if (IsValidSlot(slot))
			{
				item = Items[slot];
				return item != null;
			}
			item = null;
			return false;
		}

		/// <summary>
		/// Checks if the container contains an item with the specified template.
		/// </summary>
		/// <param name="itemTemplate">The item template to search for.</param>
		/// <returns>True if the item is found, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool ContainsItem(BaseItemTemplate itemTemplate)
		{
			for (int i = 0; i < Items.Count; ++i)
			{
				Item item = Items[i];
				if (item.Template.ID == itemTemplate.ID)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Gets the count of items matching the specified template, including stack sizes.
		/// </summary>
		/// <param name="itemTemplate">The item template to count.</param>
		/// <returns>The number of items matching the template.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int GetItemCount(BaseItemTemplate itemTemplate)
		{
			int count = 0;
			for (int i = 0; i < Items.Count; ++i)
			{
				Item item = Items[i];
				if (item != null && item.Template.ID == itemTemplate.ID)
				{
					if (item.IsStackable)
					{
						count += (int)item.Stackable.Amount;
					}
					else
					{
						count += 1;
					}
				}
			}
			return count;
		}

		/// <summary>
		/// Adds slots to the container, optionally initializing with a list of items.
		/// </summary>
		/// <param name="items">The initial items to add (can be null).</param>
		/// <param name="amount">The number of slots to add.</param>
		public void AddSlots(List<Item> items, int amount)
		{
			if (items != null)
			{
				for (int i = 0; i < items.Count; ++i)
				{
					this.Items.Add(items[i]);
				}
				return;
			}
			for (int i = 0; i < amount; ++i)
			{
				this.Items.Add(null);
			}
		}

		/// <summary>
		/// Clears all items from the container, destroying each item and setting slots to null.
		/// </summary>
		public void Clear()
		{
			for (int i = 0; i < items.Count; ++i)
			{
				Item item = items[i];
				if (item == null)
				{
					continue;
				}
				item.Destroy();
				items[i] = null;
			}
		}

		/// <summary>
		/// Checks if the container has at least one free slot.
		/// </summary>
		/// <returns>True if a free slot exists, false otherwise.</returns>
		public bool HasFreeSlot()
		{
			for (int i = 0; i < Items.Count; ++i)
			{
				if (IsSlotEmpty(i))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Gets the number of free slots in the container.
		/// </summary>
		/// <returns>The number of free slots.</returns>
		public int FreeSlots()
		{
			int count = 0;
			for (int i = 0; i < Items.Count; ++i)
			{
				if (IsSlotEmpty(i))
				{
					++count;
				}
			}
			return count;
		}

		/// <summary>
		/// Gets the number of filled slots in the container.
		/// </summary>
		/// <returns>The number of filled slots.</returns>
		public int FilledSlots()
		{
			int count = 0;
			for (int i = 0; i < Items.Count; ++i)
			{
				if (!IsSlotEmpty(i))
				{
					++count;
				}
			}
			return count;
		}

		/// <summary>
		/// Determines if the specified item can be added to the container, considering stack sizes and slot availability.
		/// </summary>
		/// <param name="item">The item to check.</param>
		/// <returns>True if the item can be added, false otherwise.</returns>
		public bool CanAddItem(Item item)
		{
			if (!CanManipulate())
			{
				return false;
			}

			// Cannot add an item with a stack size of 0; a 0 stack size means the item doesn't exist.
			if (item == null) return false;

			uint amountRemaining = item.IsStackable ? item.Stackable.Amount : 1;
			for (int i = 0; i < Items.Count; ++i)
			{
				// If we find an empty slot, we return instantly.
				if (IsSlotEmpty(i))
				{
					return true;
				}

				// If we find another item of the same type and its stack is not full.
				if (Items[i].IsStackable &&
					!Items[i].Stackable.IsStackFull &&
					Items[i].IsMatch(item))
				{
					uint remainingCapacity = Items[i].Template.MaxStackSize - Items[i].Stackable.Amount;

					amountRemaining = remainingCapacity.AbsoluteSubtract(amountRemaining);
				}

				if (amountRemaining < 1) return true;
			}
			return false;
		}

		/// <summary>
		/// Attempts to add an item to the container. Returns true if the entire stack size of the item has been successfully added.
		/// All modified items are returned.
		/// Handles stacking logic and slot assignment.
		/// </summary>
		/// <param name="item">The item to add.</param>
		/// <param name="modifiedItems">The list of items modified during the operation.</param>
		/// <returns>True if the item was successfully added, false otherwise.</returns>
		public bool TryAddItem(Item item, out List<Item> modifiedItems)
		{
			modifiedItems = new List<Item>();

			// Ensure we can add the entire item to the container.
			if (!CanAddItem(item))
			{
				return false;
			}

			if (item.IsStackable)
			{
				uint amount = item.Stackable.Amount;
				for (int i = 0; i < Items.Count; ++i)
				{
					// Search for items of the same type so we can stack it.
					if (Items[i] != null &&
						Items[i].IsStackable &&
						Items[i].Stackable.AddToStack(item))
					{
						// Set the remaining amount to the item's stack size.
						amount = item.Stackable.Amount;

						// Add the modified items to the list.
						modifiedItems.Add(Items[i]);
						modifiedItems.Add(item);

						OnSlotUpdated?.Invoke(this, item, i);
					}

					// We added the item to the container.
					if (amount < 1) return true;
				}
			}
			for (int i = 0; i < Items.Count; ++i)
			{
				// Find the first slot to put the remaining item in.
				if (IsSlotEmpty(i))
				{
					// Set the item slot to the item, presume it succeeded.
					SetItemSlot(item, i);

					// Add the modified item to the list.
					modifiedItems.Add(item);

					// Successfully added the entire item.
					return true;
				}
			}
			// We should never reach this...
			// Should probably throw an exception instead of just returning false.
			// If we get here then we have a race condition for some reason.
			return false;
		}

		/// <summary>
		/// Sets the item in the specified slot. Previous item will be lost if not referenced elsewhere.
		/// </summary>
		/// <param name="item">The item to set.</param>
		/// <param name="slot">The slot index to set the item in.</param>
		/// <returns>True if the item was successfully set, false otherwise.</returns>
		public bool SetItemSlot(Item item, int slot)
		{
			if (!IsValidSlot(slot))
			{
				// Setting the slot failed.
				return false;
			}

			Items[slot] = item;
			if (item != null)
			{
				item.Slot = slot;
			}
			OnSlotUpdated?.Invoke(this, item, slot);
			return true;
		}

		/// <summary>
		/// Swaps items between two slots.
		/// </summary>
		/// <param name="from">The source slot index.</param>
		/// <param name="to">The destination slot index.</param>
		/// <returns>True if the swap was successful, false otherwise.</returns>
		public bool SwapItemSlots(int from, int to)
		{
			return SwapItemSlots(from, to, out Item fromItem, out Item toItem);
		}

		/// <summary>
		/// Swaps items between two slots and returns the items that were swapped.
		/// </summary>
		/// <param name="from">The source slot index.</param>
		/// <param name="to">The destination slot index.</param>
		/// <param name="fromItem">The item originally in the source slot.</param>
		/// <param name="toItem">The item originally in the destination slot.</param>
		/// <returns>True if the swap was successful, false otherwise.</returns>
		public bool SwapItemSlots(int from, int to, out Item fromItem, out Item toItem)
		{
			if (!CanManipulate() ||
				from < 0 ||
				to < 0 ||
				from > Items.Count ||
				to > Items.Count)
			{
				fromItem = null;
				toItem = null;

				// Swapping the items failed.
				return false;
			}

			fromItem = Items[from];
			toItem = Items[to];

			Items[from] = toItem;
			if (toItem != null)
			{
				toItem.Slot = from;
			}

			Items[to] = fromItem;
			if (fromItem != null)
			{
				fromItem.Slot = to;
			}

			OnSlotUpdated?.Invoke(this, toItem, from);
			OnSlotUpdated?.Invoke(this, fromItem, to);
			return true;
		}

		/// <summary>
		/// Removes an item from the specified slot and returns it. Returns null if the slot was empty.
		/// </summary>
		/// <param name="slot">The slot index to remove the item from.</param>
		/// <returns>The item that was removed, or null if no item was present.</returns>
		public Item RemoveItem(int slot)
		{
			if (!CanManipulate() ||
				!IsValidSlot(slot))
			{
				return null;
			}

			Item item = Items[slot];
			item.Slot = -1;
			SetItemSlot(null, slot);
			return item;
		}
	}
}