namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character buff data transfer object.
	/// </summary>
	public struct CharacterBuffData
	{
		public readonly long ID;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly float RemainingTime;
		public readonly float TickTime;
		public readonly int Stacks;

		public CharacterBuffData(long id, long characterID, int templateID, float remainingTime, float tickTime, int stacks)
		{
			ID = id;
			CharacterID = characterID;
			TemplateID = templateID;
			RemainingTime = remainingTime;
			TickTime = tickTime;
			Stacks = stacks;
		}
	}
}