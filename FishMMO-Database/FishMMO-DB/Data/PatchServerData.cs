using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Patch server registration data transfer object.
	/// </summary>
	public struct PatchServerData
	{
		public long ID { get; set; }
		public string Address { get; set; }
		public ushort Port { get; set; }
		public DateTime LastPulse { get; set; }
	}
}