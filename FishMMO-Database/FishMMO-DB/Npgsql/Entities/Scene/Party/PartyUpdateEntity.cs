using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Tracks the last update timestamp per party for staleness detection and polling purposes.</summary>
	public class PartyUpdateEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Party ID this update entry belongs to.</summary>
		public long PartyID { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
		/// <summary>Timestamp of the most recent party update (UTC).</summary>
		public DateTime LastUpdate { get; set; }
	}
}