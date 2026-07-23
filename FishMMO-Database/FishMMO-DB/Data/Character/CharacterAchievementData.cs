namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character achievement data transfer object.
	/// </summary>
	public struct CharacterAchievementData : IVersioned<CharacterAchievementData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that earned this achievement.</summary>
		public readonly long CharacterID;
		/// <summary>Achievement template ID.</summary>
		public readonly int TemplateID;
		/// <summary>Achievement tier level.</summary>
		public readonly byte Tier;
		/// <summary>Achievement progress value.</summary>
		public readonly uint Value;

		long IVersioned<CharacterAchievementData>.Version => Version;

		public CharacterAchievementData(long id, long characterID, int templateID, byte tier, uint value)
			: this(id, version: 0, characterID, templateID, tier, value)
		{
		}

		public CharacterAchievementData(long id, long version, long characterID, int templateID, byte tier, uint value)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			Tier = tier;
			Value = value;
		}

		public CharacterAchievementData WithVersion(long newVersion)
		{
			return new CharacterAchievementData(ID, newVersion, CharacterID, TemplateID, Tier, Value);
		}
	}
}