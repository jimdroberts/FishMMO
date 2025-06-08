using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface IItemContainer
	{
		event Action<IItemContainer, Item, int> OnSlotUpdated;
		List<Item> Items { get; }
		bool CanManipulate();
		bool IsValidSlot(int slot);
		bool IsSlotEmpty(int slot);
		bool TryGetItem(int slot, out Item item);
		bool ContainsItem(BaseItemTemplate itemTemplate);
		void AddSlots(List<Item> items, int amount);
		void Clear();
		bool HasFreeSlot();
		int FreeSlots();
		int FilledSlots();
		bool CanAddItem(Item item);
		bool TryAddItem(Item item, out List<Item> modifiedItems);
		bool SetItemSlot(Item item, int slot);
		bool SwapItemSlots(int from, int to);
		bool SwapItemSlots(int from, int to, out Item fromItem, out Item toItem);
		Item RemoveItem(int slot);
	}
}