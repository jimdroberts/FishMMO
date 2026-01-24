using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_mail")]
	public class CharacterMailEntity
	{
		public long ID { get; set; }
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