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
		/// Template identifier for this skill.
		/// </summary>
		public int TemplateID { get; set; }
		/// <summary>
		/// Current level of the skill.
		/// </summary>
		public int Level { get; set; }
		/// <summary>
		/// Current experience points in this skill.
		/// </summary>
		public int Experience { get; set; }
		/// <summary>
		/// Unix timestamp (seconds) when the cast time ends.
		/// Stored as <c>double</c> rather than <c>DateTime</c> to maintain
		/// compatibility with Unity's game time representation and to avoid
		/// timezone-related serialization issues between the server and client.
		/// </summary>
		public double CastTimeEnd { get; set; }
		/// <summary>
		/// Unix timestamp (seconds) when the cooldown expires.
		/// Stored as <c>double</c> rather than <c>DateTime</c> for the same
		/// Unity compatibility reasons as <see cref="CastTimeEnd"/>.
		/// </summary>
		public double CooldownEnd { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}