using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_skills")]
	public class CharacterSkillEntity
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int Hash { get; set; }
		public int Level { get; set; }
		public float CastTimeEnd { get; set; }
		public float CooldownEnd { get; set; }
	}
}