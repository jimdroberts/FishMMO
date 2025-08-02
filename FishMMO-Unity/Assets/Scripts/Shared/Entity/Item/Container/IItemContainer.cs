using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for item containers, providing methods and events for managing items and slots.
	/// Used for inventories, equipment, banks, and other item storage systems.
	/// </summary>
	public interface IItemContainer
	{
		/// <summary>
		/// Event triggered when an item slot is updated (item added, removed, or changed).
		/// </summary>
		event Action<IItemContainer, Item, int> OnSlotUpdated;

		/// <summary>
		/// Gets the list of items contained in this container.
		/// </summary>
		List<Item> Items { get; }

		/// <summary>
		/// Determines if the container can be manipulated (e.g., items moved or swapped).
		/// </summary>
		/// <returns>True if manipulation is allowed, false otherwise.</returns>
		bool CanManipulate();

		/// <summary>
		/// Checks if the specified slot index is valid for this container.
		/// </summary>
		/// <param name="slot">The slot index to check.</param>
		/// <returns>True if the slot is valid, false otherwise.</returns>
		bool IsValidSlot(int slot);

		/// <summary>
		/// Checks if the specified slot is empty.
		/// </summary>
		/// <param name="slot">The slot index to check.</param>
		/// <returns>True if the slot is empty, false otherwise.</returns>
		bool IsSlotEmpty(int slot);

		/// <summary>
		/// Attempts to get the item in the specified slot.
		/// </summary>
		/// <param name="slot">The slot index to retrieve.</param>
		/// <param name="item">The item found in the slot, or null if not found.</param>
		/// <returns>True if an item was found, false otherwise.</returns>
		bool TryGetItem(int slot, out Item item);

		/// <summary>
		/// Checks if the container contains an item with the specified template.
		/// </summary>
		/// <param name="itemTemplate">The item template to search for.</param>
		/// <returns>True if the item is found, false otherwise.</returns>
		bool ContainsItem(BaseItemTemplate itemTemplate);

		/// <summary>
		/// Gets the count of items matching the specified template.
		/// </summary>
		/// <param name="itemTemplate">The item template to count.</param>
		/// <returns>The number of items matching the template.</returns>
		int GetItemCount(BaseItemTemplate itemTemplate);

		/// <summary>
		/// Adds slots to the container, optionally initializing with a list of items.
		/// </summary>
		/// <param name="items">The initial items to add (can be null).</param>
		/// <param name="amount">The number of slots to add.</param>
		void AddSlots(List<Item> items, int amount);

		/// <summary>
		/// Clears all items from the container.
		/// </summary>
		void Clear();

		/// <summary>
		/// Checks if the container has at least one free slot.
		/// </summary>
		/// <returns>True if a free slot exists, false otherwise.</returns>
		bool HasFreeSlot();

		/// <summary>
		/// Gets the number of free slots in the container.
		/// </summary>
		/// <returns>The number of free slots.</returns>
		int FreeSlots();

		/// <summary>
		/// Gets the number of filled slots in the container.
		/// </summary>
		/// <returns>The number of filled slots.</returns>
		int FilledSlots();

		/// <summary>
		/// Determines if the specified item can be added to the container.
		/// </summary>
		/// <param name="item">The item to check.</param>
		/// <returns>True if the item can be added, false otherwise.</returns>
		bool CanAddItem(Item item);

		/// <summary>
		/// Attempts to add the specified item to the container, returning a list of modified items.
		/// </summary>
		/// <param name="item">The item to add.</param>
		/// <param name="modifiedItems">The list of items modified during the operation.</param>
		/// <returns>True if the item was successfully added, false otherwise.</returns>
		bool TryAddItem(Item item, out List<Item> modifiedItems);

		/// <summary>
		/// Sets the item in the specified slot.
		/// </summary>
		/// <param name="item">The item to set.</param>
		/// <param name="slot">The slot index to set the item in.</param>
		/// <returns>True if the item was successfully set, false otherwise.</returns>
		bool SetItemSlot(Item item, int slot);

		/// <summary>
		/// Swaps items between two slots.
		/// </summary>
		/// <param name="from">The source slot index.</param>
		/// <param name="to">The destination slot index.</param>
		/// <returns>True if the swap was successful, false otherwise.</returns>
		bool SwapItemSlots(int from, int to);

		/// <summary>
		/// Swaps items between two slots and returns the items that were swapped.
		/// </summary>
		/// <param name="from">The source slot index.</param>
		/// <param name="to">The destination slot index.</param>
		/// <param name="fromItem">The item originally in the source slot.</param>
		/// <param name="toItem">The item originally in the destination slot.</param>
		/// <returns>True if the swap was successful, false otherwise.</returns>
		bool SwapItemSlots(int from, int to, out Item fromItem, out Item toItem);

		/// <summary>
		/// Removes the item from the specified slot.
		/// </summary>
		/// <param name="slot">The slot index to remove the item from.</param>
		/// <returns>The item that was removed, or null if no item was present.</returns>
		Item RemoveItem(int slot);
	}
}