namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One player's seat in an arena match, and what they did in it.
	/// </summary>
	/// <remarks>
	/// The team assignment here is the authority for hostility inside the arena and for the
	/// one-instance-per-party rule: a character with a seat in a match that has not ended holds an
	/// instance as far as the dungeon finder is concerned.
	/// </remarks>
	public class ArenaMatchMemberEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>Match this seat belongs to.</summary>
		public long MatchID { get; set; }
		public ArenaMatchEntity Match { get; set; }

		/// <summary>The character.</summary>
		public long CharacterID { get; set; }

		/// <summary>Team index, 0-based.</summary>
		public int Team { get; set; }

		/// <summary>Kills credited in the match.</summary>
		public int Kills { get; set; }

		/// <summary>Deaths suffered in the match.</summary>
		public int Deaths { get; set; }

		/// <summary>Mode-specific score: kills in deathmatch, captures and holds in objective modes.</summary>
		public int Score { get; set; }

		/// <summary>Seat status: 0 seated, 1 vacated (left or never arrived) and open to backfill. See <c>ArenaSeatStatus</c>.</summary>
		public int Status { get; set; }

		/// <summary>Rating change written at the end of a ranked match, or 0.</summary>
		public int RatingDelta { get; set; }
	}
}
