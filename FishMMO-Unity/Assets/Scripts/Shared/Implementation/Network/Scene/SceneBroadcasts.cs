using System;
using FishNet.Broadcast;
using FishNet.Managing.Scened;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast indicating that the client has validated the current scene.
	/// No additional data required.
	/// </summary>
	public struct ClientValidatedSceneBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast indicating that the client has unloaded one or more scenes.
	/// Contains a list of unloaded scenes.
	/// </summary>
	public struct ClientScenesUnloadedBroadcast : IBroadcast
	{
		/// <summary>List of scenes that have been unloaded by the client.</summary>
		public UnloadedScene[] UnloadedScenes;
	}

	/// <summary>
	/// Broadcast requesting the initial scene to be loaded for the client.
	/// No additional data required.
	/// </summary>
	public struct RequestInitialSceneBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast for loading a specific scene.
	/// Contains the name of the scene to load.
	/// </summary>
	public struct SceneLoadBroadcast : IBroadcast
	{
		/// <summary>Name of the scene to load.</summary>
		public string SceneName;
	}

	/// <summary>
	/// Broadcast for unloading a specific scene.
	/// Contains the name of the scene to unload.
	/// </summary>
	public struct SceneUnloadBroadcast : IBroadcast
	{
		/// <summary>Name of the scene to unload.</summary>
		public string SceneName;
	}

	/// <summary>
	/// Broadcast for requesting a character scene change via a teleporter.
	/// Contains the source teleporter and target teleporter names.
	/// </summary>
	public struct CharacterSceneChangeRequestBroadcast : IBroadcast
	{
		/// <summary>Name of the teleporter the character is coming from.</summary>
		public string FromTeleporter;
		/// <summary>Name of the teleporter the character is going to.</summary>
		public string TeleporterName;
	}

	/// <summary>
	/// Broadcast for sending a list of available scene channels to the client.
	/// Contains a list of channel addresses.
	/// </summary>
	public struct SceneChannelListBroadcast : IBroadcast
	{
		/// <summary>List of available channel addresses for scene selection.</summary>
		public ChannelAddress[] Addresses;

		/// <summary>
		/// The channel the character is on right now, as a <c>scenes.id</c>, so the picker can
		/// mark it.
		/// </summary>
		/// <remarks>
		/// The client cannot work this out for itself. <see cref="IPlayerCharacter.SceneHandle"/>
		/// is server-side state — a plain property, never replicated — so on the client it is
		/// always zero, and the picker's "which one am I on" comparison could never match. The
		/// player was shown a list of identical-looking channels with no indication of where
		/// they already were, and picking their current one produced no visible result at all.
		/// Sending it is the only way the client can know.
		/// </remarks>
		public long CurrentSceneHandle;
	}

	/// <summary>
	/// Broadcast for selecting a specific scene channel.
	/// Contains the selected channel address.
	/// </summary>
	/// <remarks>
	/// <b>Bandwidth note:</b> This broadcast sends the full <see cref="ChannelAddress"/> struct
	/// (port, scene name, character count, etc.) when only <c>SceneHandle</c> (int) is needed
	/// to identify the target. If bandwidth becomes a concern, consider replacing with a
	/// leaner struct containing only <c>int SceneHandle</c>.
	/// TODO: Optimize by sending only SceneHandle instead of the full ChannelAddress.
	/// </remarks>
	public struct SceneChannelSelectBroadcast : IBroadcast
	{
		/// <summary>Selected channel address for the scene.</summary>
		public ChannelAddress Channel;
	}

	/// <summary>
	/// Broadcast requesting the list of available scene channels from the server.
	/// Sent by the client to request an updated channel list for the current scene.
	/// </summary>
	public struct RequestSceneChannelListBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Why the server declined to move a character to another scene instance.
	/// </summary>
	/// <remarks>
	/// Both voluntary scene-instance transfers — a channel switch and a dungeon entrance —
	/// finish their validation asynchronously, against the database, after the client has
	/// already committed to the action and closed its own UI. Every one of those checks could
	/// previously fail by simply returning, so a refused request was indistinguishable from a
	/// lost one: the player saw nothing at all and had no way to tell "the channel filled up"
	/// from "the game stopped responding". Naming the refusal is what makes the action
	/// answerable.
	/// </remarks>
	public enum SceneTransferRefusalReason : byte
	{
		/// <summary>No specific reason available.</summary>
		Unspecified = 0,

		/// <summary>The destination no longer exists, or never did.</summary>
		DestinationUnavailable = 1,

		/// <summary>The destination is full.</summary>
		DestinationFull = 2,

		/// <summary>The character's state changed and no longer permits the transfer (combat, death, mid-teleport).</summary>
		CharacterStateChanged = 3,

		/// <summary>The transfer is still on cooldown for this character.</summary>
		OnCooldown = 4,

		/// <summary>
		/// The character, or its party, already has a different instance open.
		/// </summary>
		/// <remarks>
		/// A party may hold one instance at a time, not one per dungeon. Without that rule a party
		/// could open a live copy of every dungeon on the shard — enter one, walk out, enter the
		/// next — and each abandoned copy held a full physics scene and a scene row until its own
		/// idle timeout expired.
		/// <para>
		/// Distinct from <see cref="DestinationFull"/>: the instance they hold is not the one they
		/// asked for, so there is nothing to join. They have to finish it, close it, or wait for it
		/// to expire.
		/// </para>
		/// </remarks>
		PartyInstanceExists = 5,

		/// <summary>The server could not complete the request and the client should try again.</summary>
		ServerError = 6,

		/// <summary>The character is already where it asked to go.</summary>
		/// <remarks>
		/// Reachable in ordinary play rather than only through a modified client: the channel
		/// picker's list is a snapshot, and a character can be moved between instances by the
		/// world server's routing between the list being drawn and the player clicking. This
		/// refusal used to be a bare <c>return</c>, which — because the picker closes itself on
		/// send — the player experienced as the button doing nothing at all.
		/// </remarks>
		AlreadyAtDestination = 7,

		/// <summary>
		/// The character does not meet the difficulty's own entry requirements.
		/// </summary>
		/// <remarks>
		/// A dungeon declares its requirements per difficulty — a minimum level, a minimum party
		/// size — so the same character can be turned away from Hard and welcomed on Normal. Kept
		/// distinct from <see cref="CharacterStateChanged"/> because it is not transient: waiting
		/// will not fix it, and telling the player it might is worse than telling them nothing.
		/// </remarks>
		RequirementsNotMet = 8,

		/// <summary>
		/// The instance the request named is not one the requester may join.
		/// </summary>
		/// <remarks>
		/// It closed, went private, filled, or was never joinable and the row ID was guessed. All
		/// four are reported the same way on purpose: a refusal that distinguished them would let
		/// an ID be probed to learn whether a particular instance exists.
		/// </remarks>
		InstanceUnavailable = 9,

		/// <summary>
		/// The character is in a party and cannot join another group's instance without leaving it.
		/// </summary>
		/// <remarks>
		/// Joining somebody else's run also joins their party. Doing that silently would drop the
		/// character out of a group they are already in — and, if they led it, hand that group to
		/// somebody else without asking. So it is refused, and leaving is left as the player's own
		/// deliberate act.
		/// </remarks>
		AlreadyInParty = 10,
	}

	/// <summary>
	/// Broadcast sent when the server declines a voluntary scene-instance transfer, so the
	/// client can restore its UI and tell the player why rather than appearing to hang.
	/// </summary>
	public struct SceneTransferRefusedBroadcast : IBroadcast
	{
		/// <summary>Why the transfer was refused.</summary>
		public SceneTransferRefusalReason Reason;
	}

	/// <summary>
	/// Broadcast asking the server to remove the character from its current instance and return
	/// it to the open world.
	/// </summary>
	/// <remarks>
	/// The unconditional way out of instanced content. A dungeon normally provides its own exit
	/// teleporter, but that is scene-authoring data: a dungeon shipped without one, or with one
	/// a player cannot reach, would otherwise leave that player permanently inside — a character
	/// bound to an instance is routed straight back to it on every login, so quitting does not
	/// help either. This makes leaving a property of the system rather than of the content.
	/// <para>
	/// Server-gated like any other voluntary transfer: it is refused in combat, so it is not an
	/// escape, and refusal is reported through <see cref="SceneTransferRefusedBroadcast"/>.
	/// </para>
	/// </remarks>
	public struct RequestLeaveInstanceBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast asking the server for the state of the instance the character is standing in.
	/// </summary>
	/// <remarks>
	/// Sent when the player opens the instance panel and on its refresh timer. Everything the
	/// panel shows is server state that changes without the client being told — members leave,
	/// the lifetime counts down — and there is no push channel for it, so the panel asks rather
	/// than rendering something it cached.
	/// </remarks>
	public struct RequestInstanceDetailsBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// One member of an instance, as presented to another member.
	/// </summary>
	/// <remarks>
	/// <see cref="IsLeader"/> and <see cref="IsSelf"/> are resolved by the server for the
	/// specific character being answered rather than left for the client to work out. The client
	/// cannot reliably decide either — and more to the point it must not, because the same
	/// judgement decides whether the kick controls are offered.
	/// </remarks>
	[Serializable]
	public struct InstanceMemberData
	{
		/// <summary>The member's character ID. The only identity a kick request may name.</summary>
		public long CharacterID;

		/// <summary>The member's character name, for display.</summary>
		public string Name;

		/// <summary>Whether this member leads the party that owns the instance.</summary>
		public bool IsLeader;

		/// <summary>Whether this member is the character being answered.</summary>
		public bool IsSelf;
	}

	/// <summary>
	/// Broadcast carrying the state of an instance to one of its members.
	/// </summary>
	/// <remarks>
	/// Answered for every request, including the ones with nothing to report — a character that
	/// is not in an instance gets <see cref="InInstance"/> false rather than silence, for the
	/// same reason the channel list answers with an empty list: a panel the player opened and
	/// that never fills in is indistinguishable from a hung game.
	/// </remarks>
	public struct InstanceDetailsBroadcast : IBroadcast
	{
		/// <summary>False when the character is not in an instance; every other field is then unset.</summary>
		public bool InInstance;

		/// <summary>Scene name of the instance.</summary>
		public string SceneName;

		/// <summary>
		/// Seconds until the instance closes on its own, or 0 when it is not time-bounded.
		/// </summary>
		/// <remarks>
		/// A snapshot. The client counts down from it between refreshes rather than being sent a
		/// tick, so the number on screen keeps moving without a message per second.
		/// </remarks>
		public int RemainingSeconds;

		/// <summary>Character ID of the leader of the party that owns the instance.</summary>
		public long LeaderCharacterID;

		/// <summary>Name of that leader, for display.</summary>
		public string LeaderName;

		/// <summary>
		/// Whether the character being answered is the leader, and so may remove others.
		/// </summary>
		/// <remarks>
		/// Decided by the server and sent, rather than inferred client-side by comparing IDs. The
		/// client's copy of this only decides whether the controls are <em>drawn</em>; the server
		/// re-checks on the request itself, because a drawn control is not an authorisation.
		/// </remarks>
		public bool ViewerIsLeader;

		/// <summary>Everyone currently standing in the instance.</summary>
		public InstanceMemberData[] Members;

		/// <summary>Name of the difficulty the instance was opened at, for display.</summary>
		public string DifficultyName;

		/// <summary>
		/// Whether the instance is hidden from the dungeon finder's public list.
		/// </summary>
		/// <remarks>
		/// Shown to every member, not only the leader. Whether strangers can walk into the run
		/// they are in is something all of them have an interest in knowing; only the leader can
		/// change it.
		/// </remarks>
		public bool IsPrivate;
	}

	/// <summary>
	/// Client → Server broadcast showing or hiding the instance in the dungeon finder's list.
	/// </summary>
	/// <remarks>
	/// Honoured only for the leader of the party that owns the instance, and re-authorised
	/// against the row itself when it is written — the client's copy of who leads decides only
	/// whether the control is drawn.
	/// </remarks>
	public struct InstancePrivacyBroadcast : IBroadcast
	{
		/// <summary>True to hide the instance from the finder, false to offer it.</summary>
		public bool IsPrivate;
	}

	/// <summary>
	/// Broadcast asking the server to remove another character from the instance.
	/// </summary>
	/// <remarks>
	/// Honoured only for the member who opened the instance, and never for themselves — a leader
	/// who wants out uses <see cref="RequestLeaveInstanceBroadcast"/> like anyone else, which
	/// keeps "I am leaving" and "you are leaving" as separate, separately-authorised requests.
	/// </remarks>
	public struct InstanceKickBroadcast : IBroadcast
	{
		/// <summary>Character to remove from the instance.</summary>
		public long CharacterID;
	}

	/// <summary>
	/// Broadcast sent by the server when it cannot process a gameplay request because the
	/// async work queue is full. The client should display a transient "Server Busy" notification.
	/// </summary>
	/// <remarks>
	/// <b>Enhancement note:</b> This broadcast currently carries no metadata (empty payload).
	/// Consider adding <c>RetryAfterSeconds</c> (int) and <c>QueuePosition</c> (int) fields
	/// so the client can show a meaningful countdown and queue status to the user.
	/// </remarks>
	public struct ServerBusyBroadcast : IBroadcast
	{
	}

	/// <summary>
	/// Broadcast sent by the LoginServer to a queued client with their current
	/// position in the login queue.  Sent periodically at a server-configured rate.
	///
	/// <para><b>Position semantics:</b></para>
	/// <list type="bullet">
	///   <item><b>&gt; 0</b> — Waiting in queue.  Display the position to the user.</item>
	///   <item><b>0</b> — Admitted.  The client should re-initiate the handshake now.</item>
	///   <item><b>-1</b> — Cancelled.  The queue entry was purged (timeout or shutdown).</item>
	/// </list>
	///
	/// <para><b>Server-authoritative update rate:</b> The server controls how often
	/// this broadcast is sent via the <c>LoginQueueUpdateRateSeconds</c> config key.
	/// Clients are passive receivers only — there is no request path for faster updates.</para>
	///
	/// <para><b>Validation:</b> The server MUST enforce the documented QueuePosition
	/// semantics (&gt;0 waiting, 0 admitted, -1 cancelled). The client treats these
	/// values as authoritative — no local defensive validation is performed.</para>
	/// </summary>
	public struct LoginQueuePositionBroadcast : IBroadcast
	{
		/// <summary>
		/// Current 1-based queue position.  0 = admitted, -1 = cancelled.
		/// </summary>
		public int QueuePosition;

		/// <summary>
		/// Rough estimated wait time in seconds based on the server's admission rate.
		/// 0 if unknown or if the client has been admitted.
		/// </summary>
		public int EstimatedWaitSeconds;

		/// <summary>
		/// Total number of clients currently in the queue.
		/// </summary>
		public int TotalQueued;
	}

	/// <summary>
	/// Why a client is waiting in the WorldServer's scene-routing queue.
	/// </summary>
	/// <remarks>
	/// The three waits look identical to a player — a loading screen that is not
	/// progressing — but they have very different causes and very different expected
	/// durations, and only one of them is the server being full. Naming the cause is the
	/// difference between "the game has hung" and "the game is waiting for something
	/// specific".
	/// </remarks>
	public enum WorldSceneQueueReason : byte
	{
		/// <summary>Every running instance of the target scene is at capacity.</summary>
		Capacity = 0,

		/// <summary>A scene instance has been requested and is still loading on a scene server.</summary>
		SceneLoading = 1,

		/// <summary>
		/// The character's combat-logout body is still standing in a specific scene instance,
		/// and only the scene server holding it can hand it back.
		/// </summary>
		CombatLogoutBody = 2,
	}

	/// <summary>
	/// Broadcast sent by the WorldServer to a client waiting to be routed to a SceneServer,
	/// with its current position in the scene-routing queue. Sent periodically at a
	/// server-configured rate.
	///
	/// <para><b>Position semantics</b> (identical to <see cref="LoginQueuePositionBroadcast"/>):</para>
	/// <list type="bullet">
	///   <item><b>&gt; 0</b> — Waiting in queue. Display the position to the user.</item>
	///   <item><b>0</b> — Routed. A <see cref="WorldSceneConnectBroadcast"/> follows; dismiss the wait UI.</item>
	///   <item><b>-1</b> — Cancelled. The wait was abandoned and the connection is being closed.</item>
	/// </list>
	///
	/// <para><b>Why this exists.</b> The World → Scene hop is the one leg of the connection
	/// pipeline that could stall indefinitely with nothing on screen but a loading overlay.
	/// A client with no scene instance to go to is held in the WorldServer's queue and
	/// re-evaluated every cycle, so the wait is legitimate — but it was completely silent,
	/// which is indistinguishable from a hang. This is the login queue's feedback channel
	/// applied to the same problem one hop later.</para>
	///
	/// <para><b>Server-authoritative update rate:</b> The WorldServer controls how often this
	/// broadcast is sent. Clients are passive receivers only — there is no request path.</para>
	/// </summary>
	public struct WorldSceneQueuePositionBroadcast : IBroadcast
	{
		/// <summary>
		/// Current 1-based queue position. 0 = routed, -1 = cancelled.
		/// </summary>
		public int QueuePosition;

		/// <summary>
		/// Rough estimated wait in seconds, derived from how many connections the last
		/// routing cycle actually placed. 0 when unknown — which is the normal case while
		/// nothing is draining, and the client must present it as such rather than as
		/// "no wait".
		/// </summary>
		public int EstimatedWaitSeconds;

		/// <summary>
		/// Total number of clients waiting for the same scene or instance.
		/// </summary>
		public int TotalQueued;

		/// <summary>
		/// Why this client is waiting. See <see cref="WorldSceneQueueReason"/>.
		/// </summary>
		public WorldSceneQueueReason Reason;
	}
}