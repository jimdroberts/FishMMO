namespace FishMMO.Client
{
	/// <summary>
	/// Types of reference buttons used in UI, such as inventory, equipment, bank, and ability buttons.
	/// </summary>
	public enum ReferenceButtonType : byte
	{
		/// <summary>
		/// No reference type assigned.
		/// </summary>
		None = 0,
		/// <summary>
		/// Reference to an inventory slot.
		/// </summary>
		Inventory,
		/// <summary>
		/// Reference to an equipment slot.
		/// </summary>
		Equipment,
		/// <summary>
		/// Reference to a bank slot.
		/// </summary>
		Bank,
		/// <summary>
		/// Reference to an ability.
		/// </summary>
		Ability,
	}
}