using System;

namespace FishMMO.Database.Npgsql.Entities
{
	public class KickRequestEntity
	{
		public long ID { get; set; }
		public string AccountName { get; set; }
		public DateTime TimeCreated { get; set; }
	}
}