using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_itemcooldowns", Schema = "fish_mmo_postgresql")]
	[Index(nameof(CharacterID))]
	[Index(nameof(CharacterID), nameof(Category), IsUnique = true)]
	public class CharacterItemCooldownEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public string Category { get; set; }
		public float CooldownEnd { get; set; }

	}
}