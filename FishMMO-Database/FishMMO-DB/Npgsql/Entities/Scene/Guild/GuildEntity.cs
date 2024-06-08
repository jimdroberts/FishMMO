using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("guilds", Schema = "fish_mmo_postgresql")]
	[Index(nameof(Name))]
	public class GuildEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public string Name { get; set; }
		public string Notice { get; set; }

		public List<CharacterGuildEntity> Characters { get; set; }
	}
}