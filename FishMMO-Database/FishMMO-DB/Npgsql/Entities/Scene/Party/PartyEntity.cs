using System;
using System.Collections.Generic;

namespace FishMMO.Database.Npgsql.Entities
{
	public class PartyEntity
	{
		public long ID { get; set; }
		public DateTime TimeCreated { get; set; }
		public List<CharacterPartyEntity> Characters { get; set; }
	}
}