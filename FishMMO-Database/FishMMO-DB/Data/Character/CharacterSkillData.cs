namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character skill data transfer object.
	/// </summary>
	public struct CharacterSkillData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Level;
		public readonly int Experience;

		public CharacterSkillData(long id, long characterID, int templateID, int level, int experience)
		{
			ID = id;
			CharacterID = characterID;
			TemplateID = templateID;
			Level = level;
			Experience = experience;
		}
	}
}