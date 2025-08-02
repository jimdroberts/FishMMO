namespace FishMMO.Shared
{
	/// <summary>
	/// Specifies the types of inventories available to a character, used for item management and slot operations.
	/// </summary>
	public enum InventoryType : byte
	{
		/// <summary>
		/// The main inventory for storing general items.
		/// </summary>
		Inventory = 0,

		/// <summary>
		/// The equipment inventory for storing equipped items (e.g., armor, weapons).
		/// </summary>
		Equipment,

		/// <summary>
		/// The bank inventory for storing items in a bank or safe location.
		/// </summary>
		Bank,
	}
}