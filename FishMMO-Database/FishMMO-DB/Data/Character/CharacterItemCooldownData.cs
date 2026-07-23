namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character item cooldown data transfer object.
	/// </summary>
	public struct CharacterItemCooldownData : IVersioned<CharacterItemCooldownData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that owns this cooldown.</summary>
		public readonly long CharacterID;
		/// <summary>Cooldown category identifier.</summary>
		public readonly int Category;
		/// <summary>Timestamp when cooldown ends.</summary>
		public readonly double CooldownEnd;

		long IVersioned<CharacterItemCooldownData>.Version => Version;

		public CharacterItemCooldownData(long id, long characterID, int category, double cooldownEnd)
			: this(id, version: 0, characterID, category, cooldownEnd)
		{
		}

		public CharacterItemCooldownData(long id, long version, long characterID, int category, double cooldownEnd)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			Category = category;
			CooldownEnd = cooldownEnd;
		}

		public CharacterItemCooldownData WithVersion(long newVersion)
		{
			return new CharacterItemCooldownData(ID, newVersion, CharacterID, Category, CooldownEnd);
		}
	}
}