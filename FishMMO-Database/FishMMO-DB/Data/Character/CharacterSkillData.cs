namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character skill data transfer object.
	/// </summary>
	public struct CharacterSkillData : IVersioned<CharacterSkillData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that owns this skill.</summary>
		public readonly long CharacterID;
		/// <summary>Skill template ID.</summary>
		public readonly int TemplateID;
		/// <summary>Current skill level.</summary>
		public readonly int Level;
		/// <summary>Current skill experience.</summary>
		public readonly int Experience;
		/// <summary>Timestamp when cast completes.</summary>
		public readonly double CastTimeEnd;
		/// <summary>Timestamp when cooldown ends.</summary>
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