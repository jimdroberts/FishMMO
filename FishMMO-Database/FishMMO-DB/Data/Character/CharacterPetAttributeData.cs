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
		public readonly float CurrentValue;

		long IVersioned<CharacterPetAttributeData>.Version => Version;

		public CharacterPetAttributeData(long id, long characterID, int templateID, int value, float currentValue)
			: this(id, version: 0, characterID, templateID, value, currentValue)
		{
		}

		public CharacterPetAttributeData(long id, long version, long characterID, int templateID, int value, float currentValue)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Value = value;
			CurrentValue = currentValue;
		}

		public CharacterPetAttributeData WithVersion(long newVersion)
		{
			return new CharacterPetAttributeData(ID, newVersion, CharacterID, TemplateID, Value, CurrentValue);
		}
	}
}