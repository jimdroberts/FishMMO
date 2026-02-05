namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character known ability data transfer object.
	/// </summary>
	public struct CharacterKnownAbilityData : IVersioned<CharacterKnownAbilityData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;

		long IVersioned<CharacterKnownAbilityData>.Version => Version;

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

		public CharacterKnownAbilityData WithVersion(long newVersion)
		{
			return new CharacterKnownAbilityData(ID, newVersion, CharacterID, TemplateID);
		}
	}
}