using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Tracks the last update timestamp per guild for staleness detection and polling purposes.</summary>
	public class GuildUpdateEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Guild ID this update entry belongs to.</summary>
		public long GuildID { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
		/// <summary>Timestamp of the most recent guild update (UTC).</summary>
		public DateTime LastUpdate { get; set; }
	}
}