namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the possible equipment slots for items on a character.
	/// Used to determine where an item can be equipped.
	/// </summary>
	/// <remarks>
	/// EVERY MEMBER IS EXPLICITLY NUMBERED, AND MUST STAY THAT WAY. Item templates are
	/// ScriptableObjects and serialize this as its integer, so the number is the contract — not
	/// the name and not the position.
	/// <para>
	/// Inserting <c>Shoulders</c> at index 2 once shifted every slot below <c>Hands</c> in every
	/// already-authored asset: leggings became shoulders, boots became legs, a sword became feet
	/// and a shield became back. Nothing errored, because each value was still a valid slot —
	/// the items simply equipped to the wrong part of the body, and the only symptom was a sword
	/// on someone's feet.
	/// </para>
	/// <para>
	/// Add new slots at the END with the next free number. Never insert, never reorder, and never
	/// reuse the number of a slot that has been removed.
	/// </para>
	/// </remarks>
	public enum ItemSlot : byte
	{
		/// <summary>
		/// Head slot (e.g., helmets, hats).
		/// </summary>
		Head = 0,

		/// <summary>
		/// Chest slot (e.g., armor, shirts).
		/// </summary>
		Chest = 1,
		/// <summary>
		/// Shoulders slot (e.g., pauldrons, shoulder pads).
		/// </summary>
		Shoulders = 2,
		/// <summary>
		/// Hands slot (e.g., gloves, gauntlets).
		/// </summary>
		Hands = 3,
		/// <summary>
		/// Legs slot (e.g., pants, leggings).
		/// </summary>
		Legs = 4,
		/// <summary>
		/// Feet slot (e.g., boots, shoes).
		/// </summary>
		Feet = 5,
		/// <summary>
		/// Back slot (e.g., capes, cloaks).
		/// </summary>
		Back = 6,
		/// <summary>
		/// Primary hand slot (e.g., sword, staff, wand).
		/// </summary>
		Primary = 7,
		/// <summary>
		/// Secondary hand slot (e.g., shield, offhand, tome).
		/// </summary>
		Secondary = 8,
		/// <summary>
		/// Accessory slot (e.g., rings, amulets, trinkets).
		/// </summary>
		Accessory = 9,
	}
}