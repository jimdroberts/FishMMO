namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character buff data transfer object.
	/// </summary>
	public struct CharacterBuffData : IVersioned<CharacterBuffData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that owns this buff.</summary>
		public readonly long CharacterID;
		/// <summary>Buff template ID.</summary>
		public readonly int TemplateID;
		/// <summary>Remaining buff duration.</summary>
		public readonly double RemainingTime;
		/// <summary>Time between buff ticks.</summary>
		public readonly double TickTime;
		/// <summary>Number of buff stacks.</summary>
		public readonly int Stacks;
		/// <summary>
		/// Number of ticks that have fired for this buff instance (cumulative tick modifiers).
		/// Persisted so cumulative effects (DoT stacking) survive logout.
		/// </summary>
		public readonly int TickCount;

		long IVersioned<CharacterBuffData>.Version => Version;

		public CharacterBuffData(long id, long characterID, int templateID, double remainingTime, double tickTime, int stacks, int tickCount)
			: this(id, version: 0, characterID, templateID, remainingTime, tickTime, stacks, tickCount)
		{
		}

		public CharacterBuffData(long id, long version, long characterID, int templateID, double remainingTime, double tickTime, int stacks, int tickCount)
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
