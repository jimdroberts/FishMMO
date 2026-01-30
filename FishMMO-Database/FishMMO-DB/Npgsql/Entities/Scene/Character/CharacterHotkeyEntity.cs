using System;

namespace FishMMO.Database.Npgsql.Entities
{
	public class CharacterHotkeyEntity : IVersionedEntity
	{
		public long ID { get; set; }
		public long Version { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public byte Type { get; set; }
		public int Slot { get; set; }
		public long ReferenceID { get; set; }
		public DateTime TimeCreated { get; set; }
		public bool Deleted { get; set; }
		public DateTime? TimeDeleted { get; set; }
	}
}