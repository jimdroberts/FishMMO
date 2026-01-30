using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_attributes")]
	public class CharacterAttributeEntity : IVersionedEntity
	{
		public long ID { get; set; }
		public long Version { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int TemplateID { get; set; }
		public int Value { get; set; }
		public float CurrentValue { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}