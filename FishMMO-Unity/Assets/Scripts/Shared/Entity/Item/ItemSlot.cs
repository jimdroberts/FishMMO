namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the possible equipment slots for items on a character.
	/// Used to determine where an item can be equipped.
	/// </summary>
	public enum ItemSlot : byte
	{
		/// <summary>
		/// Head slot (e.g., helmets, hats).
		/// </summary>
		Head = 0,

		/// <summary>
		/// Chest slot (e.g., armor, shirts).
		/// </summary>
		Chest,

		/// <summary>
		/// Legs slot (e.g., pants, leggings).
		/// </summary>
		Legs,

		/// <summary>
		/// Hands slot (e.g., gloves, gauntlets).
		/// </summary>
		Hands,

		/// <summary>
		/// Feet slot (e.g., boots, shoes).
		/// </summary>
		Feet,

		/// <summary>
		/// Primary weapon slot (e.g., sword, staff).
		/// </summary>
		Primary,

		/// <summary>
		/// Secondary weapon slot (e.g., shield, offhand).
		/// </summary>
		Secondary,
	}
}