using System;

namespace FishMMO.Database.Npgsql.Entities
{
	public class LoginServerEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Login server display name.</summary>
		public string Name { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
		/// <summary>Most recent heartbeat / pulse timestamp (UTC) for liveness detection.</summary>
		public DateTime LastPulse { get; set; }
		/// <summary>Network address of the login server.</summary>
		public string Address { get; set; }
		/// <summary>Network port of the login server.</summary>
		public ushort Port { get; set; }
	}
}