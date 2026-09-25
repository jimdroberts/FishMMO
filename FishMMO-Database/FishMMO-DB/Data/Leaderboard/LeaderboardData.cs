using System;
using System.Collections.Generic;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Which board to read: the source column, which template or season it is filtered to, and
	/// who is eligible to appear on it.
	/// </summary>
	/// <remarks>
	/// A value type with value equality so a caller can key a cache on it directly: two queries
	/// that would produce the same SQL and the same parameters are equal.
	/// </remarks>
	public readonly struct LeaderboardQuery : IEquatable<LeaderboardQuery>
	{
		/// <summary>The stored number the board ranks by.</summary>
		public readonly LeaderboardSourceKind Source;

		/// <summary>
		/// The attribute or achievement template id for those sources; ignored for the arena, whose
		/// season is always the active one.
		/// </summary>
		public readonly int TemplateID;

		/// <summary>
		/// Arena only: games a character must have finished this season to be ranked. Values below
		/// 1 are read as 1, so a row that was inserted and never scored cannot appear.
		/// </summary>
		public readonly int MinimumGames;

		/// <summary>
		/// When false, only characters on player accounts are ranked; game masters' and
		/// administrators' characters are left off. Banned accounts are never ranked.
		/// </summary>
		public readonly bool RankStaff;

		public LeaderboardQuery(LeaderboardSourceKind source, int templateID, int minimumGames, bool rankStaff)
		{
			Source = source;
			TemplateID = templateID;
			MinimumGames = minimumGames;
			RankStaff = rankStaff;
		}

		public bool Equals(LeaderboardQuery other)
		{
			return Source == other.Source &&
				TemplateID == other.TemplateID &&
				MinimumGames == other.MinimumGames &&
				RankStaff == other.RankStaff;
		}

		public override bool Equals(object obj) => obj is LeaderboardQuery other && Equals(other);

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = (int)Source;
				hash = (hash * 397) ^ TemplateID;
				hash = (hash * 397) ^ MinimumGames;
				hash = (hash * 397) ^ (RankStaff ? 1 : 0);
				return hash;
			}
		}

		public override string ToString() => $"{Source}:{TemplateID}:min{MinimumGames}:{(RankStaff ? "staff" : "players")}";
	}

	/// <summary>One ranked character on a board.</summary>
	public readonly struct LeaderboardRowData
	{
		/// <summary>
		/// Standard competition rank: one more than the number of eligible characters with a
		/// strictly higher score. Characters with equal scores share a rank, and the next rank
		/// skips accordingly (1, 2, 2, 4).
		/// </summary>
		public readonly int Rank;
		public readonly long CharacterID;
		public readonly string Name;
		/// <summary>The ranked number. A <c>long</c> because achievement values are stored as bigint.</summary>
		public readonly long Score;
		/// <summary>Arena only: season wins. Zero for other sources.</summary>
		public readonly int Wins;
		/// <summary>Arena only: season losses. Zero for other sources.</summary>
		public readonly int Losses;

		public LeaderboardRowData(int rank, long characterID, string name, long score, int wins, int losses)
		{
			Rank = rank;
			CharacterID = characterID;
			Name = name;
			Score = score;
			Wins = wins;
			Losses = losses;
		}
	}

	/// <summary>A contiguous slice of a board, with the board's size as read in the same call.</summary>
	public sealed class LeaderboardPageData
	{
		/// <summary>Arena only: the season the page was read from, or 0 when no season is active.</summary>
		public long SeasonID { get; }
		/// <summary>Arena only: that season's display name.</summary>
		public string SeasonName { get; }
		/// <summary>Every eligible character on the board, not just this page's.</summary>
		public int TotalRanked { get; }
		/// <summary>Zero-based position of the first row.</summary>
		public int Offset { get; }
		/// <summary>The rows, best first. Fewer than asked for at the end of the board.</summary>
		public IReadOnlyList<LeaderboardRowData> Rows { get; }
		/// <summary>When the database was read (UTC).</summary>
		public DateTime FetchedAtUtc { get; }

		public LeaderboardPageData(long seasonID, string seasonName, int totalRanked, int offset, IReadOnlyList<LeaderboardRowData> rows, DateTime fetchedAtUtc)
		{
			SeasonID = seasonID;
			SeasonName = seasonName ?? string.Empty;
			TotalRanked = totalRanked;
			Offset = offset;
			Rows = rows ?? Array.Empty<LeaderboardRowData>();
			FetchedAtUtc = fetchedAtUtc;
		}
	}

	/// <summary>One character's place on a board, wherever it falls.</summary>
	public readonly struct LeaderboardStandingData
	{
		/// <summary>False when the character is not on the board: no score, ineligible, or no active season.</summary>
		public readonly bool Ranked;
		/// <summary>Competition rank, as <see cref="LeaderboardRowData.Rank"/>. 0 when not ranked.</summary>
		public readonly int Rank;
		public readonly long Score;
		public readonly int Wins;
		public readonly int Losses;
		/// <summary>When the database was read (UTC).</summary>
		public readonly DateTime FetchedAtUtc;

		public LeaderboardStandingData(bool ranked, int rank, long score, int wins, int losses, DateTime fetchedAtUtc)
		{
			Ranked = ranked;
			Rank = rank;
			Score = score;
			Wins = wins;
			Losses = losses;
			FetchedAtUtc = fetchedAtUtc;
		}

		/// <summary>A character that is not on the board.</summary>
		public static LeaderboardStandingData Unranked(DateTime fetchedAtUtc) => new LeaderboardStandingData(false, 0, 0, 0, 0, fetchedAtUtc);
	}
}
