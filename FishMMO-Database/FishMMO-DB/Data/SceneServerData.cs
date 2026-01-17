using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Scene server registration data transfer object.
	/// </summary>
	public struct SceneServerData
	{
		public long ID { get; set; }
		public string Name { get; set; }
		public DateTime LastPulse { get; set; }
		public string Address { get; set; }
		public ushort Port { get; set; }
		public int CharacterCount { get; set; }
		public bool Locked { get; set; }
	}
}