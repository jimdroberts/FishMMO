using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a character's buff data in the database.
	/// </summary>
	public class CharacterBuffEntity : IVersionedEntity
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
		/// Template identifier for this buff.
		/// </summary>
		public int TemplateID { get; set; }
		/// <summary>
		/// Remaining duration of the buff in seconds.
		/// </summary>
		public double RemainingTime { get; set; }
		/// <summary>
		/// Interval in seconds between periodic tick effects.
		/// </summary>
		public double TickTime { get; set; }
		/// <summary>
		/// Number of stacks of this buff applied.
		/// </summary>
		public int Stacks { get; set; }
		/// <summary>
		/// Total number of ticks that have occurred for this buff.
		/// </summary>
		public int TickCount { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}