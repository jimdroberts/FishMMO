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
		/// Shoulders slot (e.g., pauldrons, shoulder pads).
		/// </summary>
		Shoulders,

		/// <summary>
		/// Hands slot (e.g., gloves, gauntlets).
		/// </summary>
		Hands,

		/// <summary>
		/// Legs slot (e.g., pants, leggings).
		/// </summary>
		Legs,

		/// <summary>
		/// Feet slot (e.g., boots, shoes).
		/// </summary>
		Feet,

		/// <summary>
		/// Back slot (e.g., capes, cloaks).
		/// </summary>
		Back,

		/// <summary>
		/// Primary hand slot (e.g., sword, staff, wand).
		/// </summary>
		Primary,

		/// <summary>
		/// Secondary hand slot (e.g., shield, offhand, tome).
		/// </summary>
		Secondary,

		/// <summary>
		/// Accessory slot (e.g., rings, amulets, trinkets).
		/// </summary>
		Accessory,
	}
}