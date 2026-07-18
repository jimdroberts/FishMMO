using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a character's skill data in the database.
	/// </summary>
	public class CharacterSkillEntity : IVersionedEntity
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
		/// Hash identifier for this skill.
		/// </summary>
		public int Hash { get; set; }
		/// <summary>
		/// Current level of the skill.
		/// </summary>
		public int Level { get; set; }
		/// <summary>
		/// Timestamp (unix) when the cast time ends.
		/// </summary>
		public double CastTimeEnd { get; set; }
		/// <summary>
		/// Timestamp (unix) when the cooldown expires.
		/// </summary>
		public double CooldownEnd { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}