using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_pet_buffs")]
	public class CharacterPetBuffEntity
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int TemplateID { get; set; }
		public float RemainingTime { get; set; }
		public float TickTime { get; set; }
		public int Stacks { get; set; }
	}
}