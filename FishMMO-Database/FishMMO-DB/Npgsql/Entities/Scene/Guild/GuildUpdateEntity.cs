using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("guild_updates")]
	[Index(nameof(GuildID), IsUnique = true)]
	public class GuildUpdateEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long GuildID { get; set; }
		public DateTime LastUpdate { get; set; }
	}
}