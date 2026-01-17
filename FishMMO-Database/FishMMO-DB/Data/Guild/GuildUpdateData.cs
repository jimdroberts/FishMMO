using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Guild update data transfer object.
	/// </summary>
	public struct GuildUpdateData
	{
		public long ID { get; set; }
		public long GuildID { get; set; }
		public DateTime LastUpdate { get; set; }
	}
}