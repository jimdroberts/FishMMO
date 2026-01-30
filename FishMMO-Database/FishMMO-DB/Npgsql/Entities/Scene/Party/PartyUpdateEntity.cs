using System;

namespace FishMMO.Database.Npgsql.Entities
{
	public class PartyUpdateEntity
	{
		public long ID { get; set; }
		public long PartyID { get; set; }
		public DateTime TimeCreated { get; set; }
		public DateTime LastUpdate { get; set; }
	}
}