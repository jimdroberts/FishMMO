using System;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	// What the core sends or reads of arenas: an arena board opening (ECA action), an arena respawn (death
	// dialog) and the arena event kinds (arena templates' triggers). The rest are the Matchmaking module's (MatchmakingBroadcasts / ArenaBroadcasts there).

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
}
