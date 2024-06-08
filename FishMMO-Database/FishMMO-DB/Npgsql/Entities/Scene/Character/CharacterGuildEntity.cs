using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_guild", Schema = "fish_mmo_postgresql")]
	// add index on guild to avoid full scans when loading guild members
	[Index(nameof(CharacterID))]
	[Index(nameof(GuildID))]
	[Index(nameof(CharacterID), nameof(GuildID), IsUnique = true)]
	public class CharacterGuildEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public long GuildID { get; set; }
		public GuildEntity Guild { get; set; }
		public byte Rank { get; set; }
		public string Location { get; set; }
	}
}