using System;

namespace FishMMO.Database.Npgsql.Entities
{
	public class CharacterBuffEntity : IVersionedEntity
	{
		public long ID { get; set; }
		public long Version { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public int TemplateID { get; set; }
		public double RemainingTime { get; set; }
		public double TickTime { get; set; }
		public int Stacks { get; set; }
		public int TickCount { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}