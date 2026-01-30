using System;

namespace FishMMO.Database.Npgsql.Entities
{
	public class CharacterItemCooldownEntity : IVersionedEntity
	{
		public long ID { get; set; }
		public long Version { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public string Category { get; set; }
		public float CooldownEnd { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }

	}
}