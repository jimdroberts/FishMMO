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
		/// Seconds until this buff's next periodic tick.
		/// </summary>
		public double TickTime { get; set; }
		/// <summary>
		/// Number of stacks of this buff applied.
		/// </summary>
		public int Stacks { get; set; }
		/// <summary>
		/// Number of ticks that have fired for this buff instance, so cumulative periodic
		/// effects resume where they left off rather than restarting.
		/// </summary>
		public int TickCount { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}