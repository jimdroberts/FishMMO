namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character equipment item data transfer object.
	/// </summary>
	public struct CharacterEquipmentData : IVersioned<CharacterEquipmentData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that owns this equipment.</summary>
		public readonly long CharacterID;
		/// <summary>Item template ID.</summary>
		public readonly int TemplateID;
		/// <summary>Equipment slot index.</summary>
		public readonly int Slot;
		/// <summary>Randomization seed for item.</summary>
		public readonly int Seed;
		/// <summary>Item stack amount.</summary>
		public readonly uint Amount;

		long IVersioned<CharacterEquipmentData>.Version => Version;

		public CharacterEquipmentData(long id, long characterID, int templateID, int slot, int seed, uint amount)
			: this(id, version: 0, characterID, templateID, slot, seed, amount)
		{
		}

		public CharacterEquipmentData(long id, long version, long characterID, int templateID, int slot, int seed, uint amount)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Slot = slot;
			Seed = seed;
			Amount = amount;
		}

		public CharacterEquipmentData WithVersion(long newVersion)
		{
			return new CharacterEquipmentData(ID, newVersion, CharacterID, TemplateID, Slot, Seed, Amount);
		}
	}
}