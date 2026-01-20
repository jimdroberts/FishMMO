using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_pet_attributes")]
	public class CharacterPetAttributeEntity
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int TemplateID { get; set; }
		public int Value { get; set; }
		public float CurrentValue { get; set; }
	}
}