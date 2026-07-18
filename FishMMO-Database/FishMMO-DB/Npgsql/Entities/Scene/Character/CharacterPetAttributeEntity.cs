using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a pet's attribute data in the database.
	/// </summary>
	public class CharacterPetAttributeEntity : IVersionedEntity
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
		/// Template identifier for this pet attribute.
		/// </summary>
		public int TemplateID { get; set; }
		/// <summary>
		/// Base value of the pet attribute.
		/// </summary>
		public int Value { get; set; }
		/// <summary>
		/// Current (modified) value of the pet attribute, including buffs and debuffs.
		/// </summary>
		public float CurrentValue { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}