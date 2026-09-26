using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Implementation.World.SceneServer.Character;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Core character management system handling character spawning, despawning, saving, and lifecycle events for player characters.
	/// </summary>
	[CreateAssetMenu(fileName = "CharacterSystem", menuName = "FishMMO/Server/SceneServer/Character System", order = 1)]
	[RequiresDataContainer(typeof(CharacterMappingData))]
	[RequiresDataContainer(typeof(CharacterSystemRuntimeData))]
	[RequiresDataContainer(typeof(CharacterSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class CharacterSystem : ServerBehaviour, ICharacterSystem<NetworkConnection, Scene>
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max character-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 200;

		/// <summary>
		/// Maximum time the shutdown character save / session release blocks the main thread.
		/// Generous because losing character progress is expensive, but still bounded so an
		/// unresponsive database cannot hold process exit open indefinitely.
		/// </summary>
		private const int shutdownFlushTimeoutMs = 30_000;

		/// <summary>
		/// Interval in seconds between periodic character saves.
		/// </summary>
		[Tooltip("Interval in seconds between periodic character saves")]
		[SerializeField][Min(1f)] private float saveRate = 30.0f;

		/// <summary>
		/// Interval in seconds between out-of-bounds checks for characters.
		/// </summary>
		[Tooltip("Interval in seconds between out-of-bounds checks")]
		[SerializeField][Min(0.1f)] private float outOfBoundsCheckRate = 2.5f;

		/// <summary>
		/// Interval in seconds between batched session-lease refreshes.
		/// </summary>
		/// <remarks>
		/// Must stay comfortably under the database's session lease duration (2 minutes) so a
		/// few missed passes cannot let a live character's claim lapse and be stolen by another
		/// scene server. The refresh is a single statement for the whole population, so a short
		/// interval is cheap regardless of how many characters are resident.
		/// </remarks>
		[Tooltip("Interval in seconds between batched session lease refreshes. Must stay well under the 2 minute lease duration.")]
		[SerializeField][Min(1f)] private float sessionLeaseRefreshRate = 20.0f;

		/// <summary>
		/// Whether a character that disconnects while in combat leaves its body in the world.
		/// </summary>
		/// <remarks>
		/// Disabling this restores the previous behaviour, in which closing the client removed
		/// the character within milliseconds — making Alt+F4 a reliable way to win any losing
		/// fight.
		/// </remarks>
		[Tooltip("Keep a character's body in the world when its owner disconnects during combat, so quitting is not an escape.")]
		[SerializeField] private bool enableCombatLogoutLinger = true;

		/// <summary>
		/// Hard cap in seconds on how long a combat-logout body remains in the world.
		/// </summary>
		/// <remarks>
		/// In the ordinary case the body is removed sooner than this: the linger sweep drops it
		/// as soon as the character leaves combat, which is
		/// <c>CharacterDamageController.CombatDurationTicks</c> (20s by default) after the last
		/// attack. This value only bites when something keeps refreshing that window — someone
		/// still hitting the body — so it is the guarantee that a player cannot be pinned in the
		/// world indefinitely by an attacker who refuses to let them go.
		/// <para>
		/// Reading the combat flag is only safe because
		/// <c>CharacterDamageController.EvaluateCombatTimer</c> re-baselines on a backwards tick;
		/// removing ownership switches the timer's tick domain, and read naively that regression
		/// would clear combat instantly and defeat the whole feature.
		/// </para>
		/// <para>
		/// Set to 0 to keep the feature enabled elsewhere but never actually hold a body.
		/// </para>
		/// </remarks>
		[Tooltip("Maximum seconds a combat-logout body stays in the world. Normally removed sooner — as soon as the character leaves combat, dies, or its owner reconnects.")]
		[SerializeField][Min(0f)] private float combatLogoutLingerSeconds = 60.0f;

		/// <summary>
		/// Triggered before a character is loaded from the database. <conn, CharacterID>
		/// </summary>
		public event Action<NetworkConnection, long> OnBeforeLoadCharacter;
		/// <summary>
		/// Triggered after a character is loaded from the database. <conn, Character>
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnAfterLoadCharacter;
		/// <summary>
		/// Triggered immediately after a character is added to their respective cache.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnConnect;
		/// <summary>
		/// Triggered immediately after a character is removed from their respective cache.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnDisconnect;
		/// <summary>
		/// Triggered immediately after a character is spawned in the scene.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter, Scene> OnSpawnCharacter;
		/// <summary>
		/// Triggered immediately after a character is despawned from the scene.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnDespawnCharacter;
		/// <summary>
		/// Triggered immediately after a pet is killed.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnPetKilled;

		/// <summary>
		/// Initializes the character system, registers event handlers, and sets up character authentication and broadcast handling.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("CharacterSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (ServerManager == null)
			{
				Log.Error("CharacterSystem", "InitializeOnce: ServerManager is null");
				return ServerComponentInitializationStatus.FailedToFindServerManager;
			}

			if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				Log.Error("CharacterSystem", "Failed to initialize: ISceneServerSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.Database?.ServiceRegistry == null)
			{
				Log.Error("CharacterSystem", "Failed to initialize: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out _))
			{
				Log.Error("CharacterSystem", "Failed to initialize: ICharacterService not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			SceneServerAuthenticator loginAuthenticator = FindAnyObjectByType<SceneServerAuthenticator>();
			if (loginAuthenticator == null)
			{
				Log.Error("CharacterSystem", "Failed to initialize: SceneServerAuthenticator not found");
				throw new UnityException("SceneServerAuthenticator not found!");
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterSystemRuntimeData>(out var runtimeData))
			{
				Log.Error("CharacterSystem", "Failed to initialize: ICharacterSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Authentication events
			loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<ClientValidatedSceneBroadcast>(OnClientValidatedSceneBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ClientScenesUnloadedBroadcast>(OnClientScenesUnloadedBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<RespawnAtBindPointBroadcast>(OnClientRespawnAtBindPointBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ResurrectAcceptBroadcast>(OnClientResurrectAcceptBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<RequestLeaveInstanceBroadcast>(OnClientRequestLeaveInstanceBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<RequestInstanceDetailsBroadcast>(OnClientRequestInstanceDetailsBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<InstanceKickBroadcast>(OnClientInstanceKickBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<InstancePrivacyBroadcast>(OnClientInstancePrivacyBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<TargetSelectionBroadcast>(OnClientTargetSelectionBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<DismissBuffBroadcast>(OnClientDismissBuffBroadcastReceived, true);

			// Chat commands. See OnLeaveInstanceCommand for why this exists alongside the
			// RequestLeaveInstanceBroadcast handler.
			ChatHelper.AddCommands(new Dictionary<string, ChatCommand>()
			{
				{ "/leaveinstance", OnLeaveInstanceCommand },
				{ "/exitinstance", OnLeaveInstanceCommand },
				{ "/unstuck", OnUnstuckCommand },
				{ "/stuck", OnUnstuckCommand },
			});
			ChatHelper.SetCommandHelp("/leaveinstance", new ChatCommandHelp()
			{
				Category = "Instance",
				Summary = "Leaves the instance you are in.",
				Aliases = new[] { "/exitinstance" },
			});
			ChatHelper.SetCommandHelp("/unstuck", new ChatCommandHelp()
			{
				Category = "General",
				Summary = "Moves you to a safe spot if you are stuck. Not in combat or instances.",
				Aliases = new[] { "/stuck" },
			});

			// Scene manager events
			Server.NetworkWrapper.NetworkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;

			// Connection state events
			SubscribeToConnectionEvents();

			// Character events
			IPlayerCharacter.OnTeleport += IPlayerCharacter_OnTeleport;
			ICharacterDamageController.OnKilled += CharacterDamageController_OnKilled;
			ICharacterDamageController.OnResurrectOffered += CharacterDamageController_OnResurrectOffered;

			// Periodic callbacks
			saveRate = Mathf.Max(1f, saveRate);
			outOfBoundsCheckRate = Mathf.Max(0.1f, outOfBoundsCheckRate);
			sessionLeaseRefreshRate = Mathf.Max(1f, sessionLeaseRefreshRate);
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(saveRate, OnPeriodicSave);
				periodicSystem.RegisterPeriodicCallback(outOfBoundsCheckRate, OnPeriodicOutOfBoundsCheck);
				periodicSystem.RegisterPeriodicCallback(30f, OnPeriodicRespawnResurrectSweep);
				periodicSystem.RegisterPeriodicCallback(sessionLeaseRefreshRate, OnPeriodicSessionLeaseRefresh);
				periodicSystem.RegisterPeriodicCallback(PendingFlushRetryIntervalSeconds, OnPeriodicPendingFlushRetry);
				periodicSystem.RegisterPeriodicCallback(TransferDisconnectSweepIntervalSeconds, OnPeriodicTransferDisconnectSweep);
				periodicSystem.RegisterPeriodicCallback(SceneLoadTimeoutSweepIntervalSeconds, OnPeriodicSceneLoadTimeoutSweep);
				periodicSystem.RegisterPeriodicCallback(CombatLingerSweepIntervalSeconds, OnPeriodicCombatLingerSweep);
				periodicSystem.RegisterPeriodicCallback(CharacterResidencySweepIntervalSeconds, OnPeriodicCharacterResidencySweep);
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			runtimeData.EndSave();

			Log.Debug("CharacterSystem", $"Initialized (SaveRate={saveRate}s, OutOfBoundsCheckRate={outOfBoundsCheckRate}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the character system, unregisters event handlers, and saves all characters to the database before shutdown.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("CharacterSystem", "OnDeinitialize: Server is null");
				return;
			}

			if (ServerManager == null)
			{
				Log.Error("CharacterSystem", "OnDeinitialize: ServerManager is null");
				return;
			}

			// Authentication events
			SceneServerAuthenticator loginAuthenticator = FindAnyObjectByType<SceneServerAuthenticator>();
			if (loginAuthenticator != null)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			}

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<ClientValidatedSceneBroadcast>(OnClientValidatedSceneBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ClientScenesUnloadedBroadcast>(OnClientScenesUnloadedBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<RespawnAtBindPointBroadcast>(OnClientRespawnAtBindPointBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ResurrectAcceptBroadcast>(OnClientResurrectAcceptBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<RequestLeaveInstanceBroadcast>(OnClientRequestLeaveInstanceBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<RequestInstanceDetailsBroadcast>(OnClientRequestInstanceDetailsBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<InstanceKickBroadcast>(OnClientInstanceKickBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<InstancePrivacyBroadcast>(OnClientInstancePrivacyBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<TargetSelectionBroadcast>(OnClientTargetSelectionBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<DismissBuffBroadcast>(OnClientDismissBuffBroadcastReceived);

			// Scene manager events
			Server.NetworkWrapper.NetworkManager.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;

			// Connection state events
			UnsubscribeFromConnectionEvents();

			// Character events
			IPlayerCharacter.OnTeleport -= IPlayerCharacter_OnTeleport;
			ICharacterDamageController.OnKilled -= CharacterDamageController_OnKilled;
			ICharacterDamageController.OnResurrectOffered -= CharacterDamageController_OnResurrectOffered;

			// Static registry: a command left behind outlives this ScriptableObject and would
			// run against a destroyed instance. See ChatHelper.RemoveCommands.
			ChatHelper.RemoveCommands(new[] { "/leaveinstance", "/exitinstance" });
			ChatHelper.RemoveCommands(UnstuckCommandWords);
			nextUnstuckAt.Clear();

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicSave);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicOutOfBoundsCheck);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicRespawnResurrectSweep);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicSessionLeaseRefresh);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicPendingFlushRetry);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicTransferDisconnectSweep);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicSceneLoadTimeoutSweep);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicCombatLingerSweep);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicCharacterResidencySweep);
			}

			/* Drop every per-connection watchdog map.
			 *
			 * This behaviour is a ScriptableObject, so its fields survive a play-session
			 * restart in the editor while FishNet starts handing out ClientIds from zero again.
			 * A stale entry whose id is reissued to a fresh connection is then read as that
			 * connection's state — and for a deadline map, one that expired long ago, so the
			 * next sweep disconnects a client that has done nothing wrong. Clearing here is what
			 * keeps these maps scoped to the run that populated them.
			 */
			characterResidencyDeadlines.Clear();
			sceneLoadDeadlines.Clear();
			startScenesAckedClientIds.Clear();
			pendingTransferDisconnects.Clear();
			suppressCombatLingerClientIds.Clear();
			authCallbackLastTimeByAccount.Clear();
			sceneUnloadLastTimeByClientId.Clear();
			validatedSceneLastTimeByClientId.Clear();

			/* Every claim this server holds, connected characters' and lingering bodies' alike. Each
			 * one is either written and then released by the shutdown flush, or released as it is if
			 * there is nothing of it to write — never both, and never before its writes. */
			Dictionary<long, CharacterSessionInfo> heldClaims = null;
			if (Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> claimData))
			{
				heldClaims = new Dictionary<long, CharacterSessionInfo>(claimData.SessionTokens);
			}

			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService) ||
				!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				// Nothing can be written or released without the database. The lingers still end,
				// through their ordinary path, so no bookkeeping outlives this run.
				FinalizeAllCombatLingers("scene server shutting down");
				return;
			}

			var sessionTokens = heldClaims ?? new Dictionary<long, CharacterSessionInfo>(data.SessionTokens);
			Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem);
			// For the lane barrier before each release; resolved here, on the main thread.
			Server.DataContainerRegistry.TryGet(out IAsyncWorkerData asyncWorker);

			/* Snapshot on the main thread (Unity API access required) everything a logout would
			 * write, the way a logout captures it, paired with the claim held for each so every write
			 * can prove ownership. A server that lost a claim before shutting down must not use its
			 * final flush to overwrite the state of whichever server owns it now.
			 *
			 * The connection events were unsubscribed above, so this is the ONLY save a connected
			 * character gets on a graceful shutdown — and it used to carry its own hand-picked list of
			 * three tables. SubEntitySnapshot is the one list of sub-entity tables for exactly that
			 * reason, so it is used here too, and the item flush is captured as SaveAndDespawnCharacter
			 * captures it. The row first: the pet rows share the version BuildCharacterData stamps. */
			var entries = new List<ShutdownFlushEntry>(data.CharactersByID.Count + LingeringCharacterCount);
			var captured = new HashSet<long>();
			foreach (var character in data.CharactersByID.Values)
			{
				CharacterSessionInfo? ownership = sessionTokens.TryGetValue(character.ID, out CharacterSessionInfo held)
					? held
					: (CharacterSessionInfo?)null;
				if (TryCaptureShutdownEntry(character, ownership, inventorySystem, entries))
				{
					captured.Add(character.ID);
				}
			}

			/* Lingering bodies go into the same flush, not through FinalizeCombatLinger. That path
			 * hands each body's save and release to the async worker pool, which teardown drains on a
			 * bounded budget and may abandon — and its release used to be raced by this method's own
			 * release of the same token, handing the claim back before the body's state was written. */
			CaptureLingeringBodiesForShutdown(sessionTokens, inventorySystem, entries, captured);

			/* Anything the retry queue still holds: a save, an item flush or a release the pool
			 * dropped or the database refused earlier. Same steps, same order. */
			foreach (var kvp in DrainPendingFlushes())
			{
				PendingCharacterFlush pending = kvp.Value;
				lock (pending.Gate)
				{
					entries.Add(new ShutdownFlushEntry
					{
						CharacterID = kvp.Key,
						Row = pending.CharacterData,
						Session = pending.Session,
						ItemFlush = pending.ItemFlush,
						SubEntities = pending.SubEntities,
					});
				}
			}

			// Claims with nothing captured to write: characters still waiting for their scene to
			// load, and any whose capture threw. Released as they are.
			var releaseOnly = new List<CharacterSessionLeaseData>();
			foreach (var kvp in sessionTokens)
			{
				if (!captured.Contains(kvp.Key))
				{
					releaseOnly.Add(new CharacterSessionLeaseData(kvp.Key, kvp.Value.ServerID, kvp.Value.Token));
				}
			}

			// Bounded: an unresponsive database must not hold process exit open forever.
			// Characters already saved and released before the deadline keep their progress; the
			// token stops starting new work rather than leaving it running unobserved.
			bool flushed;
			try
			{
				flushed = UnitySyncOverAsync.TryRun(
					cancellationToken => FlushForShutdownAsync(characterService, entries, releaseOnly, asyncWorker, cancellationToken),
					shutdownFlushTimeoutMs);
			}
			catch (Exception ex)
			{
				Log.Error("CharacterSystem", $"OnDeinitialize: the shutdown flush failed: {ex}");
				flushed = false;
			}

			if (!flushed)
			{
				Log.Warning("CharacterSystem", $"OnDeinitialize: character save/session release timed out after {shutdownFlushTimeoutMs}ms; " +
					"characters not yet flushed keep their claims until the lease expires, and their progress since the last save is lost.");
			}
		}

		#region Shutdown Flush

		/// <summary>Characters whose items are flushed and claims released at once during shutdown.</summary>
		/// <remarks>
		/// Each lane holds one database connection at a time, and the async worker may still be
		/// using up to its own cap, so this stays well under the connection pool.
		/// </remarks>
		private const int ShutdownFlushParallelism = 16;

		/// <summary>
		/// Everything the shutdown flush writes and hands back for one character, captured on the
		/// main thread.
		/// </summary>
		private sealed class ShutdownFlushEntry
		{
			/// <summary>The character.</summary>
			public long CharacterID;
			/// <summary>Its row, or null when there is none to write (a pending release).</summary>
			public CharacterData? Row;
			/// <summary>The claim that authorises the writes and is released after them, or null.</summary>
			public CharacterSessionInfo? Session;
			/// <summary>Its sub-entity rows, or null.</summary>
			public SubEntitySnapshot SubEntities;
			/// <summary>Its item flush, or null.</summary>
			public Func<Task<ItemWriteOutcome>> ItemFlush;
			/// <summary>
			/// Set when the row write reports the claim gone: nothing more of this character is
			/// written or released. Its sub-entity writes would be refused by their own ownership
			/// gate; leaving them out saves the round trip and the refusal noise.
			/// </summary>
			public bool Unowned;
		}

		/// <summary>
		/// Captures one resident character for the shutdown flush. Main thread only.
		/// </summary>
		/// <returns>False when the capture threw; the character's claim is then released unwritten.</returns>
		private bool TryCaptureShutdownEntry(
			IPlayerCharacter character,
			CharacterSessionInfo? ownership,
			ICharacterInventorySystem inventorySystem,
			List<ShutdownFlushEntry> entries)
		{
			try
			{
				CharacterData row = BuildCharacterData(character);
				var subEntities = new SubEntitySnapshot();
				AppendDepartureSubEntities(character, subEntities, ownership);
				Func<Task<ItemWriteOutcome>> itemFlush = inventorySystem?.CaptureDespawnFlush(character, ownership);

				entries.Add(new ShutdownFlushEntry
				{
					CharacterID = character.ID,
					Row = row,
					Session = ownership,
					SubEntities = subEntities,
					ItemFlush = itemFlush,
				});
				return true;
			}
			catch (Exception ex)
			{
				Log.Error("CharacterSystem", $"OnDeinitialize: Failed to snapshot character {character?.ID}: {ex}");
				return false;
			}
		}

		/// <summary>
		/// Writes every captured character and hands every claim back, in the order that keeps a
		/// claim from being released before the writes it covers have finished.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why the order changed.</b> This used to walk the characters one at a time — the row,
		/// then the items, then each sub-entity table, with retry delays — and only after the whole
		/// walk, and the pending-flush walk after it, release any session. At 15–20 round trips a
		/// character a few hundred residents outlasted the shutdown budget, which is eight seconds
		/// for the whole teardown, not the thirty this method asks for; and when the budget ran out
		/// no release had happened at all. Every character stayed Online for the two-minute lease and
		/// was refused by every scene server after the restart.
		/// </para>
		/// <list type="number">
		///   <item><description>
		///     Claims with nothing to write are released first, in one statement: nothing has to
		///     precede them.
		///   </description></item>
		///   <item><description>
		///     Every row in one statement per <see cref="CharacterRowsPerBatch"/> characters. A row
		///     refused because the claim is gone marks its character: nothing more of it is written
		///     or released.
		///   </description></item>
		///   <item><description>
		///     Every sub-entity table once, for every character together.
		///   </description></item>
		///   <item><description>
		///     Then, in parallel lanes, each character's item flush — retried while it is worth
		///     retrying — and, straight after it, that character's release.
		///   </description></item>
		/// </list>
		/// <para>
		/// <b>Why no release can race a save.</b> A character's release is issued only after its
		/// row (step 2), its sub-entity rows (step 3) and its item flush (step 4) have each been
		/// awaited to completion, so everything the next owner will read is written — or has
		/// definitively failed — before the claim it reads under exists. When the deadline stops the
		/// flush, the characters not yet reached keep their claims: another server cannot load them
		/// until the lease expires, so a write of ours still in flight cannot land behind a load.
		/// </para>
		/// <para>
		/// <b>A flush that fails every attempt is still followed by its release, here only.</b> At
		/// runtime the claim is kept so the retry queue can deliver the flush later (see
		/// <see cref="SaveAndReleaseCharacterAsync"/>). At shutdown there is no later: the process is
		/// exiting and nothing will run the flush again. Holding the claim would only keep the player
		/// out for the rest of the lease and then load the very same rows, so the loss is logged and
		/// the claim returned.
		/// </para>
		/// </remarks>
		private async Task FlushForShutdownAsync(
			ICharacterService characterService,
			List<ShutdownFlushEntry> entries,
			List<CharacterSessionLeaseData> releaseOnly,
			IAsyncWorkerData asyncWorker,
			CancellationToken cancellationToken)
		{
			// 1. Claims with nothing to write.
			if (releaseOnly.Count > 0)
			{
				try
				{
					DatabaseResult<IReadOnlyList<long>> released = await characterService.ReleaseManyAsync(releaseOnly, cancellationToken);
					if (!released.IsSuccess)
					{
						await Log.Warning("CharacterSystem",
							$"OnDeinitialize: releasing {releaseOnly.Count} unwritten claim(s) failed: {released.ErrorCode} - {released.ErrorMessage}. They free themselves when their leases expire.");
					}
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					await Log.Error("CharacterSystem", $"OnDeinitialize: releasing {releaseOnly.Count} unwritten claim(s) failed: {ex}");
				}
			}

			// 2. Every row, batched. Per-row outcomes; a failed batch fails only its own rows.
			var unowned = new HashSet<long>();
			var rows = new List<CharacterPersistRequest>(entries.Count);
			foreach (ShutdownFlushEntry entry in entries)
			{
				if (entry.Row.HasValue)
				{
					rows.Add(ToPersistRequest(entry.Row.Value, entry.Session));
				}
			}
			for (int offset = 0; offset < rows.Count; offset += CharacterRowsPerBatch)
			{
				cancellationToken.ThrowIfCancellationRequested();
				int count = Math.Min(CharacterRowsPerBatch, rows.Count - offset);
				try
				{
					DatabaseResult<IReadOnlyList<CharacterPersistResult>> result =
						await characterService.PersistManyAsync(rows.GetRange(offset, count), cancellationToken);
					if (!result.IsSuccess)
					{
						await Log.Warning("CharacterSystem", $"OnDeinitialize: a batch of {count} character rows failed: {result.ErrorCode} - {result.ErrorMessage}");
						continue;
					}
					foreach (CharacterPersistResult outcome in result.Data)
					{
						if (outcome.Outcome == CharacterPersistOutcome.OwnershipLost && unowned.Add(outcome.CharacterID))
						{
							await Log.Error("CharacterSystem",
								$"OnDeinitialize: character {outcome.CharacterID} is no longer claimed by this server; " +
								"its unsaved state is discarded rather than overwriting the current owner's.");
						}
					}
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					await Log.Error("CharacterSystem", $"OnDeinitialize: a batch of {count} character rows failed: {ex}");
				}
			}

			// 3. Every sub-entity table once, for every character that still holds its claim.
			var subEntities = new SubEntitySnapshot(entries.Count);
			foreach (ShutdownFlushEntry entry in entries)
			{
				if (unowned.Contains(entry.CharacterID))
				{
					entry.Unowned = true;
					continue;
				}
				subEntities.AddFrom(entry.SubEntities);
			}
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				await SaveSubEntitiesSequentiallyAsync(subEntities, 0);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// Each table has already reported its own failure; a throw here must not stop the
				// items and releases below, which do not depend on it.
				await Log.Error("CharacterSystem", $"OnDeinitialize: the sub-entity flush failed: {ex}");
			}

			// 4. Each character's items, then its release.
			cancellationToken.ThrowIfCancellationRequested();
			int flushFailures = 0;
			int releases = 0;
			using (var lanes = new SemaphoreSlim(ShutdownFlushParallelism, ShutdownFlushParallelism))
			{
				var work = new List<Task>(entries.Count);
				foreach (ShutdownFlushEntry entry in entries)
				{
					work.Add(FlushAndReleaseForShutdownAsync(entry, lanes, asyncWorker, cancellationToken,
						() => Interlocked.Increment(ref flushFailures),
						() => Interlocked.Increment(ref releases)));
				}
				await Task.WhenAll(work);
			}

			await Log.Debug("CharacterSystem",
				$"OnDeinitialize: flushed {entries.Count} character(s); {releases} claim(s) released after their writes, " +
				$"{releaseOnly.Count} released unwritten, {unowned.Count} no longer ours, {flushFailures} item flush(es) lost.");
		}

		/// <summary>
		/// Step 4 of <see cref="FlushForShutdownAsync"/> for one character: its item flush, then its
		/// release. Never throws.
		/// </summary>
		/// <remarks>
		/// <b>The release waits for the character's lane too</b> (<see cref="DrainCharacterLaneAsync"/>).
		/// Everything other systems queued for this character before the shutdown — a quest update or
		/// turn-in, a grant, a forget, a pet dismissal, a merchant's currency row — sits on its ordered
		/// lane of the async worker and quotes the claim released here; each is ownership-gated, so
		/// one still queued when the claim is handed back is refused, not merely late. At runtime the
		/// save-and-release and the retry queue run on that lane and so after them; this flush runs
		/// off it, so it waits for it instead — briefly, because a lane that does not drain must not
		/// cost the player the two-minute lease.
		/// </remarks>
		private async Task FlushAndReleaseForShutdownAsync(
			ShutdownFlushEntry entry,
			SemaphoreSlim lanes,
			IAsyncWorkerData asyncWorker,
			CancellationToken cancellationToken,
			Action onFlushLost,
			Action onReleased)
		{
			if (entry.Unowned)
			{
				return;
			}

			try
			{
				await lanes.WaitAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				// Out of time before this character's turn: it keeps its claim, so nothing of ours
				// can land behind another server's load of it.
				return;
			}

			try
			{
				if (entry.ItemFlush != null)
				{
					ItemWriteOutcome outcome = await RunItemFlushWithRetryAsync(entry.ItemFlush, entry.CharacterID, cancellationToken);
					if (outcome == ItemWriteOutcome.NotOwned)
					{
						return;
					}
					if (outcome == ItemWriteOutcome.Retry || outcome == ItemWriteOutcome.Rejected)
					{
						onFlushLost();
						await Log.Error("CharacterSystem",
							$"OnDeinitialize: the item flush for character {entry.CharacterID} did not land ({outcome}); " +
							"its item changes since the last snapshot are lost.");
					}
				}

				if (entry.Session.HasValue &&
					!await DrainCharacterLaneAsync(asyncWorker, entry.CharacterID, ShutdownLaneDrainTimeoutMs, cancellationToken))
				{
					await Log.Warning("CharacterSystem",
						$"OnDeinitialize: character {entry.CharacterID}'s queued writes did not drain within {ShutdownLaneDrainTimeoutMs}ms; " +
						"releasing its claim anyway, and anything still queued for it will be refused by the ownership gate.");
				}

				if (entry.Session.HasValue &&
					await ReleaseCharacterSessionAsync(entry.CharacterID, entry.Session.Value.ServerID, entry.Session.Value.Token))
				{
					onReleased();
				}
			}
			catch (OperationCanceledException)
			{
				// The deadline fell between two flush attempts: the claim is kept, as above.
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"OnDeinitialize: Failed to flush character {entry.CharacterID}: {ex}");
			}
			finally
			{
				lanes.Release();
			}
		}

		/// <summary>
		/// How long a shutdown release waits for the character's queued writes before it goes ahead.
		/// </summary>
		/// <remarks>
		/// A queued write is a round trip or two, so a healthy lane drains in milliseconds. One that
		/// does not is stuck — typically on a hop to the main thread, which is blocked in this very
		/// flush — and waiting for it out of the whole budget would keep the claim, and the player,
		/// out for the lease's two minutes after the restart for the sake of a write that may never
		/// run.
		/// </remarks>
		private const int ShutdownLaneDrainTimeoutMs = 1500;

		/// <summary>
		/// Completes once every item already queued on a character's ordered lane of the async worker
		/// has run.
		/// </summary>
		/// <remarks>
		/// A marker is queued on the lane and awaited: lanes run their items one at a time in the
		/// order they were queued, so the marker runs only after everything ahead of it. Admitted even
		/// over the backpressure threshold (<see cref="IAsyncWorkerData.EnqueueRequired"/>), since a
		/// refusal here would only mean releasing without waiting.
		/// </remarks>
		/// <param name="asyncWorker">The worker, or null.</param>
		/// <param name="characterID">The character whose lane to wait for.</param>
		/// <param name="timeoutMs">How long to wait before giving up.</param>
		/// <param name="cancellationToken">The flush's deadline. Cancellation propagates.</param>
		/// <returns>False when the worker is not running or the wait timed out.</returns>
		/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
		private static async Task<bool> DrainCharacterLaneAsync(IAsyncWorkerData asyncWorker, long characterID, int timeoutMs, CancellationToken cancellationToken)
		{
			if (asyncWorker == null || characterID <= 0)
			{
				return false;
			}

			var reached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			AsyncWorkAdmission admission = asyncWorker.EnqueueRequired(() =>
			{
				reached.TrySetResult(true);
				return Task.CompletedTask;
			}, characterID, nameof(DrainCharacterLaneAsync));
			if (admission == AsyncWorkAdmission.Refused)
			{
				return false;
			}

			using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
			{
				timeout.CancelAfter(timeoutMs);
				using (timeout.Token.Register(() => reached.TrySetCanceled()))
				{
					try
					{
						return await reached.Task.ConfigureAwait(false);
					}
					catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
					{
						return false;
					}
				}
			}
		}

		#endregion

		#region Async Helpers

		/// <summary>
		/// Raises a character lifecycle event, invoking each subscriber independently.
		/// </summary>
		/// <remarks>
		/// Every one of these events is raised part-way through a teardown, with the save, the
		/// session release or the despawn still to come on the line after it. A plain
		/// <c>?.Invoke</c> walks the invocation list until one subscriber throws and then
		/// abandons both the rest of the list and the caller — so one exception in a social
		/// system left a character removed from every mapping but never saved and never
		/// released, which is the stranded-claim failure the rest of this system works hardest
		/// to avoid, or left its NetworkObject spawned in the world with no owner.
		/// <para>
		/// Losing one subscriber's bookkeeping is recoverable. Losing the teardown is not, so
		/// the exception is logged and the teardown continues. <c>AddressableLoadBatch</c>
		/// dispatches its own events this way for the same reason.
		/// </para>
		/// </remarks>
		/// <param name="handler">The event's backing delegate, or null when nothing subscribes.</param>
		/// <param name="conn">Connection to report, which is null for an unattended body.</param>
		/// <param name="character">Character the event concerns.</param>
		/// <param name="eventName">Event name, for diagnostics.</param>
		private static void DispatchCharacterEvent(
			Action<NetworkConnection, IPlayerCharacter> handler,
			NetworkConnection conn,
			IPlayerCharacter character,
			string eventName)
		{
			if (handler == null)
			{
				return;
			}

			Delegate[] subscribers = handler.GetInvocationList();
			for (int i = 0; i < subscribers.Length; ++i)
			{
				try
				{
					((Action<NetworkConnection, IPlayerCharacter>)subscribers[i]).Invoke(conn, character);
				}
				catch (Exception ex)
				{
					Log.Error("CharacterSystem", $"{eventName} handler threw; continuing teardown. {ex}");
				}
			}
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue<ICharacterSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll: false);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main Unity thread.
		/// </summary>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<ICharacterSystemMainThreadQueueData>(action);
		}

		#endregion

		/// <summary>
		/// Enqueues an async work item to the centralized async worker for controlled execution.
		/// </summary>
		/// <param name="work">The async work delegate to enqueue.</param>
		/// <param name="entityKey">Optional entity key for consistent worker routing.</param>
		/// <param name="callerName">Caller member name used for diagnostics.</param>
		/// <returns><c>true</c> if the work item was enqueued; otherwise, <c>false</c>.</returns>
		private bool EnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
					return asyncWorker.Enqueue(work, entityKey, callerName);
				else
					return asyncWorker.Enqueue(work, callerName);
			}

			return false;
		}
	}
}