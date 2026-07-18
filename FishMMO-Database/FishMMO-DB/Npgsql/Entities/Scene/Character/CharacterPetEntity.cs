using System;
using System.Collections.Generic;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a character's pet data in the database.
	/// </summary>
	public class CharacterPetEntity : IVersionedEntity
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
		/// Template identifier for this pet.
		/// </summary>
		public int TemplateID { get; set; }
		/// <summary>
		/// List of ability identifiers known by this pet.
		/// </summary>
		public List<int> Abilities { get; set; }
		/// <summary>
		/// Whether the pet is currently spawned in the world.
		/// </summary>
		public bool Spawned { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}