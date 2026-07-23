using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a pet's buff data in the database.
	/// </summary>
	public class CharacterPetBuffEntity : IVersionedEntity
	{
		/// <summary>
		/// Primary key.
		/// </summary>
		public long ID { get; set; }
		/// <summary>
		/// Application-level concurrency token. Incremented on every write to detect stale updates.
		/// </summary>
		public long Version { get; set; }
		/// <summary>
		/// Foreign key to the owning character.
		/// </summary>
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		/// <summary>
		/// Template identifier for this pet buff.
		/// </summary>
		public int TemplateID { get; set; }
		/// <summary>
		/// Remaining duration of the buff in seconds.
		/// </summary>
		public double RemainingTime { get; set; }
		/// <summary>
		/// Reserved for future use. Interval in seconds between periodic tick effects.
		/// Intentionally always written as 0 in the current PersistAsync implementation.
		/// This field is a schema placeholder for future periodic-DoT (damage-over-time)
		/// or periodic-heal mechanics. When the game logic is ready, set this to the
		/// desired tick interval and uncomment the write path in CharacterPetBuffService.
		/// </summary>
		public double TickTime { get; set; }
		/// <summary>
		/// Number of stacks of this buff applied.
		/// </summary>
		public int Stacks { get; set; }
		/// <summary>
		/// Reserved for future use. Total number of ticks that have occurred for this buff.
		/// Intentionally always written as 0 in the current PersistAsync implementation.
		/// Paired with <see cref="TickTime"/> — when periodic tick logic is added, this
		/// field tracks the cumulative tick count for resume/restore on server restart.
		/// </summary>
		public int TickCount { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}