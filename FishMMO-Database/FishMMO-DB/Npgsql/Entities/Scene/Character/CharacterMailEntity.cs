using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Entity representing a character's mail message data in the database.
	/// </summary>
	public class CharacterMailEntity : IVersionedEntity
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
		/// Foreign key to the character who sent this mail.
		/// </summary>
		public long SenderCharacterID { get; set; }
		/// <summary>
		/// Foreign key to the owning character (recipient).
		/// </summary>
		public long CharacterID { get; set; }
		/// <summary>
		/// Subject line of the mail message.
		/// </summary>
		public string Subject { get; set; }
		/// <summary>
		/// Body text of the mail message.
		/// </summary>
		public string Message { get; set; }
		/// <summary>
		/// Template identifier for an item attachment, or zero if no item is attached.
		/// </summary>
		public int ItemAttachmentTemplateID { get; set; }
		/// <summary>
		/// Randomization seed for the attached item's properties.
		/// </summary>
		public int ItemAttachmentSeed { get; set; }
		/// <summary>
		/// Quantity of the attached item.
		/// </summary>
		public uint ItemAttachmentAmount { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}