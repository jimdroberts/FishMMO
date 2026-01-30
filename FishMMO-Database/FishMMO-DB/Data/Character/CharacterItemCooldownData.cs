namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character item cooldown data transfer object.
	/// </summary>
	public struct CharacterItemCooldownData
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int Category;
		public readonly float CooldownEnd;

		public CharacterItemCooldownData(long id, long characterID, int category, float cooldownEnd)
			: this(id, version: 0, characterID, category, cooldownEnd)
		{
		}

		public CharacterItemCooldownData(long id, long version, long characterID, int category, float cooldownEnd)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			Category = category;
			CooldownEnd = cooldownEnd;
		}
	}
}