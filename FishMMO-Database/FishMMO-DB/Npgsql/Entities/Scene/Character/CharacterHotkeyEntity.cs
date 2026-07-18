using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a character's hotkey bar binding data in the database.
	/// </summary>
	public class CharacterHotkeyEntity : IVersionedEntity
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
		/// Type of the hotkey binding (e.g., ability, item).
		/// </summary>
		public byte Type { get; set; }
		/// <summary>
		/// Slot index on the hotkey bar.
		/// </summary>
		public int Slot { get; set; }
		/// <summary>
		/// Reference identifier of the bound action or item.
		/// </summary>
		public long ReferenceID { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}