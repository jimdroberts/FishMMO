namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character buff data transfer object.
	/// </summary>
	public struct CharacterBuffData : IVersioned<CharacterBuffData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly int TemplateID;
		public readonly float RemainingTime;
		public readonly float TickTime;
		public readonly int Stacks;
		/// <summary>
		/// Number of ticks that have fired for this buff instance (cumulative tick modifiers).
		/// Persisted so cumulative effects (DoT stacking) survive logout.
		/// </summary>
		public readonly int TickCount;

		long IVersioned<CharacterBuffData>.Version => Version;

		public CharacterBuffData(long id, long characterID, int templateID, float remainingTime, float tickTime, int stacks, int tickCount)
			: this(id, version: 0, characterID, templateID, remainingTime, tickTime, stacks, tickCount)
		{
		}

		public CharacterBuffData(long id, long version, long characterID, int templateID, float remainingTime, float tickTime, int stacks, int tickCount)
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			TemplateID = templateID;
			RemainingTime = remainingTime;
			TickTime = tickTime;
			Stacks = stacks;
			TickCount = tickCount;
		}

		public CharacterBuffData WithVersion(long newVersion)
		{
			return new CharacterBuffData(ID, newVersion, CharacterID, TemplateID, RemainingTime, TickTime, Stacks, TickCount);
		}
	}
}
