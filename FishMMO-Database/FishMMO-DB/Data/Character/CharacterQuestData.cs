namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character quest data transfer object.
	/// </summary>
	public struct CharacterQuestData : IVersioned<CharacterQuestData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that owns this quest.</summary>
		public readonly long CharacterID;
		/// <summary>Quest template ID.</summary>
		public readonly int TemplateID;
		/// <summary>Quest status flag.</summary>
		public readonly byte Status;
		/// <summary>Serialized objective progress values.</summary>
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