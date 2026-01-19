namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character item cooldown data transfer object.
	/// </summary>
	public struct CharacterItemCooldownData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly int Category;
		public readonly float CooldownEnd;

		public CharacterItemCooldownData(long id, long characterID, int category, float cooldownEnd)
		{
			ID = id;
			CharacterID = characterID;
			Category = category;
			CooldownEnd = cooldownEnd;
		}
	}
}