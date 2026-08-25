namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character pet buff data transfer object.
	/// </summary>
	/// <remarks>
	/// Field-for-field the same shape as <see cref="CharacterBuffData"/>, and deliberately so: a
	/// pet's buffs are ordinary buffs on an ordinary <c>IBuffController</c>, snapshotted and
	/// restored by the same conversion the owner's buffs go through. The previous shape carried
	/// only <c>Level</c> and <c>BuffTimeEnd</c>, which the service then wrote into the
	/// <c>stacks</c> and <c>remaining_time</c> columns under different names while hard-coding
	/// <c>tick_time</c> and <c>tick_count</c> to zero — so a restored damage-over-time effect
	/// lost both its tick schedule and its accumulated tick count.
	/// </remarks>
	public struct CharacterPetBuffData : IVersioned<CharacterPetBuffData>
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Optimistic concurrency version.</summary>
		public readonly long Version;
		/// <summary>Character that owns the pet carrying this buff.</summary>
		public readonly long CharacterID;
		/// <summary>Buff template ID.</summary>
		public readonly int TemplateID;
		/// <summary>Remaining buff duration, in seconds.</summary>
		public readonly double RemainingTime;
		/// <summary>Seconds until this buff's next periodic tick.</summary>
		public readonly double TickTime;
		/// <summary>Number of buff stacks.</summary>
		public readonly int Stacks;
		/// <summary>
		/// Number of ticks that have fired for this buff instance (cumulative tick modifiers).
		/// Persisted so cumulative effects (DoT stacking) survive a despawn.
		/// </summary>
		public readonly int TickCount;

		long IVersioned<CharacterPetBuffData>.Version => Version;

		public CharacterPetBuffData(long id, long characterID, int templateID, double remainingTime, double tickTime, int stacks, int tickCount)
			: this(id, version: 0, characterID, templateID, remainingTime, tickTime, stacks, tickCount)
		{
		}

		public CharacterPetBuffData(long id, long version, long characterID, int templateID, double remainingTime, double tickTime, int stacks, int tickCount)
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

		public CharacterPetBuffData WithVersion(long newVersion)
		{
			return new CharacterPetBuffData(ID, newVersion, CharacterID, TemplateID, RemainingTime, TickTime, Stacks, TickCount);
		}
	}
}
