using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>Tracks the last update timestamp per plot, so scene servers can poll for changes.</summary>
	/// <remarks>
	/// The same shape guilds use. A plot is visible from every scene server hosting a channel of its
	/// scene, and the one that processes a claim is not the one that has to redraw it elsewhere;
	/// this table is what the others read to notice.
	/// </remarks>
	public class PlotUpdateEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Plot this update entry belongs to.</summary>
		public long PlotID { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
		/// <summary>Timestamp of the most recent plot update (UTC).</summary>
		public DateTime LastUpdate { get; set; }
	}
}
