using System;

namespace FishMMO.Database.Npgsql.Entities
{
	public class CharacterSkillEntity : IVersionedEntity
	{
		public long ID { get; set; }
		public long Version { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int Hash { get; set; }
		public int Level { get; set; }
		public float CastTimeEnd { get; set; }
		public float CooldownEnd { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}