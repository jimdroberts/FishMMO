using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// A ranked arena season: the window over which ratings accumulate and the leaderboard stands.
	/// </summary>
	/// <remarks>
	/// Exactly one season is active at a time. Ratings are keyed by season, so a new season starts
	/// everybody at the placement stage again while history keeps the old numbers.
	/// </remarks>
	public class ArenaSeasonEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Display name, e.g. "Season 1".</summary>
		public string Name { get; set; }
		/// <summary>When the season began (UTC).</summary>
		public DateTime StartsUtc { get; set; }
		/// <summary>When the season ends (UTC), or null while open-ended.</summary>
		public DateTime? EndsUtc { get; set; }
		/// <summary>Whether this is the season ratings are written to.</summary>
		public bool Active { get; set; }
		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
	}
}
