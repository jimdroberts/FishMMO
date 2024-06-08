using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("guild_updates", Schema = "fish_mmo_postgresql")]
	[Index(nameof(GuildID))]
	public class GuildUpdateEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long GuildID { get; set; }
		public DateTime TimeCreated { get; set; }
	}
}