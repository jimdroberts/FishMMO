using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_mail", Schema = "fish_mmo_postgresql")]
	[Index(nameof(CharacterID))]
	public class CharacterMailEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long SenderCharacterID { get; set; }
		public long CharacterID { get; set; }
		public string Subject { get; set; }
		public string Message { get; set; }
		public int ItemAttachmentTemplateID { get; set; }
		public int ItemAttachmentSeed { get; set; }
		public uint ItemAttachmentAmount { get; set; }
	}
}