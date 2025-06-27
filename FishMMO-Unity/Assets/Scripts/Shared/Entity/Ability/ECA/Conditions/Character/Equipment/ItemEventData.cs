namespace FishMMO.Shared
{
	/// <summary>
	/// EventData for an item related action or condition.
	/// </summary>
	public class ItemEventData : EventData
	{
		public Item Item { get; }
		public int InventoryIndex { get; }
		public IItemContainer SourceContainer { get; }
		public ItemSlot TargetSlot { get; }

		public ItemEventData(ICharacter initiator, Item item, int inventoryIndex = -1, IItemContainer sourceContainer = null, ItemSlot targetSlot = ItemSlot.Head)
			: base(initiator)
		{
			Item = item;
			InventoryIndex = inventoryIndex;
			SourceContainer = sourceContainer;
			TargetSlot = targetSlot;
		}
	}
}