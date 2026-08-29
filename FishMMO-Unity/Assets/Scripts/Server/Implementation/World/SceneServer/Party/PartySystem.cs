using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages party creation, invitations, membership, and updates for the MMO server.
	/// Handles party broadcasts, chat commands, and synchronizes party state with the database.
	/// Game logic and Broadcasts run synchronously on the main thread.
	/// Database operations are async to avoid blocking the main thread.
	/// Results from async DB queries that require main-thread state changes or Broadcasts are marshalled
	/// via IPartySystemMainThreadQueueData.
	/// </summary>
	[CreateAssetMenu(fileName = "PartySystem", menuName = "FishMMO/Server/SceneServer/Party System", order = 1)]
	[RequiresDataContainer(typeof(PartySystemRuntimeData))]
	[RequiresDataContainer(typeof(PartyCharacterMappingData))]
	[RequiresDataContainer(typeof(PartySystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(PartyCombatMeterData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class PartySystem : ServerBehaviour, IPartySystem<NetworkConnection>
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max party-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Maximum number of members allowed in a party.
		/// </summary>
		[SerializeField] private int maxPartySize = 6;

		/// <summary>
		/// Maximum number of members allowed in a party.
		/// </summary>
		public int MaxPartySize => maxPartySize;
		/// <summary>
		/// The server party update pump rate limit in seconds.
		/// </summary>
		[Tooltip("The server party update pump rate limit in seconds.")]
		[SerializeField] private float updatePumpRate = 1.0f;

		/// <summary>
		/// Seconds of clock skew tolerated when advancing the party update watermark.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The party update table is timestamped by whichever scene server wrote it, from its
		/// own clock.</b> The pump then asks for everything newer than a mark it stamped from
		/// <em>its</em> clock. Those are different machines, so a scene server whose clock runs
		/// ahead can set a watermark later than an update another server is about to write with
		/// its own, slightly earlier, "now" — and that update is then behind the mark before it is
		/// ever read, and is skipped permanently.
		/// </para>
		/// <para>
		/// The watermark is therefore held back by this much. An update from a server up to this
		/// far behind is still caught; the cost is that updates inside the window are delivered
		/// twice, which the fetch already allows for (its comparison is inclusive) and which costs
		/// one redundant roster broadcast.
		/// </para>
		/// <para>
		/// <b>This narrows the window; it does not close it.</b> Skew larger than this still loses
		/// updates, and the write side has its own version of the problem — the upsert keeps the
		/// later timestamp, so a lagging server's update can be swallowed outright by a leading
		/// server's. Closing both properly means timestamping the row from the DATABASE clock and
		/// carrying the watermark in that same clock, which is a change to the shared update
		/// service that the guild system would have to make in step. Party leadership does not
		/// depend on this either way: its repair re-derives state from the rows on a schedule of
		/// its own rather than from update notifications, which is why it survives a lost one.
		/// </para>
		/// </remarks>
		[Tooltip("Seconds of scene-server clock skew tolerated when advancing the party update watermark")]
		[SerializeField] private float partyUpdateClockSkewAllowanceSeconds = 5.0f;

		/// <summary>
		/// The server party update pump rate limit in seconds.
		/// </summary>
		public float UpdatePumpRate => updatePumpRate;

		/// <summary>
		/// Invitation lifetime in seconds before automatic expiration.
		/// </summary>
		[Header("Invitation Protection")]
		[Tooltip("Invitation lifetime in seconds before automatic expiration")]
		[SerializeField] private float invitationTtlSeconds = 45.0f;

		/// <summary>
		/// Interval between invitation cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded invitation cleanup sweeps")]
		[SerializeField] private float invitationSweepIntervalSeconds = 1.0f;

		/// <summary>
		/// Maximum invitation entries scanned per cleanup sweep.
		/// </summary>
		[Tooltip("Max invitation entries scanned per sweep")]
		[SerializeField] private int invitationSweepMaxScan = 128;

		/// <summary>
		/// Maximum invitation entries removed per cleanup sweep.
		/// </summary>
		[Tooltip("Max invitation entries removed per sweep")]
		[SerializeField] private int invitationSweepMaxRemove = 128;

		/// <summary>
		/// Minimum seconds between invitations from the same inviter to the same target.
		/// </summary>
		/// <remarks>
		/// The pending-invitation slot is not a rate limit — declining clears it instantly — and
		/// the ingress debounce is per connection rather than per target. Neither stops one player
		/// from keeping a modal permanently on another player's screen. This does.
		/// </remarks>
		[Tooltip("Minimum seconds between party invitations to the same target from the same inviter")]
		[SerializeField] private float perTargetInviteCooldownSeconds = 60.0f;

		/// <summary>
		/// Debounce window in milliseconds for party ingress operations.
		/// </summary>
		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between party requests per connection and operation")]
		[SerializeField] private int ingressDebounceMilliseconds = 100;

		/// <summary>
		/// Interval in seconds between ingress-guard cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded ingress guard cleanup sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5.0f;

		/// <summary>
		/// Guard entry time-to-live in seconds.
		/// </summary>
		[Tooltip("Seconds before stale ingress guard entries are removed")]
		[SerializeField] private float ingressEntryTtlSeconds = 30.0f;

		/// <summary>
		/// Maximum stale ingress entries removed per sweep.
		/// </summary>
		[Tooltip("Maximum stale ingress guard entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		/// <summary>
		/// Idle seconds after which a character's damage/heal meters are considered a finished
		/// encounter and reset to zero.
		/// </summary>
		/// <remarks>
		/// An encounter is defined purely by activity, not by which mob was pulled. That keeps the
		/// meter from ever disagreeing with a combat-state machine it does not own, and it is why
		/// the number is a timeout rather than a hook into <c>ICharacterDamageController</c>'s
		/// combat window: that window is refreshed by being ATTACKED as well as by attacking, so a
		/// player who has done nothing but take hits for a minute would keep a stale rate on
		/// everyone's party frame.
		/// </remarks>
		[Header("Combat Meters")]
		[Tooltip("Idle seconds after which the DPS/HPS meters reset for a new encounter")]
		[SerializeField] private float encounterTimeoutSeconds = 6.0f;

		/// <summary>
		/// Floor on the meter's divisor, in seconds.
		/// </summary>
		/// <remarks>
		/// Without one the opening hit of a fight is divided by very nearly zero and the meter
		/// reads in the millions for a single tick before settling — which is the only number
		/// anybody actually sees on a short fight.
		/// </remarks>
		[Tooltip("Minimum seconds used as the DPS/HPS divisor, so the opening hit cannot divide by ~0")]
		[SerializeField] private float meterMinimumWindowSeconds = 1.0f;

		/// <summary>
		/// Seconds between bounded sweeps of finished combat meters.
		/// </summary>
		[Tooltip("Seconds between bounded combat meter cleanup sweeps")]
		[SerializeField] private float meterSweepIntervalSeconds = 10.0f;

		/// <summary>
		/// Maximum meter entries scanned per sweep.
		/// </summary>
		[Tooltip("Max combat meter entries scanned per sweep")]
		[SerializeField] private int meterSweepMaxScan = 64;

		/// <summary>
		/// Maximum meter entries removed per sweep.
		/// </summary>
		[Tooltip("Max combat meter entries removed per sweep")]
		[SerializeField] private int meterSweepMaxRemove = 64;

		/// <summary>
		/// Maximum buffs and debuffs sent per party member on the vitals pump.
		/// </summary>
		/// <remarks>
		/// A display cap, and a bound on the message. <c>IBuffController.ObservedBuffs</c> has no
		/// size limit — world buffs, consumables and a raid's worth of stacking auras all land in
		/// it — and this payload is built for every member of every party once a second and sent
		/// to each of them, so an uncapped list multiplies by the party size twice over. The
		/// default is comfortably more than the two strips on the party frame can draw, so the cap
		/// bites only where nothing would have been visible anyway.
		/// </remarks>
		[Tooltip("Max buffs/debuffs sent per party member on the vitals pump")]
		[SerializeField] private int maxVitalsBuffsPerMember = 16;

		/// <summary>
		/// Whether leadership moves to an online member when the leader disconnects.
		/// </summary>
		/// <remarks>
		/// A disconnecting leader does not leave the party — the membership row survives, so the
		/// party keeps a leader who is not there. Every leader-only action (invite, kick, promote,
		/// and closing a dungeon the party owns) then refuses for everyone still playing, and
		/// nothing resolves it until that player logs back in. Moving the rank is the lesser
		/// evil, and it is reversible in a way the deadlock is not: the returning player can be
		/// promoted back.
		/// <para>
		/// Off means a party whose leader crashes is frozen until they return. Left configurable
		/// because that is a design decision rather than a correctness one, but it defaults on.
		/// </para>
		/// </remarks>
		[Header("Leadership")]
		[Tooltip("Move party leadership to a logged-in member when the holder is not online anywhere")]
		[SerializeField] private bool transferLeadershipOnDisconnect = true;

		/// <summary>
		/// Seconds between leadership audit sweeps.
		/// </summary>
		/// <remarks>
		/// The audit exists for the parties no event ever announces. Every ordinary change writes
		/// a party update, which brings the pump straight to the party and settles its leadership
		/// — but a scene server that dies does not write one, and takes its members' disconnect
		/// handlers with it. Their session leases lapse a little later and the database stops
		/// calling them online, and from that moment the party is led by somebody nothing will
		/// ever ask about again. This is what asks.
		/// <para>
		/// Slow on purpose. It is a backstop for a rare failure, not a mechanism the common path
		/// depends on, and it costs a query per party it examines.
		/// </para>
		/// </remarks>
		[Tooltip("Seconds between party leadership audit sweeps (backstop for scene servers that died without notice)")]
		[SerializeField] private float leadershipAuditIntervalSeconds = 30.0f;

		/// <summary>
		/// Parties examined per leadership audit sweep.
		/// </summary>
		/// <remarks>
		/// Bounded and round-robin, so the cost per sweep does not grow with how many parties this
		/// server hosts — a busy shard takes longer to come round to any one party rather than
		/// doing more work at once.
		/// </remarks>
		[Tooltip("Parties examined per leadership audit sweep")]
		[SerializeField] private int leadershipAuditPartiesPerSweep = 4;

		/// <summary>
		/// Maximum parties whose scheduled leadership re-check is started in one tick.
		/// </summary>
		/// <remarks>
		/// A re-check is scheduled for every party a disconnecting member belonged to, so the
		/// moment that produces the most of them at once is a world server restart — every party
		/// on the shard comes due within the same second, and without a cap they would all be
		/// handed to a single worker as one task of several thousand sequential database round
		/// trips, at exactly the moment the database is busiest.
		/// <para>
		/// Whatever does not fit stays queued and is picked up on the following tick, so a backlog
		/// drains at this rate per pump interval rather than being dropped.
		/// </para>
		/// </remarks>
		[Tooltip("Max parties whose scheduled leadership re-check is started per tick")]
		[SerializeField] private int leadershipRecheckMaxPerTick = 16;

		/// <summary>
		/// Seconds after a party member disconnects before that party's leadership is re-examined.
		/// </summary>
		/// <remarks>
		/// <b>A delay, not a debounce.</b> Disconnecting is announced to this system BEFORE the
		/// character's session is released — the release happens after the final save, which is a
		/// database round trip later. So the party update the disconnect writes brings the pump
		/// back to the party while the database still reports the leaver as logged in, the repair
		/// correctly finds nothing wrong, and nothing writes another update to bring anybody back.
		/// The one case that matters most — the leader logs out — would then wait for the
		/// round-robin audit to come round, which on a busy server is minutes.
		/// <para>
		/// This schedules one look at the party a little after the dust has settled. It must be
		/// longer than a save takes; it is not otherwise sensitive, and a party with several
		/// members leaving at once collapses to a single re-check.
		/// </para>
		/// </remarks>
		[Tooltip("Seconds after a member disconnects before that party's leadership is re-examined")]
		[SerializeField] private float leadershipRecheckDelaySeconds = 5.0f;

		/// <summary>
		/// How long a leader must be continuously absent before leadership is moved.
		/// </summary>
		/// <remarks>
		/// <b>This is what stops zoning from costing you your party.</b> Walking through a
		/// teleporter — or leading the party into the dungeon it just opened — moves the character
		/// between scene servers, and that releases its session on the way out and re-claims it on
		/// arrival. For the whole of that gap the database reports the leader exactly as it
		/// reports somebody who logged off. Without a grace long enough to cover a scene load, the
		/// repair would take the rank off every leader who used a door.
		/// <para>
		/// Also the ceiling on how long a party can be leaderless in practice: a leader who really
		/// has gone is replaced this long after the fact. Raising it makes zoning safer on a slow
		/// shard; lowering it makes a genuine logout recover faster. It must comfortably exceed the
		/// slowest scene load on the shard.
		/// </para>
		/// </remarks>
		[Tooltip("Seconds a leader must be continuously offline before leadership moves (must exceed the slowest scene load)")]
		[SerializeField] private float leadershipAbsenceGraceSeconds = 45.0f;

		[Header("Achievements")]
		/// <summary>
		/// Achievement template awarded when a character creates a party.
		/// </summary>
		public AchievementTemplate PartyCreateAchievementTemplate;
		/// <summary>
		/// Achievement template awarded when a character joins a party.
		/// </summary>
		public AchievementTemplate PartyJoinAchievementTemplate;

		/// <summary>
		/// Unscaled time at which the next bounded combat-meter sweep is due.
		/// </summary>
		private float nextMeterSweepTime;

		/// <summary>
		/// UTC time at which the next leadership audit sweep is due.
		/// </summary>
		private DateTime nextLeadershipAuditUtc;

		/// <summary>
		/// UTC time at which the next runtime cache prune is due.
		/// </summary>
		private DateTime nextCacheSweepUtc;

		/// <summary>
		/// Rotating position in the locally-tracked party list where the audit resumes.
		/// </summary>
		/// <remarks>
		/// An index into a set whose membership changes between sweeps, so it is a position rather
		/// than a bookmark: it can skip a party or examine one twice when the set shifts under it.
		/// Both are harmless for a backstop that only has to come round eventually, and the
		/// alternative — remembering which parties have been seen — is a second collection to keep
		/// in step with the tracker for no benefit.
		/// </remarks>
		private int leadershipAuditCursor;

		/// <summary>
		/// Scratch list of locally-tracked party IDs, snapshotted for one audit sweep.
		/// </summary>
		private readonly List<long> leadershipAuditBuffer = new List<long>();

		/// <summary>
		/// Parties awaiting a prompt leadership re-check, and when each is due.
		/// </summary>
		/// <remarks>
		/// Main-thread only: written by the disconnect handler and drained by the periodic update,
		/// both of which run there. Keyed by party, so several members leaving together schedule
		/// one look rather than one each.
		/// </remarks>
		private readonly Dictionary<long, DateTime> pendingLeadershipRechecks = new Dictionary<long, DateTime>();

		/// <summary>Scratch list of party IDs whose re-check has come due.</summary>
		private readonly List<long> dueLeadershipRechecks = new List<long>();

		/// <summary>
		/// Scratch map from Unity scene handle to the local members of one party standing in it.
		/// </summary>
		/// <remarks>
		/// Reused across ticks and across parties. The vitals pump runs once a second for every
		/// party with a member on this server; building these collections fresh each time would
		/// be a steady drip of garbage for a payload whose shape barely changes.
		/// </remarks>
		private readonly Dictionary<int, List<IPlayerCharacter>> vitalsSceneGroups = new Dictionary<int, List<IPlayerCharacter>>();

		/// <summary>
		/// Last observed-buff set sent for each character, as a content signature.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Keyed by character id and holding a signature rather than the array itself, because the
		/// arrays are rebuilt from a shared scratch buffer every pump and retaining one would retain
		/// a buffer that is about to be overwritten.
		/// </para>
		/// <para>
		/// Cleared for a character when they leave the scene server (<see cref="OnCharacterLeave"/>
		/// territory, alongside the combat meter), so a character who returns is sent their buffs
		/// again rather than inheriting a signature from a previous session and having their first
		/// payload silently omit the array.
		/// </para>
		/// </remarks>
		private readonly Dictionary<long, int> lastSentBuffSignature = new Dictionary<long, int>();

		/// <summary>Spare member lists returned by <see cref="vitalsSceneGroups"/> between uses.</summary>
		private readonly Stack<List<IPlayerCharacter>> vitalsGroupPool = new Stack<List<IPlayerCharacter>>();

		/// <summary>Scratch entry list used to build one scene group's vitals payload.</summary>
		private readonly List<PartyMemberVitalsEntry> vitalsEntryBuffer = new List<PartyMemberVitalsEntry>();

		/// <summary>Scratch buff list used to re-base one member's observed buffs.</summary>
		private readonly List<ObservedBuffEntry> vitalsBuffBuffer = new List<ObservedBuffEntry>();

		/// <summary>
		/// Operation keys used by party ingress guards.
		/// </summary>
		private enum IngressOperation : byte
		{
			Create = 1,
			Invite = 2,
			AcceptInvite = 3,
			DeclineInvite = 4,
			Leave = 5,
			Remove = 6,
			ChangeRank = 7,
		}

		/// <summary>
		/// Handles party invite chat commands.
		/// </summary>
		public bool OnPartyInvite(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender == null || !CharacterStateValidation.CanAct(sender))
				return false;

			if (string.IsNullOrWhiteSpace(msg.Text))
			{
				return false;
			}

			string targetName = msg.Text.Trim().ToLowerInvariant();
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByLowerCaseName.TryGetValue(targetName, out IPlayerCharacter character))
			{
				OnServerPartyInviteBroadcastReceived(sender.Owner, new PartyInviteBroadcast()
				{
					InviterCharacterID = sender.ID,
					TargetCharacterID = character.ID,
				}, Channel.Reliable);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Called once to initialize the party system. Registers chat commands, broadcast handlers, and character events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("PartySystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IPartySystemMainThreadQueueData>(out _))
			{
				Log.Error("PartySystem", "Failed to initialize: IPartySystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem) ||
				characterSystem == null)
			{
				Log.Error("PartySystem", "Failed to initialize: ICharacterSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Chat commands
			ChatHelper.AddCommands(new Dictionary<string, ChatCommand>()
			{
				{ "/pi", OnPartyInvite },
				{ "/invite", OnPartyInvite },
			});

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<PartyCreateBroadcast>(OnServerPartyCreateBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyInviteBroadcast>(OnServerPartyInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyAcceptInviteBroadcast>(OnServerPartyAcceptInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyDeclineInviteBroadcast>(OnServerPartyDeclineInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyLeaveBroadcast>(OnServerPartyLeaveBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyRemoveBroadcast>(OnServerPartyRemoveBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PartyChangeRankBroadcast>(OnServerPartyChangeRankBroadcastReceived, true);

			// Character system events
			characterSystem.OnConnect += CharacterSystem_OnConnect;
			characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(UpdatePumpRate, OnPeriodicUpdate);
			}

			if (!Server.DataContainerRegistry.TryGet<IPartySystemRuntimeData>(out var runtimeData))
			{
				Log.Error("PartySystem", "Failed to initialize: IPartySystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IPartyCombatMeterData>(out _))
			{
				Log.Error("PartySystem", "Failed to initialize: IPartyCombatMeterData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			/* The meters are fed from the damage controller's static events. Static, so one
			 * invocation list is shared by every character in the process — which is exactly what
			 * makes them usable here: the party system needs damage totals for characters it does
			 * not own and has no component on, and there is no per-character subscription that
			 * would give it those without touching every character prefab. The handlers filter to
			 * party members themselves; see RecordCombatMeterContribution. */
			/* Removed before added. This is a ScriptableObject, so with Unity's domain reload
			 * disabled the same instance survives an editor play session — and a subscription that
			 * outlived the last one would then be joined by a second, and every hit would be
			 * metered twice. Subtracting a handler that is not attached is a no-op. */
			ICharacterDamageController.OnDamaged -= CharacterDamageController_OnDamaged;
			ICharacterDamageController.OnDamaged += CharacterDamageController_OnDamaged;
			ICharacterDamageController.OnHealed -= CharacterDamageController_OnHealed;
			ICharacterDamageController.OnHealed += CharacterDamageController_OnHealed;

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			partyUpdateClockSkewAllowanceSeconds = Mathf.Max(0.0f, partyUpdateClockSkewAllowanceSeconds);
			encounterTimeoutSeconds = Mathf.Max(1.0f, encounterTimeoutSeconds);
			meterMinimumWindowSeconds = Mathf.Max(0.1f, meterMinimumWindowSeconds);
			meterSweepIntervalSeconds = Mathf.Max(1.0f, meterSweepIntervalSeconds);
			meterSweepMaxScan = Mathf.Max(1, meterSweepMaxScan);
			meterSweepMaxRemove = Mathf.Max(1, meterSweepMaxRemove);
			maxVitalsBuffsPerMember = Mathf.Max(1, maxVitalsBuffsPerMember);
			leadershipAuditIntervalSeconds = Mathf.Max(5.0f, leadershipAuditIntervalSeconds);
			leadershipAuditPartiesPerSweep = Mathf.Max(1, leadershipAuditPartiesPerSweep);
			leadershipRecheckMaxPerTick = Mathf.Max(1, leadershipRecheckMaxPerTick);
			leadershipRecheckDelaySeconds = Mathf.Max(1.0f, leadershipRecheckDelaySeconds);
			leadershipAbsenceGraceSeconds = Mathf.Max(10.0f, leadershipAbsenceGraceSeconds);
			invitationTtlSeconds = Mathf.Max(5.0f, invitationTtlSeconds);
			invitationSweepIntervalSeconds = Mathf.Max(0.1f, invitationSweepIntervalSeconds);
			invitationSweepMaxScan = Mathf.Max(1, invitationSweepMaxScan);
			invitationSweepMaxRemove = Mathf.Max(1, invitationSweepMaxRemove);
			perTargetInviteCooldownSeconds = Mathf.Max(0.0f, perTargetInviteCooldownSeconds);
			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);
			runtimeData.EndUpdatePump();
			runtimeData.NextInvitationSweepUtc = DateTime.UtcNow;

			/* Every field this ScriptableObject carries is reset here.
			 *
			 * A ServerBehaviour asset is not recreated between runs when Unity's domain reload is
			 * disabled, so instance state survives from the last one — the same lifetime that made
			 * the static damage subscriptions above need removing before adding. The scene-group
			 * pool is the one that actually bites: it holds IPlayerCharacter references, so left
			 * alone it keeps a previous session's destroyed character objects alive and hands them
			 * to the first vitals push of the new one. */
			ResetTransientState();

			Log.Debug("PartySystem", $"Initialized (MaxPartySize={MaxPartySize}, UpdatePumpRate={UpdatePumpRate}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Called when the system is being destroyed. Unregisters broadcast handlers and character events.
		/// </summary>
		public override void OnDeinitialize()
		{
			// Static registry: a command left behind outlives this ScriptableObject and would
			// run against a destroyed instance. See ChatHelper.RemoveCommands.
			ChatHelper.RemoveCommands(new[] { "/pi", "/invite" });

			/* Same argument as the chat commands, and the same failure: these are static events,
			 * so a handler left behind keeps this destroyed ScriptableObject alive and runs on
			 * every hit landed for the rest of the process. Removed unconditionally, before the
			 * Server null test below, because a handler is just as dangerous when the server is
			 * already gone. */
			ICharacterDamageController.OnDamaged -= CharacterDamageController_OnDamaged;
			ICharacterDamageController.OnHealed -= CharacterDamageController_OnHealed;

			if (Server == null)
			{
				Log.Error("PartySystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue(drainAll: true);

			ResetTransientState();

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<PartyCreateBroadcast>(OnServerPartyCreateBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyInviteBroadcast>(OnServerPartyInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyAcceptInviteBroadcast>(OnServerPartyAcceptInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyDeclineInviteBroadcast>(OnServerPartyDeclineInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyLeaveBroadcast>(OnServerPartyLeaveBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyRemoveBroadcast>(OnServerPartyRemoveBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PartyChangeRankBroadcast>(OnServerPartyChangeRankBroadcastReceived);

			// Character system events
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnConnect -= CharacterSystem_OnConnect;
				characterSystem.OnDisconnect -= CharacterSystem_OnDisconnect;
			}

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicUpdate);
			}
		}

		/// <summary>
		/// Clears every piece of per-run state this asset carries.
		/// </summary>
		/// <remarks>
		/// Called on both initialize and deinitialize. The pooled lists are emptied rather than
		/// dropped so the pool itself survives, which is the only reason it exists; what must not
		/// survive is what they were holding.
		/// </remarks>
		private void ResetTransientState()
		{
			ReleaseSceneGroups();
			vitalsGroupPool.Clear();
			vitalsEntryBuffer.Clear();
			vitalsBuffBuffer.Clear();

			pendingLeadershipRechecks.Clear();
			dueLeadershipRechecks.Clear();
			leadershipAuditBuffer.Clear();
			leadershipAuditCursor = 0;

			nextMeterSweepTime = 0.0f;
			nextLeadershipAuditUtc = DateTime.MinValue;
			nextCacheSweepUtc = DateTime.MinValue;
		}

		/// <summary>
		/// Drains queued main-thread actions from the IPartySystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<IPartySystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IPartySystemMainThreadQueueData>(action);
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
			SweepPendingInvitations();
			SweepIngressGuards();
			SweepCombatMeters();
		}

		/// <summary>
		/// Performs a bounded cleanup pass over finished combat meters.
		/// </summary>
		/// <remarks>
		/// A meter is created for any party member who lands a hit, and nothing else ever removes
		/// one — a character who logs out mid-fight would leave its accumulator behind forever.
		/// The stale threshold is generously past the encounter timeout so the sweep can never
		/// race a live fight; an entry it drops early would only cost that character a reset
		/// meter, but not dropping them at all costs the process memory that never comes back.
		/// </remarks>
		private void SweepCombatMeters()
		{
			if (Server == null ||
				!Server.DataContainerRegistry.TryGet<IPartyCombatMeterData>(out var meterData))
			{
				return;
			}

			float now = Time.unscaledTime;
			if (now < nextMeterSweepTime)
			{
				return;
			}

			nextMeterSweepTime = now + meterSweepIntervalSeconds;

			meterData.Sweep(now, encounterTimeoutSeconds * 4.0f, meterSweepMaxScan, meterSweepMaxRemove);
		}

		/// <summary>
		/// Records damage dealt against the attacker's per-encounter meter.
		/// </summary>
		/// <param name="attacker">The character that dealt the damage, or null.</param>
		/// <param name="defender">The character that took it.</param>
		/// <param name="amount">Damage applied after modifiers.</param>
		/// <param name="damageAttribute">The damage type, unused here.</param>
		private void CharacterDamageController_OnDamaged(ICharacter attacker, ICharacter defender, int amount, DamageAttributeTemplate damageAttribute)
		{
			RecordCombatMeterContribution(attacker, amount, isHealing: false);
		}

		/// <summary>
		/// Records healing done against the healer's per-encounter meter.
		/// </summary>
		/// <param name="healer">The character that did the healing, or null.</param>
		/// <param name="healed">The character that received it.</param>
		/// <param name="amount">Healing applied.</param>
		private void CharacterDamageController_OnHealed(ICharacter healer, ICharacter healed, int amount)
		{
			RecordCombatMeterContribution(healer, amount, isHealing: true);
		}

		/// <summary>
		/// Credits one combat contribution to the controlling player's meter.
		/// </summary>
		/// <param name="source">The character that acted.</param>
		/// <param name="amount">The amount dealt or healed.</param>
		/// <param name="isHealing">True for healing, false for damage.</param>
		/// <remarks>
		/// <para>
		/// Runs on every landed hit on this scene server, so it is written to reject as early and
		/// as cheaply as possible: a null actor, then anything that is not ultimately a player,
		/// then anyone with no party. Only the last of those needs a component lookup.
		/// </para>
		/// <para>
		/// Credit is resolved to the controlling PLAYER, so a hunter whose pet does the damage
		/// reads as having done it — the same rule the loot-contribution path applies, for the
		/// same reason. Nothing that cannot be traced back to a player is metered at all: the
		/// meter exists to be drawn on a party frame, and nothing else has one.
		/// </para>
		/// </remarks>
		private void RecordCombatMeterContribution(ICharacter source, int amount, bool isHealing)
		{
			if (amount <= 0 || source == null)
			{
				return;
			}

			IPlayerCharacter credit = source as IPlayerCharacter;
			if (credit == null)
			{
				/* A pet is an NPC, so without this a hunter fighting entirely through their pet
				 * would show a flat zero on the party frame for a fight they carried. */
				if (source is Pet pet)
				{
					credit = pet.PetOwner as IPlayerCharacter;
				}

				if (credit == null)
				{
					return;
				}
			}

			// Metered only for characters whose numbers somebody can actually see.
			if (!credit.TryGet(out IPartyController partyController) ||
				partyController.ID < 1)
			{
				return;
			}

			if (Server == null ||
				!Server.DataContainerRegistry.TryGet<IPartyCombatMeterData>(out var meterData))
			{
				return;
			}

			float now = Time.unscaledTime;

			if (isHealing)
			{
				meterData.RecordHealing(credit.ID, amount, now, encounterTimeoutSeconds);
			}
			else
			{
				meterData.RecordDamage(credit.ID, amount, now, encounterTimeoutSeconds);
			}
		}

		/// <summary>
		/// Attempts to acquire ingress debounce and in-flight guard for a connection operation.
		/// </summary>
		private bool TryBeginIngressGuard(int connectionId, IngressOperation operation, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, (byte)operation, ingressDebounceMilliseconds, out guardKey);
		}

		/// <summary>
		/// Releases an ingress in-flight guard key.
		/// </summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server?.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData) == true)
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Performs bounded cleanup of stale ingress guard entries.
		/// </summary>
		private void SweepIngressGuards()
		{
			if (Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}
		}

		/// <summary>
		/// Performs a bounded TTL sweep over pending party invitations.
		/// </summary>
		private void SweepPendingInvitations()
		{
			if (!Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;
			if (nowUtc < runtimeData.NextInvitationSweepUtc)
			{
				return;
			}

			runtimeData.NextInvitationSweepUtc = nowUtc.AddSeconds(invitationSweepIntervalSeconds);

			runtimeData.SweepExpiredInvitations(
				nowUtc,
				TimeSpan.FromSeconds(invitationTtlSeconds),
				invitationSweepMaxScan,
				invitationSweepMaxRemove);

			/* Cooldown entries are pure memory and nothing else removes them, so they get the
			 * same bounded sweep. Their TTL is the cooldown itself: past it the entry can no
			 * longer refuse anything. */
			runtimeData.SweepInviteCooldowns(
				nowUtc,
				TimeSpan.FromSeconds(perTargetInviteCooldownSeconds),
				invitationSweepMaxScan,
				invitationSweepMaxRemove);
		}

		/// <summary>
		/// Pushes live vitals to every party member, grouped by the scene they are standing in.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The roster payload carries a health figure, but it reads it from the party database
		/// row — and that row is written on connect and on disconnect and at no other time. Every
		/// party bar therefore sat frozen at the value its owner logged in with, for the whole
		/// session, which made the field actively misleading rather than merely stale.
		/// </para>
		/// <para>
		/// Fixing it by persisting vitals on a timer would be a database write per member per tick
		/// for values nobody needs durably. Instead this reads the in-memory controllers, which
		/// are already authoritative and already here, and sends one message per scene group
		/// rather than one per member per party.
		/// </para>
		/// <para>
		/// <b>Grouped by Unity scene, not by scene server.</b> One scene server hosts many scenes
		/// — every open-world zone it owns and every dungeon instance running on it — and members
		/// of the same party are routinely spread across them. Sending one payload for the whole
		/// server would tell a player in a dungeon the live health of a party member standing in a
		/// city, which is precisely the claim the greyed-out facade exists to avoid making. The
		/// scene handle is the local identity of a loaded scene inside this process, which is the
		/// right granularity here for the same reason it is the wrong one across processes: both
		/// characters are in this process or neither is in the payload.
		/// </para>
		/// <para>
		/// A member on another scene server, in another scene, or offline is simply absent, and
		/// absence is the signal — see <see cref="PartyMemberVitalsUpdateBroadcast"/>.
		/// </para>
		/// </remarks>
		private void BroadcastPartyVitals()
		{
			if (!Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var mappingData) ||
				mappingData.PartyCharacterTracker.Count == 0 ||
				!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
			{
				return;
			}

			Server.DataContainerRegistry.TryGet<IPartyCombatMeterData>(out var meterData);

			float now = Time.unscaledTime;

			foreach (KeyValuePair<long, HashSet<long>> kvp in mappingData.PartyCharacterTracker)
			{
				HashSet<long> memberIDs = kvp.Value;
				if (memberIDs == null || memberIDs.Count < 1)
				{
					continue;
				}

				GroupPartyMembersByScene(memberIDs, characterMappingData);

				foreach (KeyValuePair<int, List<IPlayerCharacter>> group in vitalsSceneGroups)
				{
					BroadcastSceneGroupVitals(group.Value, meterData, now);
				}

				ReleaseSceneGroups();
			}
		}

		/// <summary>
		/// Buckets a party's locally-hosted members by the scene each is standing in.
		/// </summary>
		/// <param name="memberIDs">Character IDs of the party's members on this scene server.</param>
		/// <param name="characterMappingData">Character lookup for this scene server.</param>
		private void GroupPartyMembersByScene(HashSet<long> memberIDs, ICharacterMappingData<NetworkConnection> characterMappingData)
		{
			ReleaseSceneGroups();

			foreach (long memberID in memberIDs)
			{
				if (!characterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter member) ||
					member == null ||
					member.Owner == null)
				{
					continue;
				}

				GameObject memberObject = member.GameObject;
				if (memberObject == null)
				{
					continue;
				}

				Scene scene = memberObject.scene;
				if (!scene.IsValid())
				{
					continue;
				}

				if (!vitalsSceneGroups.TryGetValue(scene.handle, out List<IPlayerCharacter> group))
				{
					group = vitalsGroupPool.Count > 0 ? vitalsGroupPool.Pop() : new List<IPlayerCharacter>();
					group.Clear();
					vitalsSceneGroups.Add(scene.handle, group);
				}

				group.Add(member);
			}
		}

		/// <summary>
		/// Returns every scene group's member list to the pool.
		/// </summary>
		private void ReleaseSceneGroups()
		{
			foreach (KeyValuePair<int, List<IPlayerCharacter>> group in vitalsSceneGroups)
			{
				group.Value.Clear();
				vitalsGroupPool.Push(group.Value);
			}
			vitalsSceneGroups.Clear();
		}

		/// <summary>
		/// Builds and sends one scene group's vitals payload to that group's members.
		/// </summary>
		/// <param name="members">The party members sharing one scene.</param>
		/// <param name="meterData">Combat meters, or null when the container is unavailable.</param>
		/// <param name="now">Current unscaled time, in seconds.</param>
		private void BroadcastSceneGroupVitals(List<IPlayerCharacter> members, IPartyCombatMeterData meterData, float now)
		{
			if (members == null || members.Count < 1)
			{
				return;
			}

			vitalsEntryBuffer.Clear();

			for (int i = 0; i < members.Count; ++i)
			{
				IPlayerCharacter member = members[i];

				ObservedBuffEntry[] buffs = BuildObservedBuffs(member, now);

				/* The buff array is the bulk of this payload and it rarely changes, so it is sent
				 * only when the visible set actually differs from what this member last had sent.
				 *
				 * The rest of the entry still goes out every pump even when nothing moved. That is
				 * deliberate: the client greys a member out by counting the pumps they were ABSENT
				 * from (UITKParty.VitalsMisses), so dropping an unchanged member from the payload
				 * would read as "they went away" rather than "nothing happened". Omitting just the
				 * array keeps that signal intact while removing what actually costs bytes. */
				bool buffsChanged = HasObservedBuffSetChanged(member.ID, buffs);

				PartyMemberVitalsEntry entry = new PartyMemberVitalsEntry()
				{
					CharacterID = member.ID,
					BuffsChanged = buffsChanged,
					Buffs = buffsChanged ? buffs : null,
				};

				if (member.TryGet(out ICharacterAttributeController attributeController))
				{
					/* Quantised to a byte apiece. These drive a bar and a percentage readout, so
					 * 1/255 is finer than anything the panel can draw, and it replaces four bytes
					 * per value with one. */
					entry.HealthPCT = PartyVitalsQuantiser.FractionToByte(attributeController.GetHealthResourceAttributeCurrentPercentage());
					entry.ManaPCT = PartyVitalsQuantiser.FractionToByte(attributeController.GetManaResourceAttributeCurrentPercentage());
					entry.StaminaPCT = PartyVitalsQuantiser.FractionToByte(attributeController.GetStaminaResourceAttributeCurrentPercentage());
				}

				if (meterData != null)
				{
					PartyCombatMeterSample sample = meterData.GetSample(member.ID, now, encounterTimeoutSeconds, meterMinimumWindowSeconds);
					// Whole points per second; the meter is displayed rounded and clamps at 65535.
					entry.DamagePerSecond = PartyVitalsQuantiser.RateToUInt16(sample.DamagePerSecond);
					entry.HealPerSecond = PartyVitalsQuantiser.RateToUInt16(sample.HealPerSecond);
				}

				vitalsEntryBuffer.Add(entry);
			}

			PartyMemberVitalsUpdateBroadcast broadcast = new PartyMemberVitalsUpdateBroadcast()
			{
				Members = vitalsEntryBuffer.ToArray(),
			};

			/* Sent to every member of the group INCLUDING the one it describes. A player's own row
			 * is drawn from the same payload as everyone else's, so there is one code path on the
			 * client rather than two that can drift; the panel refines its own bars from local
			 * state between pushes, which is an addition to this rather than a replacement. */
			for (int i = 0; i < members.Count; ++i)
			{
				Server.NetworkWrapper.Broadcast(members[i].Owner, broadcast, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// True when <paramref name="buffs"/> differs from the set last sent for this character,
		/// recording the new set as sent.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The signature deliberately ignores <see cref="ObservedBuffEntry.RemainingSeconds"/>'s
		/// exact value and keeps only whole seconds. Remaining time falls continuously, so hashing
		/// it at full precision would report a change on every single pump and the gate would never
		/// close — while a viewer reading a duration off an icon cannot see finer than a second
		/// anyway. Stacks and the template set are compared exactly, because those are the changes
		/// a player is actually watching for.
		/// </para>
		/// <para>
		/// Order-sensitive by construction: <c>BuildObservedBuffs</c> walks a
		/// <c>SortedDictionary</c>, so an unchanged set always hashes identically.
		/// </para>
		/// </remarks>
		/// <param name="characterID">Character the buffs belong to.</param>
		/// <param name="buffs">The set about to be sent, or null when the character has none.</param>
		/// <returns>True when the array must be included in this payload.</returns>
		private bool HasObservedBuffSetChanged(long characterID, ObservedBuffEntry[] buffs)
		{
			int signature = ComputeObservedBuffSignature(buffs);

			if (lastSentBuffSignature.TryGetValue(characterID, out int previous) && previous == signature)
			{
				return false;
			}

			lastSentBuffSignature[characterID] = signature;
			return true;
		}

		/// <summary>Content signature of an observed-buff set. See <see cref="HasObservedBuffSetChanged"/>.</summary>
		internal static int ComputeObservedBuffSignature(ObservedBuffEntry[] buffs)
		{
			if (buffs == null || buffs.Length < 1)
			{
				return 0;
			}

			unchecked
			{
				// 17/31 rather than a plain sum, so a stack moving between two buffs still differs.
				int hash = 17;
				hash = (hash * 31) + buffs.Length;
				for (int i = 0; i < buffs.Length; ++i)
				{
					hash = (hash * 31) + buffs[i].TemplateID;
					hash = (hash * 31) + buffs[i].Stacks;
					hash = (hash * 31) + Mathf.FloorToInt(buffs[i].RemainingSeconds);
				}
				// Never collides with the "no buffs at all" signature above.
				return hash == 0 ? 1 : hash;
			}
		}

		/// <summary>
		/// Copies a member's server-filtered observed buffs, re-based to the current moment.
		/// </summary>
		/// <param name="member">The member whose buffs to read.</param>
		/// <param name="now">Current unscaled time, in seconds.</param>
		/// <returns>The member's visible buffs and debuffs, or null when it has none.</returns>
		/// <remarks>
		/// <para>
		/// Read from <c>IBuffController.ObservedBuffs</c> rather than from the raw buff dictionary,
		/// so this reads the same server-assembled list every other observer path does rather than
		/// opening a second source. The server keeps its own copy current
		/// because the push RPC runs locally as well as on observers.
		/// </para>
		/// <para>
		/// The seconds in that list were correct when it was last PUSHED, which is only when the
		/// buff SET changed — for a twenty-minute buff that can be a very long time ago, and a
		/// client counting down from it would show the buff expiring twenty minutes late. Each
		/// entry is therefore re-based by the age of the push before it goes on the wire, and one
		/// that has run out in the meantime is dropped rather than sent as a zero-length bar.
		/// </para>
		/// <para>
		/// Returns null rather than an empty array for a member with no buffs. Almost every entry
		/// in a payload is that case, and an empty array is still an allocation and still four
		/// bytes on the wire per member per second.
		/// </para>
		/// </remarks>
		private ObservedBuffEntry[] BuildObservedBuffs(IPlayerCharacter member, float now)
		{
			if (!member.TryGet(out IBuffController buffController))
			{
				return null;
			}

			SortedDictionary<int, Buff> source = buffController.Buffs;
			if (source == null || source.Count < 1)
			{
				return null;
			}

			/* Read straight off the buff container, at the server's current tick.
			 *
			 * This used to read a display list and then re-base every entry by the age of the last
			 * push, because those seconds were only correct at the moment the SET changed — for a
			 * twenty-minute buff that could be many minutes stale. There is no separate list any
			 * more and no push age to correct for: the duration is computed from the live tick, so
			 * the arithmetic that existed to repair staleness is simply gone. */
			uint currentTick = buffController.GetCurrentDomainTick();

			vitalsBuffBuffer.Clear();

			foreach (Buff buff in source.Values)
			{
				BaseBuffTemplate template = buff?.Template;
				if (template == null)
				{
					continue;
				}

				float remaining = buff.RemainingSeconds(currentTick);

				// A permanent buff carries no duration at all and is passed straight through.
				if (template.Duration > 0.0f && remaining <= 0.0f)
				{
					continue;
				}

				vitalsBuffBuffer.Add(new ObservedBuffEntry()
				{
					TemplateID = template.ID,
					Stacks = buff.Stacks,
					RemainingSeconds = remaining,
					TotalSeconds = template.Duration,
				});

				/* Bounded. See maxVitalsBuffsPerMember: this list has no natural size limit and
				 * this payload is sent party-size times per party per second. Truncated from the
				 * end of the server's own ordering, which is by template ID and therefore stable —
				 * so the entries that survive the cap are the same ones each tick rather than a
				 * set that reshuffles and makes the strip flicker. */
				if (vitalsBuffBuffer.Count >= maxVitalsBuffsPerMember)
				{
					break;
				}
			}

			return vitalsBuffBuffer.Count > 0 ? vitalsBuffBuffer.ToArray() : null;
		}

		/// <summary>
		/// Periodic callback that fetches and processes party updates from the database asynchronously.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicUpdate(float deltaTime)
		{
			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			/* Runs on every tick, independently of the database pump below and of whether that
			 * pump is already in flight. Vitals are the roster fields that change constantly, and
			 * they are the fields that never needed a database round trip to read — the attribute,
			 * buff and meter state is all right here in memory. */
			BroadcastPartyVitals();

			SweepPartyRuntimeCaches();
			DrainLeadershipRechecks();
			AuditPartyLeadership();

			if (!Server.DataContainerRegistry.TryGet<IPartySystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			if (!runtimeData.TryBeginUpdatePump())
			{
				return;
			}

			// Snapshot containers on main thread to avoid accessing them from worker threads
			if (!Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var mappingData))
			{
				runtimeData.EndUpdatePump();
				return;
			}

			List<long> partyIds = new List<long>(mappingData.PartyCharacterTracker.Keys);
			DateTime lastFetch = runtimeData.LastFetchTime;

			if (!TryEnqueueAsyncWork(() => FetchAndProcessPartyUpdatesAsync(partyIds, lastFetch)))
			{
				runtimeData.EndUpdatePump();
			}
		}

		/// <summary>
		/// Prunes the runtime caches the party pump and the leadership repair leave behind.
		/// </summary>
		/// <remarks>
		/// <b>Outside the leadership feature switch, deliberately.</b> The processed-update record
		/// is grown by the PUMP, which runs whatever that switch is set to, so sweeping it from
		/// inside the leadership audit would have meant a server with the switch off growing one
		/// entry per party that ever changed and never dropping any of them. Absence observations
		/// are only ever created while the switch is on, so sweeping those unconditionally costs
		/// nothing when it is off.
		/// </remarks>
		private void SweepPartyRuntimeCaches()
		{
			DateTime nowUtc = DateTime.UtcNow;
			if (nowUtc < nextCacheSweepUtc)
			{
				return;
			}
			nextCacheSweepUtc = nowUtc.AddSeconds(leadershipAuditIntervalSeconds);

			if (!Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData sweepData))
			{
				return;
			}

			sweepData.SweepLeaderAbsences(nowUtc, TimeSpan.FromSeconds(leadershipAbsenceGraceSeconds * 4.0));

			/* A processed-update record only has to outlive the window in which its update can
			 * still be re-fetched, which is the skew allowance. Several times that is generous,
			 * and past it the record is only holding memory for a party that has stopped
			 * changing. */
			sweepData.SweepProcessedPartyUpdates(nowUtc, TimeSpan.FromSeconds(Mathf.Max(60.0f, partyUpdateClockSkewAllowanceSeconds * 10.0f)));
		}

		/// <summary>
		/// Queues one party for a prompt leadership re-check.
		/// </summary>
		/// <param name="partyID">The party to look at again.</param>
		/// <remarks>
		/// Last writer wins, so a burst of departures from the same party settles into a single
		/// look after the last of them rather than one per member.
		/// </remarks>
		private void ScheduleLeadershipRecheck(long partyID)
		{
			ScheduleLeadershipRecheckAt(partyID, DateTime.UtcNow.AddSeconds(leadershipRecheckDelaySeconds));
		}

		/// <summary>
		/// Queues one party for a leadership re-check at a specific time.
		/// </summary>
		/// <param name="partyID">The party to look at again.</param>
		/// <param name="dueUtc">When to look.</param>
		/// <remarks>
		/// Used by the absence grace to arrange the second sighting that confirms a leader has
		/// really gone. Without it the first sighting would start a clock nothing ever came back
		/// to read, and the party would wait for the round-robin audit instead.
		/// <para>
		/// The later of the two times wins when a party is already queued, so a burst of
		/// departures cannot pull a confirmation forward to before its grace has elapsed.
		/// </para>
		/// </remarks>
		private void ScheduleLeadershipRecheckAt(long partyID, DateTime dueUtc)
		{
			if (partyID < 1 || !transferLeadershipOnDisconnect)
			{
				return;
			}

			if (pendingLeadershipRechecks.TryGetValue(partyID, out DateTime existingUtc) &&
				existingUtc > dueUtc)
			{
				return;
			}

			pendingLeadershipRechecks[partyID] = dueUtc;
		}

		/// <summary>
		/// Records an observation that a party's leader holds no session.
		/// </summary>
		/// <param name="partyID">The party being examined.</param>
		/// <param name="leaderCharacterID">The member holding the rank.</param>
		/// <param name="dueUtc">When the grace elapses, when this returns false.</param>
		/// <returns>True when the absence has been confirmed.</returns>
		private bool TryConfirmLeaderAbsent(long partyID, long leaderCharacterID, out DateTime dueUtc)
		{
			dueUtc = DateTime.UtcNow;

			return Server?.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData) == true &&
				   runtimeData.TryConfirmLeaderAbsent(partyID, leaderCharacterID, DateTime.UtcNow,
													  TimeSpan.FromSeconds(leadershipAbsenceGraceSeconds), out dueUtc);
		}

		/// <summary>
		/// Drops any absence observation being tracked for a party.
		/// </summary>
		/// <param name="partyID">The party to clear.</param>
		private void ClearLeaderAbsence(long partyID)
		{
			if (Server?.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData) == true)
			{
				runtimeData.ClearLeaderAbsence(partyID);
			}
		}

		/// <summary>
		/// Runs the leadership repair for every party whose scheduled re-check has come due.
		/// </summary>
		private void DrainLeadershipRechecks()
		{
			if (pendingLeadershipRechecks.Count < 1)
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;

			dueLeadershipRechecks.Clear();
			foreach (KeyValuePair<long, DateTime> pending in pendingLeadershipRechecks)
			{
				if (nowUtc < pending.Value)
				{
					continue;
				}

				dueLeadershipRechecks.Add(pending.Key);

				// Bounded per tick; the rest keep their due times and come back next time.
				if (dueLeadershipRechecks.Count >= leadershipRecheckMaxPerTick)
				{
					break;
				}
			}

			if (dueLeadershipRechecks.Count < 1)
			{
				return;
			}

			for (int i = 0; i < dueLeadershipRechecks.Count; ++i)
			{
				pendingLeadershipRechecks.Remove(dueLeadershipRechecks[i]);
			}

			/* Copied out of the scratch list. The task runs on a worker and the list is reused by
			 * the next drain, so handing it over directly would let the two share a collection. */
			List<long> partyIDs = new List<long>(dueLeadershipRechecks);

			if (!TryEnqueueAsyncWork(() => AuditPartyLeadershipAsync(partyIDs, "PartyLeadershipRecheck")))
			{
				/* Put back rather than dropped. These entries were removed a few lines above on
				 * the assumption the work would run; a full worker queue is a busy moment, not a
				 * reason for a party to stop being examined until the round-robin audit reaches
				 * it. Re-scheduled a little out so a queue that stays full does not spin. */
				DateTime retryUtc = nowUtc.AddSeconds(leadershipRecheckDelaySeconds);
				for (int i = 0; i < partyIDs.Count; ++i)
				{
					ScheduleLeadershipRecheckAt(partyIDs[i], retryUtc);
				}
			}
		}

		/// <summary>
		/// Examines a few locally-tracked parties and settles their leadership.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The backstop that makes leadership impossible to leave stuck. Every ordinary change to
		/// a party writes a party update, and the pump settles leadership for every party an
		/// update names — so a leader who disconnects, leaves, is kicked or is promoted is handled
		/// there, within a tick. What that cannot cover is a party nothing announces: a scene
		/// server that dies takes its members' disconnect handlers with it and writes no update,
		/// so if it was hosting the leader the party is led by an absent player and no event will
		/// ever bring anyone back to look at it.
		/// </para>
		/// <para>
		/// Every scene server hosting a member of the party runs this, so the party is covered as
		/// long as ANY server holding one of its members is alive — which is exactly the condition
		/// under which somebody is there to be stuck. Two servers auditing the same party at once
		/// reach the same answer by construction and the second write is a version-gated no-op;
		/// the mutation claim keeps them from interleaving in the first place.
		/// </para>
		/// </remarks>
		private void AuditPartyLeadership()
		{
			if (!transferLeadershipOnDisconnect)
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;
			if (nowUtc < nextLeadershipAuditUtc)
			{
				return;
			}
			nextLeadershipAuditUtc = nowUtc.AddSeconds(leadershipAuditIntervalSeconds);

			/* Observations belonging to parties nothing looks at any more. One that is going to
			 * resolve does so within a grace period, because whatever started it also scheduled
			 * the second look; one that has outlived several belongs to a party whose last local
			 * member has gone, and nothing would ever read it. */

			if (!Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var mappingData) ||
				mappingData.PartyCharacterTracker.Count < 1)
			{
				return;
			}

			/* Snapshotted on the main thread. The tracker is main-thread state and the audit runs
			 * on a worker, so the worker is handed a list rather than the collection. */
			leadershipAuditBuffer.Clear();
			foreach (long partyID in mappingData.PartyCharacterTracker.Keys)
			{
				leadershipAuditBuffer.Add(partyID);
			}

			if (leadershipAuditCursor >= leadershipAuditBuffer.Count)
			{
				leadershipAuditCursor = 0;
			}

			int take = Mathf.Min(leadershipAuditPartiesPerSweep, leadershipAuditBuffer.Count);
			List<long> partyIDs = new List<long>(take);
			for (int i = 0; i < take; ++i)
			{
				partyIDs.Add(leadershipAuditBuffer[(leadershipAuditCursor + i) % leadershipAuditBuffer.Count]);
			}
			leadershipAuditCursor += take;

			TryEnqueueAsyncWork(() => AuditPartyLeadershipAsync(partyIDs, "PartyLeadershipAudit"));
		}

		/// <summary>
		/// Reads and settles the leadership of each party in a sweep.
		/// </summary>
		/// <param name="partyIDs">Parties to examine.</param>
		/// <param name="caller">Name used in log lines, so a scheduled re-check and the round-robin sweep can be told apart.</param>
		/// <returns>Asynchronous audit task.</returns>
		private async Task AuditPartyLeadershipAsync(List<long> partyIDs, string caller)
		{
			try
			{
				if (partyIDs == null ||
					partyIDs.Count < 1 ||
					Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}

				for (int i = 0; i < partyIDs.Count; ++i)
				{
					long partyID = partyIDs[i];

					DatabaseResult<IReadOnlyList<CharacterPartyData>> membersResult = await charPartyService.FetchManyAsync(partyID);
					if (!membersResult.IsSuccess || membersResult.Data == null || membersResult.Data.Count < 1)
					{
						continue;
					}

					await RepairPartyLeadershipAsync(charPartyService, partyID, membersResult.Data, caller);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error auditing party leadership: {ex}");
			}
		}

		/// <summary>
		/// Asynchronously fetches party updates from the database and marshals the processing back to the main thread.
		/// </summary>
		/// <returns>Asynchronous fetch-and-process task.</returns>
		/// <param name="partyIds">List of party IDs to fetch updates for.</param>
		/// <param name="lastFetch">Timestamp of the last successful database fetch.</param>
		private async Task FetchAndProcessPartyUpdatesAsync(List<long> partyIds, DateTime lastFetch)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}
				if (partyIds == null || partyIds.Count == 0)
				{
					return;
				}

				/* Stamped BEFORE the query, not after it.
				 *
				 * The watermark used to be set to "now" on the main thread once the whole
				 * fetch-and-process round trip had finished, which silently discarded every party
				 * update written while that round trip was in flight — the next pump asked for
				 * changes since a moment those writes were already behind. A member joining,
				 * leaving or being promoted during a slow database call simply never reached the
				 * other scene servers, and nothing retried because as far as the pump was
				 * concerned it had already seen everything up to that instant.
				 *
				 * Taken before the read so the window can only ever re-deliver an update, never
				 * skip one. A duplicate costs one redundant roster broadcast; a skip costs a
				 * party that disagrees with itself until something else happens to it.
				 *
				 * Held back further by the skew allowance, because the rows are timestamped by
				 * whichever scene server wrote them and this mark is stamped here — two different
				 * clocks. See partyUpdateClockSkewAllowanceSeconds. */
				DateTime fetchStartedUtc = DateTime.UtcNow.AddSeconds(-partyUpdateClockSkewAllowanceSeconds);

				// Async DB fetch
				DatabaseResult<List<PartyUpdateData>> fetchResult = await partyUpdateService.FetchAsync(partyIds, lastFetch);
				if (!fetchResult.IsSuccess || fetchResult.Data == null || fetchResult.Data.Count < 1)
				{
					return;
				}

				List<PartyUpdateData> updates = fetchResult.Data;

				// For each unique party that was updated, fetch the current members
				HashSet<long> updatedParties = new HashSet<long>();
				Dictionary<long, IReadOnlyList<CharacterPartyData>> partyMembersMap = new Dictionary<long, IReadOnlyList<CharacterPartyData>>();
				Dictionary<long, DateTime> processedUpdateStamps = new Dictionary<long, DateTime>();

				Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData dedupeData);

				foreach (PartyUpdateData update in updates)
				{
					if (updatedParties.Contains(update.PartyID))
					{
						continue;
					}
					updatedParties.Add(update.PartyID);

					/* Already handled on an earlier tick.
					 *
					 * The watermark trails real time by the skew allowance, so an update stays in
					 * the fetch window for several pumps after it was written — five, at the
					 * defaults. Without this, each of those would re-read the roster, re-ask who is
					 * online and re-broadcast the party to everybody in it, all to reach the
					 * conclusion the first pass already reached. Skipping them is what lets the
					 * allowance be sized for the worst skew worth tolerating instead of being
					 * traded off against how much work the pump does. */
					if (dedupeData != null && dedupeData.HasProcessedPartyUpdate(update.PartyID, update.LastUpdate))
					{
						continue;
					}


					DatabaseResult<IReadOnlyList<CharacterPartyData>> membersResult = await charPartyService.FetchManyAsync(update.PartyID);
					if (membersResult.IsSuccess && membersResult.Data != null)
					{
						/* Last line of defence for the party's one-leader invariant.
						 *
						 * The removal paths hand leadership on before they delete anybody and the
						 * promotion path is serialised against them, so this should never fire —
						 * but "should never" is doing a lot of work across two scene servers,
						 * several async hops and a database that can refuse a write. Neither
						 * broken state has a way out on its own: a leaderless party cannot promote
						 * anybody (that needs a leader), and a two-leader party looks healthy to
						 * every check that merely asks whether a leader exists.
						 *
						 * The pump sees a party exactly when something changed it, which is exactly
						 * when its leadership could have broken, so this is both the cheapest place
						 * to notice and the earliest — including when the change was a leader
						 * disconnecting, which writes a party update on its way out. Two scene
						 * servers pumping the same party reach the same answer by construction,
						 * and the repair takes the party's mutation claim so it cannot land in the
						 * middle of a promotion — see RepairPartyLeadershipAsync.
						 *
						 * The roster is re-read after a repair so the broadcast below carries the
						 * new rank rather than the one that was just corrected. */
						if (membersResult.Data.Count > 0 &&
							(await RepairPartyLeadershipAsync(charPartyService, update.PartyID, membersResult.Data, "PartyUpdatePump")).Changed)
						{
							DatabaseResult<IReadOnlyList<CharacterPartyData>> repairedResult = await charPartyService.FetchManyAsync(update.PartyID);
							if (repairedResult.IsSuccess && repairedResult.Data != null)
							{
								membersResult = repairedResult;
							}
						}

						partyMembersMap[update.PartyID] = membersResult.Data;

						/* Stamped here, beside the roster that came back, and not where the update
						 * was read. A roster fetch that fails leaves the party out of the map and
						 * therefore out of this — so the update stays unprocessed and is picked up
						 * on a later tick, instead of being marked done for work that never
						 * happened. */
						processedUpdateStamps[update.PartyID] = update.LastUpdate;
					}
				}

				if (partyMembersMap.Count < 1)
				{
					/* Everything in this fetch had already been dealt with — but the watermark
					 * still has to move.
					 *
					 * Leaving it where it was would freeze it the first time a tick found nothing
					 * new: the same rows stay inside the window, every later tick skips them all,
					 * and the mark never advances again until some unrelated party changes. The
					 * fetch would then be re-reading every local party's update row for as long as
					 * the server ran. Advancing to the same instant the normal path uses is sound
					 * for the same reason it is sound there — everything at or before it has been
					 * handled. */
					TryEnqueueMainThread(() =>
					{
						if (Server?.DataContainerRegistry.TryGet(out IPartySystemRuntimeData watermarkData) == true)
						{
							watermarkData.LastFetchTime = fetchStartedUtc;
						}
					});
					return;
				}

				// Marshal all main-thread state changes + broadcasts
				bool enqueued = TryEnqueueMainThread(() =>
				{
					if (Server == null)
					{
						return;
					}

					// Update last fetch time
					if (Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData rtData))
					{
						rtData.LastFetchTime = fetchStartedUtc;
					}

					if (!Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var mapData))
					{
						return;
					}

					foreach (var kvp in partyMembersMap)
					{
						long partyID = kvp.Key;
						IReadOnlyList<CharacterPartyData> dbMembers = kvp.Value;

						var currentMemberIDs = new HashSet<long>(dbMembers.Count);
						for (int i = 0; i < dbMembers.Count; i++)
						{
							currentMemberIDs.Add(dbMembers[i].CharacterID);
						}

						// Check if we have previously cached the party member list
						if (mapData.PartyMemberTracker.TryGetValue(partyID, out var previousMembers))
						{
							// Compute the difference: members that are in previousMembers but not in currentMemberIDs
							List<long> difference = new List<long>();
							foreach (long prevID in previousMembers)
							{
								if (!currentMemberIDs.Contains(prevID))
								{
									difference.Add(prevID);
								}
							}

							foreach (long memberID in difference)
							{
								// Tell the member connection to leave their party immediately
								if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var partyCharacterMappingData) &&
									partyCharacterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter character) &&
									character != null &&
									character.TryGet(out IPartyController targetPartyController))
								{
									/* Rank is cleared with the ID. Leaving it set left an ex-member
									 * carrying PartyRank.Leader, and every leader-only gate in this
									 * file reads the pair — so the moment that character joined
									 * another party it would arrive already believing it led the
									 * one it just joined, until the next pump corrected it. */
									targetPartyController.ID = 0;
									targetPartyController.Rank = PartyRank.None;

									// The tracker keeps them until something says otherwise; this is that something.
									RemovePartyCharacterTracker(partyID, memberID);

									Server.NetworkWrapper.Broadcast(character.Owner, new PartyLeaveBroadcast(), true, Channel.Reliable);
								}
							}
						}
						/* Cache the party member IDs — unless the evictions above took this
						 * server's last member of the party with them, in which case the party is
						 * no longer ours to track at all. RemovePartyCharacterTracker drops both
						 * trackers together for exactly that reason, and re-adding one of them
						 * here unconditionally would leave a member list behind for a party this
						 * server has stopped pumping, with nothing left that would ever remove
						 * it. */
						if (mapData.PartyCharacterTracker.ContainsKey(partyID))
						{
							mapData.PartyMemberTracker[partyID] = currentMemberIDs;
						}
						else
						{
							mapData.PartyMemberTracker.Remove(partyID);
						}

						var addBroadcasts = new List<PartyAddBroadcast>(dbMembers.Count);
						for (int i = 0; i < dbMembers.Count; i++)
						{
							var x = dbMembers[i];
							addBroadcasts.Add(new PartyAddBroadcast()
							{
								PartyID = x.PartyID,
								CharacterID = x.CharacterID,
								Rank = (PartyRank)x.Rank,
								HealthPCT = x.HealthPCT,
							});
						}

						PartyAddMultipleBroadcast partyAddBroadcast = new PartyAddMultipleBroadcast()
						{
							Members = addBroadcasts.ToArray(),
						};

						if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
						{
							// Tell all of the local party members to update their party member lists
							foreach (CharacterPartyData member in dbMembers)
							{
								if (characterMappingData.CharactersByID.TryGetValue(member.CharacterID, out IPlayerCharacter character))
								{
									/* Only a character whose controller names THIS party is told
									 * about it.
									 *
									 * The test used to be "is it in any party at all", which is a
									 * different question and answers yes for somebody who has
									 * since joined a DIFFERENT one. A pump cycle that read this
									 * party's rows just before that character's row moved would
									 * then set their rank from the old party, and send them the
									 * old party's roster — and the client applies a roster payload
									 * by adopting its PartyID, so their panel would flip to the
									 * party they had left until the next pump flipped it back.
									 *
									 * Refreshing is all this loop does. Every way a character
									 * actually enters a party sets the controller itself, so a
									 * disagreement here is always a stale or in-flight state that
									 * the owning path will settle, never something to be
									 * corrected from a roster read several awaits old. */
									if (!character.TryGet(out IPartyController partyController) ||
										partyController.ID != member.PartyID)
									{
										continue;
									}
									partyController.Rank = (PartyRank)member.Rank;
									Server.NetworkWrapper.Broadcast(character.Owner, partyAddBroadcast, true, Channel.Reliable);
								}
							}
						}
					}
				});

				/* Marked only once the work is actually on its way.
				 *
				 * Recording these at the point the update was READ would drop it outright whenever
				 * the main-thread queue was full: the pump would skip it on every later tick, and
				 * the roster change it carried would never reach anybody. Left unmarked, it is
				 * simply picked up again next tick — which is the whole reason the watermark trails
				 * far enough for there to be a next chance. */
				if (enqueued && dedupeData != null)
				{
					foreach (KeyValuePair<long, DateTime> stamp in processedUpdateStamps)
					{
						dedupeData.MarkPartyUpdateProcessed(stamp.Key, stamp.Value);
					}
				}
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error fetching/processing party updates: {ex}");
			}
			finally
			{
				if (Server?.DataContainerRegistry.TryGet<IPartySystemRuntimeData>(out var runtimeData) == true)
				{
					runtimeData.EndUpdatePump();
				}
			}
		}

		/// <summary>
		/// Adds a mapping for the Party to Party Members connected to this Scene Server.
		/// </summary>
		public void AddPartyCharacterTracker(long partyID, long characterID)
		{
			if (partyID == 0)
			{
				return;
			}
			if (!Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var mappingData))
			{
				return;
			}
			var tracker = mappingData.PartyCharacterTracker;
			if (!tracker.TryGetValue(partyID, out HashSet<long> characterIDs))
			{
				tracker.Add(partyID, characterIDs = new HashSet<long>());
			}
			if (!characterIDs.Contains(characterID))
			{
				characterIDs.Add(characterID);
			}
		}

		/// <summary>
		/// Removes the mapping of Party to Party Members connected to this Scene Server.
		/// </summary>
		public void RemovePartyCharacterTracker(long partyID, long characterID)
		{
			if (partyID == 0)
			{
				return;
			}
			if (!Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var mappingData))
			{
				return;
			}
			if (mappingData.PartyCharacterTracker.TryGetValue(partyID, out HashSet<long> characterIDs))
			{
				characterIDs.Remove(characterID);

				// If there are no active party members we can remove the character and member trackers for the party.
				if (characterIDs.Count < 1)
				{
					mappingData.PartyCharacterTracker.Remove(partyID);
					mappingData.PartyMemberTracker.Remove(partyID);
				}
			}
		}

		/// <summary>
		/// Handles character connect event, adding the character to the party tracker and saving party update.
		/// </summary>
		public void CharacterSystem_OnConnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}

			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}

			if (!character.TryGet(out IPartyController partyController) ||
				partyController.ID < 1)
			{
				// not in a Party
				return;
			}

			AddPartyCharacterTracker(partyController.ID, character.ID);

			// Fire-and-forget async DB persist
			long characterID = character.ID;
			long partyID = partyController.ID;
			byte rank = (byte)partyController.Rank;
			float healthPCT = character.TryGet(out ICharacterAttributeController attrController)
				? attrController.GetHealthResourceAttributeCurrentPercentage()
				: 0.0f;

			EnqueuePersistence(() => PersistPartyMemberAndNotifyAsync(characterID, partyID, rank, healthPCT), characterID);
		}

		/// <summary>
		/// Handles character disconnect event, removing the character from the party tracker and saving party update.
		/// </summary>
		public void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			IPartySystemRuntimeData runtimeData = null;
			if (character != null && Server.DataContainerRegistry.TryGet(out runtimeData))
			{
				runtimeData.RemovePendingInvitation(character.ID);
			}

			if (character == null)
			{
				return;
			}

			/* Dropped whether or not this character was in a party, and before the party tests
			 * below can return early. A meter is created for anybody who lands a hit while
			 * grouped, and if the character is not cleared here it survives their whole logout —
			 * and, worse, is still there when they log back in, so their first fight of the new
			 * session opens with the tail of a fight from the last one. */
			if (Server != null &&
				Server.DataContainerRegistry.TryGet<IPartyCombatMeterData>(out var meterData))
			{
				meterData.Forget(character.ID);
			}

			/* Same reasoning as the meter above: a stale signature would make this character's
			 * first payload after they return omit their buff array, leaving their party's icons
			 * empty until something about their buffs happened to change. */
			lastSentBuffSignature.Remove(character.ID);

			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}

			if (!character.TryGet(out IPartyController partyController) ||
				partyController.ID < 1)
			{
				// not in a Party
				return;
			}

			long disconnectedPartyID = partyController.ID;
			RemovePartyCharacterTracker(disconnectedPartyID, character.ID);

			/* A kick or a leave deletes the membership row from a background task, and this
			 * character's controller still carries the old party ID until that delete lands.
			 * Persisting from it here would UPSERT the row straight back — putting the player
			 * into the party they had just left. Disconnecting inside that window is the whole
			 * exploit, and it is a window a player can aim for. */
			if (runtimeData != null && runtimeData.IsMembershipRemovalInFlight(character.ID))
			{
				return;
			}

			/* A disconnecting leader does not leave the party — the row survives — so the party
			 * is left being led by somebody who is not there, and every leader-only action refuses
			 * for everyone still playing.
			 *
			 * Nothing is done about that HERE, deliberately. This handler used to pick a successor
			 * from the local tracker and hand the rank over itself, and that is unfixably racy: the
			 * successor is chosen on this thread and promoted several awaits later, so a successor
			 * who disconnects in between is promoted anyway and the party is left led by another
			 * absent player with nothing to notice. Moving the check earlier or later only moves
			 * the window. And a scene server that dies outright runs no handler at all, so no
			 * amount of care here covers the case that matters most.
			 *
			 * The party update written below brings the pump back to this party within a tick, and
			 * the pump asks the database who is actually logged in and moves the rank if the
			 * holder is not — see RepairAbsentLeaderAsync. That is convergent rather than ordered,
			 * so it is correct however the state arose.
			 *
			 * It is scheduled to be asked AGAIN shortly, because this handler runs before the
			 * character's session is released and the first answer is therefore taken while the
			 * database still reports the leaver as logged in. See leadershipRecheckDelaySeconds.
			 * Scheduled for any member, not just one this server believes was the leader: the rank
			 * it believes is a pump-refreshed copy, and it is wrong in exactly the direction that
			 * would skip the check. */
			ScheduleLeadershipRecheck(disconnectedPartyID);

			EnqueuePersistence(() => PersistPartyUpdateAsync(disconnectedPartyID), character.ID);
		}

		/// <summary>
		/// Asynchronously persists a party member's data and triggers a party update notification.
		/// </summary>
		/// <param name="characterID">Character identifier to persist.</param>
		/// <param name="partyID">Party identifier associated with the character.</param>
		/// <param name="rank">Party rank value to persist.</param>
		/// <param name="healthPCT">Current health percentage snapshot.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task PersistPartyMemberAndNotifyAsync(long characterID, long partyID, byte rank, float healthPCT)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}

				DatabaseResult<CharacterPartyData?> existingResult = await charPartyService.FetchAsync(characterID);

				/* Connecting is not a membership event, so this must never CREATE one.
				 *
				 * A character kicked while they were offline still carries the party ID their
				 * last save wrote, and a load that raced the delete can hand it to this method.
				 * Persisting from it would insert the membership row straight back and put the
				 * player into the party they had been removed from — the offline twin of the
				 * disconnect-resurrection exploit the removal markers exist to close.
				 *
				 * Only a fetch that SUCCEEDED and found nothing is treated as proof of absence. A
				 * failed read is a database fault, not evidence, and is allowed to fall through to
				 * the ordinary refresh below where the upsert is harmless. */
				if (existingResult.IsSuccess && !existingResult.Data.HasValue)
				{
					await Log.Debug("PartySystem", $"Character {characterID} connected believing they were in party {partyID}, but they have no membership row. Clearing it.");
					ClearStalePartyMembershipOnMainThread(characterID, partyID);
					return;
				}

				/* The rank is taken from the ROW, never from the connecting character's copy of
				 * it. That copy was read when the character loaded, and leadership can have moved
				 * since — including because this very character disconnecting caused it to move.
				 * Writing it back here would hand the rank to a player who has just been demoted
				 * and give the party two leaders, which the pump then has to unpick. Connecting
				 * refreshes health and nothing else. */
				long version = 1;
				byte effectiveRank = rank;
				if (existingResult.IsSuccess && existingResult.Data.HasValue)
				{
					CharacterPartyData existing = existingResult.Data.Value;

					if (existing.PartyID != partyID)
					{
						/* They belong to a different party than the one they arrived believing in.
						 *
						 * Nothing is written to EITHER party: writing to the one they named would
						 * be writing to a party they are not in, and adopting the other from here
						 * would be a connect handler quietly performing a membership change. What
						 * is cleared is the state this server built from the wrong answer — the
						 * connect handler has already put them in the tracker under the party they
						 * named, and left there this server would pump that party's roster and
						 * vitals at somebody who is not in it. */
						await Log.Warning("PartySystem", $"Character {characterID} connected as a member of party {partyID} but their row names party {existing.PartyID}; clearing the stale membership.");
						ClearStalePartyMembershipOnMainThread(characterID, partyID);
						return;
					}

					version = existing.Version + 1;
					effectiveRank = existing.Rank;
				}

				CharacterPartyData partyData = new CharacterPartyData(0, version, characterID, partyID, effectiveRank, healthPCT);
				DatabaseResult persistResult = await charPartyService.PersistAsync(partyData, MaxPartySize);
				if (!persistResult.IsSuccess)
				{
					await Log.Warning("PartySystem", $"PersistPartyMemberAndNotifyAsync DB error (CharID={characterID}, PartyID={partyID}): {persistResult.ErrorCode} - {persistResult.ErrorMessage}");
					return;
				}
				DatabaseResult updateResult = await partyUpdateService.PersistAsync(partyID);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("PartySystem", $"PersistPartyMemberAndNotifyAsync party update notification failed (PartyID={partyID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error persisting party member (CharID={characterID}, PartyID={partyID}): {ex}");
			}
		}

		/// <summary>
		/// Clears a character's party state and tells its client, from a background task.
		/// </summary>
		/// <param name="characterID">The character whose membership has gone.</param>
		/// <param name="partyID">The party they believed they were in.</param>
		/// <remarks>
		/// For a character that arrived carrying a party it no longer belongs to — removed while
		/// it was offline. Everything here is main-thread state, so it is marshalled; the
		/// controller is only cleared if it still names the party this was called about, since by
		/// the time the action runs the character may legitimately have joined another one.
		/// </remarks>
		private void ClearStalePartyMembershipOnMainThread(long characterID, long partyID)
		{
			TryEnqueueMainThread(() =>
			{
				if (Server == null ||
					!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData) ||
					!characterMappingData.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character) ||
					character == null ||
					!character.TryGet(out IPartyController partyController) ||
					partyController.ID != partyID)
				{
					return;
				}

				partyController.ID = 0;
				partyController.Rank = PartyRank.None;

				RemovePartyCharacterTracker(partyID, characterID);

				if (character.Owner != null)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new PartyLeaveBroadcast(), true, Channel.Reliable);
				}
			});
		}

		/// <summary>
		/// Asynchronously persists a party update notification.
		/// </summary>
		/// <param name="partyID">Party identifier to mark as updated.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task PersistPartyUpdateAsync(long partyID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}

				DatabaseResult updateResult = await partyUpdateService.PersistAsync(partyID);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("PartySystem", $"PersistPartyUpdateAsync DB error (PartyID={partyID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error persisting party update (PartyID={partyID}): {ex}");
			}
		}

		/// <summary>
		/// Handles party creation broadcast, validates and creates a new party for the requesting character.
		/// </summary>
		public void OnServerPartyCreateBroadcastReceived(NetworkConnection conn, PartyCreateBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Create, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}

				IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();
				if (partyController == null || partyController.ID > 0)
				{
					// already in a party
					return;
				}

				// Capture immutable data for the async path
				long characterID = partyController.Character.ID;
				string sceneName = conn.FirstObject.gameObject.scene.name;
				float healthPCT = partyController.Character.TryGet(out ICharacterAttributeController attributeController)
					? attributeController.GetHealthResourceAttributeCurrentPercentage()
					: 0.0f;
				/* Stamped on the party at creation and never changed afterwards. A party belongs
				 * to the world server it was formed on; a member who later arrives on a different
				 * one is dropped from it rather than carrying it across. */
				long worldServerID = player.WorldServerID;

				deferGuardRelease = TryEnqueueIngressWork(() => CreatePartyAsync(conn, characterID, sceneName, healthPCT, worldServerID), guardKey, characterID);
				if (!deferGuardRelease) SendServerBusy(conn);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Asynchronously creates a new party, persists membership, and marshals state changes back to the main thread.
		/// </summary>
		/// <param name="conn">Requesting connection.</param>
		/// <param name="characterID">Requesting character identifier.</param>
		/// <param name="sceneName">Current scene name for broadcast context.</param>
		/// <param name="healthPCT">Current requester health percentage.</param>
		/// <returns>Asynchronous party-creation task.</returns>
		private async Task CreatePartyAsync(NetworkConnection conn, long characterID, string sceneName, float healthPCT, long worldServerID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IPartyService>(out var partyService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}

				DatabaseResult<long> createResult = await partyService.CreateAsync(worldServerID);
				if (!createResult.IsSuccess)
				{
					return;
				}

				long newPartyID = createResult.Data;

				CharacterPartyData partyData = new CharacterPartyData(0, 1, characterID, newPartyID, (byte)PartyRank.Leader, healthPCT);
				DatabaseResult persistResult = await charPartyService.PersistAsync(partyData, MaxPartySize);
				if (!persistResult.IsSuccess)
				{
					return;
				}

				// Marshal state changes + broadcast to main thread
				TryEnqueueMainThread(() =>
				{
					if (Server == null)
					{
						return;
					}

					IPartyController pc = conn.FirstObject?.GetComponent<IPartyController>();
					if (pc == null)
					{
						return;
					}

					/* The party row is already committed, so this state is applied
					 * UNCONDITIONALLY.
					 *
					 * It used to be gated on CharacterStateValidation.CanAct, which is an
					 * ACTION gate — right for deciding whether to honour a request, wrong for
					 * deciding whether to record its outcome. CanAct goes false on death, on a
					 * stun, and on teleport, and any of those landing in the handful of
					 * milliseconds between the request and the database answering left the
					 * player owning a party their client had never been told about: no roster,
					 * no Leave button, and no way out, because every party handler starts by
					 * reading the controller ID this branch skipped writing. The request was
					 * already validated when it arrived; this is bookkeeping for a decision the
					 * database has finished making. */
					pc.ID = newPartyID;
					pc.Rank = PartyRank.Leader;

					AddPartyCharacterTracker(newPartyID, characterID);

					// tell the character we made their party successfully
					Server.NetworkWrapper.Broadcast(conn, new PartyCreateBroadcast()
					{
						PartyID = newPartyID,
						Location = sceneName,
					}, true, Channel.Reliable);

					// Increment achievement for creating a party
					if (PartyCreateAchievementTemplate != null)
					{
						IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			
						if (character != null && character.TryGet(out IAchievementController achievementController) && CharacterStateValidation.CanAct(character))
						{
							achievementController.Increment(PartyCreateAchievementTemplate, 1);
						}
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error creating party (CharID={characterID}): {ex}");
			}
		}

		/// <summary>
		/// Handles party invitation broadcast, validates inviter and target, and sends invitation to the target character.
		/// Only party leaders can invite, and invitations are tracked to prevent duplicates.
		/// </summary>
		/// <param name="conn">Network connection of the inviter.</param>
		/// <param name="msg">PartyInviteBroadcast message containing inviter and target IDs.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyInviteBroadcastReceived(NetworkConnection conn, PartyInviteBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Invite, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				IPartyController inviter = conn.FirstObject.GetComponent<IPartyController>();

				// validate party leader is inviting
				if (inviter == null ||
					inviter.ID < 1 ||
					inviter.Rank != PartyRank.Leader)
				{
					return;
				}

				if (msg.TargetCharacterID < 1)
				{
					return;
				}

				/* Refused here rather than left to fail later. Inviting yourself falls through
				 * every check below and ends as "that player is already in a party" — which is
				 * true, and useless, and spends the per-target cooldown against yourself. The
				 * panel already refuses it; the server must not depend on the panel. */
				if (msg.TargetCharacterID == inviter.Character.ID)
				{
					return;
				}

				// Capture immutable data for the async path
				long inviterPartyID = inviter.ID;
				long inviterCharacterID = inviter.Character.ID;
				long targetCharacterID = msg.TargetCharacterID;

				deferGuardRelease = TryEnqueueIngressWork(() => ValidateAndSendPartyInviteAsync(conn, inviterPartyID, inviterCharacterID, targetCharacterID), guardKey, inviterCharacterID);
				if (!deferGuardRelease) SendServerBusy(conn);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Asynchronously validates party capacity and marshals the invitation back to the main thread.
		/// </summary>
		/// <param name="conn">Inviter connection for feedback sends.</param>
		/// <param name="inviterPartyID">Inviter party identifier.</param>
		/// <param name="inviterCharacterID">Inviter character identifier.</param>
		/// <param name="targetCharacterID">Target character identifier.</param>
		/// <returns>Asynchronous invite-validation task.</returns>
		private async Task ValidateAndSendPartyInviteAsync(NetworkConnection conn, long inviterPartyID, long inviterCharacterID, long targetCharacterID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}

				/* Re-verify the inviter still leads the party.
				 *
				 * The broadcast handler tested IPartyController.Rank, which the update pump
				 * refreshes, so a demoted ex-leader passes it for up to a pump interval. Their
				 * invitation would otherwise be delivered as if it came from the party's leader,
				 * and accepting it would add somebody to a party they had no authority over. */
				if (!await IsCurrentPartyLeaderAsync(charPartyService, inviterCharacterID, inviterPartyID))
				{
					return;
				}

				// Check that the party is not full
				DatabaseResult<int> countResult = await charPartyService.CountAsync(inviterPartyID);
				if (!countResult.IsSuccess || countResult.Data >= MaxPartySize)
				{
					return;
				}

				/* Blocking has existed in the friend table since it was written and nothing has
				 * ever read the column. Asked about the TARGET, not the inviter: the question is
				 * whether the person about to receive a modal has refused contact from the
				 * sender. */
				if (Server.Database.ServiceRegistry.TryGet<ICharacterFriendService>(out var friendService))
				{
					DatabaseResult<bool> blockedResult = await friendService.IsBlockedAsync(targetCharacterID, inviterCharacterID);
					if (blockedResult.IsSuccess && blockedResult.Data)
					{
						return;
					}
				}

				// Marshal the invitation logic back to the main thread
				TryEnqueueMainThread(() =>
				{
					if (Server == null)
					{
						return;
					}

					if (!Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
					{
						return;
					}

					/* The target is resolved FIRST, before any state is claimed on their behalf.
					 *
					 * The order used to be the other way round: the cooldown was taken, then the
					 * pending slot, and only then was the target looked up. A target this scene
					 * server does not host — in another zone, on another scene server, or simply
					 * logged out — fell through every one of those without a word, leaving behind
					 * a pending invitation nothing would ever answer (blocking real invitations to
					 * that player until it aged out) and a spent cooldown (blocking the inviter
					 * from retrying), for a modal that was never shown to anybody. Both are now
					 * claimed only once there is somebody to deliver to.
					 *
					 * A target on another scene server still cannot be invited — the invitation is
					 * a direct broadcast to a connection and there is no cross-server relay for it
					 * — but that now fails as a message the inviter can read instead of as
					 * silence. */
					if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData) ||
						!characterMappingData.CharactersByID.TryGetValue(targetCharacterID, out IPlayerCharacter targetCharacter) ||
						targetCharacter == null ||
						targetCharacter.Owner == null ||
						!targetCharacter.TryGet(out IPartyController targetPartyController))
					{
						SendPartyChatCode(conn, targetCharacterID, ChatHelper.TARGET_OFFLINE);
						return;
					}

					// validate target
					if (targetPartyController.ID > 0)
					{
						// we should tell the inviter the target is already in a party
						SendPartyChatCode(conn, targetCharacterID, ChatHelper.PARTY_ERROR_TARGET_IN_PARTY);
						return;
					}

					DateTime nowUtc = DateTime.UtcNow;

					/* Per (inviter, target), not per connection. Recorded before the pending slot
					 * is taken so a target who declines instantly still cannot be re-invited
					 * until the cooldown elapses. */
					if (perTargetInviteCooldownSeconds > 0.0f &&
						!runtimeData.TryBeginInviteCooldown(
							inviterCharacterID,
							targetCharacterID,
							TimeSpan.FromSeconds(perTargetInviteCooldownSeconds),
							nowUtc))
					{
						return;
					}

					PendingPartyInvitation invitation = new PendingPartyInvitation(inviterPartyID, inviterCharacterID, nowUtc);

					// if the target doesn't already have a pending invite
					if (!runtimeData.TryAddPendingInvitation(targetCharacterID, invitation))
					{
						return;
					}

					Server.NetworkWrapper.Broadcast(targetCharacter.Owner, new PartyInviteBroadcast()
					{
						InviterCharacterID = inviterCharacterID,
						TargetCharacterID = targetCharacter.ID
					}, true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error validating party invite (PartyID={inviterPartyID}): {ex}");
			}
		}

		/// <summary>
		/// Handles acceptance of a party invitation, validates the invite, adds the character to the party, and broadcasts the update.
		/// </summary>
		/// <param name="conn">Network connection of the accepting character.</param>
		/// <param name="msg">PartyAcceptInviteBroadcast message containing acceptance details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyAcceptInviteBroadcastReceived(NetworkConnection conn, PartyAcceptInviteBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.AcceptInvite, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();

				// validate character
				if (partyController == null || partyController.ID > 0)
				{
					return;
				}

				if (!Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
				{
					return;
				}

				// validate party invite
				if (!runtimeData.TryGetPendingInvitation(partyController.Character.ID, out PendingPartyInvitation invitation))
				{
					return;
				}

				/* The client names the invitation it is answering. It used to send an empty
				 * struct, so the server could only resolve "whatever is pending" — and a dialog
				 * left open past the TTL joined whichever party invited the player NEXT. This is
				 * a claim being CHECKED against the server's own pending record, never trusted. */
				if (msg.InviterCharacterID != invitation.InviterCharacterID)
				{
					return;
				}

				/* Expiry re-tested here against the issue time rather than left to the sweep. The
				 * sweep is bounded and periodic, so an invitation can outlive its TTL by up to a
				 * sweep interval — and reading the entry refreshes the queue's clock. */
				if (DateTime.UtcNow - invitation.IssuedUtc > TimeSpan.FromSeconds(invitationTtlSeconds))
				{
					runtimeData.RemovePendingInvitation(partyController.Character.ID);
					return;
				}

				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}

				// Capture immutable data for the async path
				long characterID = partyController.Character.ID;
				bool attributesExist = partyController.Character.TryGet(out ICharacterAttributeController attributeController);
				float healthPCT = attributesExist ? attributeController.GetHealthResourceAttributeCurrentPercentage() : 1.0f;

				/* Claimed even though joining does not itself move leadership. It changes the
				 * roster, and every leadership decision in this file is made from a roster read —
				 * so an accept landing between a departing leader's read and its write is a member
				 * the hand-off could not consider, and the new arrival can end up in a party whose
				 * leader has just been deleted. */
				if (!TryBeginPartyMutation(invitation.PartyID, out long mutationToken))
				{
					SendServerBusy(conn);
					return;
				}

				deferGuardRelease = TryEnqueueIngressWork(() => AcceptPartyInviteAsync(conn, characterID, invitation.PartyID, healthPCT, mutationToken), guardKey, characterID);
				if (!deferGuardRelease)
				{
					EndPartyMutation(invitation.PartyID, mutationToken);
					SendServerBusy(conn);
				}
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Asynchronously validates party capacity, persists membership, and marshals state changes back to the main thread.
		/// </summary>
		/// <param name="conn">Accepting connection.</param>
		/// <param name="characterID">Accepting character identifier.</param>
		/// <param name="partyID">Party identifier from pending invitation.</param>
		/// <param name="healthPCT">Current accepter health percentage.</param>
		/// <param name="mutationToken">Party mutation claim taken by the caller.</param>
		/// <returns>Asynchronous accept-invite task.</returns>
		private async Task AcceptPartyInviteAsync(NetworkConnection conn, long characterID, long partyID, float healthPCT, long mutationToken)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}

				/* Re-verify the accepter belongs to no party, against the DATABASE.
				 *
				 * The broadcast handler tested IPartyController.ID, and a membership row can
				 * outlive a cleared controller: the load-time eviction for a cross-world party
				 * gives up when the party is mid-change, and clears the controller while leaving
				 * the row. The membership row is keyed by character, so persisting over one is not
				 * an insert but a MOVE — the accepter is silently taken out of the party they were
				 * really in, whose remaining members are never told, because nothing marks that
				 * party as updated. */
				DatabaseResult<CharacterPartyData?> existingResult = await charPartyService.FetchAsync(characterID);
				if (!existingResult.IsSuccess)
				{
					return;
				}
				if (existingResult.Data.HasValue)
				{
					await Log.Debug("PartySystem", $"Character {characterID} accepted an invitation to party {partyID} but already belongs to party {existingResult.Data.Value.PartyID}; refused.");
					return;
				}

				// Check party capacity
				DatabaseResult<IReadOnlyList<CharacterPartyData>> membersResult = await charPartyService.FetchManyAsync(partyID);
				if (!membersResult.IsSuccess || membersResult.Data == null || membersResult.Data.Count >= MaxPartySize)
				{
					return;
				}

				CharacterPartyData partyData = new CharacterPartyData(0, 1, characterID, partyID, (byte)PartyRank.Member, healthPCT);
				DatabaseResult persistResult = await charPartyService.PersistAsync(partyData, MaxPartySize);
				if (!persistResult.IsSuccess)
				{
					return;
				}

				// Tell the other servers to update their party lists
				DatabaseResult updateResult = await partyUpdateService.PersistAsync(partyID);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("PartySystem", $"AcceptPartyInviteAsync party update notification failed (PartyID={partyID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
				}

				// Marshal state changes + broadcast to main thread
				TryEnqueueMainThread(() =>
				{
					if (Server == null)
					{
						return;
					}

					IPartyController pc = conn.FirstObject?.GetComponent<IPartyController>();
					if (pc == null)
					{
						return;
					}

					/* Applied unconditionally, for the same reason as the create path above: the
					 * membership row is already committed, and a CanAct gate here would strand a
					 * player who died, was stunned or started teleporting while the database was
					 * answering — in a party their client knows nothing about and cannot leave. */
					pc.ID = partyID;
					pc.Rank = PartyRank.Member;

					AddPartyCharacterTracker(partyID, characterID);

					// tell the new member they joined immediately, other clients will catch up with the PartyUpdate pass
					Server.NetworkWrapper.Broadcast(conn, new PartyAddBroadcast()
					{
						PartyID = partyID,
						CharacterID = characterID,
						Rank = PartyRank.Member,
						HealthPCT = healthPCT,
					}, true, Channel.Reliable);

					// Increment achievement for joining a party
					if (PartyJoinAchievementTemplate != null)
					{
						IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			
						if (character != null && character.TryGet(out IAchievementController achievementController) && CharacterStateValidation.CanAct(character))
						{
							achievementController.Increment(PartyJoinAchievementTemplate, 1);
						}
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error accepting party invite (CharID={characterID}, PartyID={partyID}): {ex}");
			}
			finally
			{
				/* The invitation is spent by having been ANSWERED, not by the answer succeeding.
				 *
				 * It used to be cleared only on the success path, so an accept that lost a race
				 * for the last slot — or hit a database fault — left the pending record in place.
				 * The player's dialog was gone, a fresh invitation to them was refused because the
				 * slot was occupied, and the inviter could not re-send because the per-target
				 * cooldown had been spent: the pair were locked out of each other for the rest of
				 * the invitation TTL over a failure neither of them could see. */
				if (Server?.DataContainerRegistry.TryGet(out IPartySystemRuntimeData invitationData) == true)
				{
					invitationData.RemovePendingInvitation(characterID);
				}

				EndPartyMutation(partyID, mutationToken);
			}
		}


		/// <summary>
		/// Adds a character to an existing party without an invitation, on behalf of the dungeon
		/// finder. Call from an async worker.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Joining somebody else's dungeon joins their group. It has to: an instance's leadership,
		/// its kick authority, and its very identity are all the owning party's, so a character
		/// standing inside one while belonging to nobody would be in a run with no one able to
		/// manage them and no way for them to be found by it again.
		/// </para>
		/// <para>
		/// <b>This is not an invitation bypass.</b> The only caller is the dungeon finder, and it
		/// calls this only after establishing that the party has published a joinable instance —
		/// which is an explicit, revocable offer by that party's leader. A character who already
		/// belongs to a party is refused before reaching here rather than being moved, because
		/// silently removing somebody from a group they are in, and possibly lead, is not
		/// something a click on a dungeon list should be able to do.
		/// </para>
		/// <para>
		/// Capacity is enforced against the same <see cref="MaxPartySize"/> the invitation path
		/// uses. A full party's instance simply cannot be joined, which is the correct outcome:
		/// there would be nobody to add the joiner to.
		/// </para>
		/// </remarks>
		/// <param name="conn">The joining character's connection.</param>
		/// <param name="characterID">The joining character.</param>
		/// <param name="partyID">Party that owns the instance being joined.</param>
		/// <param name="healthPCT">Current health fraction, for the party roster.</param>
		/// <returns>True when membership was persisted; false when it was refused or failed.</returns>
		public async Task<bool> TryAddCharacterToPartyAsync(NetworkConnection conn, long characterID, long partyID, float healthPCT)
		{
			if (conn == null || characterID <= 0 || partyID <= 0)
			{
				return false;
			}

			/* Claimed for the whole join, because the join REPAIRS leadership when it finds a
			 * party without one — see below. That repair reads a roster and writes a rank, which
			 * is the same shape as every other leadership decision here and races them the same
			 * way. Refused rather than waited on: the finder turns a false into "that group could
			 * not be joined", which is the right answer for a party being disbanded or reformed
			 * underneath the request. */
			if (!TryBeginPartyMutation(partyID, out long mutationToken))
			{
				await Log.Debug("PartySystem", $"Character {characterID} could not join party {partyID} for an instance: the party is being changed.");
				return false;
			}

			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService) ||
					!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return false;
				}

				/* Read the roster before persisting, exactly as the invitation path does.
				 *
				 * PersistAsync enforces the cap itself, but reading first is what lets a full
				 * party be reported as a refusal the finder can turn into "that group is full"
				 * rather than a bare failure indistinguishable from a database fault. */
				DatabaseResult<IReadOnlyList<CharacterPartyData>> membersResult = await charPartyService.FetchManyAsync(partyID);
				if (!membersResult.IsSuccess || membersResult.Data == null)
				{
					await Log.Warning("PartySystem", $"Could not read party {partyID} while joining an instance for character {characterID}.");
					return false;
				}

				if (membersResult.Data.Count >= MaxPartySize)
				{
					return false;
				}

				/* Already a member — treat as success rather than as a failure.
				 *
				 * Reachable without a modified client: a member who walked out of their own
				 * party's instance and came back through the finder's list arrives here still on
				 * the roster. Persisting again would be harmless but refusing would lock them out
				 * of the dungeon they had just left. */
				for (int i = 0; i < membersResult.Data.Count; ++i)
				{
					if (membersResult.Data[i].CharacterID == characterID)
					{
						return true;
					}
				}

				/* Not on this party's roster, so make sure they are not on somebody else's.
				 *
				 * The finder refuses a character who already has a party before it reaches here,
				 * from the controller — and the membership row can outlive a cleared controller.
				 * The row is keyed by character, so persisting over one would MOVE them out of
				 * their real party without telling anybody in it, which is precisely the silent
				 * group-breaking the finder's own check exists to prevent. */
				DatabaseResult<CharacterPartyData?> existingResult = await charPartyService.FetchAsync(characterID);
				if (!existingResult.IsSuccess)
				{
					return false;
				}
				if (existingResult.Data.HasValue)
				{
					await Log.Debug("PartySystem", $"Character {characterID} could not join party {partyID} for an instance: they already belong to party {existingResult.Data.Value.PartyID}.");
					return false;
				}

				CharacterPartyData partyData = new CharacterPartyData(0, 1, characterID, partyID, (byte)PartyRank.Member, healthPCT);
				DatabaseResult persistResult = await charPartyService.PersistAsync(partyData, MaxPartySize);
				if (!persistResult.IsSuccess)
				{
					return false;
				}

				/* Re-read the roster and repair a party that has been left leaderless.
				 *
				 * The join and a departure race each other, and the departure is the dangerous
				 * half: the leave path hands leadership to one of the *remaining* members, chosen
				 * from a roster read before this insert landed. If the leader left in that window
				 * the transfer either picked nobody — the party was empty as far as it could see —
				 * or picked somebody who has since gone too, and the joiner arrives into a party
				 * with no leader at all. Nothing would ever repair that on its own: promotion
				 * requires a leader to perform it, closing the dungeon requires a leader to
				 * authorise it, and the party would drift until its last member logged out.
				 *
				 * Cheap to check and unambiguous to fix, so it is checked on every join rather
				 * than only when a race is suspected. */
				DatabaseResult<IReadOnlyList<CharacterPartyData>> afterResult = await charPartyService.FetchManyAsync(partyID);
				PartyRank joinedRank = PartyRank.Member;
				if (afterResult.IsSuccess && afterResult.Data != null)
				{
					PartyLeadershipRepair repair = await EnsurePartyLeadershipAsync(charPartyService, partyID, afterResult.Data, nameof(TryAddCharacterToPartyAsync));
					if (repair.PromotedCharacterID == characterID)
					{
						joinedRank = PartyRank.Leader;
					}
				}

				// Tell the other scene servers to refresh their copies of this party.
				DatabaseResult updateResult = await partyUpdateService.PersistAsync(partyID);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("PartySystem", $"Instance join party update notification failed (PartyID={partyID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
				}

				/* The controller and the client's own view are updated on the main thread, before
				 * the finder disconnects the character to move it.
				 *
				 * The update pump would get there eventually, but "eventually" is after the
				 * transfer: the character would arrive inside the instance still believing it has
				 * no party, and the instance panel it opens on arrival would show a run it is not
				 * a member of. Sending it here means the party is already true by the time the
				 * hand-off happens. */
				TryEnqueueMainThread(() =>
				{
					if (Server == null || conn.FirstObject == null)
					{
						return;
					}

					IPartyController pc = conn.FirstObject.GetComponent<IPartyController>();
					if (pc == null)
					{
						return;
					}

					pc.ID = partyID;
					pc.Rank = joinedRank;

					AddPartyCharacterTracker(partyID, characterID);

					PartyAddBroadcast addBroadcast = new PartyAddBroadcast()
					{
						PartyID = partyID,
						CharacterID = characterID,
						Rank = joinedRank,
						HealthPCT = healthPCT,
					};

					// The joiner's own view.
					Server.NetworkWrapper.Broadcast(conn, addBroadcast, true, Channel.Reliable);

					/* And everybody already in the party who is on this scene server.
					 *
					 * The update pump reaches every member eventually, wherever they are, but
					 * "eventually" is up to a pump interval — and the people most likely to be
					 * looking at their party frame when somebody joins their dungeon are the ones
					 * standing in it. Pushing the row now means the roster is right the moment the
					 * joiner appears rather than seconds later. Members on other scene servers
					 * still converge through the pump, which is why this is an addition to that
					 * mechanism rather than a replacement for it.
					 *
					 * The joiner is skipped: they were just sent the same row directly, and their
					 * controller may not be in the tracker yet. */
					if (Server.DataContainerRegistry.TryGet<IPartyCharacterMappingData>(out var partyMapping) &&
						partyMapping.PartyCharacterTracker.TryGetValue(partyID, out HashSet<long> localMembers) &&
						Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMapping))
					{
						foreach (long memberID in localMembers)
						{
							if (memberID == characterID)
							{
								continue;
							}

							if (characterMapping.CharactersByID.TryGetValue(memberID, out IPlayerCharacter member) &&
								member?.Owner != null)
							{
								Server.NetworkWrapper.Broadcast(member.Owner, addBroadcast, true, Channel.Reliable);
							}
						}
					}

					if (PartyJoinAchievementTemplate != null &&
						conn.FirstObject.GetComponent<IPlayerCharacter>() is IPlayerCharacter character &&
						character.TryGet(out IAchievementController achievementController))
					{
						achievementController.Increment(PartyJoinAchievementTemplate, 1);
					}
				});

				return true;
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error joining party {partyID} for instance entry (CharID={characterID}): {ex}");
				return false;
			}
			finally
			{
				EndPartyMutation(partyID, mutationToken);
			}
		}

		/// <summary>
		/// Forms a party of one for a character who is opening a dungeon others may join.
		/// Call from an async worker.
		/// </summary>
		/// <remarks>
		/// An instance is owned by a party, and joining one joins that party — so an instance
		/// opened by somebody with no party has no group for a joiner to be added to, and could
		/// only ever be a solo run. Rather than refusing to let ungrouped players advertise at
		/// all, opening a dungeon publicly forms the party the listing implies.
		/// <para>
		/// Only reached when the player has explicitly chosen to open the dungeon publicly. A
		/// private or solo run creates no party, because none is needed and forming one silently
		/// would be a side effect the player did not ask for.
		/// </para>
		/// </remarks>
		/// <param name="conn">The character's connection.</param>
		/// <param name="characterID">The character forming the party.</param>
		/// <param name="worldServerID">World server the party will belong to.</param>
		/// <param name="sceneName">Scene name, for the create broadcast's location field.</param>
		/// <param name="healthPCT">Current health fraction, for the party roster.</param>
		/// <returns>The new party ID, or 0 when it could not be created.</returns>
		public async Task<long> TryCreatePartyForInstanceAsync(NetworkConnection conn, long characterID, long worldServerID, string sceneName, float healthPCT)
		{
			if (conn == null || characterID <= 0)
			{
				return 0;
			}

			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IPartyService>(out var partyService) ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return 0;
				}

				DatabaseResult<long> createResult = await partyService.CreateAsync(worldServerID);
				if (!createResult.IsSuccess || createResult.Data <= 0)
				{
					return 0;
				}

				long newPartyID = createResult.Data;

				CharacterPartyData partyData = new CharacterPartyData(0, 1, characterID, newPartyID, (byte)PartyRank.Leader, healthPCT);
				DatabaseResult persistResult = await charPartyService.PersistAsync(partyData, MaxPartySize);
				if (!persistResult.IsSuccess)
				{
					/* The party row exists but has no members, and nothing else will ever put one
					 * in it. Removed rather than left behind: an empty party is invisible, so it
					 * would accumulate silently, and the instance about to be opened would be
					 * recorded against a party nobody belongs to — which is exactly the leaderless
					 * state the join path has to repair. */
					DatabaseResult deleteResult = await partyService.DeleteAsync(newPartyID);
					if (!deleteResult.IsSuccess)
					{
						await Log.Warning("PartySystem", $"Could not remove empty party {newPartyID} after a failed instance-party creation.");
					}
					return 0;
				}

				TryEnqueueMainThread(() =>
				{
					if (Server == null || conn.FirstObject == null)
					{
						return;
					}

					IPartyController pc = conn.FirstObject.GetComponent<IPartyController>();
					if (pc == null)
					{
						return;
					}

					pc.ID = newPartyID;
					pc.Rank = PartyRank.Leader;

					AddPartyCharacterTracker(newPartyID, characterID);

					Server.NetworkWrapper.Broadcast(conn, new PartyCreateBroadcast()
					{
						PartyID = newPartyID,
						Location = sceneName,
					}, true, Channel.Reliable);
				});

				return newPartyID;
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error creating a party for an instance (CharID={characterID}): {ex}");
				return 0;
			}
		}

		/// <summary>
		/// Handles decline of a party invitation, removes pending invitation for the character.
		/// </summary>
		/// <param name="conn">Network connection of the declining character.</param>
		/// <param name="msg">PartyDeclineInviteBroadcast message containing decline details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyDeclineInviteBroadcastReceived(NetworkConnection conn, PartyDeclineInviteBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null || !CharacterStateValidation.CanAct(character))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.DeclineInvite, out long guardKey))
			{
				return;
			}

			try
			{
				if (Server.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData))
				{
					/* Only clear the invitation the client actually declined. A decline that
					 * arrives after the slot has been refilled would otherwise silently throw
					 * away an invitation the player has not been shown yet. */
					if (runtimeData.TryGetPendingInvitation(character.ID, out PendingPartyInvitation invitation) &&
						msg.InviterCharacterID == invitation.InviterCharacterID)
					{
						runtimeData.RemovePendingInvitation(character.ID);
					}
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles party leave broadcast, validates character, transfers leadership if needed, removes member from party, and updates or deletes party as appropriate.
		/// </summary>
		/// <param name="conn">Network connection of the leaving character.</param>
		/// <param name="msg">PartyLeaveBroadcast message containing leave details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyLeaveBroadcastReceived(NetworkConnection conn, PartyLeaveBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Leave, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();

				// validate character
				if (partyController == null || partyController.ID < 1)
				{
					// not in a party..
					return;
				}

				/* Capture immutable data for the async path.
				 *
				 * The rank is deliberately NOT captured. It is a cached copy that the update pump
				 * refreshes, so it can disagree with the database for up to a pump interval, and
				 * whether leadership has to move is far too important a question to answer from a
				 * stale copy — RemovePartyMemberRowsAsync re-reads it from the membership rows it
				 * already has to fetch. */
				long partyID = partyController.ID;
				long characterID = partyController.Character.ID;

				/* Serialised against every other change to this party. Leaving decides who leads
				 * it next, and that decision is made from rows read several awaits earlier — see
				 * IPartySystemRuntimeData.TryBeginPartyMutation. */
				if (!TryBeginPartyMutation(partyID, out long mutationToken))
				{
					SendServerBusy(conn);
					return;
				}

				/* Marked BEFORE the async hop. The membership row is deleted on a background task
				 * while this character still carries a live party ID, and disconnecting in that
				 * window would run the ordinary disconnect persist and write the row back in. */
				BeginMembershipRemoval(characterID);

				deferGuardRelease = TryEnqueueIngressWork(() => LeavePartyAsync(conn, characterID, partyID, mutationToken), guardKey, characterID);
				if (!deferGuardRelease)
				{
					EndMembershipRemoval(characterID);
					EndPartyMutation(partyID, mutationToken);
					SendServerBusy(conn);
					return;
				}
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Sends one of the party chat error codes to a connection.
		/// </summary>
		/// <param name="conn">Connection to inform.</param>
		/// <param name="subjectCharacterID">Character the message is about; the client names them.</param>
		/// <param name="code">A <c>ChatHelper</c> error code.</param>
		/// <remarks>
		/// The trailing space is not cosmetic: the client splits the first word off to recognise
		/// the code, so a bare code with nothing after it does not parse as one and prints raw.
		/// </remarks>
		private void SendPartyChatCode(NetworkConnection conn, long subjectCharacterID, string code)
		{
			// IsActive as well as null: this runs from a marshalled action, so the requester may
			// have disconnected between asking and being answered.
			if (Server == null || conn == null || !conn.IsActive)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new ChatBroadcast()
			{
				Channel = ChatChannel.Party,
				SenderID = subjectCharacterID,
				Text = code + " ",
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Reports a rank write that did not land, at the severity it deserves.
		/// </summary>
		/// <param name="result">The failed write.</param>
		/// <param name="message">What was being attempted.</param>
		/// <returns>Completion of the log write.</returns>
		/// <remarks>
		/// <b>Losing this race is a normal outcome, not a fault.</b> Every leadership decision here
		/// is a pure function of the roster, precisely so that two scene servers examining the same
		/// party reach the same answer — and when they do, both try to write it. The second one is
		/// refused by the version gate: <c>DUPLICATE_REPLAY</c> when it asked for the exact version
		/// the winner just wrote, <c>STALE_STATE</c> when the row has moved further on. Both mean
		/// somebody else settled this party first, which is the design working.
		/// <para>
		/// Reported at warning, those two would put "leadership transfer failed" in the log every
		/// time two servers agreed with each other — training an operator to ignore the line that
		/// matters. Anything else really is a fault and keeps its warning.
		/// </para>
		/// </remarks>
		private static async Task LogRankWriteFailure(DatabaseResult result, string message)
		{
			if (result.ErrorCode == DatabaseErrorCodes.DuplicateReplay ||
				result.ErrorCode == DatabaseErrorCodes.StaleState)
			{
				await Log.Debug("PartySystem", $"{message} — another server settled this party first ({result.ErrorCode}).");
				return;
			}

			await Log.Warning("PartySystem", $"{message}: {result.ErrorCode} - {result.ErrorMessage}");
		}

		/// <summary>
		/// Whether a character ID appears in a list.
		/// </summary>
		/// <param name="ids">The list to search.</param>
		/// <param name="characterID">The ID to look for.</param>
		/// <returns>True when present.</returns>
		/// <remarks>
		/// A linear scan rather than a set. These lists hold at most one party's worth of members,
		/// and building a HashSet to search six entries costs more than the search.
		/// </remarks>
		private static bool ContainsID(IReadOnlyList<long> ids, long characterID)
		{
			for (int i = 0; i < ids.Count; ++i)
			{
				if (ids[i] == characterID)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Moves leadership off a leader who holds no live session, onto a member who does.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The reason party leadership cannot get stuck.</b> Every other rule here is about the
		/// SHAPE of the roster — nobody holds the rank, or two people do — and a party led by
		/// somebody who logged out an hour ago satisfies all of them. It is nonetheless completely
		/// stuck: inviting, kicking, promoting and closing the instance the party is holding open
		/// are all leader-only, so the members who are actually playing can do none of them, and
		/// nothing in the shape of the data says anything is wrong.
		/// </para>
		/// <para>
		/// <b>Convergent, not event-driven.</b> This is the whole point of doing it here rather
		/// than at the moment a leader disconnects. A handler that fires on an event can only ever
		/// be as correct as its ordering: hand the rank to somebody who is disconnecting in the
		/// same breath and it lands on another absent player, with nothing left to notice. This
		/// asks a question about the state as it stands and fixes whatever it finds, so it is
		/// right no matter how it got there — a leader who quit, a leader whose successor quit
		/// during the hand-over, or a whole scene server that died without running a single
		/// disconnect handler. In the last case there is no event to hook at all: the leases those
		/// characters held simply expire, the database stops calling them online, and the next
		/// pass moves the rank.
		/// </para>
		/// <para>
		/// Promotes only from the ONLINE set, so the repair cannot produce the state it repairs.
		/// A party with nobody online is left exactly as it is: there is no one to be stuck, and
		/// whoever logs in first writes a party update on the way, which brings the pump straight
		/// back here.
		/// </para>
		/// </remarks>
		/// <param name="charPartyService">Membership service.</param>
		/// <param name="partyID">Party to inspect.</param>
		/// <param name="members">The party's full membership rows.</param>
		/// <param name="incumbent">The row of the member currently holding the rank.</param>
		/// <param name="caller">Name used in log lines.</param>
		/// <returns>What was done, if anything.</returns>
		private async Task<PartyLeadershipRepair> RepairAbsentLeaderAsync(ICharacterPartyService charPartyService, long partyID, IReadOnlyList<CharacterPartyData> members, CharacterPartyData incumbent, string caller)
		{
			if (!transferLeadershipOnDisconnect)
			{
				return default;
			}

			DatabaseResult<IReadOnlyList<long>> onlineResult = await charPartyService.FetchOnlineMemberIdsAsync(partyID);
			if (!onlineResult.IsSuccess || onlineResult.Data == null)
			{
				// Could not tell. Absence of evidence, so nothing is moved.
				return default;
			}

			IReadOnlyList<long> online = onlineResult.Data;
			if (online.Count < 1)
			{
				// Nobody is playing in this party, so nobody is stuck.
				return default;
			}

			long successorID = 0;
			bool incumbentOnline = false;

			for (int i = 0; i < online.Count; ++i)
			{
				long onlineID = online[i];

				if (onlineID == incumbent.CharacterID)
				{
					incumbentOnline = true;
					break;
				}

				// Lowest ID, matching the rule every other decision in this file uses.
				if (successorID == 0 || onlineID < successorID)
				{
					successorID = onlineID;
				}
			}

			if (incumbentOnline)
			{
				/* Present after all — most often because the "absence" was a scene transfer that
				 * has now landed. Any observation in progress is dropped, so a leader who zones
				 * repeatedly never accumulates one. */
				ClearLeaderAbsence(partyID);
				return default;
			}

			if (successorID == 0)
			{
				return default;
			}

			/* Absent once is not absent. See leadershipAbsenceGraceSeconds: a character moving
			 * between scene servers is indistinguishable from one that has logged off, and the
			 * only thing that separates them is how long it lasts. The first sighting starts a
			 * clock and schedules the second, which is what confirms it. */
			if (!TryConfirmLeaderAbsent(partyID, incumbent.CharacterID, out DateTime recheckDueUtc))
			{
				TryEnqueueMainThread(() => ScheduleLeadershipRecheckAt(partyID, recheckDueUtc));
				return default;
			}

			/* The successor's row is found in the list the caller already read rather than fetched
			 * again. A row that has moved since makes the version-gated write below fail, which is
			 * the correct outcome: nothing is changed, and the next pass reads fresher rows. */
			CharacterPartyData successor = default;
			bool successorFound = false;
			for (int i = 0; i < members.Count; ++i)
			{
				if (members[i].CharacterID == successorID)
				{
					successor = members[i];
					successorFound = true;
					break;
				}
			}

			if (!successorFound)
			{
				// Online, but not on the roster this call is reasoning about. Left for a later pass.
				return default;
			}

			DatabaseResult promoteResult = await charPartyService.UpdateRankAsync(
				successorID, partyID, (byte)PartyRank.Leader, successor.Version + 1);
			if (!promoteResult.IsSuccess)
			{
				await LogRankWriteFailure(promoteResult, $"{caller} could not promote {successorID} over absent leader {incumbent.CharacterID} in party {partyID}");
				return default;
			}

			/* Promote first, demote second — the same ordering as every other transfer here. If
			 * the demotion fails the party has two leaders, one of them absent, which the count
			 * half of EnsurePartyLeadershipAsync collapses on its next pass. The reverse ordering
			 * would leave it with none, which nothing can act on. */
			DatabaseResult demoteResult = await charPartyService.UpdateRankAsync(
				incumbent.CharacterID, partyID, (byte)PartyRank.Member, incumbent.Version + 1);
			if (!demoteResult.IsSuccess)
			{
				await LogRankWriteFailure(demoteResult, $"{caller} promoted {successorID} but could not demote absent leader {incumbent.CharacterID} in party {partyID}");
			}

			ClearLeaderAbsence(partyID);

			await Log.Debug("PartySystem", $"{caller} moved leadership of party {partyID} from absent {incumbent.CharacterID} to {successorID} after {leadershipAbsenceGraceSeconds:0}s.");
			return new PartyLeadershipRepair(true, successorID);
		}

		/// <summary>
		/// Settles a party's leadership under its mutation claim, and notifies on a change.
		/// </summary>
		/// <remarks>
		/// For the callers that do NOT already hold the claim — the update pump and the periodic
		/// audit. Taking it here rather than testing whether somebody else holds it closes the
		/// gap between the test and the writes, which is the same check-then-act mistake this
		/// whole mechanism exists to remove.
		/// <para>
		/// The rows were read before the claim was taken, which is safe by construction: every
		/// write is version-gated, so a roster that moved in between rejects the write and the
		/// next pass reads fresher rows. Re-reading under the claim would cost a query on the
		/// common path, where nothing is wrong and nothing is written.
		/// </para>
		/// </remarks>
		/// <param name="charPartyService">Membership service.</param>
		/// <param name="partyID">Party to settle.</param>
		/// <param name="members">The party's full membership rows.</param>
		/// <param name="caller">Name used in log lines.</param>
		/// <returns>What was done, if anything.</returns>
		private async Task<PartyLeadershipRepair> RepairPartyLeadershipAsync(ICharacterPartyService charPartyService, long partyID, IReadOnlyList<CharacterPartyData> members, string caller)
		{
			if (!TryBeginPartyMutation(partyID, out long mutationToken))
			{
				/* Somebody is already changing this party, and whatever they are doing settles its
				 * leadership on the way out. Skipped rather than waited on: this is a sweep, and
				 * the party will be looked at again. */
				return default;
			}

			try
			{
				PartyLeadershipRepair repair = await EnsurePartyLeadershipAsync(
					charPartyService, partyID, members, caller, repairAbsentLeader: true);

				if (repair.Changed &&
					Server?.Database?.ServiceRegistry != null &&
					Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					/* Announced so the other scene servers hosting this party's members re-read
					 * it. It is also what brings the pump back to this party, which is how a
					 * repair that only half-landed gets finished. */
					DatabaseResult updateResult = await partyUpdateService.PersistAsync(partyID);
					if (!updateResult.IsSuccess)
					{
						await Log.Warning("PartySystem", $"{caller} leadership repair notification failed (PartyID={partyID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
					}
				}

				return repair;
			}
			finally
			{
				EndPartyMutation(partyID, mutationToken);
			}
		}

		/// <summary>
		/// Re-reads a character's membership row and reports whether it currently leads the party.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Every leader-only action has to ask this again after its async hop.</b> The
		/// broadcast handlers gate on <c>IPartyController.Rank</c>, which is a cached copy the
		/// update pump refreshes — correct most of the time and stale for up to a pump interval
		/// after any rank change, including one this same player just caused.
		/// </para>
		/// <para>
		/// That window is reachable without a modified client and it is not narrow. A leader who
		/// promotes somebody and then immediately kicks or promotes again is acting as leader
		/// against a rank they gave away a moment earlier: the promotion path would promote a
		/// second player while the first is still leader, and the kick path would let a demoted
		/// ex-leader throw somebody out of a party they no longer run. The database row is the
		/// authority and reading it is one round trip on an action that is already several.
		/// </para>
		/// </remarks>
		/// <param name="charPartyService">Membership service.</param>
		/// <param name="characterID">Character claiming leadership.</param>
		/// <param name="partyID">Party they claim to lead.</param>
		/// <returns>True only when the row exists, names this party, and carries the Leader rank.</returns>
		private static async Task<bool> IsCurrentPartyLeaderAsync(ICharacterPartyService charPartyService, long characterID, long partyID)
		{
			if (charPartyService == null || characterID <= 0 || partyID <= 0)
			{
				return false;
			}

			DatabaseResult<CharacterPartyData?> result = await charPartyService.FetchAsync(characterID);
			if (!result.IsSuccess || !result.Data.HasValue)
			{
				return false;
			}

			CharacterPartyData row = result.Data.Value;
			return row.PartyID == partyID && (PartyRank)row.Rank == PartyRank.Leader;
		}

		/// <summary>
		/// What a leadership repair did, if anything.
		/// </summary>
		private readonly struct PartyLeadershipRepair
		{
			/// <summary>True when at least one rank was written.</summary>
			public readonly bool Changed;

			/// <summary>The character promoted to leader, or 0 when none was.</summary>
			public readonly long PromotedCharacterID;

			/// <summary>
			/// Initializes a repair result.
			/// </summary>
			/// <param name="changed">Whether any rank was written.</param>
			/// <param name="promotedCharacterID">The character promoted, or 0.</param>
			public PartyLeadershipRepair(bool changed, long promotedCharacterID)
			{
				Changed = changed;
				PromotedCharacterID = promotedCharacterID;
			}
		}

		/// <summary>
		/// Restores the one-leader invariant on a party, in whichever direction it is broken.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The single place leadership is settled.</b> Leaving, being kicked, being dropped on
		/// arrival at the wrong world server, joining an instance and the periodic pump all route
		/// here, so there is one rule about who leads a party rather than five that can drift
		/// apart. A no-op when the party already has exactly one leader, which is what lets every
		/// caller ask unconditionally instead of first working out whether it needs to.
		/// </para>
		/// <para>
		/// <b>Both directions, because both happen.</b> A party with NO leader cannot be repaired
		/// by anything else — promoting somebody requires a leader to do it, and so does inviting,
		/// kicking and closing an instance the party owns — so it would simply stay broken until
		/// its last member logged out. A party with TWO is the quieter failure and the reason this
		/// method is not just a promotion: it looks healthy to every check that asks "is there a
		/// leader?", so nothing notices, and meanwhile two people can each kick the other. It is
		/// reachable whenever a promote-then-demote pair is interrupted between its two writes —
		/// a database refusal, a process dying — which is a window this code opens on purpose,
		/// because the alternative ordering opens the leaderless one instead.
		/// </para>
		/// <para>
		/// <b>Lowest character ID, never at random.</b> The choice has to survive being made
		/// twice: two scene servers can be repairing the same party in the same second — each
		/// pump sees the party because each hosts one of its members — and a random pick would
		/// have them settle on two different people. A pure function of the roster gives both the
		/// same answer, so the loser's write is a version-gated no-op rather than a second
		/// decision. It also makes the outcome reproducible from a database snapshot when one has
		/// to be explained after the fact.
		/// </para>
		/// <para>
		/// Callers must hold the party's mutation claim, or — for the pump, whose repair is
		/// opportunistic rather than requested — must have checked that nobody else does. A
		/// promotion in flight is a two-leader party by design for the moment between its writes,
		/// and repairing that would undo it.
		/// </para>
		/// </remarks>
		/// <param name="charPartyService">Membership service.</param>
		/// <param name="partyID">Party to settle.</param>
		/// <param name="members">
		/// The party's membership rows as they will stand — with anybody being removed in the same
		/// operation already excluded, since a leader about to be deleted is not a leader.
		/// </param>
		/// <param name="caller">Name used in log lines, so a failure says which path produced it.</param>
		/// <param name="repairAbsentLeader">
		/// Whether to also move leadership off a leader who is not logged in anywhere. Only for
		/// callers holding the party's WHOLE roster: the removal and join paths pass a list with
		/// somebody added or taken out mid-operation, and asking the database who is online would
		/// answer about a roster that no longer matches the one being reasoned about.
		/// </param>
		/// <returns>What was done, if anything.</returns>
		private async Task<PartyLeadershipRepair> EnsurePartyLeadershipAsync(ICharacterPartyService charPartyService, long partyID, IReadOnlyList<CharacterPartyData> members, string caller, bool repairAbsentLeader = false, bool preferOnlineSuccessor = false)
		{
			if (charPartyService == null || members == null || members.Count < 1)
			{
				return default;
			}

			CharacterPartyData lowestMember = default;
			CharacterPartyData incumbent = default;
			bool hasLowest = false;
			int leaderCount = 0;

			for (int i = 0; i < members.Count; ++i)
			{
				CharacterPartyData member = members[i];

				if (!hasLowest || member.CharacterID < lowestMember.CharacterID)
				{
					lowestMember = member;
					hasLowest = true;
				}

				if ((PartyRank)member.Rank != PartyRank.Leader)
				{
					continue;
				}

				if (leaderCount == 0 || member.CharacterID < incumbent.CharacterID)
				{
					incumbent = member;
				}
				++leaderCount;
			}

			// Exactly one leader. Structurally healthy — but a leader who is not logged in
			// anywhere leaves the party just as unable to act, and that is invisible from here.
			if (leaderCount == 1)
			{
				return repairAbsentLeader
					? await RepairAbsentLeaderAsync(charPartyService, partyID, members, incumbent, caller)
					: default;
			}

			if (leaderCount == 0)
			{
				if (!hasLowest)
				{
					return default;
				}

				/* Handed to somebody who is actually playing, when the caller is in a position to
				 * ask. Lowest ID alone is deterministic but blind: a leader who clicks Leave hands
				 * the party to whoever happens to have the smallest character ID, and if that
				 * member logged off yesterday the players still standing there wait out the whole
				 * absence grace before anyone can invite or kick. Asking costs one query on a path
				 * that only runs when a party has just lost its leader.
				 *
				 * Only for the departure paths, which are performed by ONE server for a given
				 * operation. The pump and the audit deliberately keep the blind rule: they run on
				 * every server hosting a member, and two of them with momentarily different views
				 * of who is online would choose different successors and each undo the other. A
				 * pure function of the roster cannot disagree with itself. */
				CharacterPartyData successorMember = lowestMember;

				if (preferOnlineSuccessor && transferLeadershipOnDisconnect)
				{
					DatabaseResult<IReadOnlyList<long>> onlineResult = await charPartyService.FetchOnlineMemberIdsAsync(partyID);
					if (onlineResult.IsSuccess && onlineResult.Data != null)
					{
						CharacterPartyData lowestOnline = default;
						bool hasOnline = false;

						for (int i = 0; i < members.Count; ++i)
						{
							CharacterPartyData member = members[i];

							/* Intersected with the roster the caller passed, which excludes the
							 * member being removed in this same operation — their row still exists
							 * and they may well still be online, and handing the party to somebody
							 * who is about to be deleted from it is the one outcome worse than
							 * handing it to somebody offline. */
							if (!ContainsID(onlineResult.Data, member.CharacterID))
							{
								continue;
							}

							if (!hasOnline || member.CharacterID < lowestOnline.CharacterID)
							{
								lowestOnline = member;
								hasOnline = true;
							}
						}

						if (hasOnline)
						{
							successorMember = lowestOnline;
						}
					}
				}

				DatabaseResult promoteResult = await charPartyService.UpdateRankAsync(
					successorMember.CharacterID, partyID, (byte)PartyRank.Leader, successorMember.Version + 1);

				if (!promoteResult.IsSuccess)
				{
					/* Logged, not retried here. The pump asks the same question of every party it
					 * refreshes, so a party that stays leaderless is repaired on a later tick from
					 * fresher rows than the ones that just failed. */
					await Log.Warning("PartySystem", $"{caller} leadership transfer failed (PartyID={partyID}, NewLeader={lowestMember.CharacterID}): {promoteResult.ErrorCode} - {promoteResult.ErrorMessage}");
					return default;
				}

				await Log.Debug("PartySystem", $"{caller} promoted character {lowestMember.CharacterID} to leader of party {partyID}.");
				return new PartyLeadershipRepair(true, lowestMember.CharacterID);
			}

			/* More than one leader. The lowest-numbered keeps it and the rest are demoted, so the
			 * party ends up led by whoever both halves of a torn promote-then-demote would have
			 * agreed on. Nobody is promoted here, so the result reports no promotion — the caller
			 * that cares (an instance join asking whether IT became leader) must not be told yes
			 * by a repair that only took a rank away from somebody else. */
			bool changed = false;
			for (int i = 0; i < members.Count; ++i)
			{
				CharacterPartyData member = members[i];

				if ((PartyRank)member.Rank != PartyRank.Leader ||
					member.CharacterID == incumbent.CharacterID)
				{
					continue;
				}

				DatabaseResult demoteResult = await charPartyService.UpdateRankAsync(
					member.CharacterID, partyID, (byte)PartyRank.Member, member.Version + 1);

				if (!demoteResult.IsSuccess)
				{
					await LogRankWriteFailure(demoteResult, $"{caller} could not demote surplus leader {member.CharacterID} in party {partyID}");
					continue;
				}

				changed = true;
				await Log.Warning("PartySystem", $"{caller} found party {partyID} with {leaderCount} leaders and demoted {member.CharacterID}; {incumbent.CharacterID} keeps it.");
			}

			return new PartyLeadershipRepair(changed, 0);
		}

		/// <summary>
		/// Removes one membership row, transferring leadership or retiring the party as required.
		/// </summary>
		/// <remarks>
		/// The database half of leaving a party, with nothing in it that touches a connection —
		/// which is what lets it serve both a player who chose to leave and a character being
		/// dropped from a party it cannot belong to. The caller does whatever announcing is
		/// appropriate afterwards.
		/// <para>
		/// The order matters and is deliberate: leadership moves to a remaining member <em>before</em>
		/// the leaver's row is deleted, so there is no window in which the party exists with
		/// members and no leader. The join path repairs that state if it is ever reached anyway,
		/// but not creating it is better than repairing it.
		/// </para>
		/// </remarks>
		/// <param name="characterID">Member being removed.</param>
		/// <param name="partyID">Party they are being removed from.</param>
		/// <param name="caller">Name used in warnings, so a failure says which path produced it.</param>
		/// <returns>True when the member was found and the removal was attempted.</returns>
		private async Task<bool> RemovePartyMemberRowsAsync(long characterID, long partyID, string caller)
		{
			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
			{
				return false;
			}
			if (!Server.Database.ServiceRegistry.TryGet<IPartyService>(out var partyService))
			{
				return false;
			}
			if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
			{
				return false;
			}

			// Fetch current members
			DatabaseResult<IReadOnlyList<CharacterPartyData>> membersResult = await charPartyService.FetchManyAsync(partyID);
			if (!membersResult.IsSuccess || membersResult.Data == null || membersResult.Data.Count == 0)
			{
				return false;
			}

			IReadOnlyList<CharacterPartyData> members = membersResult.Data;

			// Count remaining (excluding the leaving character)
			List<CharacterPartyData> remainingMembers = new List<CharacterPartyData>();
			CharacterPartyData leavingMember = default;
			bool leavingMemberFound = false;
			foreach (CharacterPartyData member in members)
			{
				if (member.CharacterID == characterID)
				{
					leavingMember = member;
					leavingMemberFound = true;
					continue;
				}
				remainingMembers.Add(member);
			}

			// If the leaving member was not found, their Version would default to 0
			// causing incorrect optimistic concurrency tokens. Abort early.
			if (!leavingMemberFound)
			{
				return false;
			}

			int remainingCount = remainingMembers.Count;

			/* Whether leadership has to move is decided from the ROWS, never from the rank the
			 * caller passed in.
			 *
			 * Every caller that had one read it off IPartyController.Rank, which is a cached copy
			 * refreshed by the update pump — so between a rank change landing in the database and
			 * the next pump reaching this scene server the copy is simply wrong, and it is wrong
			 * in both directions. A freshly promoted leader whose controller still says Member
			 * leaves without handing leadership on, and nobody left in the party can invite, kick
			 * or promote ever again. A just-demoted ex-leader whose controller still says Leader
			 * hands on a leadership they no longer hold, and the party ends up with two.
			 *
			 * The question is also asked about the PARTY rather than about the leaver: "will
			 * anybody be left holding it?" That covers the case where the party is ALREADY
			 * leaderless when this runs — two removals racing on two scene servers, each
			 * transferring to a member the other is deleting — which the leaver-centric test
			 * cannot see and which nothing else would ever repair, since promoting somebody
			 * requires a leader to do it. */
			if (remainingCount > 0)
			{
				await EnsurePartyLeadershipAsync(charPartyService, partyID, remainingMembers, caller, preferOnlineSuccessor: true);
			}

			/* Delete the leaving member.
			 *
			 * ICharacterPartyService.DeleteAsync is keyed by CHARACTER and version — not by party.
			 * A character has at most one membership row, so that is normally the same thing, but
			 * it means this call would happily delete a row belonging to a DIFFERENT party if the
			 * character had moved between the fetch above and here: the new row starts at version
			 * 1, and the version gate would let it through.
			 *
			 * What closes that is on the other side: every path that gives a character a new
			 * membership refuses when one already exists — see AcceptPartyInviteAsync and
			 * TryAddCharacterToPartyAsync — so the row cannot be replaced underneath this, only
			 * removed. Anything that widens those checks has to widen this reasoning with them. */
			DatabaseResult deleteResult = await charPartyService.DeleteAsync(characterID, leavingMember.Version + 1);
			if (!deleteResult.IsSuccess)
			{
				await Log.Warning("PartySystem", $"{caller} member delete failed (CharID={characterID}, PartyID={partyID}): {deleteResult.ErrorCode} - {deleteResult.ErrorMessage}");
			}

			if (remainingCount < 1)
			{
				// Delete the party
				DatabaseResult partyDeleteResult = await partyService.DeleteAsync(partyID);
				if (!partyDeleteResult.IsSuccess)
				{
					await Log.Warning("PartySystem", $"{caller} party delete failed (PartyID={partyID}): {partyDeleteResult.ErrorCode} - {partyDeleteResult.ErrorMessage}");
				}
				DatabaseResult<int> updateDeleteResult = await partyUpdateService.DeleteAsync(partyID);
				if (!updateDeleteResult.IsSuccess)
				{
					await Log.Warning("PartySystem", $"{caller} party update delete failed (PartyID={partyID}): {updateDeleteResult.ErrorCode} - {updateDeleteResult.ErrorMessage}");
				}
			}
			else
			{
				// Tell the other servers to update their party lists
				DatabaseResult updateResult = await partyUpdateService.PersistAsync(partyID);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("PartySystem", $"{caller} party update notification failed (PartyID={partyID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
				}
			}

			return true;
		}

		/// <summary>
		/// Drops a character out of a party it cannot belong to, with no connection involved.
		/// </summary>
		/// <remarks>
		/// Called while a character is loading, before it has been spawned — principally when it
		/// has arrived on a world server other than the one its party belongs to. Parties are
		/// replicated between scene servers by a pump scoped to a single world server, so a
		/// membership that crossed would be updated by pumps that cannot see each other and would
		/// never converge; the membership is dropped on arrival instead.
		/// <para>
		/// Nothing is broadcast. There is no client yet to tell, and the character will be sent
		/// its party state — an absent one — as part of the load it is in the middle of.
		/// </para>
		/// </remarks>
		/// <param name="characterID">Character being removed.</param>
		/// <param name="partyID">Party it is being removed from.</param>
		/// <param name="reason">Why, for the log line.</param>
		public async Task<bool> RemoveCharacterFromPartyAsync(long characterID, long partyID, string reason)
		{
			/* Claimed like every other removal. This one runs while a character is still loading,
			 * which is the least likely moment to collide with anything — but "least likely" is
			 * not a reason to be the one path that can hand leadership to a member somebody else
			 * is simultaneously deleting. */
			if (!TryBeginPartyMutation(partyID, out long mutationToken))
			{
				/* Reported as a failure rather than swallowed. One caller is a load-time eviction
				 * that carries on without the party either way and will retry on the character's
				 * next load; the other is clearing the character's own party so it can join
				 * somebody else's, and that one must NOT proceed — the membership row still
				 * stands, and joining over it would move the character out of a party whose
				 * remaining members are never told. */
				await Log.Warning("PartySystem", $"Character {characterID} could not be removed from party {partyID} ({reason}): the party is being changed.");
				return false;
			}

			try
			{
				if (await RemovePartyMemberRowsAsync(characterID, partyID, nameof(RemoveCharacterFromPartyAsync)))
				{
					await Log.Debug("PartySystem", $"Character {characterID} was removed from party {partyID}: {reason}.");
				}

				/* True even when the rows call reported false. That result means the membership
				 * was not found, which is the state the caller wanted; a genuine failure to reach
				 * the database throws and is caught below. */
				return true;
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error removing character {characterID} from party {partyID}: {ex}");
				return false;
			}
			finally
			{
				EndPartyMutation(partyID, mutationToken);
			}
		}

		/// <summary>
		/// Asynchronously handles party leave DB operations: fetches members, transfers leadership if needed,
		/// deletes the leaving member, and cleans up or notifies other servers.
		/// </summary>
		/// <param name="conn">Leaving character connection.</param>
		/// <param name="characterID">Leaving character identifier.</param>
		/// <param name="partyID">Party identifier being left.</param>
		/// <param name="mutationToken">Party mutation claim taken by the caller.</param>
		/// <returns>Asynchronous leave-party task.</returns>
		private async Task LeavePartyAsync(NetworkConnection conn, long characterID, long partyID, long mutationToken)
		{
			try
			{
				if (!await RemovePartyMemberRowsAsync(characterID, partyID, nameof(LeavePartyAsync)))
				{
					return;
				}

				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive || conn.FirstObject == null)
					{
						return;
					}

					IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();
					if (partyController == null || partyController.Character.ID != characterID || partyController.ID != partyID)
					{
						return;
					}

					partyController.ID = 0;
					partyController.Rank = PartyRank.None;
					RemovePartyCharacterTracker(partyID, characterID);

					Server.NetworkWrapper.Broadcast(conn, new PartyLeaveBroadcast(), true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error leaving party (CharID={characterID}, PartyID={partyID}): {ex}");
			}
			finally
			{
				// Released on EVERY exit; a marker left set silences this character's persist.
				EndMembershipRemoval(characterID);
				EndPartyMutation(partyID, mutationToken);
			}
		}

		/// <summary>
		/// Claims exclusive rights to change one party's membership or ranks.
		/// </summary>
		/// <param name="partyID">The party to claim.</param>
		/// <param name="token">Receives the claim token, to be passed to <see cref="EndPartyMutation"/>.</param>
		/// <returns>True when the claim was granted.</returns>
		/// <remarks>
		/// See <see cref="IPartySystemRuntimeData.TryBeginPartyMutation"/> for why leadership
		/// cannot be made race-free without this. Every caller must release the claim on every
		/// exit path, including the ones that fail before doing any work.
		/// </remarks>
		private bool TryBeginPartyMutation(long partyID, out long token)
		{
			token = 0;
			return Server?.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData) == true &&
				   runtimeData.TryBeginPartyMutation(partyID, out token);
		}

		/// <summary>
		/// Releases a party mutation claim.
		/// </summary>
		/// <param name="partyID">The party claimed.</param>
		/// <param name="token">The token the claim was granted with.</param>
		private void EndPartyMutation(long partyID, long token)
		{
			if (Server?.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData) == true)
			{
				runtimeData.EndPartyMutation(partyID, token);
			}
		}

		/// <summary>
		/// Marks a character's party membership as being removed.
		/// </summary>
		/// <param name="characterID">The character leaving or being kicked.</param>
		private void BeginMembershipRemoval(long characterID)
		{
			if (Server?.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData) == true)
			{
				runtimeData.BeginMembershipRemoval(characterID);
			}
		}

		/// <summary>
		/// Clears the membership-removal marker for a character.
		/// </summary>
		/// <param name="characterID">The character whose removal has finished.</param>
		/// <remarks>
		/// Marshalled to the main thread: the marker is main-thread state and the callers that
		/// release it are background tasks. The disconnect handler that reads it also runs on the
		/// main thread, so the two can never interleave.
		/// </remarks>
		private void EndMembershipRemoval(long characterID)
		{
			TryEnqueueMainThread(() =>
			{
				if (Server?.DataContainerRegistry.TryGet(out IPartySystemRuntimeData runtimeData) == true)
				{
					runtimeData.EndMembershipRemoval(characterID);
				}
			});
		}

		/// <summary>
		/// Handles party member removal broadcast, validates and removes a member from the party in the database.
		/// Only party leaders can remove other members.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">PartyRemoveBroadcast message containing member ID to remove.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyRemoveBroadcastReceived(NetworkConnection conn, PartyRemoveBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Remove, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();

				// Validate that the requester is a party leader and not trying to remove themselves.
				if (partyController == null ||
					partyController.ID < 1 ||
					partyController.Rank != PartyRank.Leader)
				{
					return;
				}

				if (msg.CharacterID < 1)
				{
					return;
				}

				// Prevent party leaders from kicking themselves.
				if (msg.CharacterID == partyController.Character.ID)
				{
					return;
				}

				// Capture immutable data for the async path
				long partyID = partyController.ID;
				long memberID = msg.CharacterID;
				long characterID = partyController.Character.ID;

				// Serialised against every other change to this party, exactly as leaving is.
				if (!TryBeginPartyMutation(partyID, out long mutationToken))
				{
					SendServerBusy(conn);
					return;
				}

				/* Marked for the TARGET, not the requester: it is the target's membership row
				 * being deleted, and it is the target who could disconnect mid-delete and have
				 * the disconnect persist write it straight back. */
				BeginMembershipRemoval(memberID);

				deferGuardRelease = TryEnqueueIngressWork(() => RemovePartyMemberAsync(partyID, memberID, characterID, mutationToken), guardKey, characterID);
				if (!deferGuardRelease)
				{
					EndMembershipRemoval(memberID);
					EndPartyMutation(partyID, mutationToken);
					SendServerBusy(conn);
				}
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Asynchronously removes a member from the party, verifying rank permission and notifying other servers.
		/// </summary>
		/// <param name="partyID">Party identifier containing the member.</param>
		/// <param name="memberID">Target member character identifier.</param>
		/// <param name="requesterCharacterID">Requester character identifier.</param>
		/// <param name="mutationToken">Party mutation claim taken by the caller.</param>
		/// <returns>Asynchronous remove-member task.</returns>
		private async Task RemovePartyMemberAsync(long partyID, long memberID, long requesterCharacterID, long mutationToken)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}

				/* Re-verify the kicker still leads the party before deleting anybody's membership.
				 *
				 * The broadcast handler tested IPartyController.Rank, which the update pump
				 * refreshes — so an ex-leader who has just handed the rank on still passes that
				 * test for up to a pump interval and could throw members out of a party somebody
				 * else now runs. */
				if (!await IsCurrentPartyLeaderAsync(charPartyService, requesterCharacterID, partyID))
				{
					return;
				}

				// Fetch the target member to get their version for the versioned delete
				DatabaseResult<CharacterPartyData?> fetchResult = await charPartyService.FetchAsync(memberID);
				if (!fetchResult.IsSuccess || !fetchResult.Data.HasValue)
				{
					return;
				}

				CharacterPartyData targetMember = fetchResult.Data.Value;

				// Verify the target is actually in this party
				if (targetMember.PartyID != partyID)
				{
					return;
				}

				DatabaseResult deleteResult = await charPartyService.DeleteAsync(memberID, targetMember.Version + 1);
				if (deleteResult.IsSuccess)
				{
					// Marshal tracker update to main thread
					TryEnqueueMainThread(() =>
					{
						RemovePartyCharacterTracker(partyID, memberID);

						/* Tell the kicked member immediately if they are on this scene server.
						 * Nothing used to: their controller kept a live party ID until the next
						 * periodic pump noticed the row was gone, so for up to a pump interval
						 * they were still in a party they had been removed from — and any party
						 * action they took was authorised against that stale ID. Clearing the
						 * controller here also closes the disconnect-resurrection window, since
						 * the disconnect persist reads that same ID. */
						if (Server != null &&
							Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData) &&
							characterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter targetCharacter) &&
							targetCharacter != null &&
							targetCharacter.TryGet(out IPartyController targetPartyController) &&
							targetPartyController.ID == partyID)
						{
							targetPartyController.ID = 0;
							targetPartyController.Rank = PartyRank.None;

							if (targetCharacter.Owner != null)
							{
								Server.NetworkWrapper.Broadcast(targetCharacter.Owner, new PartyLeaveBroadcast(), true, Channel.Reliable);
							}
						}
					});

					// Tell the other servers to update their party lists.
					DatabaseResult updateResult = await partyUpdateService.PersistAsync(partyID);
					if (!updateResult.IsSuccess)
					{
						await Log.Warning("PartySystem", $"RemovePartyMemberAsync party update notification failed (PartyID={partyID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
					}
				}
				else
				{
					await Log.Warning("PartySystem", $"RemovePartyMemberAsync member delete failed (PartyID={partyID}, MemberID={memberID}): {deleteResult.ErrorCode} - {deleteResult.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error removing party member (PartyID={partyID}, MemberID={memberID}): {ex}");
			}
			finally
			{
				EndMembershipRemoval(memberID);
				EndPartyMutation(partyID, mutationToken);
			}
		}

		/// <summary>
		/// Handles party rank change broadcast, validates leader and target, and updates ranks in the database.
		/// Only party leaders can promote another member to leader.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">PartyChangeRankBroadcast message containing target member ID.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerPartyChangeRankBroadcastReceived(NetworkConnection conn, PartyChangeRankBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.ChangeRank, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				IPartyController partyController = conn.FirstObject.GetComponent<IPartyController>();

				// validate character
				if (partyController == null ||
					partyController.ID < 1 ||
					partyController.Rank != PartyRank.Leader)
				{
					return;
				}

				if (msg.CharacterID < 1)
				{
					return;
				}

				// we can't promote ourself
				if (msg.CharacterID == partyController.Character.ID)
				{
					return;
				}

				// Validate the requested rank — only Leader promotion is supported for parties.
				if (msg.Rank != PartyRank.Leader)
				{
					return;
				}

				// Capture immutable data for the async path
				long partyID = partyController.ID;
				long leaderCharacterID = partyController.Character.ID;
				long targetMemberID = msg.CharacterID;
				PartyRank newRank = msg.Rank;

				/* The most important of the claims. A promotion moves the rank in two writes, and
				 * anything else deciding leadership from rows read between them lands a second
				 * leader that nothing afterwards will notice, because a party WITH a leader looks
				 * healthy to every repair path there is. */
				if (!TryBeginPartyMutation(partyID, out long mutationToken))
				{
					SendServerBusy(conn);
					return;
				}

				deferGuardRelease = TryEnqueueIngressWork(() => ChangePartyRankAsync(partyID, leaderCharacterID, targetMemberID, newRank, mutationToken), guardKey, leaderCharacterID);
				if (!deferGuardRelease)
				{
					EndPartyMutation(partyID, mutationToken);
					SendServerBusy(conn);
				}
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Asynchronously swaps ranks between the current leader and the target member.
		/// </summary>
		/// <param name="partyID">Party identifier containing both members.</param>
		/// <param name="leaderCharacterID">Current leader character identifier.</param>
		/// <param name="targetMemberID">Target member character identifier.</param>
		/// <param name="newRank">New rank for the target member.</param>
		/// <param name="mutationToken">Party mutation claim taken by the caller.</param>
		/// <returns>Asynchronous rank-change task.</returns>
		private async Task ChangePartyRankAsync(long partyID, long leaderCharacterID, long targetMemberID, PartyRank newRank, long mutationToken)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService))
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<IPartyUpdateService>(out var partyUpdateService))
				{
					return;
				}

				// Fetch both members to get their versions
				DatabaseResult<CharacterPartyData?> leaderResult = await charPartyService.FetchAsync(leaderCharacterID);
				if (!leaderResult.IsSuccess || !leaderResult.Data.HasValue)
				{
					return;
				}

				DatabaseResult<CharacterPartyData?> targetResult = await charPartyService.FetchAsync(targetMemberID);
				if (!targetResult.IsSuccess || !targetResult.Data.HasValue)
				{
					return;
				}

				CharacterPartyData leaderData = leaderResult.Data.Value;
				CharacterPartyData targetData = targetResult.Data.Value;

				// Verify both are in the same party
				if (leaderData.PartyID != partyID || targetData.PartyID != partyID)
				{
					return;
				}

				/* And that the requester STILL leads it. The broadcast handler checked
				 * IPartyController.Rank, a cached copy the update pump refreshes — so a leader who
				 * promotes somebody and then immediately promotes somebody else is, on the second
				 * request, acting on a rank they gave away with the first. Without this the second
				 * promotion succeeds and the party ends up with two leaders: the new one this call
				 * demotes the requester in favour of, and the one from the promotion before it,
				 * whom nothing in this path ever looks at. The row is right here already. */
				if ((PartyRank)leaderData.Rank != PartyRank.Leader)
				{
					return;
				}

				/* Deliberately NOT short-circuited when the target already holds the rank. That
				 * state means the party has two leaders — the one thing this system tries hardest
				 * not to produce — and running the promote-then-demote pair anyway is what
				 * collapses it back to one. Refusing as a no-op would leave it standing. */

				// Promote the target to the requested rank FIRST so the party is never left leaderless.
				DatabaseResult promoteResult = await charPartyService.UpdateRankAsync(targetMemberID, partyID, (byte)newRank, targetData.Version + 1);
				if (!promoteResult.IsSuccess)
				{
					return;
				}

				// Demote the previous leader to member.
				// The party temporarily has two leaders between these calls, which is harmless
				// and strictly better than the zero-leader state the reverse order could create.
				DatabaseResult demoteResult = await charPartyService.UpdateRankAsync(leaderCharacterID, partyID, (byte)PartyRank.Member, leaderData.Version + 1);
				if (!demoteResult.IsSuccess)
				{
					// Rollback: revert the target back to their original rank to avoid two leaders.
					DatabaseResult rollbackResult = await charPartyService.UpdateRankAsync(targetMemberID, partyID, targetData.Rank, targetData.Version + 2);
					if (!rollbackResult.IsSuccess)
					{
						await Log.Error("PartySystem", $"CRITICAL: Promoted target {targetMemberID} but failed to demote old leader {leaderCharacterID} AND failed to rollback in party {partyID}. Party has two leaders until manually corrected.");
					}
					else
					{
						await Log.Warning("PartySystem", $"Promoted target {targetMemberID} but failed to demote old leader {leaderCharacterID} in party {partyID}. Rolled back promotion successfully.");
					}
					return;
				}

				// Tell the other servers to update their party lists
				DatabaseResult updateResult = await partyUpdateService.PersistAsync(partyID);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("PartySystem", $"ChangePartyRankAsync party update notification failed (PartyID={partyID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("PartySystem", $"Error changing party rank (PartyID={partyID}, Leader={leaderCharacterID}, Target={targetMemberID}): {ex}");
			}
			finally
			{
				EndPartyMutation(partyID, mutationToken);
			}
		}

		// Uses ServerBehaviour.TryEnqueueAsyncWork

		/// <summary>
		/// Enqueues ingress work and guarantees guard release when async processing completes.
		/// </summary>
		private bool TryEnqueueIngressWork(Func<Task> work, long guardKey, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			return TryEnqueueGuardedAsyncWork(work, EndIngressGuard, guardKey, entityKey, callerName);
		}
	}
}