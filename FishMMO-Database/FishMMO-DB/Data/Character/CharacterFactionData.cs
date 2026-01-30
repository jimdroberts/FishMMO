namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character faction data transfer object.
	/// </summary>
	public struct CharacterFactionData
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Value;

		public CharacterFactionData(long id, long characterID, int templateID, int value)
			: this(id, version: 0, characterID, templateID, value)
		{
		}

		public CharacterFactionData(long id, long version, long characterID, int templateID, int value)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Value = value;
		}
	}
}