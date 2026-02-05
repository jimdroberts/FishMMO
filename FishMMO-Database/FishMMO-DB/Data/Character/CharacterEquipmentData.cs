namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character equipment item data transfer object.
	/// </summary>
	public struct CharacterEquipmentData : IVersioned<CharacterEquipmentData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Slot;
		public readonly int Seed;
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