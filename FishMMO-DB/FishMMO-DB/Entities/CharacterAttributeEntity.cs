using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO_DB.Entities
{
	[Table("character_attributes", Schema = "fish_mmo_postgresql")]
	[Index(nameof(CharacterId))]
	public class CharacterAttributeEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long Id { get; set; }
		public long CharacterId { get; set; }
		public CharacterEntity Character { get; set; }
		public int TemplateID { get; set; }
		public int BaseValue { get; set; }
		public int Modifier { get; set; }
		public int CurrentValue { get; set; }
	}
}