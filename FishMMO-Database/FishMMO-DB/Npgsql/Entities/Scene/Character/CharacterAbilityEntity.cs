using System;
using System.Collections.Generic;

namespace FishMMO.Database.Npgsql.Entities
{
	public class CharacterAbilityEntity : IVersionedEntity
	{
		public long ID { get; set; }
		public long Version { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int TemplateID { get; set; }
		public List<int> AbilityEvents { get; set; }
		public float Cooldown { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}