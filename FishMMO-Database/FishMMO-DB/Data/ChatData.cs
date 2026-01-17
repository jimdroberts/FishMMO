using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Chat message data transfer object.
	/// </summary>
	public struct ChatData
	{
		public long ID { get; set; }
		public long CharacterID { get; set; }
		public long WorldServerID { get; set; }
		public long SceneServerID { get; set; }
		public byte Channel { get; set; }
		public string Message { get; set; }
		public DateTime ServerReceivedTime { get; set; }
		public DateTime TimeCreated { get; set; }
	}
}