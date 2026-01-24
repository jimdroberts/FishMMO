using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("guild_updates")]
	public class GuildUpdateEntity
	{
		public long ID { get; set; }
		public long GuildID { get; set; }
		public DateTime TimeCreated { get; set; }
		public DateTime LastUpdate { get; set; }
	}
}