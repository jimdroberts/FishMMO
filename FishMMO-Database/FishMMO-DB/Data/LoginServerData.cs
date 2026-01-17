using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Login server registration data transfer object.
	/// </summary>
	public struct LoginServerData
	{
		public long ID { get; set; }
		public string Name { get; set; }
		public DateTime LastPulse { get; set; }
		public string Address { get; set; }
		public ushort Port { get; set; }
	}
}