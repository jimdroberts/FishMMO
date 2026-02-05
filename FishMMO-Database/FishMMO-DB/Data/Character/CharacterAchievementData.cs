namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character achievement data transfer object.
	/// </summary>
	public struct CharacterAchievementData : IVersioned<CharacterAchievementData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly byte Tier;
		public readonly uint Value;

		long IVersioned<CharacterAchievementData>.Version => Version;

		public CharacterAchievementData(long id, long characterID, int templateID, byte tier, uint value)
			: this(id, version: 0, characterID, templateID, tier, value)
		{
		}

		public CharacterAchievementData(long id, long version, long characterID, int templateID, byte tier, uint value)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Tier = tier;
			Value = value;
		}

		public CharacterAchievementData WithVersion(long newVersion)
		{
			return new CharacterAchievementData(ID, newVersion, CharacterID, TemplateID, Tier, Value);
		}
	}
}