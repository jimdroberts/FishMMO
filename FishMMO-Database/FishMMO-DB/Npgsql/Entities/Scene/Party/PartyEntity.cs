using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("parties", Schema = "fish_mmo_postgresql")]
	public class PartyEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public List<CharacterPartyEntity> Characters { get; set; }
	}
}