using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a character's bank (stored items) data in the database.
	/// </summary>
	public class CharacterBankEntity : IVersionedEntity
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
		/// Template identifier for this bank item.
		/// </summary>
		public int TemplateID { get; set; }
		/// <summary>
		/// Slot index in the bank storage.
		/// </summary>
		public int Slot { get; set; }
		/// <summary>
		/// Randomization seed for item properties.
		/// </summary>
		public int Seed { get; set; }
		/// <summary>
		/// Quantity of the item in this slot.
		/// </summary>
		public uint Amount { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}