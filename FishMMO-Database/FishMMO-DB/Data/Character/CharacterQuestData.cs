namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character quest data transfer object.
	/// </summary>
	public struct CharacterQuestData
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Progress;
		public readonly bool Completed;

		public CharacterQuestData(long id, long characterID, int templateID, int progress, bool completed)
			: this(id, version: 0, characterID, templateID, progress, completed)
		{
		}

		public CharacterQuestData(long id, long version, long characterID, int templateID, int progress, bool completed)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Progress = progress;
			Completed = completed;
		}
	}
}