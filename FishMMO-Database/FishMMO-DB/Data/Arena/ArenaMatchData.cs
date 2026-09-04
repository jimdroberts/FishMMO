using System;

namespace FishMMO.Database.Data
{
	/// <summary>Arena match data transfer object.</summary>
	public struct ArenaMatchData
	{
		public readonly long ID;
		public readonly long WorldServerID;
		public readonly long InstanceID;
		public readonly string SceneName;
		public readonly int TemplateID;
		public readonly int Format;
		public readonly int TeamCount;
		public readonly int TeamSize;
		public readonly int Status;
		public readonly int WinnerTeam;
		public readonly bool Ranked;
		public readonly long SeasonID;
		public readonly DateTime? BackfillUntilUtc;
		public readonly DateTime TimeCreated;
		public readonly DateTime? TimeStarted;
		public readonly DateTime? TimeEnded;

		public ArenaMatchData(long id, long worldServerID, long instanceID, string sceneName, int templateID, int format, int teamCount, int teamSize, int status, int winnerTeam, DateTime timeCreated, DateTime? timeStarted, DateTime? timeEnded, bool ranked = false, long seasonID = 0, DateTime? backfillUntilUtc = null)
		{
			Ranked = ranked;
			SeasonID = seasonID;
			BackfillUntilUtc = backfillUntilUtc;
			ID = id;
			WorldServerID = worldServerID;
			InstanceID = instanceID;
			SceneName = sceneName;
			TemplateID = templateID;
			Format = format;
			TeamCount = teamCount;
			TeamSize = teamSize;
			Status = status;
			WinnerTeam = winnerTeam;
			TimeCreated = timeCreated;
			TimeStarted = timeStarted;
			TimeEnded = timeEnded;
		}
	}

	/// <summary>Arena match seat data transfer object.</summary>
	public struct ArenaMatchMemberData
	{
		public readonly long ID;
		public readonly long MatchID;
		public readonly long CharacterID;
		public readonly int Team;
		public readonly int Kills;
		public readonly int Deaths;
		public readonly int Score;
		public readonly int Status;
		public readonly int RatingDelta;

		public ArenaMatchMemberData(long id, long matchID, long characterID, int team, int kills, int deaths, int score, int status = 0, int ratingDelta = 0)
		{
			Status = status;
			RatingDelta = ratingDelta;
			ID = id;
			MatchID = matchID;
			CharacterID = characterID;
			Team = team;
			Kills = kills;
			Deaths = deaths;
			Score = score;
		}
	}

	/// <summary>One line of a character's match history: the match and their own seat in it.</summary>
	public struct ArenaHistoryData
	{
		public readonly ArenaMatchData Match;
		public readonly ArenaMatchMemberData Seat;

		public ArenaHistoryData(ArenaMatchData match, ArenaMatchMemberData seat)
		{
			Match = match;
			Seat = seat;
		}
	}

	/// <summary>A ranked season.</summary>
	public struct ArenaSeasonData
	{
		public readonly long ID;
		public readonly string Name;
		public readonly DateTime StartsUtc;
		public readonly DateTime? EndsUtc;
		public readonly bool Active;

		public ArenaSeasonData(long id, string name, DateTime startsUtc, DateTime? endsUtc, bool active)
		{
			ID = id;
			Name = name;
			StartsUtc = startsUtc;
			EndsUtc = endsUtc;
			Active = active;
		}
	}

	/// <summary>One character's rating in one season.</summary>
	public struct ArenaRatingData
	{
		public readonly long SeasonID;
		public readonly long CharacterID;
		public readonly int Rating;
		public readonly int PeakRating;
		public readonly int Games;
		public readonly int Wins;
		public readonly int Losses;

		public ArenaRatingData(long seasonID, long characterID, int rating, int peakRating, int games, int wins, int losses)
		{
			SeasonID = seasonID;
			CharacterID = characterID;
			Rating = rating;
			PeakRating = peakRating;
			Games = games;
			Wins = wins;
			Losses = losses;
		}
	}

	/// <summary>A queue lock.</summary>
	public struct ArenaPenaltyData
	{
		public readonly long CharacterID;
		public readonly DateTime LockedUntilUtc;
		public readonly string Reason;

		public ArenaPenaltyData(long characterID, DateTime lockedUntilUtc, string reason)
		{
			CharacterID = characterID;
			LockedUntilUtc = lockedUntilUtc;
			Reason = reason;
		}
	}

	/// <summary>The result of filling a vacated seat from the queue.</summary>
	public struct ArenaBackfillData
	{
		public readonly bool Filled;
		public readonly long MatchID;
		public readonly long InstanceID;
		public readonly long CharacterID;
		public readonly int Team;

		public static readonly ArenaBackfillData None = new ArenaBackfillData(false, 0, 0, 0, -1);

		public ArenaBackfillData(bool filled, long matchID, long instanceID, long characterID, int team)
		{
			Filled = filled;
			MatchID = matchID;
			InstanceID = instanceID;
			CharacterID = characterID;
			Team = team;
		}
	}

	/// <summary>
	/// The result of the group finder forming an arena match: the instance opened for it, the
	/// match row, and every seat.
	/// </summary>
	public struct ArenaMatchFormedData
	{
		/// <summary>True when a match was formed and the other fields are meaningful.</summary>
		public readonly bool Formed;
		public readonly long MatchID;
		public readonly long InstanceID;
		public readonly System.Collections.Generic.IReadOnlyList<ArenaSeat> Seats;

		public static readonly ArenaMatchFormedData None = new ArenaMatchFormedData(false, 0, 0, Array.Empty<ArenaSeat>());

		public ArenaMatchFormedData(bool formed, long matchID, long instanceID, System.Collections.Generic.IReadOnlyList<ArenaSeat> seats)
		{
			Formed = formed;
			MatchID = matchID;
			InstanceID = instanceID;
			Seats = seats ?? Array.Empty<ArenaSeat>();
		}
	}
}
