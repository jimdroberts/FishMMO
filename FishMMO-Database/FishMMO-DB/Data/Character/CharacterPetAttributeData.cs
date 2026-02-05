namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character pet attribute data transfer object.
	/// </summary>
	public struct CharacterPetAttributeData : IVersioned<CharacterPetAttributeData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Value;

		long IVersioned<CharacterPetAttributeData>.Version => Version;

		public CharacterPetAttributeData(long id, long characterID, int templateID, int value)
			: this(id, version: 0, characterID, templateID, value)
		{
		}

		public CharacterPetAttributeData(long id, long version, long characterID, int templateID, int value)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Value = value;
		}

		public CharacterPetAttributeData WithVersion(long newVersion)
		{
			return new CharacterPetAttributeData(ID, newVersion, CharacterID, TemplateID, Value);
		}
	}
}