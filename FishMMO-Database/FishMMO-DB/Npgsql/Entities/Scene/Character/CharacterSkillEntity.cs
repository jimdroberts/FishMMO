using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_skills", Schema = "fish_mmo_postgresql")]
	[Index(nameof(CharacterID))]
	[Index(nameof(CharacterID), nameof(Hash), IsUnique = true)]
	public class CharacterSkillEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int Hash { get; set; }
		public int Level { get; set; }
		public float CastTimeEnd { get; set; }
		public float CooldownEnd { get; set; }
	}
}