using FishNet.Broadcast;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>The answer to a challenge: sent, or why it went nowhere.</summary>
	public enum DuelRequestFailure : byte
	{
		/// <summary>The challenge was delivered.</summary>
		Sent = 0,
		/// <summary>The target is not here, or cannot act right now.</summary>
		TargetUnavailable = 1,
		/// <summary>The target is already duelling or already has a challenge to answer.</summary>
		TargetBusy = 2,
		/// <summary>You are already duelling or already have a challenge out or to answer.</summary>
		SelfBusy = 3,
		/// <summary>The two of you are too far apart.</summary>
		OutOfRange = 4,
		/// <summary>The target declined.</summary>
		Declined = 5,
		/// <summary>The target did not answer in time.</summary>
		Expired = 6,
		/// <summary>Too many challenges too quickly, or the same player again too soon.</summary>
		Throttled = 7,
		/// <summary>You cannot act right now (dead, stunned, teleporting).</summary>
		CannotAct = 8,
		/// <summary>One of you is in combat.</summary>
		InCombat = 9,
		/// <summary>Duels are fought in the open world, not in instances or arenas.</summary>
		NotHere = 10,
		/// <summary>There is no level ground here for a duel.</summary>
		NoRoom = 11,
	}

	/// <summary>How a duel ended, from the receiver's side.</summary>
	public enum DuelOutcome : byte
	{
		/// <summary>You won.</summary>
		Won = 0,
		/// <summary>You lost.</summary>
		Lost = 1,
		/// <summary>Neither won: the time ran out, or both left the ground.</summary>
		Draw = 2,
		/// <summary>The duel was called off and counts for nothing (the server is shutting down).</summary>
		Cancelled = 3,
	}

	/// <summary>Why a duel ended.</summary>
	public enum DuelEndReason : byte
	{
		/// <summary>The loser was beaten to their last point of health.</summary>
		Defeated = 0,
		/// <summary>The loser yielded.</summary>
		Yielded = 1,
		/// <summary>The loser stepped out of the duel ground.</summary>
		LeftGround = 2,
		/// <summary>The loser left the world (disconnected, changed scene).</summary>
		Left = 3,
		/// <summary>The loser was killed by something else.</summary>
		Died = 4,
		/// <summary>The time limit ran out.</summary>
		TimeUp = 5,
		/// <summary>The server is shutting down.</summary>
		Shutdown = 6,
	}

	/// <summary>
	/// The ground a duel is fought on: a box of <see cref="Size"/> centred on <see cref="Center"/>, turned by
	/// <see cref="Yaw"/> degrees about the world's up, its local forward running from one start to the other.
	/// </summary>
	public struct DuelArena
	{
		/// <summary>The box's centre, in world space.</summary>
		public Vector3 Center;
		/// <summary>The box's full width (x), height (y) and length (z), in metres.</summary>
		public Vector3 Size;
		/// <summary>Degrees about the world's up: local forward is <c>Quaternion.Euler(0, Yaw, 0) * Vector3.forward</c>.</summary>
		public float Yaw;
	}

	/// <summary>Challenges a player to a duel. Client to server.</summary>
	public struct DuelRequestBroadcast : IBroadcast
	{
		/// <summary>The player challenged.</summary>
		public long TargetCharacterID;
	}

	/// <summary>Tells a player they have been challenged. Server to client.</summary>
	public struct DuelInviteBroadcast : IBroadcast
	{
		/// <summary>Who challenges.</summary>
		public long ChallengerCharacterID;
		/// <summary>Their name, so the prompt needs no lookup.</summary>
		public string ChallengerName;
		/// <summary>How long the challenge stays open.</summary>
		public float ExpiresInSeconds;
	}

	/// <summary>Accepts or declines a challenge. Client to server.</summary>
	public struct DuelInviteResponseBroadcast : IBroadcast
	{
		/// <summary>The challenge being answered: an answer to an older one is ignored.</summary>
		public long ChallengerCharacterID;
		/// <summary>True to fight.</summary>
		public bool Accept;
	}

	/// <summary>What became of a challenge the receiver sent. Server to client.</summary>
	public struct DuelRequestResultBroadcast : IBroadcast
	{
		/// <summary>The player challenged.</summary>
		public long TargetCharacterID;
		/// <summary>Sent, or why not.</summary>
		public DuelRequestFailure Failure;
	}

	/// <summary>
	/// A duel was agreed: both players have been set down on the ground, facing each other, and the countdown runs.
	/// Server to each duellist.
	/// </summary>
	public struct DuelStartBroadcast : IBroadcast
	{
		/// <summary>The duel, as later messages name it.</summary>
		public long DuelID;
		/// <summary>The other duellist.</summary>
		public long OpponentCharacterID;
		/// <summary>Their name.</summary>
		public string OpponentName;
		/// <summary>The ground: leaving it forfeits.</summary>
		public DuelArena Arena;
		/// <summary>
		/// The receiver's own facing at the start, in degrees about the world's up (towards the opponent). The client turns
		/// its camera to it: a character faces where its camera looks, so a turn the camera did not make would be undone by
		/// the next input.
		/// </summary>
		public float StartYaw;
		/// <summary>Seconds until the fight starts.</summary>
		public float CountdownSeconds;
		/// <summary>Seconds the fight may last once started; then it is a draw. Zero: no limit.</summary>
		public float TimeLimitSeconds;
	}

	/// <summary>The countdown is over: fight. Server to each duellist.</summary>
	public struct DuelLiveBroadcast : IBroadcast
	{
		/// <summary>The duel.</summary>
		public long DuelID;
	}

	/// <summary>A duel ended. Server to each duellist still here.</summary>
	public struct DuelEndBroadcast : IBroadcast
	{
		/// <summary>The duel.</summary>
		public long DuelID;
		/// <summary>The result for the receiver.</summary>
		public DuelOutcome Outcome;
		/// <summary>Why it ended.</summary>
		public DuelEndReason Reason;
	}

	/// <summary>The receiver's duel record once the result is written. Server to client.</summary>
	public struct DuelRecordBroadcast : IBroadcast
	{
		/// <summary>The duel the record follows.</summary>
		public long DuelID;
		/// <summary>The rating now.</summary>
		public int Rating;
		/// <summary>What this duel did to it.</summary>
		public int RatingChange;
		/// <summary>Duels won.</summary>
		public int Wins;
		/// <summary>Duels lost.</summary>
		public int Losses;
		/// <summary>Duels drawn.</summary>
		public int Draws;
	}

	/// <summary>Yields the duel the sender is in (or withdraws during the countdown): a loss. Client to server.</summary>
	public struct DuelYieldBroadcast : IBroadcast
	{
	}
}
