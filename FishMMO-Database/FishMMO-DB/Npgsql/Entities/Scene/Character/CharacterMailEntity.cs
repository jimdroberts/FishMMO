using System;

namespace FishMMO.Database.Npgsql.Entities
{
	public class CharacterMailEntity : IVersionedEntity
	{
		public long ID { get; set; }
		public long Version { get; set; }
		public long SenderCharacterID { get; set; }
		public long CharacterID { get; set; }
		public string Subject { get; set; }
		public string Message { get; set; }
		public int ItemAttachmentTemplateID { get; set; }
		public int ItemAttachmentSeed { get; set; }
		public uint ItemAttachmentAmount { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}