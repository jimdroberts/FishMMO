using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// A PvP season: the window over which ranked arena ratings accumulate and the season board stands.
	/// </summary>
	/// <remarks>
	/// Exactly one season is active at a time. Ratings are keyed by season, so a new season starts
	/// everybody at the placement stage again while history keeps the old numbers. Every season ever
	/// played stays a row: ending one only closes it, so its ratings and its place in the count are
	/// never lost.
	/// </remarks>
	public class ArenaSeasonEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>The season's place in the count, from 1. Unique; the next season is one more than the highest.</summary>
		public int Number { get; set; }
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
