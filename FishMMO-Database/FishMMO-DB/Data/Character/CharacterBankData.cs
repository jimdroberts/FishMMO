namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character bank item data transfer object.
	/// </summary>
	public struct CharacterBankData
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Slot;
		public readonly int Seed;
		public readonly uint Amount;

		public CharacterBankData(long id, long characterID, int templateID, int slot, int seed, uint amount)
			: this(id, version: 0, characterID, templateID, slot, seed, amount)
		{
		}

		public CharacterBankData(long id, long version, long characterID, int templateID, int slot, int seed, uint amount)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Slot = slot;
			Seed = seed;
			Amount = amount;
		}
	}
}