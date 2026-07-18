using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a character's achievement data in the database.
	/// </summary>
	public class CharacterAchievementEntity : IVersionedEntity
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
		/// Template identifier for this achievement.
		/// </summary>
		public int TemplateID { get; set; }
		/// <summary>
		/// Current tier level of the achievement.
		/// </summary>
		public byte Tier { get; set; }
		/// <summary>
		/// Progress value toward completing the achievement.
		/// </summary>
		public uint Value { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}