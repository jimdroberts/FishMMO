using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_knownabilities")]
	[Index(nameof(CharacterID))]
	[Index(nameof(CharacterID), nameof(TemplateID), IsUnique = true)]
	public class CharacterKnownAbilityEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int TemplateID { get; set; }
	}
}