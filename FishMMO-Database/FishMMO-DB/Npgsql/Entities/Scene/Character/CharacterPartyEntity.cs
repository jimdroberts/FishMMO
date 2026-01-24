using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("character_party")]
	public class CharacterPartyEntity
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }
		public long PartyID { get; set; }
		public PartyEntity Party { get; set; }
		public byte Rank { get; set; }
		public DateTime TimeCreated { get; set; }
		public float HealthPCT { get; set; }
	}
}