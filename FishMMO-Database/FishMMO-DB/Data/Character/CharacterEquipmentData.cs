namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character equipment item data transfer object.
	/// </summary>
	public struct CharacterEquipmentData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Slot;
		public readonly int Seed;
		public readonly uint Amount;

		public CharacterEquipmentData(long id, long characterID, int templateID, int slot, int seed, uint amount)
		{
			ID = id;
			CharacterID = characterID;
			TemplateID = templateID;
			Slot = slot;
			Seed = seed;
			Amount = amount;
		}
	}
}