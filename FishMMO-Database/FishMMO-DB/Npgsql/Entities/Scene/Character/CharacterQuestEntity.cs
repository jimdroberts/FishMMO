using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_quests", Schema = "fish_mmo_postgresql")]
	[Index(nameof(CharacterID))]
	[Index(nameof(CharacterID), nameof(Name), IsUnique = true)]
	public class CharacterQuestEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public string Name { get; set; }
		public int Progress { get; set; }
		public bool Completed { get; set; }
		// PRIMARY KEY (character, name) is created manually.
	}
}