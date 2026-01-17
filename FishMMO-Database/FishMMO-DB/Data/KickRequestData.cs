using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Kick request data transfer object.
	/// </summary>
	public struct KickRequestData
	{
		public long ID { get; set; }
		public string AccountName { get; set; }
		public DateTime TimeCreated { get; set; }
	}
}