using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_guild")]
	public class CharacterGuildEntity
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public long GuildID { get; set; }
		public GuildEntity Guild { get; set; }
		public byte Rank { get; set; }
		public string Location { get; set; }
	}
}