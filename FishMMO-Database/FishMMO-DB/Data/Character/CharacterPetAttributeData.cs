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
		/// <summary>Which of the character's pets: 0 for the first. See <c>CharacterPetEntity.Slot</c>.</summary>
		public readonly int Slot;
		/// <summary>Attribute template ID.</summary>
		public readonly int TemplateID;
		/// <summary>Base attribute value.</summary>
		public readonly int Value;
		/// <summary>Current attribute value.</summary>
		public readonly float CurrentValue;

		long IVersioned<CharacterPetAttributeData>.Version => Version;

		public CharacterPetAttributeData(long id, long characterID, int slot, int templateID, int value, float currentValue)
			: this(id, version: 0, characterID, slot, templateID, value, currentValue)
		{
		}

		public CharacterPetAttributeData(long id, long version, long characterID, int slot, int templateID, int value, float currentValue)
		{
			Slot = slot;
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Value = value;
			CurrentValue = currentValue;
		}

		public CharacterPetAttributeData WithVersion(long newVersion)
		{
			return new CharacterPetAttributeData(ID, newVersion, CharacterID, Slot, TemplateID, Value, CurrentValue);
		}
	}
}