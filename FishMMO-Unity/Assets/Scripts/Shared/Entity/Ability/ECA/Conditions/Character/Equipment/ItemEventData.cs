namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for an item-related action or condition.
	/// Used to pass item, inventory, and container information to actions and conditions that operate on items.
	/// </summary>
	public class ItemEventData : EventData
	{
		/// <summary>
		/// The item involved in the event (e.g., to equip, unequip, or check).
		/// </summary>
		public Item Item { get; }

		/// <summary>
		/// The index in the inventory where the item is located, or -1 if not applicable.
		/// </summary>
		public int InventoryIndex { get; }

		/// <summary>
		/// The container from which the item originates (e.g., inventory, bank, etc.).
		/// </summary>
		public IItemContainer SourceContainer { get; }

		/// <summary>
		/// The equipment slot the item is being moved to or checked against.
		/// </summary>
		public ItemSlot TargetSlot { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="ItemEventData"/> class.
		/// </summary>
		/// <param name="initiator">The character initiating the event.</param>
		/// <param name="item">The item involved in the event.</param>
		/// <param name="inventoryIndex">The index in the inventory where the item is located, or -1 if not applicable.</param>
		/// <param name="sourceContainer">The container from which the item originates.</param>
		/// <param name="targetSlot">The equipment slot the item is being moved to or checked against.</param>
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