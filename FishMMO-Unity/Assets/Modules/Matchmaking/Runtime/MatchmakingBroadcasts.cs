using System;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Client → Server broadcast asking for the instances joinable at one difficulty.
	/// </summary>
	/// <remarks>
	/// Sent when the panel opens, when the player changes difficulty tab, and when they press
	/// Refresh — never on a timer. The list is only interesting while somebody is looking at it,
	/// and a panel that polls turns every open finder on the shard into standing database load.
	/// <para>
	/// Debounced per connection on the server. The reply is a database query the client controls
	/// the timing of, which is the shape of request that has to be rate limited whether or not
	/// the client cooperates.
	/// </para>
	/// </remarks>
	public struct DungeonFinderListBroadcast : IBroadcast
	{
		/// <summary>ID of the dungeon entrance the player is standing at.</summary>
		public long InteractableID;

		/// <summary>Difficulty index being browsed.</summary>
		public int Difficulty;
	}

	/// <summary>
	/// One joinable instance, as offered in the dungeon finder's list.
	/// </summary>
	/// <remarks>
	/// Deliberately thin. It names who is running it and how full it is — enough to choose
	/// between rows — and nothing that would let the list be used to track where a particular
	/// player is: no character IDs, no scene handles, and no members beyond the leader's name.
	/// </remarks>
	[Serializable]
	public struct DungeonInstanceEntry
	{
		/// <summary>Instance row ID. The only identity a join request may name.</summary>
		public long InstanceID;

		/// <summary>Name of the party leader running it, or empty when it cannot be resolved.</summary>
		public string LeaderName;

		/// <summary>How many characters are currently inside.</summary>
		public int MemberCount;

		/// <summary>Capacity at this difficulty.</summary>
		public int MaxMembers;

		/// <summary>Seconds until it closes on its own, or 0 when not yet known.</summary>
		public int RemainingSeconds;

		/// <summary>True while the instance is still being loaded and cannot be entered yet.</summary>
		public bool IsLoading;

		/// <summary>True when this is the requesting character's own party's instance.</summary>
		public bool IsOwnParty;
	}

	/// <summary>
	/// Server → Client reply listing the instances joinable at one difficulty.
	/// </summary>
	/// <remarks>
	/// Sent from every exit of the list handler, including the refused and the empty ones. The
	/// panel disables its list while a request is outstanding — the guard that stops Refresh being
	/// held down — and a handler that returned silently would leave the list disabled for the rest
	/// of the window's life. The same contract the merchant and container paths follow.
	/// </remarks>
	public struct DungeonFinderListResultBroadcast : IBroadcast
	{
		/// <summary>The entrance the request named.</summary>
		public long InteractableID;

		/// <summary>The difficulty the request named, echoed so a late reply can be discarded.</summary>
		public int Difficulty;

		/// <summary>Joinable instances. Empty when there are none, or when the request was refused.</summary>
		public DungeonInstanceEntry[] Instances;

		/// <summary>Why the list is empty, when it is empty for a reason worth saying.</summary>
		public DungeonListFailureReason Reason;
	}

	/// <summary>
	/// Client → Server broadcast asking to open a new instance of a dungeon.
	/// </summary>
	/// <remarks>
	/// Names a difficulty and a visibility and nothing about the instance itself. Everything that
	/// decides whether it is allowed — the party's existing instance, the difficulty's level and
	/// party requirements, the character's state — is resolved server-side from the entrance and
	/// the roster, never taken from this message.
	/// </remarks>
	public struct DungeonFinderCreateBroadcast : IBroadcast
	{
		/// <summary>ID of the dungeon entrance scene object.</summary>
		public long InteractableID;

		/// <summary>Difficulty index to open it at. Validated against the dungeon's own list.</summary>
		public int Difficulty;

		/// <summary>True to open it hidden from the finder's public list.</summary>
		public bool IsPrivate;
	}

	/// <summary>
	/// Client → Server broadcast asking to join somebody else's instance.
	/// </summary>
	/// <remarks>
	/// Joining an instance run by another group also joins their party, so this is a social
	/// action as well as a transfer and is refused for a character who already has a party of
	/// their own to leave first. The instance ID is checked against what the finder would
	/// actually have offered — public, not full, the right dungeon — rather than trusted, because
	/// a row ID is guessable and the panel is not the only thing that can send this.
	/// </remarks>
	public struct DungeonFinderJoinBroadcast : IBroadcast
	{
		/// <summary>ID of the dungeon entrance scene object.</summary>
		public long InteractableID;

		/// <summary>Instance to join, from a <see cref="DungeonInstanceEntry"/>.</summary>
		public long InstanceID;
	}

	/// <summary>
	/// Client → Server broadcast asking the group finder to find the player a group for one
	/// dungeon at one difficulty.
	/// </summary>
	/// <remarks>
	/// Sent from the dungeon finder panel, so it names the entrance and is validated like every
	/// other finder request: the player must be standing at it. They must then <em>stay</em> at
	/// it — the server drops a waiter who walks away, and the panel leaves the queue when it is
	/// closed — so that being moved into the dungeon is never a surprise to somebody who has
	/// wandered off. Refused, accepted and every later change is answered with a
	/// <see cref="GroupFinderStatusBroadcast"/>.
	/// </remarks>
	public struct GroupFinderQueueBroadcast : IBroadcast
	{
		/// <summary>ID of the dungeon entrance scene object.</summary>
		public long InteractableID;

		/// <summary>Difficulty index to queue for. Validated against the dungeon's own list.</summary>
		public int Difficulty;
	}

	/// <summary>
	/// Client → Server broadcast leaving the group finder queue.
	/// </summary>
	/// <remarks>
	/// Carries nothing: a character is in at most one queue. Sent by the finder panel's Leave
	/// Queue button and, implicitly, by closing the panel. Refused when the group has already
	/// formed — see <see cref="GroupFinderState.Matched"/>.
	/// </remarks>
	public struct GroupFinderLeaveBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Server → Client broadcast reporting the character's group finder status.
	/// </summary>
	/// <remarks>
	/// Sent whenever the status changes: on the reply to a queue request, whenever the count of
	/// waiting players moves, when a group forms, and when the character leaves or is removed.
	/// Not sent on a timer while nothing changes. It carries enough for the finder panel to draw
	/// the wait — which dungeon, how many are waiting out of how many are needed — and nothing
	/// that identifies the other people waiting.
	/// </remarks>
	public struct GroupFinderStatusBroadcast : IBroadcast
	{
		/// <summary>Where the character stands.</summary>
		public GroupFinderState State;

		/// <summary>
		/// Which queue this is about: <see cref="SceneType.Group"/> for the dungeon group finder,
		/// <see cref="SceneType.PvP"/> for the arena board. One character is in at most one queue,
		/// so a status for one kind also means "not in the other". Each panel draws only its own.
		/// </summary>
		public SceneType Kind;

		/// <summary>Template ID of the dungeon queued for, or 0 when the entrance had none.</summary>
		public int DungeonTemplateID;

		/// <summary>Template ID of the arena queued for, when <see cref="Kind"/> is PvP.</summary>
		/// <remarks>
		/// <b>The scene name is not sent.</b> It is
		/// <c>DungeonTemplate.DungeonSceneName</c> / <c>ArenaTemplate.ArenaSceneName</c> — authored
		/// content the recipient already holds behind the two ids above — and both panels resolved
		/// the template first anyway, using the string only when that missed. A template a client
		/// cannot resolve is one whose name would mean nothing to it either, so the miss path says
		/// "this dungeon" / "this arena" instead.
		/// </remarks>
		public int ArenaTemplateID;

		/// <summary>Difficulty index queued for.</summary>
		public int Difficulty;

		/// <summary>How many players are waiting for this dungeon at this difficulty, including this one.</summary>
		public int WaitingCount;

		/// <summary>How many the finder needs before it opens a run.</summary>
		public int GroupSize;

		/// <summary>Why the state is <see cref="GroupFinderState.None"/>, when it is for a reason worth saying.</summary>
		public GroupFinderRefusalReason Reason;
	}

	/// <summary>
	/// Why a dungeon finder list request produced nothing.
	/// </summary>
	public enum DungeonListFailureReason : byte
	{
		/// <summary>The list is the true answer, whether or not it is empty.</summary>
		None = 0,
		/// <summary>The entrance no longer exists, or the player has walked away from it.</summary>
		NoEntrance = 1,
		/// <summary>The dungeon does not offer the difficulty that was asked for.</summary>
		UnknownDifficulty = 2,
		/// <summary>Asked for again too soon. The previous list is still the current one.</summary>
		OnCooldown = 3,
		/// <summary>The server could not read the list; the client should offer Refresh again.</summary>
		ServerError = 4,
	}
}
