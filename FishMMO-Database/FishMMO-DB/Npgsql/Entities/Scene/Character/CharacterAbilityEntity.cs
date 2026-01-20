using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_abilities")]
	public class CharacterAbilityEntity
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int TemplateID { get; set; }
		public List<int> AbilityEvents { get; set; }
		public float Cooldown { get; set; }
	}
}