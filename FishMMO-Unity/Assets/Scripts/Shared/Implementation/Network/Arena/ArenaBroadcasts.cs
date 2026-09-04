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
		/// <summary>Everyone is in; the start timer is running.</summary>
		Countdown = 1,
		/// <summary>Play.</summary>
		Live = 2,
		/// <summary>Over. The results screen is showing.</summary>
		Ended = 3,
		/// <summary>Abandoned before play.</summary>
		Cancelled = 4,
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
		/// <summary>Flag stand: 1 when carried. Control point: capture progress so far.</summary>
		public int Progress;
		/// <summary>Flag stand: the carrier, or 0 when home. Control point: the team whose capture is in progress, or -1, as a long.</summary>
		public long Holder;
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
	}
}
