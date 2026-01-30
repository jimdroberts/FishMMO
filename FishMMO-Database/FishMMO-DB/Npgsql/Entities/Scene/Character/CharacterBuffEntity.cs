using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_buffs")]
	public class CharacterBuffEntity : IVersionedEntity
	{
		public long ID { get; set; }
		public long Version { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int TemplateID { get; set; }
		public float RemainingTime { get; set; }
		public float TickTime { get; set; }
		public int Stacks { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}