using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Party update data transfer object.
	/// </summary>
	public struct PartyUpdateData
	{
		public long ID { get; set; }
		public long PartyID { get; set; }
		public DateTime LastUpdate { get; set; }
	}
}