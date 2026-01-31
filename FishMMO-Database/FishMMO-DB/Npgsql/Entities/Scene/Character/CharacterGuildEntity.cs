using System;

namespace FishMMO.Database.Npgsql.Entities
{
	public class CharacterGuildEntity : IVersionedEntity
	{
		public long ID { get; set; }
		public long Version { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public long GuildID { get; set; }
		public GuildEntity Guild { get; set; }
		public byte Rank { get; set; }
		public string Location { get; set; }
		public DateTime TimeCreated { get; set; }
	}
}