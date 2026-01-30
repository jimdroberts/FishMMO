namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character buff data transfer object.
	/// </summary>
	public struct CharacterBuffData
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly float RemainingTime;
		public readonly float TickTime;
		public readonly int Stacks;

		public CharacterBuffData(long id, long characterID, int templateID, float remainingTime, float tickTime, int stacks)
			: this(id, version: 0, characterID, templateID, remainingTime, tickTime, stacks)
		{
		}

		public CharacterBuffData(long id, long version, long characterID, int templateID, float remainingTime, float tickTime, int stacks)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			RemainingTime = remainingTime;
			TickTime = tickTime;
			Stacks = stacks;
		}
	}
}