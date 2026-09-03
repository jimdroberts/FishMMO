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
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(UpdatePumpRate, OnPeriodicUpdate);
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
			runtimeData.EndUpdatePump();
			runtimeData.NextInvitationSweepUtc = DateTime.UtcNow;

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

			/* Cooldown entries are pure memory and nothing else ever removes them, so they need
			 * the same bounded sweep the invitations get. Their TTL is the cooldown itself: once
			 * it has elapsed the entry can no longer refuse anything. */
			runtimeData.SweepInviteCooldowns(
				nowUtc,
				TimeSpan.FromSeconds(perTargetInviteCooldownSeconds),
				invitationSweepMaxScan,
				invitationSweepMaxRemove);

			runtimeData.SweepApplicationCooldowns(
				nowUtc,
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
				DateTime.UtcNow);
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
		private async Task FetchAndProcessGuildUpdatesAsync(List<long> guildIds, DateTime lastFetch)
		{
			try
			{
				if (!TryGetDbService(out IGuildUpdateService guildUpdateService) ||
					!TryGetDbService(out ICharacterGuildService charGuildService))
				{
					return;
				}

				if (guildIds.Count == 0)
				{
					return;
				}

				// Async DB fetch
				DatabaseResult<List<GuildUpdateData>> fetchResult = await guildUpdateService.FetchAsync(guildIds, lastFetch);
				if (!fetchResult.IsSuccess || fetchResult.Data == null || fetchResult.Data.Count < 1)
				{
					return;
				}

				List<GuildUpdateData> updates = fetchResult.Data;

				// For each unique guild that was updated, fetch the current members
				HashSet<long> updatedGuilds = new HashSet<long>();
				// Collect per-guild member data
				Dictionary<long, IReadOnlyList<CharacterGuildData>> guildMembersMap = new Dictionary<long, IReadOnlyList<CharacterGuildData>>();
				/* Per-guild rank ladders, fetched here on the async path so the main-thread block
				 * below can decide who may see an officer note WITHOUT a database call and
				 * WITHOUT trusting the rank cached on the character. The membership rows and the
				 * ladder are read in the same pass, so the filter is applied against the same
				 * snapshot the roster itself was built from. */
				Dictionary<long, IReadOnlyList<GuildRankData>> guildLaddersMap = new Dictionary<long, IReadOnlyList<GuildRankData>>();

				TryGetDbService(out IGuildRankService rankService);

				foreach (GuildUpdateData update in updates)
				{
					if (updatedGuilds.Contains(update.GuildID))
					{
						continue;
					}
					updatedGuilds.Add(update.GuildID);

					DatabaseResult<IReadOnlyList<CharacterGuildData>> membersResult = await charGuildService.FetchManyAsync(update.GuildID);
					if (membersResult.IsSuccess && membersResult.Data != null)
					{
						guildMembersMap[update.GuildID] = membersResult.Data;
					}

					if (rankService != null)
					{
						IReadOnlyList<GuildRankData> ladder = await FetchOrSeedLadderAsync(update.GuildID, rankService);
						if (ladder != null)
						{
							guildLaddersMap[update.GuildID] = ladder;
						}
					}
				}

				// Marshal all main-thread state changes + broadcasts
				TryEnqueueMainThread(() =>
				{
					if (Server == null)
					{
						return;
					}

					// Update last fetch time
					if (Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData rtData))
					{
						rtData.LastFetchTime = DateTime.UtcNow;
					}

					if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mapData))
					{
						return;
					}

					foreach (var kvp in guildMembersMap)
					{
						long guildID = kvp.Key;
						IReadOnlyList<CharacterGuildData> dbMembers = kvp.Value;

						var currentMemberIDs = new HashSet<long>(dbMembers.Count);
						for (int i = 0; i < dbMembers.Count; i++)
						{
							currentMemberIDs.Add(dbMembers[i].CharacterID);
						}

						// Check if we have previously cached the guild member list
						if (mapData.GuildMemberTracker.TryGetValue(guildID, out var previousMembers))
						{
							// Compute the difference: members that are in previousMembers but not in currentMemberIDs
							var difference = new List<long>();
							foreach (long prevID in previousMembers)
							{
								if (!currentMemberIDs.Contains(prevID))
								{
									difference.Add(prevID);
								}
							}

							foreach (long memberID in difference)
							{
								// Tell the member connection to leave their guild immediately
								if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var guildCharacterMappingData) &&
									guildCharacterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter character) &&
									character != null &&
									character.TryGet(out IGuildController targetGuildController))
								{
									targetGuildController.ID = 0;
									Server.NetworkWrapper.Broadcast(character.Owner, new GuildLeaveBroadcast(), true, Channel.Reliable);
								}
							}
						}
						// Cache the guild member IDs
						mapData.GuildMemberTracker[guildID] = currentMemberIDs;

						guildLaddersMap.TryGetValue(guildID, out IReadOnlyList<GuildRankData> ladder);

						/* Two projections of the same roster. The officer note is a column a
						 * client either may read or may never receive — hiding it in the panel
						 * would leave it in the packet — so the message is built twice and the
						 * recipient's own rank decides which copy they get. */
						GuildAddMultipleBroadcast publicRoster = BuildRoster(dbMembers, includeOfficerNotes: false);
						GuildAddMultipleBroadcast officerRoster = BuildRoster(dbMembers, includeOfficerNotes: true);

						byte guildLeaderRankOrder = 0;
						if (ladder != null)
						{
							for (int i = 0; i < ladder.Count; ++i)
							{
								if (ladder[i].RankOrder > guildLeaderRankOrder)
								{
									guildLeaderRankOrder = ladder[i].RankOrder;
								}
							}
						}

						if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
						{
							// Tell all of the local guild members to update their guild member lists
							foreach (CharacterGuildData member in dbMembers)
							{
								if (characterMappingData.CharactersByID.TryGetValue(member.CharacterID, out IPlayerCharacter character))
								{
									if (!character.TryGet(out IGuildController guildController) ||
										guildController.ID < 1)
									{
										continue;
									}

									/* Refresh the server-side cache of this member's standing from
									 * the row that was just read. The cache is only ever a
									 * pre-filter — every operation re-resolves before deciding —
									 * but a rank change made on another scene server reaches this
									 * one through the pump, and leaving the cache stale would
									 * leave the player's own panel offering actions the server
									 * will refuse. */
									GuildPermissions memberPermissions = PermissionsForOrder(ladder, member.Rank);
									guildController.RankOrder = member.Rank;
									guildController.Permissions = memberPermissions;
									guildController.LeaderRankOrder = guildLeaderRankOrder;

									bool mayReadOfficerNotes = (memberPermissions & GuildPermissions.ViewOfficerNotes) == GuildPermissions.ViewOfficerNotes;
									Server.NetworkWrapper.Broadcast(character.Owner, mayReadOfficerNotes ? officerRoster : publicRoster, true, Channel.Reliable);

									Server.NetworkWrapper.Broadcast(character.Owner, new GuildRankListBroadcast()
									{
										GuildID = guildID,
										Ranks = BuildRankEntries(ladder),
										ViewerRankOrder = member.Rank,
										ViewerPermissions = (long)memberPermissions,
										LeaderRankOrder = guildLeaderRankOrder,
									}, true, Channel.Reliable);
								}
							}
						}
					}
				});
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
					mappingData.GuildMemberTracker.Remove(guildID);
				}
			}
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
			byte rank = guildController.RankOrder;
			string sceneName = character.SceneName;

			EnqueuePersistence(() => PersistGuildMemberAsync(characterID, guildID, rank, sceneName), characterID);
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
			 * character's controller still carries the old guild ID until that delete lands.
			 * Persisting from it here would UPSERT the row straight back — putting the player
			 * into the guild they had just been removed from, permanently, because nothing
			 * afterwards knows the row is not supposed to exist. Disconnecting inside that
			 * window is the whole exploit, and it is a window a player can aim for. */
			if (runtimeData != null && runtimeData.IsMembershipRemovalInFlight(character.ID))
			{
				return;
			}

			// Fire-and-forget async DB persist with "Offline" location
			long characterID = character.ID;
			long guildID = guildController.ID;
			byte rank = guildController.RankOrder;

			EnqueuePersistence(() => PersistGuildMemberAsync(characterID, guildID, rank, "Offline"), characterID);
		}

		/// <summary>
		/// Asynchronously persists a guild member's data and triggers a guild update notification.
		/// </summary>
		/// <param name="characterID">Character identifier to persist.</param>
		/// <param name="guildID">Guild identifier associated with the character.</param>
		/// <param name="rank">Guild rank value to persist.</param>
		/// <param name="location">Current member location label.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task PersistGuildMemberAsync(long characterID, long guildID, byte rank, string location)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
				{
					return;
				}

				// Fetch existing version for sequence-based optimistic concurrency
				long version = 1;
				DatabaseResult<CharacterGuildData?> existingResult = await charGuildService.FetchAsync(characterID);
				if (existingResult.IsSuccess && existingResult.Data.HasValue)
				{
					version = existingResult.Data.Value.Version + 1;
				}

				CharacterGuildData guildData = new CharacterGuildData(0, version, characterID, guildID, rank, location);
				DatabaseResult persistResult = await charGuildService.PersistAsync(guildData, maxGuildSize);
				if (!persistResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"PersistGuildMemberAsync DB error (CharID={characterID}, GuildID={guildID}): {persistResult.ErrorCode} - {persistResult.ErrorMessage}");
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
				if (!string.Equals(location, "Offline", StringComparison.OrdinalIgnoreCase))
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

			return EnqueuePersistence(() => PersistCreationFeeCurrencyToDbAsync(dtos, characterID), characterID);
		}

		/// <summary>
		/// Writes the fee currency's attribute row. Worker thread.
		/// </summary>
		private async Task PersistCreationFeeCurrencyToDbAsync(List<CharacterAttributeData> dtos, long characterID)
		{
			try
			{
				if (!TryGetDbService(out ICharacterAttributeService attributeService))
				{
					await Log.Error("GuildSystem", "PersistCreationFeeCurrencyToDbAsync: Failed to resolve ICharacterAttributeService");
					return;
				}

				await BulkWriteReporting.ReportAsync("GuildSystem", "Guild creation fee save",
					await attributeService.PersistAsync(dtos), $"CharID={characterID}");
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

			if (!TryEnqueueAsyncWork(async () =>
			{
				if (!TryGetDbService(out ICurrencyLedgerService ledgerService))
				{
					return;
				}

				DatabaseResult record = await ledgerService.RecordAsync(characterID, amount, (int)reason, (int)state);
				if (!record.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"Currency ledger: could not record {amount} ({reason}/{state}) for CharID={characterID}. {record.ErrorMessage}");
				}
			}, characterID))
			{
				Log.Warning("GuildSystem", $"Currency ledger: async worker rejected the record for CharID={characterID}.");
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
					Server.NetworkWrapper.Broadcast(conn, new GuildAddBroadcast()
					{
						GuildID = gc.ID,
						CharacterID = characterID,
						RankOrder = gc.RankOrder,
						Location = sceneName,
					}, true, Channel.Reliable);

					// Hand the founder the (empty) notice and message of the day so the panel's
					// info band is populated from the moment the guild exists.
					_ = PublishGuildInfoAsync(newGuildID, characterID);

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
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				// Check guild is not full
				DatabaseResult<int> countResult = await charGuildService.CountAsync(guildID);
				if (!countResult.IsSuccess)
				{
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
				 * the person about to receive a modal has refused contact from the sender. */
				if (TryGetDbService(out ICharacterFriendService friendService))
				{
					DatabaseResult<bool> blockedResult = await friendService.IsBlockedAsync(targetCharacterID, inviterCharacterID);
					if (blockedResult.IsSuccess && blockedResult.Data)
					{
						SendGuildResult(conn, GuildResultType.TargetIsBlocked);
						return;
					}
				}

				// Marshal invite logic back to main thread
				TryEnqueueMainThread(() =>
				{
					if (!Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData))
					{
						return;
					}

					DateTime nowUtc = DateTime.UtcNow;

					/* Per (inviter, target), not per connection. Recorded before the pending slot
					 * is taken so a target who declines instantly still cannot be re-invited
					 * until the cooldown elapses — declining used to free the slot and let the
					 * next invite through on the following frame. */
					if (perTargetInviteCooldownSeconds > 0.0f &&
						!runtimeData.TryBeginInviteCooldown(
							inviterCharacterID,
							targetCharacterID,
							TimeSpan.FromSeconds(perTargetInviteCooldownSeconds),
							nowUtc))
					{
						SendGuildResult(conn, GuildResultType.InviteOnCooldown);
						return;
					}

					PendingGuildInvitation invitation = new PendingGuildInvitation(guildID, inviterCharacterID, nowUtc);

					// if the target doesn't already have a pending invite
					if (runtimeData.TryAddPendingInvitation(targetCharacterID, invitation) &&
						Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData) &&
						characterMappingData.CharactersByID.TryGetValue(targetCharacterID, out IPlayerCharacter targetCharacter) &&
						targetCharacter.TryGet(out IGuildController targetGuildController))
					{
						// validate target
						if (targetGuildController.ID > 0)
						{
							// we should tell the inviter the target is already in a guild
							if (conn != null && conn.IsActive)
							{
								Server.NetworkWrapper.Broadcast(conn, new ChatBroadcast()
								{
									Channel = ChatChannel.Guild,
									SenderID = targetCharacterID,
									Text = ChatHelper.GUILD_ERROR_TARGET_IN_GUILD + " ",
								}, true, Channel.Reliable);
							}
							runtimeData.RemovePendingInvitation(targetCharacterID);
							return;
						}

						Server.NetworkWrapper.Broadcast(targetCharacter.Owner, new GuildInviteBroadcast()
						{
							InviterCharacterID = inviterCharacterID,
							TargetCharacterID = targetCharacter.ID
						}, true, Channel.Reliable);
					}
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
				if (DateTime.UtcNow - invitation.IssuedUtc > TimeSpan.FromSeconds(invitationTtlSeconds))
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
		/// <returns>Asynchronous join task.</returns>
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
		/// </remarks>
		private async Task JoinGuildAsync(NetworkConnection conn, long characterID, long guildID, string sceneName, bool fromInvitation)
		{
			try
			{
				if (!TryGetDbService(out ICharacterGuildService charGuildService) ||
					!TryGetDbService(out IGuildUpdateService guildUpdateService))
				{
					return;
				}

				/* The guild can be disbanded between the invite and the accept — the last member
				 * leaving deletes the row outright. Without this the accept persisted a
				 * membership pointing at a guild that no longer exists, failed its foreign key or
				 * (worse) succeeded against a recycled id, and told the player nothing either
				 * way. Checked before capacity, because a missing guild counts as zero members
				 * and would otherwise sail through the capacity test. */
				if (!TryGetDbService(out IGuildService guildExistsService))
				{
					return;
				}

				DatabaseResult<GuildData?> guildResult = await guildExistsService.FetchAsync(guildID);
				if (!guildResult.IsSuccess || !guildResult.Data.HasValue)
				{
					SendGuildResult(conn, GuildResultType.GuildNotFound);
					if (fromInvitation)
					{
						ClearPendingInvitation(characterID);
					}
					return;
				}

				// Check guild capacity
				DatabaseResult<int> countResult = await charGuildService.CountAsync(guildID);
				if (!countResult.IsSuccess)
				{
					return;
				}
				if (countResult.Data >= maxGuildSize)
				{
					SendGuildResult(conn, GuildResultType.GuildFull);
					return;
				}

				// Persist membership
				/* New members land on the LOWEST rung the guild actually has, not on a constant.
				 * A guild that deleted its bottom rank would otherwise admit people into a rank
				 * with no row, which resolves to no permissions and cannot be promoted out of by
				 * name. */
				byte joinRankOrder = await ResolveLowestRankOrderAsync(guildID);
				CharacterGuildData memberData = new CharacterGuildData(0, 1, characterID, guildID, joinRankOrder, sceneName);
				DatabaseResult saveResult = await charGuildService.PersistAsync(memberData, maxGuildSize);
				if (!saveResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"JoinGuildAsync membership persist failed (CharID={characterID}, GuildID={guildID}): {saveResult.ErrorCode} - {saveResult.ErrorMessage}");
					SendGuildResult(conn, GuildResultType.GuildNotFound);
					return;
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
					Server.NetworkWrapper.Broadcast(conn, new GuildAddBroadcast()
					{
						GuildID = gc.ID,
						CharacterID = characterID,
						RankOrder = joinRankOrder,
						Location = sceneName,
					}, true, Channel.Reliable);

					// The new member has no guild text yet; send it alongside the join rather than
					// making them wait for somebody to edit it.
					_ = PublishGuildInfoAsync(guildID, characterID);

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
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error joining guild (CharID={characterID}, GuildID={guildID}, FromInvitation={fromInvitation}): {ex}");
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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.DeclineInvite, out long guardKey))
			{
				return;
			}

			try
			{
				IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();

				if (character != null && Server.DataContainerRegistry.TryGet(out IGuildSystemRuntimeData runtimeData) && CharacterStateValidation.CanAct(character))
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
					return;
				}

				IReadOnlyList<CharacterGuildData> members = membersResult.Data;
				int remainingCount = members.Count - 1;

				// Find the leaving member's version for optimistic concurrency on delete
				long leavingMemberVersion = 1;
				byte leavingRankOrder = 0;
				foreach (CharacterGuildData member in members)
				{
					if (member.CharacterID == characterID)
					{
						leavingMemberVersion = member.Version + 1;
						leavingRankOrder = member.Rank;
						break;
					}
				}

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
						SendGuildResult(conn, GuildResultType.InsufficientRank);
						return;
					}
				}

				// Remove the guild member
				DatabaseResult deleteResult = await charGuildService.DeleteAsync(characterID, leavingMemberVersion);
				if (!deleteResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"LeaveGuildAsync member delete failed (CharID={characterID}, GuildID={guildID}): {deleteResult.ErrorCode} - {deleteResult.ErrorMessage}");
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

					// Skipped when the guild itself has just been deleted — there is no log left.
					if (remainingCount > 0)
					{
						AppendGuildLog(guildID, GuildLogEventType.Left, characterID);
					}
				});
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
				if (!memberResult.IsSuccess || !memberResult.Data.HasValue)
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
				if (!memberResult.IsSuccess || !memberResult.Data.HasValue)
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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

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
		/// Sent to the members this scene server hosts, resolved through the guild character
		/// tracker. Members elsewhere get their copy from their own scene server, which is running
		/// the same code against the same row.
		/// </remarks>
		private async Task PublishGuildInfoAsync(long guildID, long onlyCharacterID = 0)
		{
			if (!TryGetDbService(out IGuildService guildService))
			{
				return;
			}

			DatabaseResult<GuildData?> guildResult = await guildService.FetchAsync(guildID);
			if (!guildResult.IsSuccess || !guildResult.Data.HasValue)
			{
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

			TryEnqueueMainThread(() =>
			{
				if (Server == null ||
					!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
				{
					return;
				}

				if (onlyCharacterID > 0)
				{
					if (characterMappingData.CharactersByID.TryGetValue(onlyCharacterID, out IPlayerCharacter single) &&
						single?.Owner != null)
					{
						Server.NetworkWrapper.Broadcast(single.Owner, broadcast, true, Channel.Reliable);
					}
					return;
				}

				if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData) ||
					!mappingData.GuildCharacterTracker.TryGetValue(guildID, out HashSet<long> memberIDs))
				{
					return;
				}

				foreach (long memberID in memberIDs)
				{
					if (characterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter member) &&
						member?.Owner != null)
					{
						Server.NetworkWrapper.Broadcast(member.Owner, broadcast, true, Channel.Reliable);
					}
				}
			});
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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

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
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				DatabaseResult<CharacterGuildData?> successorResult = await charGuildService.FetchAsync(successorID);
				if (!successorResult.IsSuccess || !successorResult.Data.HasValue)
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
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				DatabaseResult<CharacterGuildData?> outgoingResult = await charGuildService.FetchAsync(currentLeaderID);
				if (outgoingResult.IsSuccess && outgoingResult.Data.HasValue)
				{
					CharacterGuildData outgoing = outgoingResult.Data.Value;
					DatabaseResult demoteResult = await charGuildService.UpdateRankAsync(currentLeaderID, guildID, demotedRankOrder, outgoing.Version + 1);
					if (!demoteResult.IsSuccess)
					{
						await Log.Warning("GuildSystem", $"TransferGuildLeadershipAsync demote failed (GuildID={guildID}, OutgoingLeader={currentLeaderID}): {demoteResult.ErrorCode} - {demoteResult.ErrorMessage}");
					}
				}

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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

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
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				DatabaseResult<GuildData?> guildResult = await guildService.FetchAsync(guildID);
				if (!guildResult.IsSuccess || !guildResult.Data.HasValue)
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
					return;
				}

				DatabaseResult<int> updateDeleteResult = await guildUpdateService.DeleteAsync(guildID);
				if (!updateDeleteResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"DisbandGuildAsync guild update delete failed (GuildID={guildID}): {updateDeleteResult.ErrorCode} - {updateDeleteResult.ErrorMessage}");
				}

				TryEnqueueMainThread(() =>
				{
					if (Server == null ||
						!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData) ||
						!mappingData.GuildCharacterTracker.TryGetValue(guildID, out HashSet<long> memberIDs) ||
						!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
					{
						return;
					}

					// Copied before iterating: clearing each member mutates the tracker.
					List<long> localMembers = new List<long>(memberIDs);

					foreach (long memberID in localMembers)
					{
						if (!characterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter member) ||
							member == null ||
							!member.TryGet(out IGuildController memberGuildController) ||
							memberGuildController.ID != guildID)
						{
							continue;
						}

						memberGuildController.ID = 0;
						memberGuildController.RankOrder = 0;
						memberGuildController.Permissions = GuildPermissions.None;
						memberGuildController.LeaderRankOrder = 0;

						if (member.Owner != null)
						{
							Server.NetworkWrapper.Broadcast(member.Owner, new GuildLeaveBroadcast(), true, Channel.Reliable);
						}
					}

					mappingData.GuildCharacterTracker.Remove(guildID);
					mappingData.GuildMemberTracker.Remove(guildID);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error disbanding guild (GuildID={guildID}): {ex}");
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

			bool prune = false;
			if (++guildLogAppendsSincePrune >= guildLogPruneInterval)
			{
				guildLogAppendsSincePrune = 0;
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
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

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
						TimeUtcTicks = row.TimeCreated.Ticks,
					};
				}

				GuildLogBroadcast broadcast = new GuildLogBroadcast()
				{
					GuildID = guildID,
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