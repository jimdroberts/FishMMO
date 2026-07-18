using System;
using System.Collections.Generic;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a character's ability data in the database.
	/// </summary>
	public class CharacterAbilityEntity : IVersionedEntity
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
		/// Template identifier for this ability.
		/// </summary>
		public int TemplateID { get; set; }
		/// <summary>
		/// List of ability event identifiers associated with this ability.
		/// </summary>
		public List<int> AbilityEvents { get; set; }
		/// <summary>
		/// Cooldown duration in seconds for this ability.
		/// </summary>
		public float Cooldown { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}