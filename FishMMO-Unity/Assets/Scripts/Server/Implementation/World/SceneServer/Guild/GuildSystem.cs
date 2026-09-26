using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
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
	/// Manages guild creation, membership, ranks, invitations, and updates with database synchronization.
	/// Game logic and Broadcasts run synchronously on the main thread.
	/// Database operations are async to avoid blocking the main thread.
	/// Results from async DB queries that require main-thread state changes or Broadcasts are marshalled
	/// via IGuildSystemMainThreadQueueData.
	/// </summary>
	[CreateAssetMenu(fileName = "GuildSystem", menuName = "FishMMO/Server/SceneServer/Guild System", order = 1)]
	[RequiresDataContainer(typeof(GuildSystemRuntimeData))]
	[RequiresDataContainer(typeof(GuildCharacterMappingData))]
	[RequiresDataContainer(typeof(GuildSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class GuildSystem : ServerBehaviour, IGuildSystem<NetworkConnection>
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max guild-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Maximum number of members allowed per guild.
		/// </summary>
		[SerializeField]
		private int maxGuildSize = 100;
		/// <summary>
		/// Periodic guild update polling interval in seconds.
		/// </summary>
		[Tooltip("The server guild update pump rate limit in seconds.")]
		[SerializeField]
		private float updatePumpRate = 1.0f;

		/// <summary>
		/// Seconds of clock skew tolerated when advancing the guild update watermark.
		/// </summary>
		/// <remarks>
		/// The guild update rows are stamped by the DATABASE clock
		/// (<c>GuildUpdateService.PersistAsync</c>) and the pump's mark by this server's, so the
		/// mark trails this server's "now" by this much: an update stamped up to this far ahead is
		/// still caught. Updates inside the window are fetched again on later passes and skipped by
		/// the processed-update record, so the allowance costs a cheap re-fetch, not a re-send. The
		/// same model, and the same default, as the party pump's.
		/// </remarks>
		[Tooltip("Seconds of skew between this server's clock and the database's tolerated when advancing the guild update watermark")]
		[SerializeField]
		private float guildUpdateClockSkewAllowanceSeconds = 5.0f;

		/// <summary>
		/// Seconds between checks that every guild with members on this server still exists.
		/// </summary>
		/// <remarks>
		/// A disbanded guild leaves no update row behind — the row is deleted with the guild — so the
		/// update pump never hears of it, and this sweep is how a guild disbanded on ANOTHER scene
		/// server is taken from its members here: within this many seconds. The pump used to ask
		/// after every tracked guild on every pass, once a second, to learn that a handful of times a
		/// day. A disband on THIS server clears its local members at once and needs no sweep.
		/// </remarks>
		[Tooltip("Seconds between checks that every guild with members on this server still exists; a guild disbanded on another server is noticed within this long")]
		[SerializeField]
		private float guildExistenceSweepSeconds = 30.0f;

		/// <summary>
		/// Non-zero while an existence sweep is in flight. See <see cref="OnPeriodicGuildExistenceSweep"/>.
		/// </summary>
		private int guildExistenceSweepInFlight;

		/// <summary>
		/// Failures of the existence sweep. Touched only by the one sweep in flight, which the
		/// in-flight flag serialises.
		/// </summary>
		private readonly RepeatingFaultLog guildExistenceSweepFaults = new RepeatingFaultLog("GuildSystem", "Guild existence sweep");

		/// <summary>
		/// Seconds between sweeps of the guild pump's processed-update record.
		/// </summary>
		private const float ProcessedGuildUpdateSweepIntervalSeconds = 30.0f;

		/// <summary>
		/// When the processed-update record is next swept, in <see cref="MonotonicClock"/> seconds.
		/// </summary>
		/// <remarks>
		/// The schedule is a local duration. The sweep itself ages the records on the wall clock,
		/// because they are the update rows' database timestamps; see
		/// <see cref="UpdatePumpWatermark.ProcessedRecordLifetime"/>.
		/// </remarks>
		private double nextProcessedGuildUpdateSweepAt;

		/// <summary>
		/// How long an unread guild update stays worth re-reading, and how long its processed record is kept.
		/// </summary>
		/// <summary>The database's clock as the update pump last read it.</summary>
		private readonly UpdatePumpWatermark.DatabaseClock guildDatabaseClock = new UpdatePumpWatermark.DatabaseClock();

		private TimeSpan GuildUpdateRetryHorizon => UpdatePumpWatermark.RetryHorizon(guildUpdateClockSkewAllowanceSeconds);

		/// <summary>
		/// The recipients of one copy of a guild delivery: their connections, for the multicast, and
		/// their character IDs, for the baselines recorded once it has gone.
		/// </summary>
		/// <remarks>
		/// Scratch, main-thread only, and emptied after every guild. The multicast reads the set and
		/// never keeps it, so one audience can carry every guild's delivery in turn.
		/// </remarks>
		private sealed class GuildAudience
		{
			public readonly HashSet<NetworkConnection> Connections = new HashSet<NetworkConnection>();
			public readonly List<long> CharacterIDs = new List<long>();

			public int Count => Connections.Count;

			public void Add(NetworkConnection connection, long characterID)
			{
				if (Connections.Add(connection))
				{
					CharacterIDs.Add(characterID);
				}
			}

			public void AddAll(GuildAudience other)
			{
				foreach (NetworkConnection connection in other.Connections)
				{
					Connections.Add(connection);
				}
				CharacterIDs.AddRange(other.CharacterIDs);
			}

			public void Clear()
			{
				Connections.Clear();
				CharacterIDs.Clear();
			}
		}

		/// <summary>Members sent the whole roster WITHOUT officer notes: they have no baseline in that audience.</summary>
		private readonly GuildAudience guildPublicFullAudience = new GuildAudience();

		/// <summary>Members sent the delta WITHOUT officer notes.</summary>
		private readonly GuildAudience guildPublicDeltaAudience = new GuildAudience();

		/// <summary>Members sent the whole roster WITH officer notes.</summary>
		private readonly GuildAudience guildOfficerFullAudience = new GuildAudience();

		/// <summary>Members sent the delta WITH officer notes.</summary>
		private readonly GuildAudience guildOfficerDeltaAudience = new GuildAudience();

		/// <summary>
		/// Members owed a rank list, keyed by their own rank order.
		/// </summary>
		/// <remarks>
		/// <see cref="GuildRankListBroadcast"/> carries the viewer's own rank and permission mask, so
		/// it is identical only for members holding the same rank: one multicast per rank that has a
		/// local member owed one, rather than one message per member.
		/// </remarks>
		private readonly Dictionary<byte, GuildAudience> guildRankListAudiences = new Dictionary<byte, GuildAudience>();

		/// <summary>Spare audiences returned by <see cref="guildRankListAudiences"/> between guilds.</summary>
		private readonly Stack<GuildAudience> guildAudiencePool = new Stack<GuildAudience>();

		/// <summary>Scratch output of <see cref="GuildRosterDelta.Diff"/>: indices of changed rows.</summary>
		private readonly List<int> guildDeltaChangedIndices = new List<int>();

		/// <summary>Scratch output of <see cref="GuildRosterDelta.Diff"/>: removed character IDs.</summary>
		private readonly List<long> guildDeltaRemovedIDs = new List<long>();

		/// <summary>
		/// What each local member's client holds of its guild's roster and ladder. Main-thread only.
		/// </summary>
		private readonly GuildRecipientBaselines guildRecipientBaselines = new GuildRecipientBaselines();

		/// <summary>A guild's rank ladder as last delivered, and the generation that identifies it.</summary>
		private readonly struct DeliveredLadder
		{
			public readonly GuildRankEntry[] Entries;
			public readonly long Generation;

			public DeliveredLadder(GuildRankEntry[] entries, long generation)
			{
				Entries = entries;
				Generation = generation;
			}
		}

		/// <summary>
		/// The ladder last delivered for each guild tracked here. Dropped with the guild's roster.
		/// </summary>
		private readonly Dictionary<long, DeliveredLadder> guildDeliveredLadders = new Dictionary<long, DeliveredLadder>();

		/// <summary>
		/// Source of ladder generations. Shared by every guild and never reset while running, so a
		/// generation recorded for a guild that was dropped and later tracked again can never match
		/// the new one.
		/// </summary>
		private long guildLadderGenerationCounter;

		/// <summary>
		/// Currency attribute a character pays to found a guild. Issue #186.
		/// </summary>
		/// <remarks>
		/// Any <see cref="CharacterAttributeTemplate"/> will do — gold, a premium currency, a
		/// faction token — because currency in FishMMO is an attribute and
		/// <see cref="CharacterCurrency"/> spends against the attribute's BASE value. Left empty,
		/// or with <see cref="guildCreationFee"/> at zero, founding a guild is free and nothing
		/// about the create path changes.
		/// </remarks>
		[Header("Creation Fee")]
		[Tooltip("Currency attribute a character pays to found a guild. Leave empty for no fee.")]
		[SerializeField]
		private CharacterAttributeTemplate guildCreationFeeCurrency;

		/// <summary>
		/// Amount of <see cref="guildCreationFeeCurrency"/> charged to found a guild. Zero or less
		/// disables the fee.
		/// </summary>
		[Tooltip("Amount charged to found a guild. Zero or less means no fee.")]
		[SerializeField]
		private long guildCreationFee = 0;

		/// <summary>True when founding a guild costs something on this server.</summary>
		private bool HasCreationFee => guildCreationFeeCurrency != null && guildCreationFee > 0;

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
		/// the ingress debounce is per connection rather than per target, so neither stops one
		/// player from keeping a modal permanently on another player's screen. This does.
		/// </remarks>
		[Tooltip("Minimum seconds between guild invitations to the same target from the same inviter")]
		[SerializeField] private float perTargetInviteCooldownSeconds = 60.0f;

		/// <summary>
		/// Number of activity log rows retained per guild, and the most a client can be sent.
		/// </summary>
		/// <remarks>
		/// An append-only table on a long-lived guild grows without limit and nothing else in the
		/// schema would ever remove from it, so the append path trims to this depth. It is also
		/// the read cap: the panel shows a scrollback, not an archive.
		/// </remarks>
		[Header("Activity Log")]
		[Tooltip("Activity log rows retained per guild")]
		[SerializeField] private int guildLogRetainedEntries = 100;

		/// <summary>
		/// How many appends may pass before the log is trimmed again.
		/// </summary>
		/// <remarks>
		/// Pruning on every append would double the write cost of every guild event for a table
		/// that only needs to stay roughly bounded. Trimming every N appends keeps the row count
		/// within N of the target and costs one extra statement per N events.
		/// </remarks>
		[Tooltip("Appends between activity log prune passes")]
		[SerializeField] private int guildLogPruneInterval = 25;

		/// <summary>
		/// Debounce window in milliseconds for guild ingress operations.
		/// </summary>
		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between guild requests per connection and operation")]
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
		/// Achievement to increment when a player creates a guild.
		/// </summary>
		/// <summary>
		/// Directory rows returned per browse request.
		/// </summary>
		/// <remarks>
		/// A cap, not a page cursor. The directory is browsed by SEARCHING rather than by paging
		/// — a player looking for a guild types "pvp", they do not read page four of everything —
		/// so a bounded result set with a search box is the honest shape, and it keeps one request
		/// from serialising every recruiting guild on the shard.
		/// </remarks>
		[Header("Recruitment")]
		[Tooltip("Directory rows returned per browse request")]
		[SerializeField] private int guildDirectoryPageSize = 50;

		/// <summary>
		/// Pending applications sent to an officer per request.
		/// </summary>
		[Tooltip("Pending applications sent per queue request")]
		[SerializeField] private int guildApplicationPageSize = 50;

		/// <summary>
		/// Most applications one character may have outstanding at once.
		/// </summary>
		/// <remarks>
		/// Enforced inside the INSERT alongside the per-guild uniqueness. The unique index stops
		/// repeat applications to one guild; this stops one player queuing themselves into every
		/// guild on the shard and making the officer queues useless for everybody.
		/// </remarks>
		[Tooltip("Most applications one character may have outstanding")]
		[SerializeField] private int maxPendingApplicationsPerCharacter = 5;

		/// <summary>
		/// Minimum seconds between applications from the same character.
		/// </summary>
		/// <remarks>
		/// The rate limit proper. The ingress debounce is a hundred milliseconds and exists to
		/// absorb a double-click; the per-guild unique index does not constrain a sweep ACROSS
		/// guilds; and the outstanding cap can be reset by withdrawing. A player working down the
		/// directory can defeat all three, and this is what stops them.
		/// </remarks>
		[Tooltip("Minimum seconds between guild applications from the same character")]
		[SerializeField] private float applicationCooldownSeconds = 30.0f;

		[Header("Achievements")]
		public AchievementTemplate GuildCreateAchievementTemplate;

		/// <summary>
		/// Achievement to increment when a player joins a guild.
		/// </summary>
		public AchievementTemplate GuildJoinAchievementTemplate;

		/// <summary>
		/// Operation keys used by guild ingress guards.
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
			SetInfo = 8,
			TransferLeadership = 9,
			Disband = 10,
			LogRequest = 11,
			RankList = 12,
			EditRank = 13,
			CreateRank = 14,
			DeleteRank = 15,
			SetNote = 16,
			SetRecruitment = 17,
			Directory = 18,
			Apply = 19,
			ApplicationList = 20,
			ResolveApplication = 21,
		}

		/// <summary>
		/// Gets the update pump rate for guild synchronization.
		/// </summary>
		public float UpdatePumpRate { get { return updatePumpRate; } }

		/// <summary>
		/// Maximum number of members allowed in a guild.
		/// </summary>
		public int MaxGuildSize { get { return maxGuildSize; } }

		/// <summary>
		/// Handles guild invite chat commands.
		/// </summary>
		/// <param name="sender">The character sending the invite.</param>
		/// <param name="msg">Chat broadcast message containing the target character name.</param>
		/// <returns>True if invite was sent, false otherwise.</returns>
		public bool OnGuildInvite(IPlayerCharacter sender, ChatBroadcast msg)
		{
			if (sender == null || string.IsNullOrWhiteSpace(msg.Text))
			{
				return false;
			}

			string characterName = msg.Text.Trim().ToLowerInvariant();
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByLowerCaseName.TryGetValue(characterName, out IPlayerCharacter character))
			{
				OnServerGuildInviteBroadcastReceived(sender.Owner, new GuildInviteBroadcast()
				{
					InviterCharacterID = sender.ID,
					TargetCharacterID = character.ID,
				}, Channel.Reliable);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Initializes the guild system, registering chat commands and broadcast handlers, and character events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("GuildSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IGuildSystemMainThreadQueueData>(out _))
			{
				Log.Error("GuildSystem", "Failed to initialize: IGuildSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem) ||
				characterSystem == null)
			{
				Log.Error("GuildSystem", "Failed to initialize: ICharacterSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Chat commands
			ChatHelper.AddCommands(new Dictionary<string, ChatCommand>()
			{
				{ "/gi", OnGuildInvite },
				{ "/ginvite", OnGuildInvite },
			});
			ChatHelper.SetCommandHelp("/ginvite", new ChatCommandHelp()
			{
				Category = "Social",
				Arguments = "<character>",
				Summary = "Invites a character to your guild.",
				Aliases = new[] { "/gi" },
			});

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<GuildCreateBroadcast>(OnServerGuildCreateBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildInviteBroadcast>(OnServerGuildInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildAcceptInviteBroadcast>(OnServerGuildAcceptInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildDeclineInviteBroadcast>(OnServerGuildDeclineInviteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildLeaveBroadcast>(OnServerGuildLeaveBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildRemoveBroadcast>(OnServerGuildRemoveBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildChangeRankBroadcast>(OnServerGuildChangeRankBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildSetMessageOfTheDayBroadcast>(OnServerGuildSetMessageOfTheDayBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildSetNoticeBroadcast>(OnServerGuildSetNoticeBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildTransferLeadershipBroadcast>(OnServerGuildTransferLeadershipBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildDisbandBroadcast>(OnServerGuildDisbandBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildLogRequestBroadcast>(OnServerGuildLogRequestBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildRankListRequestBroadcast>(OnServerGuildRankListRequestBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildEditRankBroadcast>(OnServerGuildEditRankBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildCreateRankBroadcast>(OnServerGuildCreateRankBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildDeleteRankBroadcast>(OnServerGuildDeleteRankBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildSetMemberNoteBroadcast>(OnServerGuildSetMemberNoteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildSetRecruitmentBroadcast>(OnServerGuildSetRecruitmentBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildDirectoryRequestBroadcast>(OnServerGuildDirectoryRequestBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildApplyBroadcast>(OnServerGuildApplyBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildApplicationListRequestBroadcast>(OnServerGuildApplicationListRequestBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<GuildResolveApplicationBroadcast>(OnServerGuildResolveApplicationBroadcastReceived, true);

			// Character system events
			characterSystem.OnConnect += CharacterSystem_OnConnect;
			characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;

			// Periodic callbacks
			guildExistenceSweepSeconds = Mathf.Max(1.0f, guildExistenceSweepSeconds);
			Interlocked.Exchange(ref guildExistenceSweepInFlight, 0);

			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(UpdatePumpRate, OnPeriodicUpdate);
				periodicSystem.RegisterPeriodicCallback(guildExistenceSweepSeconds, OnPeriodicGuildExistenceSweep);
			}

			if (!Server.DataContainerRegistry.TryGet<IGuildSystemRuntimeData>(out var runtimeData))
			{
				Log.Error("GuildSystem", "Failed to initialize: IGuildSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			invitationTtlSeconds = Mathf.Max(5.0f, invitationTtlSeconds);
			invitationSweepIntervalSeconds = Mathf.Max(0.1f, invitationSweepIntervalSeconds);
			invitationSweepMaxScan = Mathf.Max(1, invitationSweepMaxScan);
			invitationSweepMaxRemove = Mathf.Max(1, invitationSweepMaxRemove);
			perTargetInviteCooldownSeconds = Mathf.Max(0.0f, perTargetInviteCooldownSeconds);
			guildLogRetainedEntries = Mathf.Clamp(guildLogRetainedEntries, 10, 200);
			guildLogPruneInterval = Mathf.Max(1, guildLogPruneInterval);
			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);
			guildUpdateClockSkewAllowanceSeconds = Mathf.Max(0.0f, guildUpdateClockSkewAllowanceSeconds);
			runtimeData.EndUpdatePump();
			runtimeData.NextInvitationSweepAt = MonotonicClock.NowSeconds;

			/* Instance state on a ScriptableObject survives between editor play sessions when domain
			 * reload is off, and the recipient sets hold connections: emptied here so a previous
			 * session's connections are never multicast to. */
			nextProcessedGuildUpdateSweepAt = double.NegativeInfinity;
			ReleaseGuildAudiences();
			guildAudiencePool.Clear();
			guildRecipientBaselines.Clear();
			guildDeliveredLadders.Clear();

			Log.Debug("GuildSystem", $"Initialized (MaxGuildSize={MaxGuildSize}, UpdatePumpRate={UpdatePumpRate}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the guild system, unregistering broadcast handlers and character events.
		/// </summary>
		public override void OnDeinitialize()
		{
			// Static registry: a command left behind outlives this ScriptableObject and would
			// run against a destroyed instance. See ChatHelper.RemoveCommands.
			ChatHelper.RemoveCommands(new[] { "/gi", "/ginvite" });

			if (Server == null)
			{
				Log.Error("GuildSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue(drainAll: true);

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<GuildCreateBroadcast>(OnServerGuildCreateBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildInviteBroadcast>(OnServerGuildInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildAcceptInviteBroadcast>(OnServerGuildAcceptInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildDeclineInviteBroadcast>(OnServerGuildDeclineInviteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildLeaveBroadcast>(OnServerGuildLeaveBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildRemoveBroadcast>(OnServerGuildRemoveBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildChangeRankBroadcast>(OnServerGuildChangeRankBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildSetMessageOfTheDayBroadcast>(OnServerGuildSetMessageOfTheDayBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildSetNoticeBroadcast>(OnServerGuildSetNoticeBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildTransferLeadershipBroadcast>(OnServerGuildTransferLeadershipBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildDisbandBroadcast>(OnServerGuildDisbandBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildLogRequestBroadcast>(OnServerGuildLogRequestBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildRankListRequestBroadcast>(OnServerGuildRankListRequestBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildEditRankBroadcast>(OnServerGuildEditRankBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildCreateRankBroadcast>(OnServerGuildCreateRankBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildDeleteRankBroadcast>(OnServerGuildDeleteRankBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildSetMemberNoteBroadcast>(OnServerGuildSetMemberNoteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildSetRecruitmentBroadcast>(OnServerGuildSetRecruitmentBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildDirectoryRequestBroadcast>(OnServerGuildDirectoryRequestBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildApplyBroadcast>(OnServerGuildApplyBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildApplicationListRequestBroadcast>(OnServerGuildApplicationListRequestBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<GuildResolveApplicationBroadcast>(OnServerGuildResolveApplicationBroadcastReceived);

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
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicGuildExistenceSweep);
			}
		}

		/// <summary>
		/// Drains queued main-thread actions from the IGuildSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			MainThreadQueueHelper.Drain<IGuildSystemMainThreadQueueData>(Server, maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return MainThreadQueueHelper.TryEnqueue<IGuildSystemMainThreadQueueData>(Server, action);
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
			SweepPendingInvitations();
			SweepIngressGuards();
		}

		/// <summary>
		/// Attempts to acquire ingress debounce and in-flight guard for a connection operation.
		/// </summary>
		private bool TryBeginIngressGuard(int connectionId, IngressOperation operation, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
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
			if (Server?.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData) == true)
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Performs bounded cleanup of stale ingress guard entries.
		/// </summary>
		private void SweepIngressGuards()
		{
			if (Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}
		}

		/// <summary>
		/// Performs a bounded TTL sweep over pending guild invitations.
		/// </summary>
		private void SweepPendingInvitations()
		{
			if (!Server.DataContainerRegistry.TryGet<IGuildSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			double now = MonotonicClock.NowSeconds;
			if (now < runtimeData.NextInvitationSweepAt)
			{
				return;
			}

			runtimeData.NextInvitationSweepAt = now + invitationSweepIntervalSeconds;

			runtimeData.SweepExpiredInvitations(
				now,
				TimeSpan.FromSeconds(invitationTtlSeconds),
				invitationSweepMaxScan,
				invitationSweepMaxRemove);

			/* Cooldown entries are pure memory and nothing else ever removes them, so they need
			 * the same bounded sweep the invitations get. Their TTL is the cooldown itself: once
			 * it has elapsed the entry can no longer refuse anything. */
			runtimeData.SweepInviteCooldowns(
				now,
				TimeSpan.FromSeconds(perTargetInviteCooldownSeconds),
				invitationSweepMaxScan,
				invitationSweepMaxRemove);

			runtimeData.SweepApplicationCooldowns(
				now,
				TimeSpan.FromSeconds(applicationCooldownSeconds),
				invitationSweepMaxScan,
				invitationSweepMaxRemove);
		}

		/// <summary>
		/// Begins the per-character guild application cooldown.
		/// </summary>
		/// <param name="characterID">The applying character.</param>
		/// <returns>True when the application may proceed.</returns>
		private bool TryBeginApplicationCooldown(long characterID)
		{
			if (Server == null ||
				!Server.DataContainerRegistry.TryGet<IGuildSystemRuntimeData>(out var runtimeData) ||
				runtimeData == null)
			{
				/* No runtime data means no rate limiting is possible. Refusing would take guild
				 * applications offline entirely over a container lookup; the database still
				 * enforces the per-guild uniqueness and the outstanding cap. */
				return true;
			}

			return runtimeData.TryBeginApplicationCooldown(
				characterID,
				TimeSpan.FromSeconds(applicationCooldownSeconds),
				MonotonicClock.NowSeconds);
		}

		/// <summary>
		/// Periodic callback that fetches and processes guild updates from the database asynchronously.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicUpdate(float deltaTime)
		{
			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<IGuildSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			/* Independent of whether a pump is in flight: the record only has to outlive the window
			 * in which its update can still be re-fetched (UpdatePumpWatermark.ProcessedRecordLifetime),
			 * and past that it is memory held for a guild that has stopped changing. */
			double now = MonotonicClock.NowSeconds;
			if (now >= nextProcessedGuildUpdateSweepAt)
			{
				nextProcessedGuildUpdateSweepAt = now + ProcessedGuildUpdateSweepIntervalSeconds;
				// The records are database stamps: aged on the database's clock as the pump last read it.
				if (guildDatabaseClock.TryNow(out DateTime databaseNowUtc))
				{
					runtimeData.SweepProcessedGuildUpdates(databaseNowUtc, UpdatePumpWatermark.ProcessedRecordLifetime(guildUpdateClockSkewAllowanceSeconds));
				}
			}

			if (!runtimeData.TryBeginUpdatePump())
			{
				return;
			}

			// Snapshot main-thread-only Dictionary keys before going async (C4 fix).
			if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData) ||
				mappingData.GuildCharacterTracker.Count == 0)
			{
				runtimeData.EndUpdatePump();
				return;
			}

			List<long> guildIds = new List<long>(mappingData.GuildCharacterTracker.Keys);
			DateTime lastFetch = runtimeData.LastFetchTime;

			if (!TryEnqueueAsyncWork(() => FetchAndProcessGuildUpdatesAsync(guildIds, lastFetch)))
			{
				runtimeData.EndUpdatePump();
			}
		}

		/// <summary>
		/// Asynchronously fetches guild updates from the database and marshals the processing back to the main thread.
		/// </summary>
		/// <returns>Asynchronous fetch-and-process task.</returns>
		/// <remarks>
		/// <para>
		/// <b>The same watermark model as the party pump</b> — see <see cref="UpdatePumpWatermark"/>
		/// for the rule itself. The mark is stamped from this server's clock BEFORE the query, less a
		/// skew allowance (the rows are stamped by the database's clock); it is held back for an
		/// update whose snapshot could not be read, but only within a retry horizon; and every update
		/// delivered is recorded, so the passes that fetch it again inside the window skip it rather
		/// than re-reading and re-broadcasting the guild.
		/// </para>
		/// <para>
		/// This pump used to hold its mark at an unread update's timestamp with no horizon and no
		/// record of what it had delivered. A guild whose read failed every time pinned the mark for
		/// good, and every guild updated after it was re-read — two serial queries each — and its
		/// whole roster re-broadcast to every local member, every second, for as long as the server
		/// ran. Its mark was also capped at the fetch start with no allowance, so a database clock
		/// running ahead of this server's re-sent each update several times.
		/// </para>
		/// <para>
		/// <b>The snapshot is read in bulk</b>: one roster query and one ladder query for every
		/// changed guild, instead of two per guild in series. A bulk read has no per-guild failure
		/// — it fails because the database did — so a failed one holds every guild in the pass, each
		/// within its own horizon. Seeding a ladder for a guild that has none is the one per-guild
		/// step left, and its failure holds that guild alone.
		/// </para>
		/// <para>
		/// A guild enters the delivery only with BOTH its roster and its ladder: a roster sent
		/// without a ladder would have to carry SOME permission mask, and the only one available is
		/// None, which used to strip every local member's cached permissions and blank their panel.
		/// </para>
		/// </remarks>
		private async Task FetchAndProcessGuildUpdatesAsync(List<long> guildIds, DateTime lastFetch)
		{
			try
			{
				if (!TryGetDbService(out IGuildUpdateService guildUpdateService) ||
					!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildRankService rankService))
				{
					return;
				}

				if (guildIds.Count == 0)
				{
					return;
				}

				/* The mark is the database's own clock, read by the fetch before the rows, so no host
				 * clock is compared with the rows' stamps; see UpdatePumpWatermark.FetchStarted and
				 * PartySystem's pump. guildUpdateClockSkewAllowanceSeconds is now a commit window. */
				DatabaseResult<UpdatePumpRead<GuildUpdateData>> fetchResult = await guildUpdateService.FetchAsync(guildIds, lastFetch);
				if (!fetchResult.IsSuccess)
				{
					// The mark stays where it was, so nothing is skipped: the next pass asks again.
					await Log.Warning("GuildSystem", $"FetchAndProcessGuildUpdatesAsync update fetch failed ({guildIds.Count} guilds since {lastFetch:O}): {fetchResult.ErrorCode} - {fetchResult.ErrorMessage}");
					return;
				}

				guildDatabaseClock.Observe(fetchResult.Data.ReadStartedUtc);
				DateTime fetchStartedUtc = UpdatePumpWatermark.FetchStarted(fetchResult.Data.ReadStartedUtc, guildUpdateClockSkewAllowanceSeconds);
				TimeSpan retryHorizon = GuildUpdateRetryHorizon;
				DateTime retryHorizonUtc = fetchStartedUtc - retryHorizon;

				/* No existence read here for the guilds that did not change. A disbanded guild has no
				 * update row to find — the row goes with the guild — so it is noticed by the slower
				 * existence sweep (OnPeriodicGuildExistenceSweep), not by asking after every tracked
				 * guild on every pass. The guilds this pass DID read are checked below, and only
				 * those whose roster came back empty, which is what a deleted guild reads as. */

				if (fetchResult.Data.Updates == null || fetchResult.Data.Updates.Count < 1)
				{
					return;
				}

				Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData dedupeData);

				/* The newest not-yet-processed update per guild. The table holds one row per guild,
				 * so a guild appears at most once; the newest is kept regardless, because that is
				 * the stamp the processed record has to reach. */
				Dictionary<long, DateTime> pending = new Dictionary<long, DateTime>();
				foreach (GuildUpdateData update in fetchResult.Data.Updates)
				{
					if (dedupeData != null && dedupeData.HasProcessedGuildUpdate(update.GuildID, update.LastUpdate))
					{
						continue;
					}

					if (!pending.TryGetValue(update.GuildID, out DateTime known) || update.LastUpdate > known)
					{
						pending[update.GuildID] = update.LastUpdate;
					}
				}

				DateTime watermarkUtc = fetchStartedUtc;

				Dictionary<long, IReadOnlyList<CharacterGuildData>> guildMembersMap = new Dictionary<long, IReadOnlyList<CharacterGuildData>>(pending.Count);
				Dictionary<long, IReadOnlyList<GuildRankData>> guildLaddersMap = new Dictionary<long, IReadOnlyList<GuildRankData>>(pending.Count);
				Dictionary<long, DateTime> processedUpdateStamps = new Dictionary<long, DateTime>(pending.Count);

				if (pending.Count > 0)
				{
					long[] pendingIDs = new long[pending.Count];
					pending.Keys.CopyTo(pendingIDs, 0);

					/* One roster query and one ladder query for every changed guild. The ladder is
					 * read in the same pass as the rows so the officer-note filter and the cached
					 * standings below are decided against the snapshot the roster was built from,
					 * without a database call and without trusting the rank cached on a character. */
					IReadOnlyDictionary<long, IReadOnlyList<CharacterGuildData>> rosters = null;
					IReadOnlyDictionary<long, IReadOnlyList<GuildRankData>> ladders = null;

					DatabaseResult<IReadOnlyDictionary<long, IReadOnlyList<CharacterGuildData>>> rosterResult = await charGuildService.FetchManyAsync(pendingIDs);
					if (!rosterResult.IsSuccess || rosterResult.Data == null)
					{
						await Log.Warning("GuildSystem", $"FetchAndProcessGuildUpdatesAsync roster read failed for {pendingIDs.Length} guild(s); each is held for the next pass within {retryHorizon.TotalSeconds:0}s: {rosterResult.ErrorCode} - {rosterResult.ErrorMessage}");
					}
					else
					{
						DatabaseResult<IReadOnlyDictionary<long, IReadOnlyList<GuildRankData>>> ladderResult = await rankService.FetchManyAsync(pendingIDs);
						if (!ladderResult.IsSuccess || ladderResult.Data == null)
						{
							await Log.Warning("GuildSystem", $"FetchAndProcessGuildUpdatesAsync ladder read failed for {pendingIDs.Length} guild(s); each is held for the next pass within {retryHorizon.TotalSeconds:0}s: {ladderResult.ErrorCode} - {ladderResult.ErrorMessage}");
						}
						else
						{
							rosters = rosterResult.Data;
							ladders = ladderResult.Data;
						}
					}

					/* A guild whose roster came back EMPTY has either lost its last member or been
					 * deleted since its update was fetched: the bulk read answers "no rows" for both.
					 * Those alone are asked about — a roster with rows proves the guild existed when it
					 * was read — and asked BEFORE the empty ladder below would be re-seeded for a guild
					 * that is gone. A deleted guild is cleared here and its update needs nothing more:
					 * its row went with it. A check that could not be made holds the guild for the next
					 * pass, like any other read that failed; a guild is never taken from its members on
					 * the strength of a read that did not happen. */
					HashSet<long> vanishedGuilds = null;
					HashSet<long> existenceUnknown = null;
					if (rosters != null)
					{
						List<long> emptyRosters = null;
						foreach (long guildID in pendingIDs)
						{
							if (rosters.TryGetValue(guildID, out IReadOnlyList<CharacterGuildData> read) && read != null && read.Count == 0)
							{
								(emptyRosters ??= new List<long>()).Add(guildID);
							}
						}

						if (emptyRosters != null)
						{
							vanishedGuilds = await FetchVanishedGuildsAsync(emptyRosters);
							if (vanishedGuilds == null)
							{
								existenceUnknown = new HashSet<long>(emptyRosters);
							}
							else if (vanishedGuilds.Count > 0)
							{
								HashSet<long> cleared = vanishedGuilds;
								TryEnqueueMainThread(() =>
								{
									foreach (long guildID in cleared)
									{
										ClearLocalGuildMembers(guildID);
									}
								});
							}
						}
					}

					foreach (KeyValuePair<long, DateTime> entry in pending)
					{
						long guildID = entry.Key;
						IReadOnlyList<CharacterGuildData> roster = null;
						IReadOnlyList<GuildRankData> ladder = null;

						if (vanishedGuilds != null && vanishedGuilds.Contains(guildID))
						{
							// Gone, and cleared above. Nothing to deliver, and nothing to hold the mark for.
							continue;
						}

						if (rosters != null &&
							(existenceUnknown == null || !existenceUnknown.Contains(guildID)) &&
							rosters.TryGetValue(guildID, out roster) &&
							ladders.TryGetValue(guildID, out ladder) &&
							ladder != null &&
							ladder.Count == 0)
						{
							/* A guild from before ranks were rows: the first read of it grows the
							 * seeded ladder. Per guild, and only ever once per such guild; its
							 * failure is logged there and holds this guild alone. */
							ladder = await FetchOrSeedLadderAsync(guildID, rankService);
						}

						bool read = roster != null && ladder != null &&
									(existenceUnknown == null || !existenceUnknown.Contains(guildID));
						UpdatePumpWatermark.Outcome outcome = UpdatePumpWatermark.Classify(read, entry.Value, retryHorizonUtc);
						watermarkUtc = UpdatePumpWatermark.Hold(watermarkUtc, outcome, entry.Value);

						switch (outcome)
						{
							case UpdatePumpWatermark.Outcome.Processed:
								guildMembersMap[guildID] = roster;
								guildLaddersMap[guildID] = ladder;
								processedUpdateStamps[guildID] = entry.Value;
								break;
							case UpdatePumpWatermark.Outcome.GiveUp:
								await Log.Error("GuildSystem", $"Guild update pump has been unable to read guild {guildID} for longer than {retryHorizon.TotalSeconds:0}s; giving up on the update of {entry.Value:O}. Its members on this server keep the roster they had until the guild next changes.");
								break;
						}
					}
				}

				if (guildMembersMap.Count == 0)
				{
					/* Nothing to deliver — every update in the fetch was already processed, or none
					 * could be read — but the mark still moves. Left where it was, it would freeze
					 * the first time a pass found nothing new, and every later pass would re-fetch
					 * the same rows for as long as nothing else changed. Everything before the mark
					 * has been handled, and anything held is at or after it. */
					TryEnqueueMainThread(() =>
					{
						if (Server?.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData watermarkData) == true)
						{
							watermarkData.LastFetchTime = watermarkUtc;
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

					if (Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData rtData))
					{
						rtData.LastFetchTime = watermarkUtc;
					}

					if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mapData))
					{
						return;
					}

					foreach (var kvp in guildMembersMap)
					{
						// Always present: a guild enters guildMembersMap only together with its ladder.
						ApplyGuildSnapshot(kvp.Key, kvp.Value, guildLaddersMap[kvp.Key], mapData);
					}
				});

				/* Recorded only once the delivery is actually queued. Recording at the read would
				 * drop the update outright whenever the main-thread queue was full: every later pass
				 * would skip it and the change it carried would never reach anybody. Unrecorded, the
				 * next pass simply picks it up again. */
				if (enqueued && dedupeData != null)
				{
					foreach (KeyValuePair<long, DateTime> stamp in processedUpdateStamps)
					{
						dedupeData.MarkGuildUpdateProcessed(stamp.Key, stamp.Value);
					}
				}
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error fetching/processing guild updates: {ex}");
			}
			finally
			{
				if (Server?.DataContainerRegistry.TryGet<IGuildSystemRuntimeData>(out var runtimeData) == true)
				{
					runtimeData.EndUpdatePump();
				}
			}
		}

		/// <summary>
		/// Periodic callback that checks every guild with members on this server still exists.
		/// </summary>
		/// <param name="deltaTime">Elapsed seconds (unused).</param>
		/// <remarks>
		/// Separate from the update pump, on its own slower period: the pump learns of every change
		/// that leaves an update row, and a disband is the one that does not. Its own in-flight flag
		/// rather than the pump's, so a slow pump pass never postpones it and it never postpones one.
		/// </remarks>
		private void OnPeriodicGuildExistenceSweep(float deltaTime)
		{
			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			// Snapshot main-thread-only Dictionary keys before going async.
			if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData) ||
				mappingData.GuildCharacterTracker.Count == 0)
			{
				return;
			}

			if (Interlocked.CompareExchange(ref guildExistenceSweepInFlight, 1, 0) != 0)
			{
				return;
			}

			List<long> guildIds = new List<long>(mappingData.GuildCharacterTracker.Keys);
			if (!TryEnqueueAsyncWork(() => SweepVanishedGuildsAsync(guildIds)))
			{
				Interlocked.Exchange(ref guildExistenceSweepInFlight, 0);
			}
		}

		/// <summary>
		/// Asks which of the tracked guilds still exist and clears the local members of any that do not.
		/// </summary>
		/// <param name="guildIds">The guilds this server held members of when the sweep started.</param>
		/// <returns>Asynchronous sweep task.</returns>
		/// <remarks>
		/// Without it, members of a guild disbanded on another server kept it until they logged out
		/// (issue #267). A failed read clears nothing: a guild must never be taken from its members on
		/// the strength of a read that did not happen. The next sweep asks again.
		/// </remarks>
		private async Task SweepVanishedGuildsAsync(List<long> guildIds)
		{
			try
			{
				if (Server == null || Server.ServerState != ConnectionState.Started ||
					!TryGetDbService(out IGuildService guildService))
				{
					return;
				}

				DatabaseResult<IReadOnlyCollection<long>> existing = await guildService.FetchExistingIdsAsync(guildIds);
				if (!existing.IsSuccess || existing.Data == null)
				{
					guildExistenceSweepFaults.Report(new InvalidOperationException($"could not check which of {guildIds.Count} guild(s) still exist: [{existing.ErrorCode}] {existing.ErrorMessage}"), MonotonicClock.NowSeconds);
					return;
				}
				guildExistenceSweepFaults.ReportSuccess();

				HashSet<long> vanished = VanishedGuilds(guildIds, existing.Data);
				if (vanished.Count > 0)
				{
					TryEnqueueMainThread(() =>
					{
						foreach (long guildID in vanished)
						{
							ClearLocalGuildMembers(guildID);
						}
					});
				}
			}
			catch (Exception ex)
			{
				guildExistenceSweepFaults.Report(ex, MonotonicClock.NowSeconds);
			}
			finally
			{
				Interlocked.Exchange(ref guildExistenceSweepInFlight, 0);
			}
		}

		/// <summary>
		/// Reads which of some guilds no longer exist, for the update pump.
		/// </summary>
		/// <param name="guildIds">The guilds to ask about.</param>
		/// <returns>The ones that are gone, or null when the read failed (logged here).</returns>
		private async Task<HashSet<long>> FetchVanishedGuildsAsync(IReadOnlyCollection<long> guildIds)
		{
			if (!TryGetDbService(out IGuildService guildService))
			{
				return null;
			}

			DatabaseResult<IReadOnlyCollection<long>> existing = await guildService.FetchExistingIdsAsync(guildIds);
			if (!existing.IsSuccess || existing.Data == null)
			{
				await Log.Warning("GuildSystem", $"FetchAndProcessGuildUpdatesAsync could not check whether {guildIds.Count} guild(s) with an empty roster still exist; each is held for the next pass: {existing.ErrorCode} - {existing.ErrorMessage}");
				return null;
			}

			return VanishedGuilds(guildIds, existing.Data);
		}

		/// <summary>
		/// The guilds asked about that an existence read did not return.
		/// </summary>
		/// <param name="asked">The guilds asked about.</param>
		/// <param name="present">The ones the read found.</param>
		/// <returns>The rest: never null.</returns>
		internal static HashSet<long> VanishedGuilds(IEnumerable<long> asked, IEnumerable<long> present)
		{
			HashSet<long> found = present as HashSet<long> ?? new HashSet<long>(present ?? Array.Empty<long>());
			HashSet<long> vanished = new HashSet<long>();
			if (asked != null)
			{
				foreach (long guildID in asked)
				{
					if (guildID > 0 && !found.Contains(guildID))
					{
						vanished.Add(guildID);
					}
				}
			}
			return vanished;
		}

		/// <summary>
		/// Applies one guild's freshly read snapshot on this server and delivers it. Main thread only.
		/// </summary>
		/// <param name="guildID">The guild.</param>
		/// <param name="dbMembers">The guild's membership rows, as read.</param>
		/// <param name="ladder">The guild's rank ladder, read in the same pass.</param>
		/// <param name="mapData">Guild membership tracking for this server.</param>
		/// <remarks>
		/// <para>
		/// <b>Only what changed is sent.</b> The snapshot is compared with the roster this server
		/// last delivered (<see cref="IGuildCharacterMappingData.GuildMemberTracker"/>), and each
		/// local member lands in one of four roster audiences — the public or officer-note copy,
		/// whole or delta — and at most one rank-list audience:
		/// </para>
		/// <list type="bullet">
		/// <item>a member whose client already holds this guild's roster from this server, in the
		/// same officer-note audience, is sent a <see cref="GuildRosterDeltaBroadcast"/> of the rows
		/// that changed for that audience — nothing, when nothing did; the whole roster when more
		/// than half did (<see cref="GuildRosterDelta.Choose"/>);</item>
		/// <item>anybody else is sent the whole roster, and recorded as holding it;</item>
		/// <item>the rank list goes only to a member whose client does not hold the current ladder
		/// generation at their current rank — so a member changing zone costs one row, not a
		/// roster and a ladder for everybody.</item>
		/// </list>
		/// <para>
		/// Every copy is one multicast, serialised once, and each recipient still receives its
		/// roster or delta before its rank list. It used to be a whole roster and a whole rank list,
		/// serialised separately, for every local member on every change to anybody in the guild.
		/// </para>
		/// </remarks>
		private void ApplyGuildSnapshot(long guildID, IReadOnlyList<CharacterGuildData> dbMembers, IReadOnlyList<GuildRankData> ladder, IGuildCharacterMappingData mapData)
		{
			Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData);

			// The roster as it now stands, in the full projection: what the next pass diffs against.
			GuildAddEntry[] currentRows = new GuildAddEntry[dbMembers.Count];
			var currentByID = new Dictionary<long, GuildAddEntry>(dbMembers.Count);
			for (int i = 0; i < dbMembers.Count; i++)
			{
				currentRows[i] = BuildRosterEntry(dbMembers[i], includeOfficerNote: true);
				currentByID[currentRows[i].CharacterID] = currentRows[i];
			}

			mapData.GuildMemberTracker.TryGetValue(guildID, out Dictionary<long, GuildAddEntry> previousRows);

			// Members in the previous snapshot and not in this one have left, been kicked, or moved.
			if (previousRows != null)
			{
				List<long> departed = null;
				foreach (long prevID in previousRows.Keys)
				{
					if (!currentByID.ContainsKey(prevID))
					{
						(departed ??= new List<long>()).Add(prevID);
					}
				}

				if (departed != null)
				{
					foreach (long memberID in departed)
					{
						/* Untracked as well as cleared. This used to clear the standing and leave
						 * the character in the local tracker: once cleared, their disconnect finds
						 * no guild and never removes them, so a guild whose last local member was
						 * evicted here stayed tracked — and polled by this pump — for the life of
						 * the server. A character absent from a guild's roster is not a local
						 * member of it, whatever state their controller is in. */
						RemoveGuildCharacterTracker(guildID, memberID);

						if (characterMappingData != null &&
							characterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter character) &&
							character != null)
						{
							ClearGuildStanding(character, guildID);
						}
					}
				}
			}

			/* The evictions above can take this server's last local member of the guild with
			 * them, and RemoveGuildCharacterTracker drops the delivered roster and ladder together
			 * for exactly that case. A guild no longer tracked here has nobody here to deliver to,
			 * and re-storing its roster would leave a baseline behind for a guild nothing pumps. */
			if (!mapData.GuildCharacterTracker.ContainsKey(guildID))
			{
				ForgetDeliveredGuild(mapData, guildID);
				return;
			}

			mapData.GuildMemberTracker[guildID] = currentByID;

			if (characterMappingData == null)
			{
				return;
			}

			byte leaderRankOrder = LeaderRankOrderOf(ladder);
			long ladderGeneration = AdvanceDeliveredLadder(guildID, ladder, out GuildRankEntry[] rankEntries);

			ReleaseGuildAudiences();
			try
			{
				foreach (CharacterGuildData member in dbMembers)
				{
					if (!characterMappingData.CharactersByID.TryGetValue(member.CharacterID, out IPlayerCharacter character) ||
						character == null)
					{
						continue;
					}

					/* Only a character whose controller names THIS guild is refreshed or told. The
					 * test used to be "is in any guild at all", which answers yes for somebody who
					 * has since moved to a DIFFERENT guild: a snapshot read several awaits ago would
					 * then overwrite their standing with this guild's and send them this guild's
					 * roster, which the client adopts. Every way a character enters a guild sets the
					 * controller itself, so a disagreement here is a stale or in-flight state that
					 * its own path settles — the same rule the party pump applies. */
					if (!character.TryGet(out IGuildController guildController) ||
						guildController.ID != guildID)
					{
						continue;
					}

					/* Refresh the server-side cache of this member's standing from the row that was
					 * just read. The cache is only ever a pre-filter — every operation re-resolves
					 * before deciding — but a rank change made on another scene server reaches this
					 * one through the pump, and leaving the cache stale would leave the player's own
					 * panel offering actions the server will refuse. */
					GuildPermissions memberPermissions = PermissionsForOrder(ladder, member.Rank);
					guildController.RankOrder = member.Rank;
					guildController.Permissions = memberPermissions;
					guildController.LeaderRankOrder = leaderRankOrder;

					NetworkConnection owner = character.Owner;
					if (owner == null || !owner.IsActive)
					{
						continue;
					}

					/* The officer note is a column a client either may read or never receives —
					 * hiding it in the panel would leave it in the packet — so the recipient's own
					 * rank decides which copy they are in the audience for, and a baseline held in
					 * the other copy is no baseline for this one. */
					bool officerAudience = (memberPermissions & GuildPermissions.ViewOfficerNotes) == GuildPermissions.ViewOfficerNotes;
					bool holdsRoster = previousRows != null &&
									   guildRecipientBaselines.HasRoster(member.CharacterID, guildID, officerAudience);

					GuildAudience rosterAudience = officerAudience
						? (holdsRoster ? guildOfficerDeltaAudience : guildOfficerFullAudience)
						: (holdsRoster ? guildPublicDeltaAudience : guildPublicFullAudience);
					rosterAudience.Add(owner, member.CharacterID);

					if (!guildRecipientBaselines.HasLadder(member.CharacterID, guildID, ladderGeneration, member.Rank))
					{
						RankListAudience(member.Rank).Add(owner, member.CharacterID);
					}
				}

				DeliverRosterCopy(guildID, currentRows, previousRows, officerAudience: false, guildPublicFullAudience, guildPublicDeltaAudience);
				DeliverRosterCopy(guildID, currentRows, previousRows, officerAudience: true, guildOfficerFullAudience, guildOfficerDeltaAudience);

				// The ladder's wire array is built once per guild and shared by every copy.
				DeliverRankListAudiences(guildID, ladder, rankEntries, ladderGeneration, leaderRankOrder);
			}
			finally
			{
				ReleaseGuildAudiences();
			}
		}

		/// <summary>
		/// Sends the rank list to every rank audience gathered for one guild, one multicast per rank,
		/// and records each recipient as holding that ladder generation at that rank. Main thread only.
		/// </summary>
		/// <param name="guildID">The guild.</param>
		/// <param name="ladder">The ladder the lists describe.</param>
		/// <param name="rankEntries">Its wire array, shared by every copy.</param>
		/// <param name="generation">Its generation (<see cref="AdvanceDeliveredLadder"/>).</param>
		/// <param name="leaderRankOrder">The leader's seat on it.</param>
		/// <remarks>
		/// <see cref="GuildRankListBroadcast"/> carries the viewer's own rank and mask, so it is the
		/// same message for everybody holding one rank and a different one for each rank. Used by the
		/// update pump's delivery and by the publish that follows a ladder edit, so the two record
		/// what they sent in exactly the same way.
		/// </remarks>
		private void DeliverRankListAudiences(long guildID, IReadOnlyList<GuildRankData> ladder, GuildRankEntry[] rankEntries, long generation, byte leaderRankOrder)
		{
			foreach (KeyValuePair<byte, GuildAudience> audience in guildRankListAudiences)
			{
				if (audience.Value.Count < 1)
				{
					continue;
				}

				Server.NetworkWrapper.Broadcast(audience.Value.Connections, new GuildRankListBroadcast()
				{
					Ranks = rankEntries,
					ViewerRankOrder = audience.Key,
					ViewerPermissions = (long)PermissionsForOrder(ladder, audience.Key),
					LeaderRankOrder = leaderRankOrder,
				}, true, Channel.Reliable);

				for (int i = 0; i < audience.Value.CharacterIDs.Count; ++i)
				{
					guildRecipientBaselines.MarkLadder(audience.Value.CharacterIDs[i], guildID, generation, audience.Key);
				}
			}
		}

		/// <summary>
		/// Delivers one officer-note audience's copy of a guild's roster change. Main thread only.
		/// </summary>
		/// <param name="guildID">The guild.</param>
		/// <param name="currentRows">The roster as it now stands, in the full projection.</param>
		/// <param name="previousRows">The roster as last delivered, or null when none was.</param>
		/// <param name="officerAudience">Which copy: with officer notes, or without.</param>
		/// <param name="full">Members with no baseline in this copy: sent the whole roster.</param>
		/// <param name="delta">Members holding this copy: sent what changed.</param>
		private void DeliverRosterCopy(long guildID, GuildAddEntry[] currentRows, Dictionary<long, GuildAddEntry> previousRows, bool officerAudience, GuildAudience full, GuildAudience delta)
		{
			if (delta.Count > 0)
			{
				GuildRosterDelta.Diff(previousRows, currentRows, officerAudience, guildDeltaChangedIndices, guildDeltaRemovedIDs);

				switch (GuildRosterDelta.Choose(true, guildDeltaChangedIndices.Count, guildDeltaRemovedIDs.Count, currentRows.Length))
				{
					case GuildRosterDelta.Delivery.Full:
						// Most of the roster moved; they are sent it whole, alongside the members who never had it.
						full.AddAll(delta);
						break;

					case GuildRosterDelta.Delivery.Delta:
						GuildAddEntry[] upserts = new GuildAddEntry[guildDeltaChangedIndices.Count];
						for (int i = 0; i < upserts.Length; ++i)
						{
							upserts[i] = ProjectRosterEntry(currentRows[guildDeltaChangedIndices[i]], officerAudience);
						}

						Server.NetworkWrapper.Broadcast(delta.Connections, new GuildRosterDeltaBroadcast()
						{
							GuildID = guildID,
							Upserts = upserts,
							Removals = guildDeltaRemovedIDs.ToArray(),
						}, true, Channel.Reliable);
						break;
				}
			}

			if (full.Count > 0)
			{
				Server.NetworkWrapper.Broadcast(full.Connections, ProjectRoster(guildID, currentRows, officerAudience), true, Channel.Reliable);

				for (int i = 0; i < full.CharacterIDs.Count; ++i)
				{
					guildRecipientBaselines.MarkRoster(full.CharacterIDs[i], guildID, officerAudience);
				}
			}
		}

		/// <summary>
		/// Records a freshly read ladder as delivered, returning the generation that identifies it.
		/// </summary>
		/// <param name="guildID">The guild.</param>
		/// <param name="ladder">The ladder as read.</param>
		/// <param name="entries">The ladder's wire array — the one already recorded when unchanged.</param>
		/// <returns>The ladder's generation: unchanged while the ladder is, new whenever it moves.</returns>
		private long AdvanceDeliveredLadder(long guildID, IReadOnlyList<GuildRankData> ladder, out GuildRankEntry[] entries)
		{
			entries = BuildRankEntries(ladder);

			if (guildDeliveredLadders.TryGetValue(guildID, out DeliveredLadder delivered) &&
				GuildRosterDelta.SameLadder(delivered.Entries, entries))
			{
				entries = delivered.Entries;
				return delivered.Generation;
			}

			long generation = ++guildLadderGenerationCounter;
			guildDeliveredLadders[guildID] = new DeliveredLadder(entries, generation);
			return generation;
		}

		/// <summary>
		/// Drops everything this server recorded as delivered for a guild. Main thread only.
		/// </summary>
		/// <remarks>
		/// For a guild no longer tracked here. Recipients' baselines naming it were forgotten as
		/// each member left the tracker, so nothing can be sent a delta against what is dropped.
		/// </remarks>
		private void ForgetDeliveredGuild(IGuildCharacterMappingData mapData, long guildID)
		{
			mapData.GuildMemberTracker.Remove(guildID);
			guildDeliveredLadders.Remove(guildID);
		}

		/// <summary>
		/// The rank-list audience for one rank, taken from the pool on first use this guild.
		/// </summary>
		private GuildAudience RankListAudience(byte rankOrder)
		{
			if (!guildRankListAudiences.TryGetValue(rankOrder, out GuildAudience audience))
			{
				audience = guildAudiencePool.Count > 0 ? guildAudiencePool.Pop() : new GuildAudience();
				guildRankListAudiences.Add(rankOrder, audience);
			}
			return audience;
		}

		/// <summary>
		/// Empties the guild delivery's scratch audiences, returning the per-rank ones to the pool.
		/// </summary>
		private void ReleaseGuildAudiences()
		{
			guildPublicFullAudience.Clear();
			guildPublicDeltaAudience.Clear();
			guildOfficerFullAudience.Clear();
			guildOfficerDeltaAudience.Clear();

			foreach (KeyValuePair<byte, GuildAudience> audience in guildRankListAudiences)
			{
				audience.Value.Clear();
				guildAudiencePool.Push(audience.Value);
			}
			guildRankListAudiences.Clear();

			guildDeltaChangedIndices.Clear();
			guildDeltaRemovedIDs.Clear();
		}

		/// <summary>
		/// Adds a mapping for the Guild to Guild Members connected to this Scene Server.
		/// </summary>
		/// <param name="guildID">ID of the guild.</param>
		/// <param name="characterID">ID of the character to add.</param>
		public void AddGuildCharacterTracker(long guildID, long characterID)
		{
			if (guildID == 0)
			{
				return;
			}
			if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData))
			{
				return;
			}
			var tracker = mappingData.GuildCharacterTracker;
			if (!tracker.TryGetValue(guildID, out HashSet<long> characterIDs))
			{
				tracker.Add(guildID, characterIDs = new HashSet<long>());
			}
			if (!characterIDs.Contains(characterID))
			{
				characterIDs.Add(characterID);
			}

			/* Connecting or joining: whatever this client holds of a guild roster or ladder was not
			 * sent by this server's pump for this membership, so the next delivery sends it whole
			 * rather than as a delta against a baseline the client does not have. */
			guildRecipientBaselines.Forget(characterID);
		}

		/// <inheritdoc/>
		public void ForgetGuildDeliveryBaselines(long characterID)
		{
			guildRecipientBaselines.Forget(characterID);
		}

		/// <summary>
		/// Removes the mapping of Guild to Guild Members connected to this Scene Server.
		/// </summary>
		/// <param name="guildID">ID of the guild.</param>
		/// <param name="characterID">ID of the character to remove.</param>
		public void RemoveGuildCharacterTracker(long guildID, long characterID)
		{
			if (guildID == 0)
			{
				return;
			}
			if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData))
			{
				return;
			}
			if (mappingData.GuildCharacterTracker.TryGetValue(guildID, out HashSet<long> characterIDs))
			{
				characterIDs.Remove(characterID);

				// If there are no active guild members we can remove the character and member trackers for the guild.
				if (characterIDs.Count < 1)
				{
					mappingData.GuildCharacterTracker.Remove(guildID);
					ForgetDeliveredGuild(mappingData, guildID);
				}
			}

			/* Leaving, kicked, evicted or disconnecting: the client drops this guild's roster and
			 * ladder (or is gone), so nothing recorded as delivered to it for this guild still
			 * holds. Only this guild's: a character who has already moved on holds the next one. */
			guildRecipientBaselines.Forget(characterID, guildID);
		}

		/// <summary>
		/// Handles character connect event, adding the character to the guild tracker and persisting guild update.
		/// </summary>
		/// <param name="conn">Network connection of the character.</param>
		/// <param name="character">The character that connected.</param>
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

			// Every character, in a guild or not: a member who later leaves needs to know too.
			SendGuildCreationCost(conn);

			if (!character.TryGet(out IGuildController guildController) ||
				guildController.ID < 1)
			{
				// not in a guild
				return;
			}

			AddGuildCharacterTracker(guildController.ID, character.ID);

			// Fire-and-forget async DB persist
			long characterID = character.ID;
			long guildID = guildController.ID;
			string sceneName = character.SceneName;

			EnqueuePersistence(() => PersistGuildMemberAsync(characterID, guildID, sceneName), characterID);
		}

		/// <summary>
		/// Handles character disconnect event, removing the character from the guild tracker and persisting guild update.
		/// </summary>
		/// <param name="conn">Network connection of the character.</param>
		/// <param name="character">The character that disconnected.</param>
		public void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			IGuildSystemRuntimeData runtimeData = null;
			if (character != null && Server.DataContainerRegistry.TryGet(out runtimeData))
			{
				runtimeData.RemovePendingInvitation(character.ID);
			}

			if (character == null)
			{
				return;
			}

			/* Before the guild tests below can return early: the client is gone, and whatever it
			 * was sent went with it. A baseline left behind would have this character's next session
			 * on this server sent deltas against a roster its new client never received. */
			guildRecipientBaselines.Forget(character.ID);

			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}

			if (!character.TryGet(out IGuildController guildController) ||
				guildController.ID < 1)
			{
				// not in a guild
				return;
			}

			RemoveGuildCharacterTracker(guildController.ID, character.ID);

			/* A kick or a leave deletes the membership row from a background task, and this
			 * character's controller still carries the old guild ID until that delete lands. The
			 * location write can no longer undo that — it is an UPDATE that neither inserts nor
			 * bumps the version the delete is gated on (see PersistGuildMemberAsync) — so this
			 * skip is no longer a safety measure. It stays because an "Offline" label written onto
			 * a row that is about to be deleted is a round trip for nothing. */
			if (runtimeData != null && runtimeData.IsMembershipRemovalInFlight(character.ID))
			{
				return;
			}

			// Fire-and-forget async DB persist with "Offline" location
			long characterID = character.ID;
			long guildID = guildController.ID;

			EnqueuePersistence(() => PersistGuildMemberAsync(characterID, guildID, GuildRosterDelta.OfflineLocation), characterID);
		}

		/// <summary>
		/// Asynchronously persists a guild member's location and triggers a guild update notification.
		/// </summary>
		/// <param name="characterID">Character identifier to persist.</param>
		/// <param name="guildID">Guild identifier the server believes the character is in.</param>
		/// <param name="location">Current member location label.</param>
		/// <returns>Asynchronous persistence task.</returns>
		/// <remarks>
		/// <para>
		/// This is a LOCATION write, and only the location is this method's to decide. Everything
		/// else on the row — whether it exists at all, which guild it names, the rank it holds —
		/// is taken from the row as it stands, never from the controller. The controller is a
		/// cache the update pump refreshes about once a second, and a membership change made on
		/// another scene server reaches it only on that pump; writing the cache back with a
		/// version guaranteed to win used to overwrite whatever had happened in between:
		/// </para>
		/// <list type="bullet">
		/// <item>a member kicked elsewhere who logged out or changed zone before the pump ran was
		/// INSERTED straight back into the guild, permanently;</item>
		/// <item>a promotion or demotion made elsewhere was reverted;</item>
		/// <item>a rank inserted below the leader moves the leader up a rung, and a leader who left
		/// before the pump ran wrote their old order back — leaving the top seat empty and the
		/// guild with nobody able to administer it.</item>
		/// </list>
		/// <para>
		/// So the write is <see cref="ICharacterGuildService.UpdateLocationAsync"/>: one UPDATE of the
		/// label, matched on character AND guild. A missing row, or a row naming a different guild,
		/// means this controller is stale, and the update simply matches nothing. It used to be a
		/// fetch followed by the membership UPSERT, and a delete landing between the two was undone
		/// by the UPSERT's insert branch; an UPDATE has no insert branch to undo it with.
		/// </para>
		/// </remarks>
		private async Task PersistGuildMemberAsync(long characterID, long guildID, string location)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
				{
					return;
				}

				/* A location-only UPDATE, never the membership UPSERT. This used to read the row for
				 * its version and rank and write it back through PersistAsync — and a kick landing
				 * between the read and the write left no row for the UPSERT to lose to, so it
				 * INSERTED one and the kicked member was back in the guild for good. The update
				 * can only change a row that exists, in this guild, and touches nothing but the
				 * label (issue #267, ICharacterGuildService.UpdateLocationAsync). */
				DatabaseResult<bool> locationResult = await charGuildService.UpdateLocationAsync(characterID, guildID, location);
				if (!locationResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"PersistGuildMemberAsync DB error (CharID={characterID}, GuildID={guildID}): {locationResult.ErrorCode} - {locationResult.ErrorMessage}");
					return;
				}

				if (!locationResult.Data)
				{
					// Removed, or moved to another guild, somewhere this server has not heard about yet.
					await Log.Debug("GuildSystem", $"PersistGuildMemberAsync skipped: CharID={characterID} no longer holds a membership row in GuildID={guildID}.");
					return;
				}
				DatabaseResult updateResult = await guildUpdateService.PersistAsync(guildID);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"PersistGuildMemberAsync guild update notification failed (GuildID={guildID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
				}

				/* Send the guild's notice and message of the day to this member alone. The roster
				 * pump does not carry them — it deals in membership rows — so without this a
				 * player would only ever see the text if somebody edited it while they were
				 * logged in, which is precisely why the columns sat unused. Skipped when the
				 * member is going Offline: there is nobody left to render it. */
				if (!GuildRosterDelta.IsOffline(location))
				{
					await PublishGuildInfoAsync(guildID, characterID);
				}
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error persisting guild member (CharID={characterID}, GuildID={guildID}): {ex}");
			}
		}

		/// <summary>
		/// Handles guild creation broadcast, validates and creates a new guild for the requesting character.
		/// Fires an async task that checks name availability, creates the guild, persists membership,
		/// and marshals the result back to the main thread.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">GuildCreateBroadcast message containing guild creation details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildCreateBroadcastReceived(NetworkConnection conn, GuildCreateBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			IPlayerCharacter player = request.Character;

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

				IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
				if (guildController == null || guildController.ID > 0)
				{
					// already in a guild
					Server.NetworkWrapper.Broadcast(conn, new GuildResultBroadcast()
					{
						Result = GuildResultType.AlreadyInGuild,
					}, true, Channel.Reliable);
					return;
				}

				// remove white space
				msg.GuildName = msg.GuildName.Trim();

				if (!Authentication.IsAllowedGuildName(msg.GuildName))
				{
					Server.NetworkWrapper.Broadcast(conn, new GuildResultBroadcast()
					{
						Result = GuildResultType.InvalidGuildName,
					}, true, Channel.Reliable);
					return;
				}

				// Capture immutable data for the async path
				long characterID = guildController.Character.ID;
				string guildName = msg.GuildName;
				string sceneName = conn.FirstObject.gameObject.scene.name;

				/* The fee is taken NOW, on this thread, before the asynchronous create begins.
				 * Charging on success instead would leave a window between the affordability
				 * check and the charge in which the same balance can be spent elsewhere — a
				 * merchant purchase races it — so the guild would exist and the fee go unpaid.
				 * Taking it first and refunding on every failure is the order the merchant path
				 * already uses, and the refund is the price of getting it right. Issue #186. */
				long feeCharged = 0;
				if (HasCreationFee)
				{
					if (!CharacterCurrency.CanAfford(player, guildCreationFeeCurrency, guildCreationFee))
					{
						Server.NetworkWrapper.Broadcast(conn, new GuildResultBroadcast()
						{
							Result = GuildResultType.InsufficientFunds,
						}, true, Channel.Reliable);
						return;
					}

					// Deduct, persist, and refund if the write is refused — TrySpend owns that ordering.
					if (!CharacterCurrency.TrySpend(player, guildCreationFeeCurrency, guildCreationFee, () => TryPersistCreationFeeCurrency(player)))
					{
						Server.NetworkWrapper.Broadcast(conn, new GuildResultBroadcast()
						{
							Result = GuildResultType.Failed,
						}, true, Channel.Reliable);
						return;
					}
					feeCharged = guildCreationFee;
				}

				deferGuardRelease = TryEnqueueIngressWork(() => CreateGuildAsync(conn, characterID, guildName, sceneName, feeCharged), guardKey, characterID);
				if (!deferGuardRelease)
				{
					// The create never started, so the fee is returned here and now.
					RefundCreationFee(characterID, feeCharged);
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

		#region Creation Fee

		/// <summary>
		/// Tells one connection what founding a guild costs here. Issue #186.
		/// </summary>
		/// <remarks>
		/// Only when there is a fee. The client's default is "no fee", so a server that charges
		/// nothing has nothing to say.
		/// </remarks>
		private void SendGuildCreationCost(NetworkConnection conn)
		{
			if (!HasCreationFee || conn == null || !conn.IsActive || Server == null)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new GuildCreationCostBroadcast()
			{
				CurrencyTemplateID = guildCreationFeeCurrency.ID,
				Amount = guildCreationFee,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Persists the fee currency's attribute row. Used as <see cref="CharacterCurrency.TrySpend"/>'s
		/// persist step and again for a refund. Main thread only.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Only the currency attribute is written — a fee changes nothing else — and it is written
		/// immediately rather than left to the periodic save, for the same reason the guild row
		/// is: a crash between the two would otherwise leave a guild that was never paid for.
		/// </para>
		/// <para>
		/// <c>Version++</c> AND <c>MarkPersistPending</c>, together. The periodic save clears an
		/// attribute's dirty flag only when the confirmation quotes the version it stamped; a
		/// bump from here without the mark would move the attribute past a version an in-flight
		/// save is waiting on, and the attribute would stay dirty — and be rewritten on every
		/// pass — for the rest of the session. See <c>CharacterInventorySystem.BuildAttributeDataList</c>.
		/// </para>
		/// <para>
		/// Ownership-gated: the row quotes the session claim held for the character now, and lands
		/// only while it is still held. With no claim nothing is queued and false is returned, which
		/// <see cref="CharacterCurrency.TrySpend"/> answers with a refund and the create with
		/// <c>Failed</c> — a character this server does not hold is not one it may charge.
		/// </para>
		/// </remarks>
		/// <returns>True when the write was queued. False means nothing was queued and the caller must not rely on it.</returns>
		private bool TryPersistCreationFeeCurrency(IPlayerCharacter character)
		{
			if (character == null ||
				guildCreationFeeCurrency == null ||
				!character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(guildCreationFeeCurrency, out CharacterAttribute currency))
			{
				return false;
			}

			if (!TryCaptureSessionClaim(character.ID, out CharacterSessionLeaseData claim))
			{
				Log.Warning("GuildSystem", $"TryPersistCreationFeeCurrency: this server holds no session claim for CharID={character.ID}; nothing was queued.");
				return false;
			}

			currency.Version++;
			currency.MarkPersistPending(currency.Version);

			long characterID = character.ID;
			var dtos = new List<CharacterAttributeData>(1)
			{
				new CharacterAttributeData(
					id: 0,
					version: currency.Version,
					characterID: characterID,
					templateID: guildCreationFeeCurrency.ID,
					value: currency.Value,
					currentValue: 0.0f),
			};

			return EnqueuePersistence(() => PersistCreationFeeCurrencyToDbAsync(dtos, characterID, claim), characterID);
		}

		/// <summary>
		/// Writes the fee currency's attribute row. Worker thread.
		/// </summary>
		private async Task PersistCreationFeeCurrencyToDbAsync(List<CharacterAttributeData> dtos, long characterID, CharacterSessionLeaseData claim)
		{
			try
			{
				if (!TryGetDbService(out ICharacterAttributeService attributeService))
				{
					await Log.Error("GuildSystem", "PersistCreationFeeCurrencyToDbAsync: Failed to resolve ICharacterAttributeService");
					return;
				}

				await BulkWriteReporting.ReportAsync("GuildSystem", "Guild creation fee save",
					await attributeService.PersistOwnedAsync(dtos, ClaimsOf(claim)), $"CharID={characterID}");
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"PersistCreationFeeCurrencyToDbAsync failed (CharID={characterID}): {ex}");
			}
		}

		/// <summary>
		/// Gives a charged creation fee back after the create did not happen. Main thread only.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Resolved by character ID, not by the connection: the refund is owed whether or not the
		/// requester is still connected, and the mapping is what still knows the character while
		/// a logout is in progress. A character that has already left this server cannot be
		/// refunded in memory — its logout save carried the reduced balance — so that case is
		/// logged as an error with the amount, for an operator to restore. The window is a couple
		/// of database round trips wide and the same one the merchant path accepts.
		/// </para>
		/// <para>
		/// Recorded in the currency ledger as Returned, so a fee that was charged and given back
		/// leaves the same two-sided trail a refunded purchase does.
		/// </para>
		/// </remarks>
		private void RefundCreationFee(long characterID, long amount)
		{
			if (amount <= 0 || guildCreationFeeCurrency == null)
			{
				return;
			}

			if (Server == null ||
				!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mappingData) ||
				!mappingData.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character) ||
				character == null)
			{
				Log.Error("GuildSystem", $"Guild creation fee of {amount} {guildCreationFeeCurrency.Name} could not be refunded: CharID={characterID} is no longer resident on this server. Restore it by hand.");
				return;
			}

			if (!CharacterCurrency.TryAdd(character, guildCreationFeeCurrency, amount))
			{
				Log.Error("GuildSystem", $"Guild creation fee of {amount} {guildCreationFeeCurrency.Name} could not be refunded to CharID={characterID}: the character has no such attribute.");
				return;
			}

			if (!TryPersistCreationFeeCurrency(character))
			{
				Log.Error("GuildSystem", $"Guild creation fee refund persist rejected for CharID={characterID}; in-memory balance is correct but the DB holds the deduction until the next save.");
			}

			RecordCurrencyMovement(characterID, amount, CurrencyMovementReason.GuildCreation, absorbed: false);
		}

		/// <summary>
		/// Answers a failed create from the worker thread: refund, then tell the requester.
		/// </summary>
		/// <remarks>
		/// One main-thread action for both halves so the refund cannot be skipped by the
		/// connection check that guards the message — a player who has since disconnected is
		/// still owed the money.
		/// </remarks>
		private void FailCreate(NetworkConnection conn, long characterID, long feeCharged, GuildResultType result)
		{
			TryEnqueueMainThread(() =>
			{
				RefundCreationFee(characterID, feeCharged);

				if (conn == null || !conn.IsActive || Server == null)
				{
					return;
				}
				Server.NetworkWrapper.Broadcast(conn, new GuildResultBroadcast()
				{
					Result = result,
				}, true, Channel.Reliable);
			});
		}

		/// <summary>
		/// Records a currency movement in the ledger. Fire-and-forget; nothing waits on it.
		/// </summary>
		/// <remarks>
		/// The same bookkeeping the merchant and ability-craft paths keep, so a guild fee is
		/// auditable alongside every other sink: Absorbed when the guild was founded, Returned
		/// when the fee was charged and then given back.
		/// </remarks>
		private void RecordCurrencyMovement(long characterID, long amount, CurrencyMovementReason reason, bool absorbed)
		{
			if (characterID <= 0 || amount <= 0)
			{
				return;
			}

			CurrencyMovementState state = absorbed
				? CurrencyMovementState.Absorbed
				: CurrencyMovementState.Returned;

			if (!EnqueuePersistence(async () =>
			{
				if (!TryGetDbService(out ICurrencyLedgerService ledgerService))
				{
					return;
				}

				DatabaseResult record = await ledgerService.RecordAsync(characterID, amount, (int)reason, (int)state);
				if (!record.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"Currency ledger: could not record {amount} ({reason}/{state}) for CharID={characterID}: {record.ErrorCode} - {record.ErrorMessage}");
				}
			}, characterID))
			{
				Log.Warning("GuildSystem", $"Currency ledger: the persistence queue is saturated; the record for CharID={characterID} is still written, but late (behind the backlog, or through the teardown fallback).");
			}
		}

		#endregion

		/// <summary>
		/// Asynchronously checks guild name availability, creates the guild, persists membership,
		/// and marshals in-memory state changes + Broadcasts back to the main thread.
		/// </summary>
		/// <param name="conn">Requesting connection.</param>
		/// <param name="characterID">Requesting character identifier.</param>
		/// <param name="guildName">Requested guild name.</param>
		/// <param name="sceneName">Requester scene name.</param>
		/// <param name="feeCharged">
		/// The creation fee already taken from the requester on the main thread, or 0. Every path
		/// that does not end in a guild must give it back — see <see cref="FailCreate"/>.
		/// </param>
		/// <returns>Asynchronous guild creation task.</returns>
		private async Task CreateGuildAsync(NetworkConnection conn, long characterID, string guildName, string sceneName, long feeCharged)
		{
			// Set the moment the guild has a leader row: past that point the fee bought something.
			bool guildExists = false;
			try
			{
				if (!TryGetDbService(out IGuildService guildService) ||
					!TryGetDbService(out ICharacterGuildService charGuildService))
				{
					FailCreate(conn, characterID, feeCharged, GuildResultType.Failed);
					return;
				}

				// Check if guild name already exists
				DatabaseResult<bool> existsResult = await guildService.ExistsAsync(guildName);
				if (!existsResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"CreateGuildAsync name check failed (CharID={characterID}, Name='{guildName}'): {existsResult.ErrorCode} - {existsResult.ErrorMessage}");
					FailCreate(conn, characterID, feeCharged, GuildResultType.Failed);
					return;
				}
				if (existsResult.Data)
				{
					FailCreate(conn, characterID, feeCharged, GuildResultType.NameAlreadyExists);
					return;
				}

				/* Create the guild. PersistAsync now REPORTS a name collision rather than handing
				 * back the id of the guild that already owns the name, so this is the authoritative
				 * uniqueness check; the ExistsAsync above only saves a doomed insert in the common
				 * case and cannot be relied on, being a separate round trip. */
				DatabaseResult<long?> createResult = await guildService.PersistAsync(guildName);
				if (!createResult.IsSuccess || !createResult.Data.HasValue)
				{
					GuildResultType failure = createResult.ErrorCode == DatabaseErrorCodes.AlreadyExists
						? GuildResultType.NameAlreadyExists
						: GuildResultType.Failed;

					// A taken name is the player's answer; anything else is a fault and is logged.
					if (failure == GuildResultType.Failed)
					{
						await Log.Warning("GuildSystem", $"CreateGuildAsync guild insert failed (CharID={characterID}, Name='{guildName}'): {createResult.ErrorCode} - {createResult.ErrorMessage}");
					}

					FailCreate(conn, characterID, feeCharged, failure);
					return;
				}

				long newGuildID = createResult.Data.Value;

				// Save the character as guild leader
				/* The founder is seeded at the DEFAULT leader order. A brand-new guild has the
				 * seeded three-rung ladder and nothing above it, so this is its top seat; a guild
				 * that later adds ranks moves its leader by editing the ladder, not by this line. */
				CharacterGuildData memberData = new CharacterGuildData(0, 1, characterID, newGuildID, GuildRankDefaults.DefaultLeaderRankOrder, sceneName);
				DatabaseResult leaderResult = await charGuildService.PersistAsync(memberData, maxGuildSize);
				if (!leaderResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"CreateGuildAsync leader membership persist failed (CharID={characterID}, GuildID={newGuildID}): {leaderResult.ErrorCode} - {leaderResult.ErrorMessage}");

					/* Compensate. The guild row and the leader row are two independent commits —
					 * ExecuteWriteAsync opens no transaction — so returning here left a guild with
					 * zero members that nothing sweeps, holding its name against the unique index
					 * for the life of the deployment. Deleting it is the only way that name ever
					 * becomes available again. */
					DatabaseResult cleanupResult = await guildService.DeleteAsync(newGuildID);
					if (!cleanupResult.IsSuccess)
					{
						await Log.Error("GuildSystem", $"CreateGuildAsync could not remove the orphaned guild {newGuildID}; its name stays reserved: {cleanupResult.ErrorCode} - {cleanupResult.ErrorMessage}");
					}

					FailCreate(conn, characterID, feeCharged, GuildResultType.Failed);
					return;
				}

				/* From here the guild exists with a leader: the fee has bought something and is
				 * absorbed. Recorded before the marshal below so a requester who disconnects in
				 * the meantime still leaves the same ledger trail. */
				guildExists = true;
				RecordCurrencyMovement(characterID, feeCharged, CurrencyMovementReason.GuildCreation, absorbed: true);

				/* Announce the new guild to the cross-server update pump. Every OTHER membership
				 * mutation writes this marker, and this one did not, so a guild created and then
				 * left alone was polled (it is in the character tracker) but had nothing to fetch:
				 * the roster the founder had just been sent was never re-sent, and any column the
				 * immediate add could not know stayed wrong until the founder relogged. Written
				 * AFTER guildExists is set, so a failure here cannot refund a fee that bought a
				 * guild — the marker is an optimisation of freshness, not part of the purchase. */
				if (TryGetDbService(out IGuildUpdateService guildUpdateService))
				{
					DatabaseResult createUpdateResult = await guildUpdateService.PersistAsync(newGuildID);
					if (!createUpdateResult.IsSuccess)
					{
						await Log.Warning("GuildSystem", $"CreateGuildAsync guild update notification failed (GuildID={newGuildID}): {createUpdateResult.ErrorCode} - {createUpdateResult.ErrorMessage}");
					}
				}

				// Marshal in-memory state changes + Broadcast back to main thread
				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive || conn.FirstObject == null) return;

					IGuildController gc = conn.FirstObject.GetComponent<IGuildController>();
					if (gc == null || gc.ID > 0) return;

					gc.ID = newGuildID;
					gc.RankOrder = GuildRankDefaults.DefaultLeaderRankOrder;
					gc.Permissions = GuildRankDefaults.LeaderPermissions;
					gc.LeaderRankOrder = GuildRankDefaults.DefaultLeaderRankOrder;

					AddGuildCharacterTracker(gc.ID, characterID);

					// tell the character we made their guild successfully
					/* Projected from the LIVE character, not hand-built: this row has no database
					 * membership row behind it yet, so the race can only come from the character
					 * itself. An inline entry here omitted RaceID and left the founder staring at
					 * an em dash in the roster's race column until a relog re-read the roster. */
					Server.NetworkWrapper.Broadcast(conn, new GuildAddBroadcast()
					{
						GuildID = gc.ID,
						Member = BuildSelfRosterEntry(conn.FirstObject.GetComponent<IPlayerCharacter>(), characterID, gc.RankOrder, sceneName),
					}, true, Channel.Reliable);

					// Hand the founder the (empty) notice and message of the day so the panel's
					// info band is populated from the moment the guild exists.
					TryEnqueueAsyncWork(() => PublishGuildInfoAsync(newGuildID, characterID), characterID);

					AppendGuildLog(newGuildID, GuildLogEventType.Created, characterID);

					// Increment achievement for creating a guild
					if (GuildCreateAchievementTemplate != null)
					{
						IPlayerCharacter pc = conn.FirstObject.GetComponent<IPlayerCharacter>();

						if (pc != null && pc.TryGet(out IAchievementController achievementController) && CharacterStateValidation.CanAct(pc))
						{
							achievementController.Increment(GuildCreateAchievementTemplate, 1);
						}
					}
				});

				/* The founder's client learns what it may DO from GuildRankListBroadcast — the
				 * GuildAddBroadcast above carries only a rank ORDER, and the client deliberately
				 * never derives a permission mask from a number. Without this send the founder
				 * sits at Permissions.None until a relog: no invite button, no MOTD/notice
				 * editing, and a rank rendered as a bare "3" because the rank names live only in
				 * the ladder. Resolving also seeds the default three-rung ladder rows if the
				 * create path has not written them yet. */
				GuildAuthority authority = await ResolveGuildAuthorityAsync(newGuildID, characterID);
				SendGuildRankList(conn, authority);
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error creating guild '{guildName}' for CharID={characterID}: {ex}");

				/* Refunded only if the guild does not exist. An exception after the leader row
				 * landed (the marshal, the rank list) means the player HAS a guild; giving the
				 * fee back as well would mint currency. */
				if (!guildExists)
				{
					FailCreate(conn, characterID, feeCharged, GuildResultType.Failed);
				}
			}
		}

		/// <summary>
		/// Handles guild invitation broadcast, validates inviter and target, and sends invitation to the target character.
		/// Only guild leaders or officers can invite, and invitations are tracked to prevent duplicates.
		/// Fires an async task to verify guild capacity before sending the invite.
		/// </summary>
		/// <param name="conn">Network connection of the inviter.</param>
		/// <param name="msg">GuildInviteBroadcast message containing inviter and target IDs.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildInviteBroadcastReceived(NetworkConnection conn, GuildInviteBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out _))
			{
				return;
			}

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
				IGuildController inviter = conn.FirstObject.GetComponent<IGuildController>();

				/* Pre-filter only. The cached mask keeps an obviously-illegal request off the
				 * database, but it is a value the pump refreshes rather than the authority —
				 * InviteToGuildAsync re-resolves the inviter's standing from the guild's own rank
				 * rows before the invitation is actually issued. */
				if (inviter == null ||
					inviter.ID < 1 ||
					inviter.Character.ID == msg.TargetCharacterID ||
					!inviter.HasGuildPermission(GuildPermissions.Invite))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				// Capture immutable data for async path
				long inviterCharacterID = inviter.Character.ID;
				long guildID = inviter.ID;
				long targetCharacterID = msg.TargetCharacterID;

				deferGuardRelease = TryEnqueueIngressWork(() => InviteToGuildAsync(conn, inviterCharacterID, guildID, targetCharacterID), guardKey, inviterCharacterID);
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
		/// Asynchronously verifies guild capacity and marshals the invite back to the main thread.
		/// </summary>
		/// <param name="conn">Inviter connection for error feedback.</param>
		/// <param name="inviterCharacterID">Inviter character identifier.</param>
		/// <param name="guildID">Inviter guild identifier.</param>
		/// <param name="targetCharacterID">Target character identifier.</param>
		/// <returns>Asynchronous invite task.</returns>
		private async Task InviteToGuildAsync(NetworkConnection conn, long inviterCharacterID, long guildID, long targetCharacterID)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService))
				{
					return;
				}

				/* AUTHORITATIVE permission check. The handler that queued this work read a cached
				 * mask on the main thread; between then and now the inviter may have been
				 * demoted, kicked, or had the Invite bit taken off their rank. Re-resolving here
				 * is what makes the cached read a performance decision rather than a security
				 * one. */
				GuildAuthority inviterAuthority = await ResolveGuildAuthorityAsync(guildID, inviterCharacterID);
				if (!inviterAuthority.Has(GuildPermissions.Invite))
				{
					SendGuildResult(conn, AuthorityRefusal(inviterAuthority));
					return;
				}

				// Check guild is not full
				DatabaseResult<int> countResult = await charGuildService.CountAsync(guildID);
				if (!countResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"InviteToGuildAsync member count failed (GuildID={guildID}, TargetID={targetCharacterID}): {countResult.ErrorCode} - {countResult.ErrorMessage}");
					SendGuildResult(conn, GuildResultType.Failed);
					return;
				}
				if (countResult.Data >= maxGuildSize)
				{
					SendGuildResult(conn, GuildResultType.GuildFull);
					return;
				}

				/* Blocking has existed in the friend table since it was written and nothing has
				 * ever read the column, so a blocked player could still be invited by whoever
				 * they blocked. Asked about the TARGET, not the inviter: the question is whether
				 * the person about to receive a modal has refused contact from the sender.
				 *
				 * A check that could not be MADE refuses. The block is the target's protection
				 * from exactly this modal, and reading a failed lookup as "not blocked" handed
				 * that protection to whatever the database was doing at the time. */
				if (TryGetDbService(out ICharacterFriendService friendService))
				{
					DatabaseResult<bool> blockedResult = await friendService.IsBlockedAsync(targetCharacterID, inviterCharacterID);
					if (!blockedResult.IsSuccess)
					{
						await Log.Warning("GuildSystem", $"InviteToGuildAsync block check failed (InviterID={inviterCharacterID}, TargetID={targetCharacterID}): {blockedResult.ErrorCode} - {blockedResult.ErrorMessage}");
						SendGuildResult(conn, GuildResultType.Failed);
						return;
					}
					if (blockedResult.Data)
					{
						SendGuildResult(conn, GuildResultType.TargetIsBlocked);
						return;
					}
				}

				// Marshal invite logic back to main thread
				TryEnqueueMainThread(() =>
				{
					if (Server == null || !Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
					{
						return;
					}

					/* The target is resolved FIRST, before any state is claimed on their behalf — the
					 * same order the party path uses, for the same reason.
					 *
					 * It used to be the other way round: the cooldown was taken, then the pending slot,
					 * and only then was the target looked up. A target this scene server does not host
					 * — in another zone, on another scene server, or logged out — fell through every one
					 * of those in silence, leaving behind a pending invitation nothing would ever answer
					 * (blocking every real invitation to that player until it aged out, and re-touched
					 * on each retry) and a spent cooldown, for a modal that was never shown to anybody.
					 * Both are now claimed only once there is somebody to deliver to, and the miss is
					 * answered on the guild channel so the inviter can read it. */
					if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData) ||
						!characterMappingData.CharactersByID.TryGetValue(targetCharacterID, out IPlayerCharacter targetCharacter) ||
						targetCharacter == null ||
						targetCharacter.Owner == null ||
						!targetCharacter.TryGet(out IGuildController targetGuildController))
					{
						SendGuildChatCode(conn, targetCharacterID, ChatHelper.TARGET_OFFLINE);
						return;
					}

					// validate target
					if (targetGuildController.ID > 0)
					{
						// we should tell the inviter the target is already in a guild
						SendGuildChatCode(conn, targetCharacterID, ChatHelper.GUILD_ERROR_TARGET_IN_GUILD);
						return;
					}

					double now = MonotonicClock.NowSeconds;

					/* Per (inviter, target), not per connection. Recorded before the pending slot
					 * is taken so a target who declines instantly still cannot be re-invited
					 * until the cooldown elapses — declining used to free the slot and let the
					 * next invite through on the following frame. */
					if (perTargetInviteCooldownSeconds > 0.0f &&
						!runtimeData.TryBeginInviteCooldown(
							inviterCharacterID,
							targetCharacterID,
							TimeSpan.FromSeconds(perTargetInviteCooldownSeconds),
							now))
					{
						SendGuildResult(conn, GuildResultType.InviteOnCooldown);
						return;
					}

					PendingGuildInvitation invitation = new PendingGuildInvitation(guildID, inviterCharacterID, now);

					// if the target doesn't already have a pending invite
					if (!runtimeData.TryAddPendingInvitation(targetCharacterID, invitation))
					{
						return;
					}

					Server.NetworkWrapper.Broadcast(targetCharacter.Owner, new GuildInviteBroadcast()
					{
						InviterCharacterID = inviterCharacterID,
						TargetCharacterID = targetCharacter.ID
					}, true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error inviting to guild (GuildID={guildID}, TargetID={targetCharacterID}): {ex}");
			}
		}

		/// <summary>
		/// Clears a character's pending invitation from the async path.
		/// </summary>
		/// <param name="characterID">The character whose invitation should be dropped.</param>
		/// <remarks>
		/// Marshalled: the runtime data container is main-thread state and the callers are
		/// background tasks.
		/// </remarks>
		private void ClearPendingInvitation(long characterID)
		{
			TryEnqueueMainThread(() =>
			{
				if (Server != null && Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
				{
					runtimeData.RemovePendingInvitation(characterID);
				}
			});
		}

		/// <summary>
		/// Answers the requester on the guild channel with a chat error code about one character.
		/// </summary>
		/// <param name="conn">The requester.</param>
		/// <param name="subjectCharacterID">The character the code is about; the client names them from it.</param>
		/// <param name="code">A <see cref="ChatHelper"/> error code the client's chat table knows.</param>
		/// <remarks>
		/// Main thread only, and checks <c>IsActive</c> as well as null: the callers are marshalled
		/// actions, so the requester may have disconnected between asking and being answered.
		/// The party path has the same helper.
		/// </remarks>
		private void SendGuildChatCode(NetworkConnection conn, long subjectCharacterID, string code)
		{
			if (Server == null || conn == null || !conn.IsActive)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new ChatBroadcast()
			{
				Channel = ChatChannel.Guild,
				SenderID = subjectCharacterID,
				Text = code + " ",
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Sends a guild operation result to a connection, if it is still active.
		/// </summary>
		/// <param name="conn">The connection to notify.</param>
		/// <param name="result">The result to report.</param>
		/// <remarks>
		/// Every refusal path used to be a bare <c>return</c>, so a player whose request was
		/// rejected saw exactly what a player whose request was accepted-and-lost saw: nothing.
		/// A refusal the client can render is the difference between a rule and a bug report.
		/// Marshalled to the main thread because most callers are on the async path.
		/// </remarks>
		private void SendGuildResult(NetworkConnection conn, GuildResultType result)
		{
			if (conn == null)
			{
				return;
			}

			TryEnqueueMainThread(() =>
			{
				if (conn == null || !conn.IsActive || Server == null)
				{
					return;
				}

				Server.NetworkWrapper.Broadcast(conn, new GuildResultBroadcast()
				{
					Result = result,
				}, true, Channel.Reliable);
			});
		}

		/// <summary>
		/// Handles acceptance of a guild invitation, validates the invite, fires an async task to check capacity,
		/// persist membership, and marshal results back to the main thread.
		/// </summary>
		/// <param name="conn">Network connection of the accepting character.</param>
		/// <param name="msg">GuildAcceptInviteBroadcast message containing acceptance details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildAcceptInviteBroadcastReceived(NetworkConnection conn, GuildAcceptInviteBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out _))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.AcceptInvite, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();

				// validate character
				if (guildController == null || guildController.ID > 0)
				{
					return;
				}

				if (!Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
				{
					return;
				}

				// validate guild invite
				if (!runtimeData.TryGetPendingInvitation(guildController.Character.ID, out PendingGuildInvitation invitation))
				{
					SendGuildResult(conn, GuildResultType.InvitationExpired);
					return;
				}

				/* The client names the invitation it is answering. It used to send an empty
				 * struct, so the server could only resolve "whatever is pending" — and an invite
				 * dialog left open past the TTL then accepted whichever guild invited the player
				 * NEXT. This is a claim being CHECKED, not trusted: the authority is the server's
				 * own pending record and the client is only allowed to disagree with it by being
				 * refused. */
				if (msg.InviterCharacterID != invitation.InviterCharacterID)
				{
					SendGuildResult(conn, GuildResultType.InvitationExpired);
					return;
				}

				/* Expiry is re-tested here against the issue time rather than left to the sweep.
				 * The sweep is bounded and periodic, so an invitation can outlive its TTL by up
				 * to a sweep interval — and reading the entry refreshes the queue's clock, which
				 * pushed the sweep further away every time the entry was looked at. */
				if (MonotonicClock.NowSeconds - invitation.IssuedAt > invitationTtlSeconds)
				{
					runtimeData.RemovePendingInvitation(guildController.Character.ID);
					SendGuildResult(conn, GuildResultType.InvitationExpired);
					return;
				}

				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}

				// Capture immutable data for async path
				long characterID = guildController.Character.ID;
				string sceneName = conn.FirstObject.gameObject.scene.name;

				deferGuardRelease = TryEnqueueIngressWork(() => AcceptGuildInviteAsync(conn, characterID, invitation.GuildID, sceneName), guardKey, characterID);
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
		/// Asynchronously checks guild capacity, persists membership, notifies other servers,
		/// and marshals state changes + Broadcast back to the main thread.
		/// </summary>
		/// <param name="conn">Accepting connection.</param>
		/// <param name="characterID">Accepting character identifier.</param>
		/// <param name="guildID">Guild identifier from pending invitation.</param>
		/// <param name="sceneName">Current scene name.</param>
		/// <returns>Asynchronous accept-invite task.</returns>
		private Task AcceptGuildInviteAsync(NetworkConnection conn, long characterID, long guildID, string sceneName)
		{
			return JoinGuildAsync(conn, characterID, guildID, sceneName, fromInvitation: true);
		}

		/// <summary>
		/// THE join path. Every way a character can end up in a guild goes through here.
		/// </summary>
		/// <param name="conn">The joining connection.</param>
		/// <param name="characterID">The joining character.</param>
		/// <param name="guildID">The guild being joined.</param>
		/// <param name="sceneName">Current scene name.</param>
		/// <param name="fromInvitation">
		/// True when an invitation is being answered, false when a recruitment application was
		/// accepted. The only difference it makes is whether a pending invitation is cleared.
		/// </param>
		/// <returns>
		/// The outcome, which has also been sent to <paramref name="conn"/>. Returned as well
		/// because the joiner is not always the one who asked: an application is accepted by an
		/// officer, who is owed the answer too.
		/// </returns>
		/// <remarks>
		/// <para>
		/// E10 accepts an application by calling THIS, not by writing a membership row of its own.
		/// That is deliberate and it is the point: the capacity check, the guild-still-exists
		/// check, the bottom-rung rank resolution, the tracker registration, the guild-info push
		/// and the achievement are all things an application accept has to get right, and a second
		/// implementation of them would drift. In particular a separate accept path is how a guild
		/// ends up over its member cap — the applicant queue is exactly the mechanism that lets
		/// several joins land at once.
		/// </para>
		/// <para>
		/// The capacity and existence checks therefore run at ADMISSION time, not at application
		/// time, which is what makes "an accept arriving after the guild filled" a refusal rather
		/// than an overflow.
		/// </para>
		/// <para>
		/// A database that could not ANSWER is never read as a "no". A failed read reports
		/// <see cref="GuildResultType.Failed"/> and leaves the invitation pending, so the player
		/// can simply accept again; only a guild that is really gone consumes it.
		/// </para>
		/// </remarks>
		private async Task<GuildResultType> JoinGuildAsync(NetworkConnection conn, long characterID, long guildID, string sceneName, bool fromInvitation)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
				{
					return GuildResultType.Failed;
				}

				/* The guild can be disbanded between the invite and the accept — the last member
				 * leaving deletes the row outright. Without this the accept persisted a
				 * membership pointing at a guild that no longer exists, failed its foreign key or
				 * (worse) succeeded against a recycled id, and told the player nothing either
				 * way. Checked before capacity, because a missing guild counts as zero members
				 * and would otherwise sail through the capacity test. */
				if (!TryGetDbService(out IGuildService guildExistsService))
				{
					return GuildResultType.Failed;
				}

				DatabaseResult<GuildData?> guildResult = await guildExistsService.FetchAsync(guildID);
				if (!guildResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"JoinGuildAsync guild fetch failed (CharID={characterID}, GuildID={guildID}): {guildResult.ErrorCode} - {guildResult.ErrorMessage}");
					SendGuildResult(conn, GuildResultType.Failed);
					return GuildResultType.Failed;
				}

				if (!guildResult.Data.HasValue)
				{
					SendGuildResult(conn, GuildResultType.GuildNotFound);
					if (fromInvitation)
					{
						ClearPendingInvitation(characterID);
					}
					return GuildResultType.GuildNotFound;
				}

				// Check guild capacity
				DatabaseResult<int> countResult = await charGuildService.CountAsync(guildID);
				if (!countResult.IsSuccess)
				{
					/* A failed count is not a count of zero, and it is not "full" either. The
					 * persist below enforces the cap again inside its own INSERT, so this read is
					 * only ever an early answer — which makes Failed the honest one. */
					await Log.Warning("GuildSystem", $"JoinGuildAsync member count failed (CharID={characterID}, GuildID={guildID}): {countResult.ErrorCode} - {countResult.ErrorMessage}");
					SendGuildResult(conn, GuildResultType.Failed);
					return GuildResultType.Failed;
				}
				if (countResult.Data >= maxGuildSize)
				{
					SendGuildResult(conn, GuildResultType.GuildFull);
					return GuildResultType.GuildFull;
				}

				// Persist membership
				/* New members land on the LOWEST rung the guild actually has, not on a constant.
				 * A guild that deleted its bottom rank would otherwise admit people into a rank
				 * with no row, which resolves to no permissions and cannot be promoted out of by
				 * name. An unreadable ladder is therefore a refusal, not a guess — see
				 * ResolveLowestRankOrderAsync. */
				byte? lowestRankOrder = await ResolveLowestRankOrderAsync(guildID);
				if (!lowestRankOrder.HasValue)
				{
					SendGuildResult(conn, GuildResultType.Failed);
					return GuildResultType.Failed;
				}

				byte joinRankOrder = lowestRankOrder.Value;
				CharacterGuildData memberData = new CharacterGuildData(0, 1, characterID, guildID, joinRankOrder, sceneName);
				DatabaseResult saveResult = await charGuildService.PersistAsync(memberData, maxGuildSize);
				if (!saveResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"JoinGuildAsync membership persist failed (CharID={characterID}, GuildID={guildID}): {saveResult.ErrorCode} - {saveResult.ErrorMessage}");

					/* The persist is where capacity and existence are finally decided — the reads
					 * above are a separate round trip and several joins can pass them together —
					 * so its refusals are the ones that must reach the player as what they are.
					 * Every one of them used to arrive as "guild not found". A stale version means
					 * the character already holds a membership row: the version-1 insert cannot
					 * overwrite it, which is what stops a join moving somebody between guilds. */
					GuildResultType refusal = saveResult.ErrorCode == DatabaseErrorCodes.CapacityExceeded ? GuildResultType.GuildFull
						: saveResult.ErrorCode == DatabaseErrorCodes.StaleState ? GuildResultType.AlreadyInGuild
						: saveResult.ErrorCode == DatabaseErrorCodes.NotFound ? GuildResultType.GuildNotFound
						: GuildResultType.Failed;

					SendGuildResult(conn, refusal);
					return refusal;
				}

				/* Every OTHER application this character has outstanding is dropped the moment
				 * they join anything. An application that outlives its applicant's guildless
				 * state is an accept waiting to fail — and worse, a second guild's officer
				 * pressing Accept on somebody who is already in a guild would otherwise get a
				 * silent no-op with no idea why. */
				if (TryGetDbService(out IGuildApplicationService applicationService))
				{
					DatabaseResult<int> withdrawResult = await applicationService.DeleteManyByCharacterAsync(characterID);
					if (!withdrawResult.IsSuccess)
					{
						await Log.Warning("GuildSystem", $"JoinGuildAsync application cleanup failed (CharID={characterID}): {withdrawResult.ErrorCode} - {withdrawResult.ErrorMessage}");
					}
				}

				// Tell the other servers to update their guild lists
				DatabaseResult updateResult = await guildUpdateService.PersistAsync(guildID);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"AcceptGuildInviteAsync guild update notification failed (GuildID={guildID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
				}

				// Marshal state changes + Broadcast back to main thread
				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive || conn.FirstObject == null) return;

					IGuildController gc = conn.FirstObject.GetComponent<IGuildController>();
					if (gc == null || gc.ID > 0) return;

					gc.ID = guildID;
					gc.RankOrder = joinRankOrder;
					gc.Permissions = GuildPermissions.None;

					if (fromInvitation &&
						Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
					{
						runtimeData.RemovePendingInvitation(characterID);
					}

					AddGuildCharacterTracker(gc.ID, characterID);

					// tell the new member they joined immediately, other clients will catch up with the GuildUpdate pass
					/* Same projection as the create path. Without the race the new member's own
					 * row reads as an em dash until the pump's next full roster lands. */
					Server.NetworkWrapper.Broadcast(conn, new GuildAddBroadcast()
					{
						GuildID = gc.ID,
						Member = BuildSelfRosterEntry(conn.FirstObject.GetComponent<IPlayerCharacter>(), characterID, joinRankOrder, sceneName),
					}, true, Channel.Reliable);

					// The new member has no guild text yet; send it alongside the join rather than
					// making them wait for somebody to edit it.
					TryEnqueueAsyncWork(() => PublishGuildInfoAsync(guildID, characterID), characterID);

					AppendGuildLog(guildID, GuildLogEventType.Joined, characterID);

					// Increment achievement for joining a guild
					if (GuildJoinAchievementTemplate != null)
					{
						IPlayerCharacter pc = conn.FirstObject.GetComponent<IPlayerCharacter>();

						if (pc != null && pc.TryGet(out IAchievementController achievementController) && CharacterStateValidation.CanAct(pc))
						{
							achievementController.Increment(GuildJoinAchievementTemplate, 1);
						}
					}
				});

				return GuildResultType.Success;
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error joining guild (CharID={characterID}, GuildID={guildID}, FromInvitation={fromInvitation}): {ex}");
				return GuildResultType.Failed;
			}
		}

		/// <summary>
		/// Handles decline of a guild invitation, removes pending invitation for the character.
		/// </summary>
		/// <param name="conn">Network connection of the declining character.</param>
		/// <param name="msg">GuildDeclineInviteBroadcast message containing decline details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildDeclineInviteBroadcastReceived(NetworkConnection conn, GuildDeclineInviteBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			IPlayerCharacter character = request.Character;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.DeclineInvite, out long guardKey))
			{
				return;
			}

			try
			{
				if (Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
				{
					/* Only clear the invitation the client actually declined. A decline that
					 * arrives after the slot has been refilled would otherwise silently throw
					 * away an invitation the player has not been shown yet. */
					if (runtimeData.TryGetPendingInvitation(character.ID, out PendingGuildInvitation invitation) &&
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
		/// Handles guild leave broadcast, validates character, captures necessary data,
		/// and fires an async task that handles leadership transfer, member removal, and guild cleanup.
		/// </summary>
		/// <param name="conn">Network connection of the leaving character.</param>
		/// <param name="msg">GuildLeaveBroadcast message containing leave details.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildLeaveBroadcastReceived(NetworkConnection conn, GuildLeaveBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out _))
			{
				return;
			}

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
				IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();

				// validate character
				if (guildController == null || guildController.ID < 1)
				{
					// not in a guild..
					return;
				}

				// Capture immutable data for async path
				long characterID = guildController.Character.ID;
				long guildID = guildController.ID;

				/* Marked BEFORE the async hop. The membership row is deleted on a background
				 * task while this character still carries a live guild ID, and disconnecting in
				 * that window would run the ordinary disconnect persist and write the row back.
				 * The marker is cleared on every exit from LeaveGuildAsync. */
				BeginMembershipRemoval(characterID);

				deferGuardRelease = TryEnqueueIngressWork(() => LeaveGuildAsync(conn, characterID, guildID), guardKey, characterID);
				if (!deferGuardRelease)
				{
					EndMembershipRemoval(characterID);
				}
				if (!deferGuardRelease)
				{
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
		/// Asynchronously handles guild leave: fetches members for leadership transfer,
		/// removes the member, and either deletes or updates the guild.
		/// </summary>
		/// <param name="conn">Leaving character connection.</param>
		/// <param name="characterID">Leaving character identifier.</param>
		/// <param name="guildID">Guild identifier being left.</param>
		/// <returns>Asynchronous leave-guild task.</returns>
		/// <remarks>
		/// The leaver's rank is no longer passed in from the handler. It is read from the
		/// membership rows this method already fetches, which removes a parameter that could
		/// disagree with the database and makes "is the leaver the leader?" a question about the
		/// guild's own ladder rather than about a captured enum.
		/// </remarks>
		private async Task LeaveGuildAsync(NetworkConnection conn, long characterID, long guildID)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildService guildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
				{
					return;
				}

				// Fetch current members to determine leadership transfer
				DatabaseResult<IReadOnlyList<CharacterGuildData>> membersResult = await charGuildService.FetchManyAsync(guildID);
				if (!membersResult.IsSuccess || membersResult.Data == null)
				{
					await Log.Warning("GuildSystem", $"LeaveGuildAsync roster fetch failed (CharID={characterID}, GuildID={guildID}): {membersResult.ErrorCode} - {membersResult.ErrorMessage}");
					SendGuildResult(conn, GuildResultType.Failed);
					return;
				}

				IReadOnlyList<CharacterGuildData> members = membersResult.Data;

				// Find the leaving member's version for optimistic concurrency on delete
				bool leaverFound = false;
				long leavingMemberVersion = 1;
				byte leavingRankOrder = 0;
				foreach (CharacterGuildData member in members)
				{
					if (member.CharacterID == characterID)
					{
						leaverFound = true;
						leavingMemberVersion = member.Version + 1;
						leavingRankOrder = member.Rank;
						break;
					}
				}

				/* The leaver has no row: they were kicked, or the guild disbanded, on another
				 * scene server, and this one's controller has not been told yet. There is nothing
				 * left to delete — and everything below counts the leaver among the rows it just
				 * read. With them missing, "members minus the leaver" undercounted by one, so a
				 * two-member guild whose other member was still in it read as empty and was
				 * DELETED, cascading the remaining member's row and releasing the guild's land.
				 * The only thing still to do is the local half: tell the player they are out. */
				if (!leaverFound)
				{
					await Log.Debug("GuildSystem", $"LeaveGuildAsync: CharID={characterID} has no membership row in GuildID={guildID}; clearing the stale local membership only.");
					CompleteLocalLeave(conn, characterID, guildID, recordLeft: false);
					return;
				}

				int remainingCount = members.Count - 1;

				/* The leader is whichever member sits highest on the ladder, read from the rows
				 * rather than compared against a constant. A guild that added a rank above the
				 * seeded three has a leader this code cannot name in advance. */
				byte topRankOrder = 0;
				foreach (CharacterGuildData member in members)
				{
					if (member.Rank > topRankOrder)
					{
						topRankOrder = member.Rank;
					}
				}

				bool leaverIsLeader = leavingRankOrder > 0 && leavingRankOrder >= topRankOrder;

				// Handle leadership transfer if the leaving member is the leader
				if (leaverIsLeader && remainingCount > 0)
				{
					/* Succession prefers the most senior remaining member, whoever that is. The
					 * old code looked specifically for GuildRank.Officer and fell back to anyone;
					 * with an arbitrary ladder there is no "officer" to look for, and "the next
					 * one down" is both the same answer for a default guild and the right answer
					 * for an edited one. */
					List<CharacterGuildData> remainingMembers = new List<CharacterGuildData>();
					byte highestRemainingOrder = 0;

					foreach (CharacterGuildData member in members)
					{
						if (member.CharacterID == characterID)
						{
							continue;
						}

						if (member.Rank > highestRemainingOrder)
						{
							highestRemainingOrder = member.Rank;
						}
						remainingMembers.Add(member);
					}

					List<CharacterGuildData> mostSenior = new List<CharacterGuildData>();
					foreach (CharacterGuildData member in remainingMembers)
					{
						if (member.Rank == highestRemainingOrder)
						{
							mostSenior.Add(member);
						}
					}

					CharacterGuildData? newLeader = null;
					var rng = new DeterministicRNG();
					if (mostSenior.Count > 0)
					{
						// pick a random member from the most senior remaining rank
						newLeader = mostSenior[rng.Next(mostSenior.Count)];
					}
					else if (remainingMembers.Count > 0)
					{
						newLeader = remainingMembers[rng.Next(remainingMembers.Count)];
					}

					/* A guild with no leader can never promote, kick, invite or disband again —
					 * every one of those paths needs a permission only the top rank holds — so it
					 * is not a degraded state, it is a permanently soft-locked one with no
					 * in-game recovery. The old code logged the failed transfer and then deleted the
					 * leader anyway, manufacturing exactly that. Refusing the leave leaves the
					 * player in a guild they wanted to leave, which they can retry; the
					 * alternative leaves everyone else in a guild nobody can administer. */
					if (!newLeader.HasValue)
					{
						await Log.Error("GuildSystem", $"LeaveGuildAsync found no successor among {remainingCount} remaining members (GuildID={guildID}); refusing the leave rather than leaving the guild leaderless.");
						SendGuildResult(conn, GuildResultType.InsufficientRank);
						return;
					}

					// update the guild leader status in the database
					/* Promoted to the seat the LEAVER held, so the guild's top rank stays
					 * occupied whatever number that rank happens to be. Promoting to a constant
					 * would silently demote the guild's leadership to rank 3 in a guild whose
					 * ladder goes to 5. */
					DatabaseResult leaderResult = await charGuildService.UpdateRankAsync(newLeader.Value.CharacterID, newLeader.Value.GuildID, leavingRankOrder, newLeader.Value.Version + 1);
					if (!leaderResult.IsSuccess)
					{
						await Log.Error("GuildSystem", $"LeaveGuildAsync leadership transfer failed (GuildID={guildID}, NewLeader={newLeader.Value.CharacterID}): {leaderResult.ErrorCode} - {leaderResult.ErrorMessage}; refusing the leave rather than leaving the guild leaderless.");
						SendGuildResult(conn, GuildResultType.Failed);
						return;
					}
				}

				// Remove the guild member
				DatabaseResult deleteResult = await charGuildService.DeleteAsync(characterID, leavingMemberVersion);
				if (!deleteResult.IsSuccess)
				{
					/* The leave did not happen, so nothing after this line may run. It used to
					 * carry on regardless: the player was told they had left, their controller was
					 * cleared and "Left" was logged, while the row stayed — and they were back in
					 * the guild at their next login. A departing leader was worse off still, because
					 * the successor had already been promoted into the same seat above.
					 *
					 * The promotion is NOT rolled back. Two leaders is the state the transfer path
					 * already accepts as recoverable, and a second write to undo the first could
					 * fail in its turn; pressing Leave again resolves it, since the successor now
					 * sits at the leaver's order and the transfer to them is a no-op re-rank. */
					bool successorPromoted = leaverIsLeader && remainingCount > 0;
					string failure = $"LeaveGuildAsync member delete failed (CharID={characterID}, GuildID={guildID}): {deleteResult.ErrorCode} - {deleteResult.ErrorMessage}";
					if (successorPromoted)
					{
						await Log.Error("GuildSystem", $"{failure}. The successor has already been promoted, so the guild now has two members at rank order {leavingRankOrder}.");
					}
					else
					{
						await Log.Warning("GuildSystem", failure);
					}
					SendGuildResult(conn, GuildResultType.Failed);
					return;
				}

				if (remainingCount < 1)
				{
					// Delete the guild entirely
					DatabaseResult guildDeleteResult = await guildService.DeleteAsync(guildID);
					if (!guildDeleteResult.IsSuccess)
					{
						await Log.Warning("GuildSystem", $"LeaveGuildAsync guild delete failed (GuildID={guildID}): {guildDeleteResult.ErrorCode} - {guildDeleteResult.ErrorMessage}");
					}
					DatabaseResult<int> updateDeleteResult = await guildUpdateService.DeleteAsync(guildID);
					if (!updateDeleteResult.IsSuccess)
					{
						await Log.Warning("GuildSystem", $"LeaveGuildAsync guild update delete failed (GuildID={guildID}): {updateDeleteResult.ErrorCode} - {updateDeleteResult.ErrorMessage}");
					}

					// Any land the guild held goes back to the world with it.
					await ReleaseGuildPlotsAsync(guildID);
				}
				else
				{
					// Tell the other servers to update their guild lists
					DatabaseResult updateResult = await guildUpdateService.PersistAsync(guildID);
					if (!updateResult.IsSuccess)
					{
						await Log.Warning("GuildSystem", $"LeaveGuildAsync guild update notification failed (GuildID={guildID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
					}
				}

				// Skipped when the guild itself has just been deleted — there is no log left.
				CompleteLocalLeave(conn, characterID, guildID, recordLeft: remainingCount > 0);
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error leaving guild (CharID={characterID}, GuildID={guildID}): {ex}");
			}
			finally
			{
				/* Released on EVERY exit, including the refusals above. A marker left set would
				 * suppress the disconnect persist for the rest of the session and quietly stop
				 * recording this character's guild location. */
				EndMembershipRemoval(characterID);
			}
		}

		/// <summary>
		/// Clears a departed member's guild state on this server and tells their client.
		/// </summary>
		/// <param name="conn">The departing member's connection.</param>
		/// <param name="characterID">The departing member.</param>
		/// <param name="guildID">The guild they left.</param>
		/// <param name="recordLeft">Whether to write a "Left" row to the guild's activity log.</param>
		/// <remarks>
		/// Marshalled: the controller and the trackers are main-thread state. Called only once the
		/// membership row is known to be gone — deleted by the leave itself, or found already
		/// missing — because a controller cleared ahead of the row is a player told they left a
		/// guild they are still in.
		/// </remarks>
		private void CompleteLocalLeave(NetworkConnection conn, long characterID, long guildID, bool recordLeft)
		{
			TryEnqueueMainThread(() =>
			{
				if (conn == null || !conn.IsActive || conn.FirstObject == null)
				{
					return;
				}

				IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
				if (guildController == null || guildController.Character.ID != characterID || guildController.ID != guildID)
				{
					return;
				}

				guildController.ID = 0;
				guildController.RankOrder = 0;
				guildController.Permissions = GuildPermissions.None;
				guildController.LeaderRankOrder = 0;
				RemoveGuildCharacterTracker(guildID, characterID);

				Server.NetworkWrapper.Broadcast(conn, new GuildLeaveBroadcast(), true, Channel.Reliable);

				if (recordLeft)
				{
					AppendGuildLog(guildID, GuildLogEventType.Left, characterID);
				}
			});
		}

		/// <summary>
		/// Returns every plot a guild owned to the world. Called wherever a guild stops existing.
		/// </summary>
		/// <param name="guildID">The guild that no longer exists.</param>
		/// <returns>Asynchronous release task.</returns>
		/// <remarks>
		/// <para>
		/// Plot ownership carries no foreign key — the owner columns use zero for "none", which has
		/// nothing to point at — so nothing in the schema notices the guild has gone. Left alone,
		/// those plots stay owned by an identifier no guild answers to: unclaimable because they
		/// are owned, and untaxable because guild land is deferred rather than charged, which is
		/// to say removed from the game permanently and silently.
		/// </para>
		/// <para>
		/// Done in the guild system rather than the housing system because this is where a guild
		/// stops existing, and a sweep looking for orphans would have to enumerate every
		/// guild-owned plot in the world to find the few that had been abandoned. It is a helper
		/// because there are TWO such places — the last member leaving, and a disband — and only
		/// the first used to release anything: a disbanded guild's land was lost exactly as
		/// described above.
		/// </para>
		/// </remarks>
		private async Task ReleaseGuildPlotsAsync(long guildID)
		{
			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet(out IPlotService plotService))
			{
				return;
			}

			DatabaseResult<int> plotReleaseResult = await plotService.ReleaseAllForGuildAsync(guildID);
			if (!plotReleaseResult.IsSuccess)
			{
				await Log.Warning("GuildSystem", $"ReleaseGuildPlotsAsync guild plot release failed (GuildID={guildID}): {plotReleaseResult.ErrorCode} - {plotReleaseResult.ErrorMessage}");
			}
			else if (plotReleaseResult.Data > 0)
			{
				await Log.Debug("GuildSystem", $"Released {plotReleaseResult.Data} plot(s) held by deleted GuildID={guildID}.");
			}
		}

		/// <summary>
		/// Marks a character's guild membership as being removed.
		/// </summary>
		/// <param name="characterID">The character leaving or being kicked.</param>
		private void BeginMembershipRemoval(long characterID)
		{
			if (Server?.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData) == true)
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
				if (Server?.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData) == true)
				{
					runtimeData.EndMembershipRemoval(characterID);
				}
			});
		}

		/// <summary>
		/// Handles guild member removal broadcast, validates and removes a member from the guild.
		/// Only officers and leaders can remove other members.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">GuildRemoveBroadcast message containing member ID to remove.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildRemoveBroadcastReceived(NetworkConnection conn, GuildRemoveBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out _))
			{
				return;
			}

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
				IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();

				// validate character — pre-filter; RemoveGuildMemberAsync re-resolves the standing
				if (guildController == null ||
					guildController.ID < 1 ||
					!guildController.HasGuildPermission(GuildPermissions.Kick))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				if (msg.CharacterID < 1)
				{
					return;
				}

				// we can't kick ourself
				if (msg.CharacterID == guildController.Character.ID)
				{
					return;
				}

				// Capture immutable data for async path
				long guildID = guildController.ID;
				long memberID = msg.CharacterID;
				long characterID = guildController.Character.ID;

				/* Marked for the TARGET, not the requester: it is the target's membership row
				 * being deleted, and it is the target who could disconnect mid-delete and have
				 * the disconnect persist write it straight back. */
				BeginMembershipRemoval(memberID);

				deferGuardRelease = TryEnqueueIngressWork(() => RemoveGuildMemberAsync(guildID, memberID, characterID), guardKey, characterID);
				if (!deferGuardRelease)
				{
					EndMembershipRemoval(memberID);
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
		/// Asynchronously removes a guild member, validates rank permissions, and triggers guild update.
		/// Marshals tracker cleanup back to the main thread.
		/// </summary>
		/// <param name="guildID">Guild identifier containing the target member.</param>
		/// <param name="memberID">Target member character identifier.</param>
		/// <param name="requesterCharacterID">Requester character identifier.</param>
		/// <param name="requesterRank">Requester rank for permission checks.</param>
		/// <returns>Asynchronous remove-member task.</returns>
		private async Task RemoveGuildMemberAsync(long guildID, long memberID, long requesterCharacterID)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
				{
					return;
				}

				/* AUTHORITATIVE. The requester's rank arrived here as a parameter captured from
				 * their controller before the async hop; it is now re-read from the guild's own
				 * rows, so a demotion that landed while this was queued is honoured. */
				GuildAuthority requester = await ResolveGuildAuthorityAsync(guildID, requesterCharacterID);

				// Verify the target member exists and check rank permission
				DatabaseResult<CharacterGuildData?> memberResult = await charGuildService.FetchAsync(memberID);
				if (!memberResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"RemoveGuildMemberAsync target fetch failed (GuildID={guildID}, MemberID={memberID}): {memberResult.ErrorCode} - {memberResult.ErrorMessage}");
					return;
				}

				if (!memberResult.Data.HasValue)
				{
					return;
				}

				CharacterGuildData targetMember = memberResult.Data.Value;

				// Verify target is in the same guild
				if (targetMember.GuildID != guildID)
				{
					return;
				}

				// THE decision. Permission plus strict seniority — see GuildRules.CanKick.
				if (GuildRules.CanKick(requester, targetMember.Rank) != GuildActionResult.Allowed)
				{
					return;
				}

				// Delete the member
				DatabaseResult deleteResult = await charGuildService.DeleteAsync(memberID, targetMember.Version + 1);
				if (!deleteResult.IsSuccess)
				{
					/* Most often STALE_STATE: the member's row moved between the read and the
					 * delete — a rank change, or their own location persist. Nothing was removed
					 * and the kick can simply be issued again. */
					await Log.Warning("GuildSystem", $"RemoveGuildMemberAsync member delete failed (GuildID={guildID}, MemberID={memberID}): {deleteResult.ErrorCode} - {deleteResult.ErrorMessage}");
					return;
				}

				// Tell the other servers to update their guild lists
				DatabaseResult updateResult = await guildUpdateService.PersistAsync(guildID);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"RemoveGuildMemberAsync guild update notification failed (GuildID={guildID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
				}

				AppendGuildLog(guildID, GuildLogEventType.Kicked, requesterCharacterID, memberID);

				// Marshal tracker cleanup to main thread
				TryEnqueueMainThread(() =>
				{
					RemoveGuildCharacterTracker(guildID, memberID);

					/* Tell the kicked member immediately if they are on this scene server.
					 * Nothing used to: their controller kept a live guild ID until the next
					 * periodic pump noticed the row was gone, which is up to a full pump interval
					 * of being in a guild they had been removed from — and any guild action they
					 * took in that window was authorised against the stale ID. Clearing the
					 * controller here also closes the disconnect-resurrection window for good,
					 * since the disconnect persist reads that same ID. */
					if (Server != null &&
						Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData) &&
						characterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter targetCharacter) &&
						targetCharacter != null &&
						targetCharacter.TryGet(out IGuildController targetGuildController) &&
						targetGuildController.ID == guildID)
					{
						targetGuildController.ID = 0;
						targetGuildController.RankOrder = 0;
						targetGuildController.Permissions = GuildPermissions.None;
						targetGuildController.LeaderRankOrder = 0;

						if (targetCharacter.Owner != null)
						{
							Server.NetworkWrapper.Broadcast(targetCharacter.Owner, new GuildLeaveBroadcast(), true, Channel.Reliable);
						}
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error removing guild member (GuildID={guildID}, MemberID={memberID}): {ex}");
			}
			finally
			{
				EndMembershipRemoval(memberID);
			}
		}

		/// <summary>
		/// Handles guild rank change broadcast, validates leader and target, and fires an async task
		/// to update ranks in the database.
		/// Only guild leaders can promote another member to a new rank.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">GuildChangeRankBroadcast message containing target member ID and new rank.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildChangeRankBroadcastReceived(NetworkConnection conn, GuildChangeRankBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out _))
			{
				return;
			}

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
				IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();

				// validate character — pre-filter; ChangeGuildRankAsync re-resolves the standing
				if (guildController == null ||
					guildController.ID < 1 ||
					!guildController.HasGuildPermission(GuildPermissions.Promote))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				if (msg.CharacterID < 1)
				{
					return;
				}

				// we can't promote ourself
				if (msg.CharacterID == guildController.Character.ID)
				{
					return;
				}

				// Capture immutable data for async path
				long guildID = guildController.ID;
				long memberID = msg.CharacterID;
				/* The requested ladder position, as sent. There is nothing to validate about it
				 * HERE beyond it being a legal byte: whether the guild has a rank at that
				 * position, and whether the requester is allowed to put somebody there, are both
				 * questions about rows this thread must not read. ChangeGuildRankAsync answers
				 * them. */
				byte newRankOrder = msg.RankOrder;
				if (newRankOrder < GuildRankDefaults.MinRankOrder || newRankOrder > GuildRankDefaults.MaxRankOrder)
				{
					return;
				}

				long requesterCharacterID = guildController.Character.ID;

				deferGuardRelease = TryEnqueueIngressWork(() => ChangeGuildRankAsync(guildID, memberID, newRankOrder, requesterCharacterID), guardKey, guildID);
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
		/// Asynchronously updates a guild member's rank and triggers a guild update notification.
		/// </summary>
		/// <param name="guildID">Guild identifier containing the member.</param>
		/// <param name="memberID">Member character identifier.</param>
		/// <param name="newRankOrder">Ladder position to move the member to.</param>
		/// <param name="requesterCharacterID">The character who requested the change, for the activity log.</param>
		/// <returns>Asynchronous rank-change task.</returns>
		/// <remarks>
		/// Four separate refusals live here, and they are separate on purpose:
		/// the requester must hold <c>Promote</c>; the destination rank must EXIST in this guild;
		/// the requester must outrank both the member's current rank and the destination rank;
		/// and the guild's top seat cannot be entered or left through this path.
		/// Dropping any one of them is a privilege escalation, and three of the four are invisible
		/// in the request itself.
		/// </remarks>
		private async Task ChangeGuildRankAsync(long guildID, long memberID, byte newRankOrder, long requesterCharacterID)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
				{
					return;
				}

				// AUTHORITATIVE: re-resolve the requester against the guild's own rank rows.
				GuildAuthority requester = await ResolveGuildAuthorityAsync(guildID, requesterCharacterID);

				// Fetch the member's current version for optimistic concurrency
				DatabaseResult<CharacterGuildData?> memberResult = await charGuildService.FetchAsync(memberID);
				if (!memberResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"ChangeGuildRankAsync target fetch failed (GuildID={guildID}, MemberID={memberID}): {memberResult.ErrorCode} - {memberResult.ErrorMessage}");
					return;
				}

				if (!memberResult.Data.HasValue)
				{
					return;
				}

				/* Confirm the target is actually in the requester's guild. The UPDATE's own WHERE
				 * clause happens to carry the guild id, so the wrong-guild case was saved by SQL
				 * rather than by anything in this method — which meant the app-level code was one
				 * refactor of that statement away from letting a leader re-rank a stranger, and
				 * meanwhile burned a round trip and a version bump on a write it knew would miss.
				 * The check belongs where the decision is made. */
				if (memberResult.Data.Value.GuildID != guildID)
				{
					await Log.Warning("GuildSystem", $"ChangeGuildRankAsync refused: target is not in the requesting guild (GuildID={guildID}, MemberID={memberID}, TargetGuildID={memberResult.Data.Value.GuildID}).");
					return;
				}

				byte currentRankOrder = memberResult.Data.Value.Rank;

				/* THE decision. Destination must exist, the top seat is off limits to this path,
				 * and the requester must outrank BOTH the member's current rank and the
				 * destination — see GuildRules.CanChangeMemberRank. */
				if (GuildRules.CanChangeMemberRank(requester, currentRankOrder, newRankOrder) != GuildActionResult.Allowed)
				{
					return;
				}

				DatabaseResult rankResult = await charGuildService.UpdateRankAsync(memberID, guildID, newRankOrder, memberResult.Data.Value.Version + 1);
				if (rankResult.IsSuccess)
				{
					/* Promotion or demotion is decided by comparing against the rank the member
					 * actually held, read above — not by what the requester called the action. */
					requester.TryGetRank(newRankOrder, out GuildRankData destinationRank);

					AppendGuildLog(
						guildID,
						newRankOrder > currentRankOrder ? GuildLogEventType.Promoted : GuildLogEventType.Demoted,
						requesterCharacterID,
						memberID,
						destinationRank.Name ?? string.Empty);

					// Tell the other servers to update their guild lists
					DatabaseResult updateResult = await guildUpdateService.PersistAsync(guildID);
					if (!updateResult.IsSuccess)
					{
						await Log.Warning("GuildSystem", $"ChangeGuildRankAsync guild update notification failed (GuildID={guildID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
					}
				}
				else
				{
					await Log.Warning("GuildSystem", $"ChangeGuildRankAsync rank update failed (GuildID={guildID}, MemberID={memberID}): {rankResult.ErrorCode} - {rankResult.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error changing guild rank (GuildID={guildID}, MemberID={memberID}): {ex}");
			}
		}

		/// <summary>
		/// Handles a request to change the guild message of the day.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">The requested message of the day.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildSetMessageOfTheDayBroadcastReceived(NetworkConnection conn, GuildSetMessageOfTheDayBroadcast msg, Channel channel)
		{
			HandleGuildTextEdit(conn, msg.MessageOfTheDay, GuildTextLimits.MaxMessageOfTheDayLength, isMessageOfTheDay: true);
		}

		/// <summary>
		/// Handles a request to change the guild notice.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">The requested notice text.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildSetNoticeBroadcastReceived(NetworkConnection conn, GuildSetNoticeBroadcast msg, Channel channel)
		{
			HandleGuildTextEdit(conn, msg.Notice, GuildTextLimits.MaxNoticeLength, isMessageOfTheDay: false);
		}

		/// <summary>
		/// Shared validation and dispatch for the two guild text fields.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="text">The requested text.</param>
		/// <param name="maxLength">Maximum accepted length for this field.</param>
		/// <param name="isMessageOfTheDay">True for the message of the day, false for the notice.</param>
		/// <remarks>
		/// The two fields are separately permissioned — <c>EditMessageOfTheDay</c> and
		/// <c>EditNotice</c> — because a guild that wants a recruiter able to keep the MOTD
		/// current should not have to also let them rewrite the notice. Under the old enum both
		/// were "officer or better" and there was no way to separate them.
		///
		/// The permission is read from the SERVER's copy of the controller as a pre-filter and
		/// re-resolved from the guild's rank rows on the async path; never from the message. The
		/// length cap is re-applied here as well: the client trims so the player can see the
		/// limit, but a hand-built packet would otherwise reach a 500-character column and fail at
		/// the database instead of at the boundary.
		/// </remarks>
		private void HandleGuildTextEdit(NetworkConnection conn, string text, int maxLength, bool isMessageOfTheDay)
		{
			if (!TryBeginPlayerRequest(conn, out _))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.SetInfo, out long guardKey))
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

				IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
				if (guildController == null || guildController.ID < 1)
				{
					return;
				}

				GuildPermissions required = isMessageOfTheDay
					? GuildPermissions.EditMessageOfTheDay
					: GuildPermissions.EditNotice;

				if (!guildController.HasGuildPermission(required))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				string sanitized = text ?? string.Empty;
				sanitized = sanitized.Trim();
				if (sanitized.Length > maxLength)
				{
					sanitized = sanitized.Substring(0, maxLength);
				}

				long guildID = guildController.ID;
				long editorCharacterID = guildController.Character.ID;

				deferGuardRelease = TryEnqueueIngressWork(() => SetGuildTextAsync(guildID, sanitized, isMessageOfTheDay, editorCharacterID), guardKey, guildID);
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
		/// Persists one of the guild text fields and re-publishes the guild information.
		/// </summary>
		/// <param name="guildID">Guild identifier being edited.</param>
		/// <param name="text">The sanitized text to store.</param>
		/// <param name="isMessageOfTheDay">True for the message of the day, false for the notice.</param>
		/// <param name="editorCharacterID">The character who made the edit, for the activity log.</param>
		/// <returns>Asynchronous edit task.</returns>
		private async Task SetGuildTextAsync(long guildID, string text, bool isMessageOfTheDay, long editorCharacterID)
		{
			try
			{
				if (!TryGetDbService(out IGuildService guildService))
				{
					return;
				}

				// AUTHORITATIVE: the pre-filter above read a cache; this reads the guild's rows.
				GuildAuthority editor = await ResolveGuildAuthorityAsync(guildID, editorCharacterID);
				if (!editor.Has(isMessageOfTheDay ? GuildPermissions.EditMessageOfTheDay : GuildPermissions.EditNotice))
				{
					return;
				}

				DatabaseResult persistResult = isMessageOfTheDay
					? await guildService.PersistMessageOfTheDayAsync(guildID, text)
					: await guildService.PersistNoticeAsync(guildID, text);

				if (!persistResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"SetGuildTextAsync persist failed (GuildID={guildID}, MOTD={isMessageOfTheDay}): {persistResult.ErrorCode} - {persistResult.ErrorMessage}");
					return;
				}

				AppendGuildLog(
					guildID,
					isMessageOfTheDay ? GuildLogEventType.MessageOfTheDayChanged : GuildLogEventType.NoticeChanged,
					editorCharacterID);

				await PublishGuildInfoAsync(guildID);
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error setting guild text (GuildID={guildID}, MOTD={isMessageOfTheDay}): {ex}");
			}
		}

		/// <summary>
		/// Reads a guild's descriptive text and sends it to every member on this scene server.
		/// </summary>
		/// <param name="guildID">Guild identifier to publish.</param>
		/// <param name="onlyCharacterID">
		/// When non-zero, sends to just this one character instead of the whole local roster.
		/// </param>
		/// <returns>Asynchronous publish task.</returns>
		/// <remarks>
		/// <para>
		/// Sent to the members this scene server hosts, resolved through the guild character
		/// tracker. Members elsewhere get their copy from their own scene server, which is running
		/// the same code against the same row.
		/// </para>
		/// <para>
		/// Catches its own exceptions. The create and join paths hand it to the async worker from a
		/// main-thread block, as a courtesy nothing waits on — they used to discard the task
		/// outright, running the read on the main thread with any fault unobserved, and a fault
		/// nobody observes is a fault nobody hears about.
		/// </para>
		/// </remarks>
		private async Task PublishGuildInfoAsync(long guildID, long onlyCharacterID = 0)
		{
			try
			{
				if (!TryGetDbService(out IGuildService guildService))
				{
					return;
				}

				DatabaseResult<GuildData?> guildResult = await guildService.FetchAsync(guildID);
				if (!guildResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"PublishGuildInfoAsync guild fetch failed (GuildID={guildID}): {guildResult.ErrorCode} - {guildResult.ErrorMessage}");
					return;
				}

				if (!guildResult.Data.HasValue)
				{
					// Disbanded since the caller looked; there is nothing to show.
					return;
				}

				GuildData guild = guildResult.Data.Value;

				GuildInfoBroadcast broadcast = new GuildInfoBroadcast()
				{
					GuildID = guild.ID,
					Name = guild.Name ?? string.Empty,
					Notice = guild.Notice ?? string.Empty,
					MessageOfTheDay = guild.MessageOfTheDay ?? string.Empty,
				};

				// One multicast to the local roster, or the one named member; see BroadcastToGuildMembers.
				BroadcastToGuildMembers(guildID, broadcast, onlyCharacterID);
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error publishing guild info (GuildID={guildID}): {ex}");
			}
		}

		/// <summary>
		/// Handles a request to transfer guild leadership to another member.
		/// </summary>
		/// <param name="conn">Network connection of the current leader.</param>
		/// <param name="msg">The broadcast naming the successor.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		/// <remarks>
		/// The successor may be OFFLINE. Leadership is a database rank, not a session, and a guild
		/// whose leader can only hand over while the successor happens to be logged in is a guild
		/// that stays stuck for exactly the reason S5 describes.
		/// </remarks>
		public void OnServerGuildTransferLeadershipBroadcastReceived(NetworkConnection conn, GuildTransferLeadershipBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out _))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.TransferLeadership, out long guardKey))
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

				IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
				if (guildController == null || guildController.ID < 1)
				{
					return;
				}

				if (!guildController.HasGuildPermission(GuildPermissions.TransferLeadership))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				if (msg.CharacterID < 1 || msg.CharacterID == guildController.Character.ID)
				{
					return;
				}

				long guildID = guildController.ID;
				long currentLeaderID = guildController.Character.ID;
				long successorID = msg.CharacterID;

				deferGuardRelease = TryEnqueueIngressWork(() => TransferGuildLeadershipAsync(conn, guildID, currentLeaderID, successorID), guardKey, guildID);
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
		/// Promotes a member to leader and demotes the outgoing leader to officer.
		/// </summary>
		/// <param name="conn">Requesting connection, for feedback.</param>
		/// <param name="guildID">Guild identifier.</param>
		/// <param name="currentLeaderID">The outgoing leader's character identifier.</param>
		/// <param name="successorID">The incoming leader's character identifier.</param>
		/// <returns>Asynchronous transfer task.</returns>
		/// <remarks>
		/// The successor is promoted FIRST. If the second write fails the guild briefly has two
		/// leaders, which is recoverable by either of them; doing it the other way round would
		/// produce a guild with none, which is not recoverable at all. The pump reconciles both
		/// ranks from the database on its next pass either way.
		/// </remarks>
		private async Task TransferGuildLeadershipAsync(NetworkConnection conn, long guildID, long currentLeaderID, long successorID)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
				{
					return;
				}

				/* AUTHORITATIVE, and stricter than the permission alone: the requester must also
				 * currently OCCUPY the top seat. A rank other than the leader's could in principle
				 * be granted TransferLeadership by a rank editor, and handing the top seat away is
				 * not something a subordinate rank should be able to do to its own leader. */
				GuildAuthority requester = await ResolveGuildAuthorityAsync(guildID, currentLeaderID);
				if (GuildRules.CanTransferLeadership(requester) != GuildActionResult.Allowed)
				{
					SendGuildResult(conn, AuthorityRefusal(requester));
					return;
				}

				DatabaseResult<CharacterGuildData?> successorResult = await charGuildService.FetchAsync(successorID);
				if (!successorResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"TransferGuildLeadershipAsync successor fetch failed (GuildID={guildID}, Successor={successorID}): {successorResult.ErrorCode} - {successorResult.ErrorMessage}");
					SendGuildResult(conn, GuildResultType.Failed);
					return;
				}

				if (!successorResult.Data.HasValue)
				{
					SendGuildResult(conn, GuildResultType.GuildNotFound);
					return;
				}

				CharacterGuildData successor = successorResult.Data.Value;
				if (successor.GuildID != guildID)
				{
					// Not a member of this guild — nothing to transfer to.
					SendGuildResult(conn, GuildResultType.GuildNotFound);
					return;
				}

				/* Promoted into the seat the OUTGOING leader vacates, whatever number that is,
				 * and the outgoing leader drops to the rung immediately below it. Both were
				 * constants (Leader / Officer) before; in a guild with five ranks that would have
				 * moved the leadership to rank 3 and parked the ex-leader at rank 2, skipping the
				 * two ranks in between and handing whoever sat at rank 4 or 5 seniority over the
				 * guild's own leader. */
				byte leaderRankOrder = requester.RankOrder;
				byte demotedRankOrder = FindNextRankBelow(requester.Ladder, leaderRankOrder);

				DatabaseResult promoteResult = await charGuildService.UpdateRankAsync(successorID, guildID, leaderRankOrder, successor.Version + 1);
				if (!promoteResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"TransferGuildLeadershipAsync promote failed (GuildID={guildID}, Successor={successorID}): {promoteResult.ErrorCode} - {promoteResult.ErrorMessage}");
					// The requester's standing was just resolved and allows this; what failed was the write.
					SendGuildResult(conn, GuildResultType.Failed);
					return;
				}

				/* Re-read rather than reusing the version the resolve saw: the outgoing leader's own
				 * connect or disconnect persist can move it in the meantime, and the freshest version
				 * is the narrowest window for the demote to lose. Every way this second half can fail
				 * leaves two leaders — the state the remarks accept — so every one of them is logged
				 * as such. A read failure used to skip the demote in silence and still report
				 * Success, which made the two-leader state invisible to everyone but the players. */
				DatabaseResult<CharacterGuildData?> outgoingResult = await charGuildService.FetchAsync(currentLeaderID);
				if (!outgoingResult.IsSuccess)
				{
					await Log.Error("GuildSystem", $"TransferGuildLeadershipAsync could not read the outgoing leader to demote them (GuildID={guildID}, OutgoingLeader={currentLeaderID}); the guild now has two members at rank order {leaderRankOrder}: {outgoingResult.ErrorCode} - {outgoingResult.ErrorMessage}");
				}
				else if (outgoingResult.Data.HasValue && outgoingResult.Data.Value.GuildID == guildID)
				{
					CharacterGuildData outgoing = outgoingResult.Data.Value;
					DatabaseResult demoteResult = await charGuildService.UpdateRankAsync(currentLeaderID, guildID, demotedRankOrder, outgoing.Version + 1);
					if (!demoteResult.IsSuccess)
					{
						await Log.Error("GuildSystem", $"TransferGuildLeadershipAsync demote failed (GuildID={guildID}, OutgoingLeader={currentLeaderID}); the guild now has two members at rank order {leaderRankOrder}: {demoteResult.ErrorCode} - {demoteResult.ErrorMessage}");
					}
				}
				// else: the outgoing leader left the guild in between, so the successor is its only leader.

				DatabaseResult updateResult = await guildUpdateService.PersistAsync(guildID);
				if (!updateResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"TransferGuildLeadershipAsync guild update notification failed (GuildID={guildID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
				}

				AppendGuildLog(guildID, GuildLogEventType.LeadershipTransferred, currentLeaderID, successorID);

				SendGuildResult(conn, GuildResultType.Success);
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error transferring guild leadership (GuildID={guildID}, Successor={successorID}): {ex}");
			}
		}

		/// <summary>
		/// Handles a request to disband the guild.
		/// </summary>
		/// <param name="conn">Network connection of the leader.</param>
		/// <param name="msg">The broadcast carrying the confirmation name.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildDisbandBroadcastReceived(NetworkConnection conn, GuildDisbandBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out _))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Disband, out long guardKey))
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

				IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
				if (guildController == null || guildController.ID < 1)
				{
					return;
				}

				if (!guildController.HasGuildPermission(GuildPermissions.Disband))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				long guildID = guildController.ID;
				long requesterCharacterID = guildController.Character.ID;
				string confirmation = msg.ConfirmationName ?? string.Empty;

				deferGuardRelease = TryEnqueueIngressWork(() => DisbandGuildAsync(conn, guildID, confirmation, requesterCharacterID), guardKey, guildID);
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
		/// Deletes a guild and evicts every member.
		/// </summary>
		/// <param name="conn">Requesting connection, for feedback.</param>
		/// <param name="guildID">Guild identifier to delete.</param>
		/// <param name="confirmationName">The guild name the requester typed.</param>
		/// <param name="requesterCharacterID">The character requesting the disband.</param>
		/// <returns>Asynchronous disband task.</returns>
		/// <remarks>
		/// The name is re-checked against the database rather than against anything the client
		/// sent alongside it. Membership rows go via the guild table's CASCADE, and every local
		/// member is told immediately instead of being left holding a live guild ID until the pump
		/// notices — which is the same window the kick path had to close.
		/// </remarks>
		private async Task DisbandGuildAsync(NetworkConnection conn, long guildID, string confirmationName, long requesterCharacterID)
		{
			try
			{
				if (!TryGetDbService(out IGuildService guildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
				{
					return;
				}

				/* AUTHORITATIVE. Disband is the one guild action with no undo, so the cached
				 * pre-filter in the handler is re-established here before anything is deleted. */
				GuildAuthority requester = await ResolveGuildAuthorityAsync(guildID, requesterCharacterID);
				if (!requester.Has(GuildPermissions.Disband))
				{
					SendGuildResult(conn, AuthorityRefusal(requester));
					return;
				}

				DatabaseResult<GuildData?> guildResult = await guildService.FetchAsync(guildID);
				if (!guildResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"DisbandGuildAsync guild fetch failed (GuildID={guildID}): {guildResult.ErrorCode} - {guildResult.ErrorMessage}");
					SendGuildResult(conn, GuildResultType.Failed);
					return;
				}

				if (!guildResult.Data.HasValue)
				{
					SendGuildResult(conn, GuildResultType.GuildNotFound);
					return;
				}

				/* Typing the name is the confirmation. Compared case-insensitively against the
				 * stored name so the player is confirming the guild that actually exists, not the
				 * one their client last rendered. */
				if (!string.Equals(guildResult.Data.Value.Name, confirmationName, StringComparison.OrdinalIgnoreCase))
				{
					SendGuildResult(conn, GuildResultType.InvalidGuildName);
					return;
				}

				DatabaseResult deleteResult = await guildService.DeleteAsync(guildID);
				if (!deleteResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"DisbandGuildAsync guild delete failed (GuildID={guildID}): {deleteResult.ErrorCode} - {deleteResult.ErrorMessage}");
					SendGuildResult(conn, GuildResultType.Failed);
					return;
				}

				DatabaseResult<int> updateDeleteResult = await guildUpdateService.DeleteAsync(guildID);
				if (!updateDeleteResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"DisbandGuildAsync guild update delete failed (GuildID={guildID}): {updateDeleteResult.ErrorCode} - {updateDeleteResult.ErrorMessage}");
				}

				// The guild is gone, so its land goes back to the world — see ReleaseGuildPlotsAsync.
				await ReleaseGuildPlotsAsync(guildID);

				// Every other server learns of this through its existence sweep (OnPeriodicGuildExistenceSweep).
				TryEnqueueMainThread(() => ClearLocalGuildMembers(guildID));
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error disbanding guild (GuildID={guildID}): {ex}");
			}
		}

		/// <summary>
		/// Takes every local member of a guild that no longer exists out of it. Main thread only.
		/// </summary>
		/// <remarks>
		/// For a disband done here, and for one done on another server and noticed by the existence
		/// sweep, or by the update pump when a guild it was reading turns out to be gone. The same clear in both, so a member of a guild disbanded elsewhere is left in
		/// exactly the state a local disband leaves them in.
		/// </remarks>
		private void ClearLocalGuildMembers(long guildID)
		{
			if (Server == null ||
				!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData) ||
				!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
			{
				return;
			}

			if (mappingData.GuildCharacterTracker.TryGetValue(guildID, out HashSet<long> memberIDs))
			{
				// Copied before iterating: clearing each member mutates the tracker.
				foreach (long memberID in new List<long>(memberIDs))
				{
					if (characterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter member) && member != null)
					{
						ClearGuildStanding(member, guildID);
					}
				}
			}

			mappingData.GuildCharacterTracker.Remove(guildID);
			ForgetDeliveredGuild(mappingData, guildID);
		}

		/// <summary>
		/// Takes one local character out of <paramref name="guildID"/> and tells their client. Main thread only.
		/// </summary>
		/// <remarks>
		/// Clears the cached standing as well as the id. The update pump used to clear only the id,
		/// leaving a removed member's rank and permissions cached — a stale pre-filter that kept their
		/// panel offering actions the server then refused. And it did so without checking the id was
		/// still this guild's, so a member who had already joined another guild was taken out of THAT
		/// one (issue #267). Does nothing unless the character is still in <paramref name="guildID"/>.
		/// </remarks>
		private void ClearGuildStanding(IPlayerCharacter member, long guildID)
		{
			if (!member.TryGet(out IGuildController guildController) || guildController.ID != guildID)
			{
				return;
			}

			guildController.ID = 0;
			guildController.RankOrder = 0;
			guildController.Permissions = GuildPermissions.None;
			guildController.LeaderRankOrder = 0;

			// The leave below clears the client's roster and ladder; nothing delivered for this guild still holds.
			guildRecipientBaselines.Forget(member.ID, guildID);

			if (member.Owner != null)
			{
				Server.NetworkWrapper.Broadcast(member.Owner, new GuildLeaveBroadcast(), true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Counts appends since the last activity log prune.
		/// </summary>
		private int guildLogAppendsSincePrune;

		/// <summary>
		/// Appends one row to a guild's activity log, trimming the table periodically.
		/// </summary>
		/// <param name="guildID">Guild the event belongs to.</param>
		/// <param name="eventType">What happened.</param>
		/// <param name="actorCharacterID">The acting character, or zero.</param>
		/// <param name="targetCharacterID">The subject character, or zero.</param>
		/// <param name="detail">Optional short detail, such as a rank name.</param>
		/// <remarks>
		/// Fire-and-forget through the persistence queue. A guild event must not fail, or be
		/// delayed, because the log could not be written — the log is a record OF the game, not a
		/// step IN it, and making the two share a failure path would let a full disk stop players
		/// from being promoted.
		/// </remarks>
		private void AppendGuildLog(long guildID, GuildLogEventType eventType, long actorCharacterID, long targetCharacterID = 0, string detail = null)
		{
			if (guildID < 1 || Server?.Database?.ServiceRegistry == null)
			{
				return;
			}

			/* Interlocked: guild actions finish on worker threads, and a plain ++ from two of them
			 * could lose a count. Harmless before — a prune ran a little late — but the counter is
			 * shared state all the same (issue #267). */
			bool prune = false;
			if (Interlocked.Increment(ref guildLogAppendsSincePrune) >= guildLogPruneInterval)
			{
				Interlocked.Exchange(ref guildLogAppendsSincePrune, 0);
				prune = true;
			}

			int retain = guildLogRetainedEntries;

			EnqueuePersistence(async () =>
			{
				try
				{
					if (!TryGetDbService(out IGuildLogService logService))
					{
						return;
					}

					GuildLogData entry = new GuildLogData(
						0,
						guildID,
						eventType,
						actorCharacterID,
						targetCharacterID,
						detail ?? string.Empty,
						DateTime.UtcNow);

					DatabaseResult appendResult = await logService.AppendAsync(entry);
					if (!appendResult.IsSuccess)
					{
						await Log.Warning("GuildSystem", $"AppendGuildLog failed (GuildID={guildID}, Event={eventType}): {appendResult.ErrorCode} - {appendResult.ErrorMessage}");
						return;
					}

					if (prune)
					{
						DatabaseResult<int> pruneResult = await logService.PruneAsync(guildID, retain);
						if (!pruneResult.IsSuccess)
						{
							await Log.Warning("GuildSystem", $"AppendGuildLog prune failed (GuildID={guildID}): {pruneResult.ErrorCode} - {pruneResult.ErrorMessage}");
						}
					}
				}
				catch (Exception ex)
				{
					await Log.Error("GuildSystem", $"Error appending guild log (GuildID={guildID}, Event={eventType}): {ex}");
				}
			}, guildID);
		}

		/// <summary>
		/// Handles a request for the guild's recent activity log.
		/// </summary>
		/// <param name="conn">Network connection of the requester.</param>
		/// <param name="msg">The request broadcast.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		/// <remarks>
		/// The guild is taken from the requester's SERVER-side controller, never from the message.
		/// The request carries no guild id precisely so that there is nothing to forge: a player
		/// can only ever ask for the log of the guild the server already believes they are in.
		/// </remarks>
		public void OnServerGuildLogRequestBroadcastReceived(NetworkConnection conn, GuildLogRequestBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out _))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.LogRequest, out long guardKey))
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

				IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
				if (guildController == null || guildController.ID < 1)
				{
					return;
				}

				long guildID = guildController.ID;
				int limit = guildLogRetainedEntries;

				deferGuardRelease = TryEnqueueIngressWork(() => SendGuildLogAsync(conn, guildID, limit), guardKey, guildID);
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
		/// Reads a guild's recent log and sends it to one connection.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="guildID">Guild identifier.</param>
		/// <param name="limit">Maximum entries to send.</param>
		/// <returns>Asynchronous send task.</returns>
		private async Task SendGuildLogAsync(NetworkConnection conn, long guildID, int limit)
		{
			try
			{
				if (!TryGetDbService(out IGuildLogService logService))
				{
					return;
				}

				DatabaseResult<IReadOnlyList<GuildLogData>> fetchResult = await logService.FetchRecentAsync(guildID, limit);
				if (!fetchResult.IsSuccess || fetchResult.Data == null)
				{
					await Log.Warning("GuildSystem", $"SendGuildLogAsync fetch failed (GuildID={guildID}): {fetchResult.ErrorCode} - {fetchResult.ErrorMessage}");
					SendGuildResult(conn, GuildResultType.Failed);
					return;
				}

				IReadOnlyList<GuildLogData> rows = fetchResult.Data;
				GuildLogEntry[] entries = new GuildLogEntry[rows.Count];
				for (int i = 0; i < rows.Count; ++i)
				{
					GuildLogData row = rows[i];
					entries[i] = new GuildLogEntry()
					{
						Event = (GuildLogEvent)row.EventType,
						ActorCharacterID = row.ActorCharacterID,
						TargetCharacterID = row.TargetCharacterID,
						Detail = row.Detail ?? string.Empty,
						TimeUnixSeconds = ToUnixSeconds(row.TimeCreated),
					};
				}

				GuildLogBroadcast broadcast = new GuildLogBroadcast()
				{
					Entries = entries,
				};

				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive || Server == null)
					{
						return;
					}

					/* Re-checked on delivery, not only on request. The read is asynchronous and the
					 * requester may have left or been kicked while it was in flight, and a log is
					 * exactly the kind of thing an ex-member should stop receiving. */
					if (conn.FirstObject == null)
					{
						return;
					}

					IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
					if (guildController == null || guildController.ID != guildID)
					{
						return;
					}

					Server.NetworkWrapper.Broadcast(conn, broadcast, true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error sending guild log (GuildID={guildID}): {ex}");
			}
		}

		/// <summary>
		/// Enqueues ingress work and guarantees guard release when async processing completes.
		/// </summary>
		private bool TryEnqueueIngressWork(Func<Task> work, long guardKey, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			return TryEnqueueGuardedAsyncWork(work, EndIngressGuard, guardKey, entityKey, callerName);
		}
	}
}