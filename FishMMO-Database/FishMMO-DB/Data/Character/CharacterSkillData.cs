namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character skill data transfer object.
	/// </summary>
	public struct CharacterSkillData : IVersioned<CharacterSkillData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Level;
		public readonly int Experience;

		long IVersioned<CharacterSkillData>.Version => Version;

		public CharacterSkillData(long id, long characterID, int templateID, int level, int experience)
			: this(id, version: 0, characterID, templateID, level, experience)
		{
		}

		public CharacterSkillData(long id, long version, long characterID, int templateID, int level, int experience)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Level = level;
			Experience = experience;
		}

		public CharacterSkillData WithVersion(long newVersion)
		{
			return new CharacterSkillData(ID, newVersion, CharacterID, TemplateID, Level, Experience);
		}
	}
}