namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character pet attribute data transfer object.
	/// </summary>
	public struct CharacterPetAttributeData : IVersioned<CharacterPetAttributeData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that owns this pet attribute.</summary>
		public readonly long CharacterID;
		/// <summary>Attribute template ID.</summary>
		public readonly int TemplateID;
		/// <summary>Base attribute value.</summary>
		public readonly int Value;
		/// <summary>Current attribute value.</summary>
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