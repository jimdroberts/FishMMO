using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_party")]
	// add index on party to avoid full scans when loading guild members
	public class CharacterPartyEntity
	{
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public long PartyID { get; set; }
		public PartyEntity Party { get; set; }
		public byte Rank { get; set; }
		public float HealthPCT { get; set; }
	}
}