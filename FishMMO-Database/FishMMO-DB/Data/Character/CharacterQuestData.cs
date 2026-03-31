namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character quest data transfer object.
	/// </summary>
	public struct CharacterQuestData : IVersioned<CharacterQuestData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly byte Status;
		public readonly string ObjectiveValues;

		long IVersioned<CharacterQuestData>.Version => Version;

		public CharacterQuestData(long id, long characterID, int templateID, byte status, string objectiveValues)
			: this(id, version: 0, characterID, templateID, status, objectiveValues)
		{
		}

		public CharacterQuestData(long id, long version, long characterID, int templateID, byte status, string objectiveValues)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Status = status;
			ObjectiveValues = objectiveValues;
		}

		public CharacterQuestData WithVersion(long newVersion)
		{
			return new CharacterQuestData(ID, newVersion, CharacterID, TemplateID, Status, ObjectiveValues);
		}
	}
}