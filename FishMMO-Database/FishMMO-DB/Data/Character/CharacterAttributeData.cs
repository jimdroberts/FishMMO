namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character attribute data transfer object.
	/// </summary>
	public struct CharacterAttributeData : IVersioned<CharacterAttributeData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that owns this attribute.</summary>
		public readonly long CharacterID;
		/// <summary>Attribute template ID.</summary>
		public readonly int TemplateID;
		/// <summary>Base attribute value.</summary>
		public readonly int Value;
		/// <summary>Current attribute value.</summary>
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
