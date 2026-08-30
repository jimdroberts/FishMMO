namespace FishMMO.Database.Data
{
	/// <summary>
	/// One item belonging to a character, in whichever container currently holds it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Replaces <c>CharacterInventoryData</c>, <c>CharacterEquipmentData</c> and
	/// <c>CharacterBankData</c>, which were three copies of one shape distinguished only by the
	/// table they were written to. <see cref="Container"/> is what that distinction became.
	/// </para>
	/// <para>
	/// <b><see cref="ID"/> identifies the ITEM, not the slot.</b> That is the whole point of the
	/// merge. Under the previous schema a row was keyed <c>(character_id, slot)</c>, so moving an
	/// item between two slots produced two different rows with two different ids and passing two
	/// items through one slot gave them the same id. Here the row is keyed by <see cref="ID"/> and
	/// <see cref="Container"/> and <see cref="Slot"/> are ordinary mutable columns, so an item keeps
	/// its number from the moment it is first written until it is destroyed — across slot moves,
	/// across container moves, and across sessions.
	/// </para>
	/// <para>
	/// An <see cref="ID"/> of zero means "not yet written": the database assigns one on first
	/// insert and returns it, and the caller writes it back onto the runtime item. See
	/// <c>ICharacterItemService.PersistAsync</c>.
	/// </para>
	/// </remarks>
	public struct CharacterItemData : IVersioned<CharacterItemData>
	{
		/// <summary>Primary key, and the item's durable identity. Zero until the first insert.</summary>
		public readonly long ID;

		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;

		/// <summary>Character that owns this item.</summary>
		public readonly long CharacterID;

		/// <summary>Which container holds it.</summary>
		public readonly ItemContainerType Container;

		/// <summary>Slot index within <see cref="Container"/>.</summary>
		public readonly int Slot;

		/// <summary>Item template ID.</summary>
		public readonly int TemplateID;

		/// <summary>Randomization seed for a generated item, or zero.</summary>
		public readonly int Seed;

		/// <summary>Stack amount.</summary>
		public readonly uint Amount;

		long IVersioned<CharacterItemData>.Version => Version;

		public CharacterItemData(long id, long characterID, ItemContainerType container, int templateID, int slot, int seed, uint amount)
			: this(id, version: 0, characterID, container, templateID, slot, seed, amount)
		{
		}

		public CharacterItemData(long id, long version, long characterID, ItemContainerType container, int templateID, int slot, int seed, uint amount)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			Container = container;
			TemplateID = templateID;
			Slot = slot;
			Seed = seed;
			Amount = amount;
		}

		public CharacterItemData WithVersion(long newVersion)
		{
			return new CharacterItemData(ID, newVersion, CharacterID, Container, TemplateID, Slot, Seed, Amount);
		}

		/// <summary>The same row with a database-assigned <see cref="ID"/> filled in.</summary>
		/// <remarks>
		/// For the caller that has just received <c>RETURNING id</c> from a first insert and wants
		/// to hand the completed row on. The runtime item is updated separately; this exists so a
		/// DTO in a batch can be completed without rebuilding it field by field.
		/// </remarks>
		public CharacterItemData WithID(long newID)
		{
			return new CharacterItemData(newID, Version, CharacterID, Container, TemplateID, Slot, Seed, Amount);
		}
	}
}
