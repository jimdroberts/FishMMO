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
		public readonly double CastTimeEnd;
		public readonly double CooldownEnd;

		long IVersioned<CharacterSkillData>.Version => Version;

		public CharacterSkillData(long id, long characterID, int templateID, int level, int experience, double castTimeEnd, double cooldownEnd)
			: this(id, version: 0, characterID, templateID, level, experience, castTimeEnd, cooldownEnd)
		{
		}

		public CharacterSkillData(long id, long version, long characterID, int templateID, int level, int experience, double castTimeEnd, double cooldownEnd)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Level = level;
			Experience = experience;
			CastTimeEnd = castTimeEnd;
			CooldownEnd = cooldownEnd;
		}

		public CharacterSkillData WithVersion(long newVersion)
		{
			return new CharacterSkillData(ID, newVersion, CharacterID, TemplateID, Level, Experience, CastTimeEnd, CooldownEnd);
		}
	}
}