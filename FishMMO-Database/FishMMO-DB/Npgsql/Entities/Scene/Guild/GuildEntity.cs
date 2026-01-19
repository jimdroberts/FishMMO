using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("guilds")]
	public class GuildEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public string Name { get; set; }
		public string Notice { get; set; }
		public DateTime TimeCreated { get; set; }

		public List<CharacterGuildEntity> Characters { get; set; }
	}
}