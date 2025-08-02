namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for bank controllers, managing currency and item slots for a character's bank.
	/// Extends character behaviour and item container functionality.
	/// </summary>
	public interface IBankController : ICharacterBehaviour, IItemContainer
	{
		/// <summary>
		/// The ID of the last interactable bank object used by the player.
		/// </summary>
		long LastInteractableID { get; set; }

		/// <summary>
		/// The amount of currency stored in the bank.
		/// </summary>
		long Currency { get; set; }

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