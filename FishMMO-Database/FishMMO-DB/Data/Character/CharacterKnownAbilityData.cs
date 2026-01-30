namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character known ability data transfer object.
	/// </summary>
	public struct CharacterKnownAbilityData
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;

		public CharacterKnownAbilityData(long id, long characterID, int templateID)
			: this(id, version: 0, characterID, templateID)
		{
		}

		public CharacterKnownAbilityData(long id, long version, long characterID, int templateID)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
		}
	}
}