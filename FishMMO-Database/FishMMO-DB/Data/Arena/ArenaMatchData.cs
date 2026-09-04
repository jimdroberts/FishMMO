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
		public readonly DateTime TimeCreated;
		public readonly DateTime? TimeStarted;
		public readonly DateTime? TimeEnded;

		public ArenaMatchData(long id, long worldServerID, long instanceID, string sceneName, int templateID, int format, int teamCount, int teamSize, int status, int winnerTeam, DateTime timeCreated, DateTime? timeStarted, DateTime? timeEnded)
		{
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

		public ArenaMatchMemberData(long id, long matchID, long characterID, int team, int kills, int deaths, int score)
		{
			ID = id;
			MatchID = matchID;
			CharacterID = characterID;
			Team = team;
			Kills = kills;
			Deaths = deaths;
			Score = score;
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
