using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_quests")]
	public class CharacterQuestEntity : IVersionedEntity
	{
		public long ID { get; set; }
		public long Version { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public string Name { get; set; }
		public int Progress { get; set; }
		public bool Completed { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}