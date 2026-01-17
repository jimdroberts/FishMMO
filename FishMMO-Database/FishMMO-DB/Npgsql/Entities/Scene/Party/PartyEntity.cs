using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("parties")]
	public class PartyEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public DateTime TimeCreated { get; set; }
		public List<CharacterPartyEntity> Characters { get; set; }
	}
}