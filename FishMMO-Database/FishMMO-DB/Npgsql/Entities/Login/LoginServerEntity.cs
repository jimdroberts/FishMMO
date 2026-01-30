using System;

namespace FishMMO.Database.Npgsql.Entities
{
	public class LoginServerEntity
	{
		public long ID { get; set; }
		public string Name { get; set; }
		public DateTime TimeCreated { get; set; }
		public DateTime LastPulse { get; set; }
		public string Address { get; set; }
		public ushort Port { get; set; }
	}
}