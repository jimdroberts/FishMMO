using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One character's ranked arena rating for one season.
	/// </summary>
	/// <remarks>
	/// Separate from the PvP Rank attribute on purpose: the attribute is a lifetime badge that only
	/// ever goes up on wins, while this is an Elo-style number that seasons reset and losses lower.
	/// </remarks>
	public class ArenaRatingEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Season the rating belongs to.</summary>
		public long SeasonID { get; set; }
		/// <summary>The character.</summary>
		public long CharacterID { get; set; }
		/// <summary>Current rating.</summary>
		public int Rating { get; set; }
		/// <summary>Highest rating reached this season.</summary>
		public int PeakRating { get; set; }
		/// <summary>Ranked matches played to a result this season.</summary>
		public int Games { get; set; }
		/// <summary>Ranked wins this season.</summary>
		public int Wins { get; set; }
		/// <summary>Ranked losses this season.</summary>
		public int Losses { get; set; }
		/// <summary>Last time the row moved (UTC).</summary>
		public DateTime LastUpdated { get; set; }
	}
}
