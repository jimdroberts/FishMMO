using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

			SceneServerAuthenticator loginAuthenticator = FindFirstObjectByType<SceneServerAuthenticator>();
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

			// Scene manager events
			Server.NetworkWrapper.NetworkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;

			// Connection state events
			SubscribeToConnectionEvents();

			// Character events
			IPlayerCharacter.OnTeleport += IPlayerCharacter_OnTeleport;
			ICharacterDamageController.OnKilled += CharacterDamageController_OnKilled;

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
			SceneServerAuthenticator loginAuthenticator = FindFirstObjectByType<SceneServerAuthenticator>();
			if (loginAuthenticator != null)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			}

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<ClientValidatedSceneBroadcast>(OnClientValidatedSceneBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ClientScenesUnloadedBroadcast>(OnClientScenesUnloadedBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<RespawnAtBindPointBroadcast>(OnClientRespawnAtBindPointBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ResurrectAcceptBroadcast>(OnClientResurrectAcceptBroadcastReceived);

			// Scene manager events
			Server.NetworkWrapper.NetworkManager.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;

			// Connection state events
			UnsubscribeFromConnectionEvents();

			// Character events
			IPlayerCharacter.OnTeleport -= IPlayerCharacter_OnTeleport;
			ICharacterDamageController.OnKilled -= CharacterDamageController_OnKilled;

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
			}

			// Bring every lingering body back into the normal save/despawn/release path before
			// the shutdown snapshot below runs, so its state is captured and its claim handed
			// back rather than being left Online until the lease expires.
			FinalizeAllCombatLingers("scene server shutting down");

			// Save all characters and release all sessions before shutdown
			if (Server.Database?.ServiceRegistry != null &&
				Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
			{
				if (Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
				{
					// Capture all session tokens (spawned + waiting-to-load characters)
					var sessionTokens = new Dictionary<long, CharacterSessionInfo>(data.SessionTokens);

					// Snapshot character data on the main thread (Unity API access required),
					// paired with the claim held for each so the shutdown write can prove
					// ownership. A server that lost a claim before shutting down must not use
					// its final flush to overwrite the state of whichever server owns it now.
					var characterDataList = new List<(CharacterData Data, CharacterSessionInfo? Ownership)>();
					foreach (var character in data.CharactersByID.Values)
					{
						try
						{
							CharacterSessionInfo? ownership = sessionTokens.TryGetValue(character.ID, out CharacterSessionInfo held)
								? held
								: (CharacterSessionInfo?)null;
							characterDataList.Add((BuildCharacterData(character), ownership));
						}
						catch (Exception ex)
						{
							Log.Error("CharacterSystem", $"OnDeinitialize: Failed to snapshot character {character.ID}: {ex}");
						}
					}

					// Bounded: an unresponsive database must not hold process exit open forever.
					// Characters already saved before the deadline keep their progress; the token
					// cancels the in-flight write rather than leaving it running unobserved.
					bool flushed = UnitySyncOverAsync.TryRun(async cancellationToken =>
					{
						// Save all spawned characters
						foreach (var entry in characterDataList)
						{
							cancellationToken.ThrowIfCancellationRequested();
							try
							{
								CharacterData charData = entry.Data;
								DatabaseResult result = entry.Ownership.HasValue
									? await characterService.PersistOwnedAsync(
										charData,
										new CharacterSessionLeaseData(charData.ID, entry.Ownership.Value.ServerID, entry.Ownership.Value.Token),
										cancellationToken)
									: await characterService.PersistAsync(charData, cancellationToken);

								if (!result.IsSuccess)
								{
									// Forbidden is not a transient failure: the claim is gone and
									// the snapshot is unpersistable by design. Name it plainly so
									// a shutdown after a lease lapse is not mistaken for data loss
									// caused by the shutdown itself.
									if (result.ErrorCode == DatabaseErrorCodes.Forbidden)
									{
										await Log.Error("CharacterSystem",
											$"OnDeinitialize: character {charData.ID} is no longer claimed by this server; " +
											"its unsaved state is discarded rather than overwriting the current owner's.");
									}
									else
									{
										await Log.Warning("CharacterSystem", $"OnDeinitialize: DB error saving character {charData.ID}: {result.ErrorCode} - {result.ErrorMessage}");
									}
								}
							}
							catch (Exception ex)
							{
								await Log.Error("CharacterSystem", $"OnDeinitialize: Failed to save character {entry.Data.ID}: {ex}");
							}
						}

						// Flush anything still queued for retry before releasing live sessions,
						// so a save or release that the async worker pool dropped earlier is
						// not lost to shutdown as well.
						foreach (var kvp in DrainPendingFlushes())
						{
							cancellationToken.ThrowIfCancellationRequested();
							try
							{
								if (kvp.Value.CharacterData.HasValue)
								{
									await characterService.PersistAsync(kvp.Value.CharacterData.Value, cancellationToken);
								}
								if (kvp.Value.Session.HasValue)
								{
									await ReleaseCharacterSessionAsync(kvp.Key, kvp.Value.Session.Value.ServerID, kvp.Value.Session.Value.Token);
								}
							}
							catch (Exception ex)
							{
								await Log.Error("CharacterSystem", $"OnDeinitialize: Failed to flush pending work for character {kvp.Key}: {ex}");
							}
						}

						// Release all claimed sessions (spawned + waiting-to-load characters)
						foreach (var kvp in sessionTokens)
						{
							cancellationToken.ThrowIfCancellationRequested();
							try
							{
								await ReleaseCharacterSessionAsync(kvp.Key, kvp.Value.ServerID, kvp.Value.Token);
							}
							catch (Exception ex)
							{
								await Log.Error("CharacterSystem", $"OnDeinitialize: Failed to release session for character {kvp.Key}: {ex}");
							}
						}
					}, shutdownFlushTimeoutMs);

					if (!flushed)
					{
						Log.Warning("CharacterSystem", $"OnDeinitialize: character save/session release timed out after {shutdownFlushTimeoutMs}ms; some progress may not have been persisted.");
					}
				}
			}
		}

		#region Async Helpers

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