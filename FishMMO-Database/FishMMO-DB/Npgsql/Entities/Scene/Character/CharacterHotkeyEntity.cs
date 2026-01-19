using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_hotkeys")]
	public class CharacterHotkeyEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public byte Type { get; set; }
		public int Slot { get; set; }
		public long ReferenceID { get; set; }
	}
}