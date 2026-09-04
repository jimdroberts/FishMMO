using System;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server → Client broadcast opening the arena board for one board object.
	/// </summary>
	/// <remarks>
	/// Names the board and the arenas it offers by template ID. Everything else — names, modes,
	/// formats, artwork — is resolved client-side from the template cache, exactly as the dungeon
	/// finder resolves its dungeon. Nothing is armed server-side by this message.
	/// </remarks>
	public struct ArenaBoardBroadcast : IBroadcast
	{
		/// <summary>ID of the arena board scene object.</summary>
		public long InteractableID;

		/// <summary>Arenas this board offers.</summary>
		public int[] ArenaTemplateIDs;
	}

	/// <summary>
	/// Client → Server broadcast asking to queue for an arena at one format.
	/// </summary>
	/// <remarks>
	/// Validated like a dungeon finder request: the player must be at the board, and must stay at
	/// it while queued. With <see cref="AsParty"/> the player's whole party queues together — every
	/// member must be at the same board, and the party must fit on one team of the format.
	/// </remarks>
	public struct ArenaQueueBroadcast : IBroadcast
	{
		/// <summary>ID of the arena board scene object.</summary>
		public long InteractableID;

		/// <summary>Arena to queue for. Must be one the board offers.</summary>
		public int ArenaTemplateID;

		/// <summary>Format index into the arena's own list.</summary>
		public int Format;

		/// <summary>True to queue the whole party as a pre-made group.</summary>
		public bool AsParty;
	}

	/// <summary>
	/// Where a match is, as the client sees it.
	/// </summary>
	public enum ArenaMatchPhase : byte
	{
		/// <summary>Waiting for every seat to arrive.</summary>
		Gathering = 0,
		/// <summary>Everyone is in; each player is asked to accept before the timer runs.</summary>
		ReadyCheck = 1,
		/// <summary>Everyone accepted; the start timer is running.</summary>
		Countdown = 2,
		/// <summary>Play.</summary>
		Live = 3,
		/// <summary>Over. The results screen is showing.</summary>
		Ended = 4,
		/// <summary>Abandoned before play.</summary>
		Cancelled = 5,
	}

	/// <summary>
	/// One seat as the client sees it.
	/// </summary>
	[Serializable]
	public struct ArenaMemberEntry
	{
		public long CharacterID;
		public int Team;
		public int Kills;
		public int Deaths;
		public int Score;
		/// <summary>True once the player has arrived in the arena.</summary>
		public bool Present;
		/// <summary>Ready check: true once this seat accepted.</summary>
		public bool Ready;
		/// <summary>Live: true while this seat's player is disconnected inside the reconnect grace.</summary>
		public bool Reconnecting;
	}

	/// <summary>
	/// One objective as the client sees it.
	/// </summary>
	[Serializable]
	public struct ArenaObjectiveEntry
	{
		/// <summary>Scene object id of the flag stand or control point.</summary>
		public long ObjectiveID;
		/// <summary>Flag stand or control point.</summary>
		public FishMMO.Shared.Core.ArenaObjectiveKind Kind;
		/// <summary>Flag stand: the flag's team. Control point: the owner, or -1 for neutral.</summary>
		public int Team;
		/// <summary>Flag stand: 0 home, 1 carried, 2 dropped (see <see cref="ArenaFlagState"/>). Control point: capture progress so far.</summary>
		public int Progress;
		/// <summary>Flag stand: the carrier, or 0 when not carried. Control point: the team whose capture is in progress, or -1, as a long.</summary>
		public long Holder;
		/// <summary>Flag stand: where a dropped flag lies. Zero otherwise.</summary>
		public UnityEngine.Vector3 Position;
	}

	/// <summary>
	/// Server → Client broadcast to everyone in an arena instance with the match's current state.
	/// </summary>
	/// <remarks>
	/// Sent on every phase change, every score change, every second of the countdown, and once a
	/// second while live for the clock. The roster is what the client publishes to its own
	/// <see cref="ArenaTeamRegistry"/>, so predicted targeting agrees with the server about who
	/// may be hit.
	/// </remarks>
	public struct ArenaMatchStateBroadcast : IBroadcast
	{
		public long MatchID;
		public int ArenaTemplateID;
		public int Format;
		public ArenaMatchPhase Phase;

		/// <summary>Countdown seconds left while counting down; match seconds left while live; 0 otherwise.</summary>
		public int SecondsRemaining;

		/// <summary>Score per team.</summary>
		public int[] TeamScores;

		/// <summary>Every seat.</summary>
		public ArenaMemberEntry[] Members;

		/// <summary>Every objective in the arena, for modes that have them. Empty for deathmatch.</summary>
		public ArenaObjectiveEntry[] Objectives;
	}

	/// <summary>
	/// Server → Client broadcast with the final result, sent to everyone who was in the match.
	/// </summary>
	public struct ArenaResultsBroadcast : IBroadcast
	{
		public long MatchID;
		public int ArenaTemplateID;
		public int Format;

		/// <summary>Winning team, or -1 for a draw.</summary>
		public int WinnerTeam;

		/// <summary>Score per team.</summary>
		public int[] TeamScores;

		/// <summary>Every seat, already ordered by score for the pedestal.</summary>
		public ArenaMemberEntry[] Placements;

		/// <summary>The recipient's team.</summary>
		public int YourTeam;

		/// <summary>The recipient's PvP rank change.</summary>
		public int RankDelta;

		/// <summary>Whether the match moved season ratings.</summary>
		public bool Ranked;

		/// <summary>The recipient's season rating change. 0 for an unranked match.</summary>
		public int RatingDelta;

		/// <summary>The recipient's season rating after the match. 0 for an unranked match.</summary>
		public int NewRating;

		/// <summary>Placement games the recipient still has to play before their rating shows. 0 once placed or unranked.</summary>
		public int PlacementGamesRemaining;

		/// <summary>Seconds until everyone is returned to the world.</summary>
		public int SecondsUntilReturn;
	}

	/// <summary>
	/// Server → Client broadcast to a dead arena player: how long until they respawn.
	/// </summary>
	/// <remarks>
	/// The death dialog shows this instead of its respawn choices: inside a live arena the server
	/// respawns the player itself, at their team's spawn, and a bind-point respawn would leave the
	/// match.
	/// </remarks>
	public struct ArenaRespawnBroadcast : IBroadcast
	{
		/// <summary>Seconds until the server respawns the player. 0 means no respawn this match.</summary>
		public int SecondsUntilRespawn;

		/// <summary>Who killed them, or 0 when nobody did (environment, or unknown).</summary>
		public long KillerID;

		/// <summary>The killer's name, so the recap can show it even when the killer is culled from view.</summary>
		public string KillerName;

		/// <summary>The killer's team, or -1.</summary>
		public int KillerTeam;
	}

	/// <summary>
	/// Server → Client broadcast starting or updating a ready check.
	/// </summary>
	/// <remarks>
	/// Sent to every seat when everyone has arrived, then again whenever an answer lands. The
	/// client shows Accept and Decline until it has answered; afterwards it shows the count.
	/// </remarks>
	public struct ArenaReadyCheckBroadcast : IBroadcast
	{
		public long MatchID;
		/// <summary>Seconds left to answer.</summary>
		public int SecondsRemaining;
		/// <summary>Seats that accepted so far.</summary>
		public int Accepted;
		/// <summary>Seats being asked.</summary>
		public int Total;
		/// <summary>Whether the recipient has already answered.</summary>
		public bool YouAnswered;
	}

	/// <summary>
	/// Client → Server answer to a ready check.
	/// </summary>
	public struct ArenaReadyResponseBroadcast : IBroadcast
	{
		public long MatchID;
		public bool Accept;
	}

	/// <summary>
	/// A moment of play worth announcing.
	/// </summary>
	public enum ArenaEventKind : byte
	{
		/// <summary>Actor killed Target.</summary>
		Kill = 0,
		/// <summary>The first kill of the match.</summary>
		FirstBlood = 1,
		/// <summary>Actor reached a streak of <c>Value</c> kills without dying.</summary>
		KillingSpree = 2,
		/// <summary>Actor ended Target's streak of <c>Value</c>.</summary>
		SpreeEnded = 3,
		/// <summary>Actor took team <c>Team</c>'s flag.</summary>
		FlagTaken = 4,
		/// <summary>Actor dropped the flag they carried (died or left).</summary>
		FlagDropped = 5,
		/// <summary>Actor returned their own team's dropped flag.</summary>
		FlagReturned = 6,
		/// <summary>Actor captured a flag for <c>Team</c>.</summary>
		FlagCaptured = 7,
		/// <summary>Actor captured a control point for <c>Team</c>.</summary>
		PointCaptured = 8,
		/// <summary>Target disconnected; <c>Value</c> seconds to reconnect.</summary>
		PlayerDisconnected = 9,
		/// <summary>Target came back inside the grace.</summary>
		PlayerReconnected = 10,
		/// <summary>Target left for good and forfeited.</summary>
		PlayerForfeited = 11,
		/// <summary>Target joined a vacated seat from the queue.</summary>
		PlayerBackfilled = 12,
		/// <summary>Team <c>Team</c> is <c>Value</c> points from the score limit.</summary>
		NearScoreLimit = 13,
		/// <summary><c>Value</c> seconds left on the clock.</summary>
		TimeWarning = 14,
	}

	/// <summary>
	/// Server → Client broadcast of one announceable moment, to everyone in the arena.
	/// </summary>
	/// <remarks>
	/// Feeds the kill feed and the announcer. Names travel with the event because the actor or
	/// target may be culled from the recipient's view, or already gone.
	/// </remarks>
	public struct ArenaEventBroadcast : IBroadcast
	{
		public long MatchID;
		public ArenaEventKind Kind;
		public long ActorID;
		public string ActorName;
		public int ActorTeam;
		public long TargetID;
		public string TargetName;
		public int TargetTeam;
		/// <summary>Team the event concerns, where the kind says so; else -1.</summary>
		public int Team;
		/// <summary>Streak length, seconds, or points, where the kind says so.</summary>
		public int Value;
	}

	/// <summary>
	/// Client → Server request for the recipient's arena profile at a board: season, rating, lock.
	/// </summary>
	public struct ArenaProfileRequestBroadcast : IBroadcast
	{
		public long InteractableID;
	}

	/// <summary>
	/// Server → Client the recipient's own standing in the current season.
	/// </summary>
	public struct ArenaProfileBroadcast : IBroadcast
	{
		public long SeasonID;
		public string SeasonName;
		/// <summary>Season rating, or the default when unplayed.</summary>
		public int Rating;
		public int PeakRating;
		public int Games;
		public int Wins;
		public int Losses;
		/// <summary>Placement games still to play before the rating is shown as final.</summary>
		public int PlacementGamesRemaining;
		/// <summary>Seconds the recipient is locked out of the arena queue, or 0.</summary>
		public int QueueLockSeconds;
		/// <summary>Why they are locked, or empty.</summary>
		public string QueueLockReason;
	}

	/// <summary>
	/// Client → Server request for the recipient's recent matches.
	/// </summary>
	public struct ArenaHistoryRequestBroadcast : IBroadcast
	{
		public long InteractableID;
	}

	/// <summary>One finished match as the history shows it.</summary>
	[Serializable]
	public struct ArenaHistoryEntry
	{
		public long MatchID;
		public int ArenaTemplateID;
		public int Format;
		public bool Ranked;
		/// <summary>The recipient's team.</summary>
		public int Team;
		/// <summary>Winning team, or -1 for a draw.</summary>
		public int WinnerTeam;
		public int Kills;
		public int Deaths;
		public int Score;
		public int RatingDelta;
		/// <summary>True when the recipient left before the end.</summary>
		public bool Deserted;
		/// <summary>When it ended, as Unix seconds UTC.</summary>
		public long EndedUnix;
	}

	/// <summary>
	/// Server → Client the recipient's recent matches, newest first.
	/// </summary>
	public struct ArenaHistoryBroadcast : IBroadcast
	{
		public ArenaHistoryEntry[] Entries;
	}

	/// <summary>
	/// Client → Server request for the season leaderboard.
	/// </summary>
	public struct ArenaLeaderboardRequestBroadcast : IBroadcast
	{
		public long InteractableID;
	}

	/// <summary>One row of the leaderboard.</summary>
	[Serializable]
	public struct ArenaLeaderboardEntry
	{
		public long CharacterID;
		public string CharacterName;
		public int Rating;
		public int Wins;
		public int Losses;
	}

	/// <summary>
	/// Server → Client the season leaderboard.
	/// </summary>
	public struct ArenaLeaderboardBroadcast : IBroadcast
	{
		public long SeasonID;
		public string SeasonName;
		public ArenaLeaderboardEntry[] Entries;
		/// <summary>The recipient's own rank on the full board, or 0 when unranked.</summary>
		public int YourRank;
	}
}
