using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_achievements", Schema = "fish_mmo_postgresql")]
	[Index(nameof(CharacterID))]
	public class CharacterAchievementEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int TemplateID { get; set; }
		public byte Tier { get; set; }
		public uint Value { get; set; }
	}
}