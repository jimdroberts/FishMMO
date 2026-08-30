namespace FishMMO.Database.Data
{
	/// <summary>
	/// Which of a character's containers an item row belongs to.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The discriminator that replaced three parallel tables (<c>character_inventory</c>,
	/// <c>character_equipment</c>, <c>character_bank</c>). Those tables each had their own identity
	/// sequence, so inventory row 42 and equipment row 42 were two different items wearing the same
	/// number — which made <c>Item.ID</c> unusable as an identity and forced a second, process-local
	/// id alongside it. One table means one sequence, so an item id names exactly one item for as
	/// long as that item exists, whichever container it currently sits in.
	/// </para>
	/// <para>
	/// <b>The numeric values must match <c>FishMMO.Shared.InventoryType</c> exactly.</b> They are
	/// two enums rather than one because this assembly cannot reference the Unity shared assembly,
	/// and the mapping between them is a cast. <c>ItemContainerTypeParityTests</c> pins the pairing;
	/// do not renumber either side.
	/// </para>
	/// </remarks>
	public enum ItemContainerType : byte
	{
		/// <summary>The character's general-purpose inventory.</summary>
		Inventory = 0,

		/// <summary>The character's equipped items. Slot is an <c>ItemSlot</c> socket, not a free index.</summary>
		Equipment = 1,

		/// <summary>The character's bank storage.</summary>
		Bank = 2,
	}
}
