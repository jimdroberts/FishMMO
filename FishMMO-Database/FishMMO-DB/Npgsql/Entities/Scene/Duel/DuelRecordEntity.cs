using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One character's duel record: an Elo-style rating and the wins, losses and draws behind it (the Duels module).
	/// </summary>
	/// <remarks>
	/// A row is inserted by a character's first finished duel; a character who has never duelled has none and is not on
	/// the duel boards. The rating never resets.
	/// </remarks>
	public class DuelRecordEntity
	{
		/// <summary>The character, and the primary key: one record per character.</summary>
		public long CharacterID { get; set; }
		/// <summary>Current rating.</summary>
		public int Rating { get; set; }
		/// <summary>Highest rating reached.</summary>
		public int PeakRating { get; set; }
		/// <summary>Duels won.</summary>
		public int Wins { get; set; }
		/// <summary>Duels lost, forfeits included.</summary>
		public int Losses { get; set; }
		/// <summary>Duels that ran out of time.</summary>
		public int Draws { get; set; }
		/// <summary>Last time the row moved (UTC).</summary>
		public DateTime LastUpdated { get; set; }
	}
}
