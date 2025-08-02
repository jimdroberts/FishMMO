namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for inventory controllers, managing activation and slot swapping logic for a character's inventory.
	/// Extends character behaviour and item container functionality.
	/// </summary>
	public interface IInventoryController : ICharacterBehaviour, IItemContainer
	{
		/// <summary>
		/// Activates the item in the specified inventory slot, typically triggering its use effect.
		/// </summary>
		/// <param name="index">The inventory slot index to activate.</param>
		void Activate(int index);

		/// <summary>
		/// Determines if two item slots can be swapped, preventing invalid swaps (e.g., same slot).
		/// </summary>
		/// <param name="from">The source slot index.</param>
		/// <param name="to">The destination slot index.</param>
		/// <param name="fromInventory">The inventory type of the source slot.</param>
		/// <returns>True if the slots can be swapped, false otherwise.</returns>
		bool CanSwapItemSlots(int from, int to, InventoryType fromInventory);
	}
}