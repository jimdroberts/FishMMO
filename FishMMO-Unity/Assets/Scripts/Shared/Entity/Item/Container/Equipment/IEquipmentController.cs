using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for equipment controllers, managing equip/unequip logic and activation of equipment slots.
	/// Extends character behaviour and item container functionality.
	/// </summary>
	public interface IEquipmentController : ICharacterBehaviour, IItemContainer
	{
		/// <summary>
		/// Activates the item in the specified equipment slot, typically triggering its use effect.
		/// </summary>
		/// <param name="index">The equipment slot index to activate.</param>
		void Activate(int index);

		/// <summary>
		/// Equips the specified item into the given equipment slot, handling swaps and unequips as needed.
		/// </summary>
		/// <param name="item">The item to equip.</param>
		/// <param name="inventoryIndex">The index in the source inventory.</param>
		/// <param name="container">The source item container (e.g., inventory or bank).</param>
		/// <param name="toSlot">The equipment slot to equip the item into.</param>
		/// <returns>True if the item was successfully equipped, false otherwise.</returns>
		bool Equip(Item item, int inventoryIndex, IItemContainer container, ItemSlot toSlot);

		/// <summary>
		/// Unequips the item from the specified slot and adds it to the given container (e.g., inventory or bank).
		/// </summary>
		/// <param name="container">The destination item container.</param>
		/// <param name="slot">The equipment slot to unequip from.</param>
		/// <param name="modifiedItems">The list of items modified during the operation.</param>
		/// <returns>True if the item was successfully unequipped and added, false otherwise.</returns>
		bool Unequip(IItemContainer container, byte slot, out List<Item> modifiedItems);
	}
}