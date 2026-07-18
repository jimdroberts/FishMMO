using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a character's quest data in the database.
	/// </summary>
	public class CharacterQuestEntity : IVersionedEntity
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
		/// Template identifier for this quest.
		/// </summary>
		public int TemplateID { get; set; }
		/// <summary>
		/// Current status of the quest (e.g., inactive, active, completed).
		/// </summary>
		public byte Status { get; set; }
		/// <summary>
		/// Serialized objective progress values for this quest.
		/// Format: comma-separated integers ("0,5,0,3") where each index
		/// corresponds to the quest template's objective at the same index.
		/// The reader and writer MUST agree on objective ordering; changing
		/// the template after quests are in-flight requires a migration.
		/// </summary>
		public string ObjectiveValues { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}