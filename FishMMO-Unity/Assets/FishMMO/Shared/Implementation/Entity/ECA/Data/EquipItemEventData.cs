using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data carrying the item and slot involved in an equip or unequip event.
	/// </summary>
	public class EquipItemEventData : EventData
	{
		/// <summary>
		/// The item being equipped or unequipped.
		/// </summary>
		public Item Item { get; }

		/// <summary>
		/// The equipment slot involved.
		/// </summary>
		public ItemSlot Slot { get; }

		/// <summary>
		/// Creates a new EquipItemEventData.
		/// </summary>
		/// <param name="initiator">The character performing the equip/unequip.</param>
		/// <param name="item">The item involved.</param>
		/// <param name="slot">The equipment slot involved.</param>
		public EquipItemEventData(ICharacter initiator, Item item, ItemSlot slot)
			: base(initiator)
		{
			Item = item;
			Slot = slot;
		}
	}
}