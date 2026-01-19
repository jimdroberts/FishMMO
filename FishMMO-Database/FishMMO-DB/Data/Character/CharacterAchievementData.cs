namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character achievement data transfer object.
	/// </summary>
	public struct CharacterAchievementData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly byte Tier;
		public readonly uint Value;

		public CharacterAchievementData(long id, long characterID, int templateID, byte tier, uint value)
		{
			ID = id;
			CharacterID = characterID;
			TemplateID = templateID;
			Tier = tier;
			Value = value;
		}
	}
}