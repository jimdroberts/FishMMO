namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character pet buff data transfer object.
	/// </summary>
	public struct CharacterPetBuffData : IVersioned<CharacterPetBuffData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that owns this pet buff.</summary>
		public readonly long CharacterID;
		/// <summary>Buff template ID.</summary>
		public readonly int TemplateID;
		/// <summary>Buff level.</summary>
		public readonly int Level;
		/// <summary>Timestamp when buff expires.</summary>
		public readonly double BuffTimeEnd;

		long IVersioned<CharacterPetBuffData>.Version => Version;

		public CharacterPetBuffData(long id, long characterID, int templateID, int level, double buffTimeEnd)
			: this(id, version: 0, characterID, templateID, level, buffTimeEnd)
		{
		}

		public CharacterPetBuffData(long id, long version, long characterID, int templateID, int level, double buffTimeEnd)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Level = level;
			BuffTimeEnd = buffTimeEnd;
		}

		public CharacterPetBuffData WithVersion(long newVersion)
		{
			return new CharacterPetBuffData(ID, newVersion, CharacterID, TemplateID, Level, BuffTimeEnd);
		}
	}
}