namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character pet buff data transfer object.
	/// </summary>
	public struct CharacterPetBuffData
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Level;
		public readonly float BuffTimeEnd;

		public CharacterPetBuffData(long id, long characterID, int templateID, int level, float buffTimeEnd)
			: this(id, version: 0, characterID, templateID, level, buffTimeEnd)
		{
		}

		public CharacterPetBuffData(long id, long version, long characterID, int templateID, int level, float buffTimeEnd)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Level = level;
			BuffTimeEnd = buffTimeEnd;
		}
	}
}