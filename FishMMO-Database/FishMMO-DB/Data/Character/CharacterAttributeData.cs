namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character attribute data transfer object.
	/// </summary>
	public struct CharacterAttributeData : IVersioned<CharacterAttributeData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly int Value;
		public readonly float CurrentValue;

		long IVersioned<CharacterAttributeData>.Version => Version;

		public CharacterAttributeData(long id, long characterID, int templateID, int value, float currentValue)
			: this(id, version: 0, characterID, templateID, value, currentValue)
		{
		}

		public CharacterAttributeData(long id, long version, long characterID, int templateID, int value, float currentValue)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Value = value;
			CurrentValue = currentValue;
		}

		public CharacterAttributeData WithVersion(long newVersion)
		{
			return new CharacterAttributeData(ID, newVersion, CharacterID, TemplateID, Value, CurrentValue);
		}
	}
}