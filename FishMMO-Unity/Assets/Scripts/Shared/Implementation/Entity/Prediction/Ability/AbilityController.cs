using FishNet.Managing.Timing;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls the activation, management, and synchronization of abilities for a character, including known abilities, events, and network state.
	/// Handles ability casting, queuing, cooldowns, and client/server synchronization.
	/// </summary>
	public partial class AbilityController : CharacterBehaviour, IAbilityController, IPredictableController
	{
		/// <summary>
		/// Static RNG for generating player ability seeds (server-side).
		/// All FishNet callbacks (WritePayload, ResetState, Replicate, etc.)
		/// run on Unity's main thread, so no lock is needed.
		/// </summary>
		/// <summary>
		/// Static RNG for generating per-character ability seeds (server-side).
		/// Called from <see cref="EnsureAbilitySeedGenerator"/> on the authoritative server.
		/// </summary>
		private static DeterministicRNG playerSeedGenerator = new DeterministicRNG();

		/// <summary>
		/// Constant representing no active ability.
		/// </summary>
		public const long NO_ABILITY = 0;

		/// <summary>
		/// The ID of the currently activating ability, or NO_ABILITY if none.
		/// Typed as long to match <see cref="Ability.ID"/> (database-generated).
		/// </summary>
		private long currentAbilityID;

		/// <summary>
		/// Bit field tracking raw local input accumulated between ticks.
		/// Written by Activate(), ActivateConsumable(), Interrupt(), Release().
		/// Snapshotted into replicate data by HandleCharacterInput(), then one-shot
		/// flags are cleared. Reconcile does NOT touch this field, so inputs
		/// queued between ticks are never stomped.
		/// </summary>
		private int localInputFlags;

		/// <summary>
		/// Bit field tracking replicated/reconciled activation state.
		/// Set from replicate data inside Replicate() and restored from server
		/// state inside Reconcile(). All Replicate-phase logic (TryStartAbility,
		/// ProcessActiveAbility, etc.) reads and writes this field.
		/// </summary>
		private int replicatedFlags;

		/// <summary>
		/// Server-side flag set when TryStartAbility or TryStartConsumable fails
		/// despite the input having a queued ability. Included in the next reconcile
		/// via <see cref="AbilityActivationFlags.Denied"/> so clients can fire
		/// <see cref="OnAbilityDenied"/> authoritatively instead of heuristically.
		/// Invariant: a replicate tick chooses exactly one activation path — either
		/// ability or consumable — so one boolean denial signal cannot drop a second
		/// same-tick denial under the current input contract.
		/// Cleared after each <see cref="OnCreateReconcile"/>.
		/// </summary>
		private bool wasDenied;

		/// <summary>Server: a Denied or NoSpawn report that still rides the reconciles of ticks without input.</summary>
		private bool stickyDenied;
		private bool stickyNoSpawn;
		private int stickyBlankTicks;
		/// <summary>Server: the last tick ran no input (default data).</summary>
		private bool serverTickWasBlank;

		/// <summary>How many ticks without input a one-shot report keeps riding: the same second the reconcile stamp holds the input's tick.</summary>
		private const int StickyReportBlankTicks = 30;

		/// <summary>Cached longest range among <c>KnownAbilities</c>; see <see cref="LongestKnownAbilityRange"/>.</summary>
		private float longestKnownAbilityRange;

		/// <summary>True when <see cref="longestKnownAbilityRange"/> needs recomputing.</summary>
		private bool longestKnownAbilityRangeDirty = true;

		/// <summary>
		/// The longest range among this character's known abilities — how far it can reach, and so
		/// how far out its observer needs tick-exact transforms for lag compensation to be honest.
		/// </summary>
		/// <remarks>
		/// Recomputed only when the known set changes, because it is read per observer per LOD
		/// evaluation. Note <c>Ability.Range</c> is <c>Speed * LifeTime</c> and no ability template
		/// currently authors a Speed, so this is 0 for all present content and
		/// <c>ObserverStreamingPolicy.EngagementRange</c>'s floor is what applies.
		/// </remarks>
		public float LongestKnownAbilityRange
		{
			get
			{
				if (longestKnownAbilityRangeDirty)
				{
					longestKnownAbilityRange = 0f;
					if (KnownAbilities != null)
					{
						foreach (Ability ability in KnownAbilities.Values)
						{
							if (ability != null && ability.Range > longestKnownAbilityRange)
							{
								longestKnownAbilityRange = ability.Range;
							}
						}
					}
					longestKnownAbilityRangeDirty = false;
				}
				return longestKnownAbilityRange;
			}
		}

		/// <summary>
		/// The replicate tick on which the server completed an activation that produced no ability
		/// object, so the reconcile can tell the owner to take back the object it spawned for that
		/// tick. <see cref="TimeManager.UNSET_TICK"/> when there is nothing to report.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A tick rather than a bool, because the pairing it relied on is a project setting.</b>
		/// It was a plain flag set during the replicate and consumed by the next
		/// <see cref="OnCreateReconcile"/>, which is exact only while the server runs exactly one
		/// replicate per tick. FishNet's <c>Replicate_NonAuthoritative</c> has a catch-up path that
		/// runs a SECOND replicate body in the same tick (<c>consumeExcess &amp;&amp; count &gt;
		/// leaveInBuffer</c>), and then two replicates share one reconcile — so a flag raised at tick
		/// T would be stamped onto the reconcile for T+1, and
		/// <see cref="ShouldDestroySpawnsAtReconcileTick"/> would destroy an owner-predicted object
		/// the server really did spawn. That is the one case its truth table calls unrecoverable,
		/// because the replay starts at T+1 and never re-creates it.
		/// </para>
		/// <para>
		/// That path is disabled today: every shipped scene sets
		/// <c>PredictionManager._dropExcessiveReplicates: 1</c> and a dedicated server is not
		/// client-only, so <c>consumeExcess</c> is false. Carrying the tick means the guarantee no
		/// longer depends on an inspector checkbox nobody knew was load-bearing — see
		/// <see cref="ShouldFlagNoSpawn"/> for what happens when the ticks disagree.
		/// </para>
		/// </remarks>
		internal uint serverSpawnedNothingTick = TimeManager.UNSET_TICK;

		/// <summary>
		/// The server tick the running activation was ANNOUNCED on, or <see cref="TimeManager.UNSET_TICK"/>
		/// when nothing is running.
		/// </summary>
		/// <remarks>
		/// Written only by <c>BroadcastCastState</c>, which is the single funnel every start and
		/// every ending passes through, and read by two things: the spawn payload, which carries it
		/// so an observer arriving mid-cast can start the row partway rather than not at all, and
		/// <c>SuppressesRedundantCastStop</c>, which uses it to recognise an activation that began
		/// and ended on one tick. Server-side; a client's copy is whatever its own payload said.
		/// </remarks>
		private uint castStartServerTick = TimeManager.UNSET_TICK;

		/// <summary>
		/// The ID of the next ability to activate after the current one, or NO_ABILITY if none.
		/// </summary>
		private long queuedAbilityID;

		/// <summary>
		/// Remaining activation ticks for the current ability (deterministic, avoids float drift).
		/// </summary>
		private uint remainingTicks;

		/// <summary>
		/// Number of ticks the charged ability has been held beyond completion.
		/// Reset on cancel or release. Enforces max hold duration.
		/// </summary>
		private uint chargedHoldTicks;

		/// <summary>
		/// The inventory slot of the consumable being activated, or -1 if none.
		/// Used to lock the slot during activation and for reconciliation.
		/// </summary>
		private int consumableSlot = -1;

		/// <summary>
		/// Consumable uses this owner predicted, by the tick they started on (template id). A reconcile replay of that tick
		/// must start the use again even though the predicted use already took the item's charge or removed the item (the
		/// reconcile restores resources, not the inventory), or the replay never reaches the finish that re-applies the
		/// effect. Owner-side only; pruned as ticks fall out of any replay window.
		/// </summary>
		private readonly Dictionary<uint, int> predictedConsumableStarts = new Dictionary<uint, int>();

		/// <summary>
		/// RNG for ability-specific randomization (server-side).
		/// </summary>
		private DeterministicRNG abilitySeedGenerator;

		/// <summary>
		/// The seed used to initialize the ability RNG.
		/// </summary>
		private int abilitySeed = 0;

		/// <summary>
		/// The current seed value for ability RNG.
		/// </summary>
		private int currentSeed = 0;

		/// <summary>
		/// Reusable list for reading ability event IDs from network payloads.
		/// Hoisted from ReadPayload to avoid per-call allocation.
		/// </summary>
		private readonly List<int> readPayloadAbilityEvents = new List<int>();

		/// <summary>
		/// What this client predicted, per replicate tick, so <see cref="OnReconcile"/> can compare
		/// the server's state for tick T against the client's state for tick T rather than against
		/// the client's live state several ticks later. See <see cref="PredictedAbilityStateHistory"/>.
		/// </summary>
		private readonly PredictedAbilityStateHistory predictedStateHistory = new PredictedAbilityStateHistory();

		/// <summary>
		/// Cached <see cref="EventData"/> for <see cref="Ability.MeetsActivationConditions"/>.
		/// Avoids allocating a new instance every tick when no ability is active
		/// (CanActivate is called every tick from Replicate).
		/// </summary>
		private EventData cachedCheckEventData;

		/// <summary>
		/// Cached <see cref="IBuffController"/> resolved once in <see cref="OnStartNetwork"/>.
		/// Avoids per-tick <see cref="ICharacter.TryGet{T}"/> lookups.
		/// </summary>
		private IBuffController cachedBuffController;
		/// <summary>
		/// Cached <see cref="ICooldownController"/> resolved once in <see cref="OnStartNetwork"/>.
		/// </summary>
		private ICooldownController cachedCooldownController;
		/// <summary>
		/// Cached <see cref="ICharacterDamageController"/> resolved once in <see cref="OnStartNetwork"/>.
		/// </summary>
		private ICharacterDamageController cachedDamageController;
		/// <summary>
		/// Cached <see cref="ICharacterAttributeController"/> resolved once in <see cref="OnStartNetwork"/>.
		/// </summary>
		private ICharacterAttributeController cachedAttributeController;
		/// <summary>
		/// Cached <see cref="IInventoryController"/> resolved once in <see cref="OnStartNetwork"/>.
		/// </summary>
		private IInventoryController cachedInventoryController;
		/// <summary>
		/// Cached <see cref="ITargetController"/> resolved once in <see cref="OnStartNetwork"/>.
		/// </summary>
		private ITargetController cachedTargetController;
		/// <summary>
		/// Cached <see cref="ICharacterAnimationController"/> resolved once in <see cref="OnStartNetwork"/>.
		/// </summary>
		private ICharacterAnimationController cachedAnimationController;

		/// <summary>
		/// Transform used as the spawn point for ability objects (e.g., projectiles).
		/// </summary>
		public Transform AbilitySpawner;

		/// <summary>
		/// Attribute template for attack speed reduction (physical abilities).
		/// </summary>
		public CharacterAttributeTemplate AttackSpeedReductionTemplate;

		/// <summary>
		/// Attribute template for cast speed reduction (magical abilities).
		/// </summary>
		public CharacterAttributeTemplate CastSpeedReductionTemplate;

		/// <summary>
		/// Attribute template for cooldown reduction.
		/// </summary>
		public CharacterAttributeTemplate CooldownReductionTemplate;

		/// <summary>
		/// Maximum tick difference for observer held-state prediction.
		/// At high RTT (e.g., 200ms at 30 tick/s = 6+ ticks of buffered data),
		/// a window of 1 causes channeled beams/charges to visually stutter
		/// on observers. Widen to match expected RTT in ticks.
		/// </summary>
		public uint ObserverPredictionWindowTicks = 1;

		/// <summary>
		/// Ability event template for converting blood resource (e.g., health for mana).
		/// </summary>
		public AbilityEvent BloodResourceConversionTemplate;

		/// <summary>
		/// Ability event template for charged abilities.
		/// </summary>
		public AbilityEvent ChargedTemplate;

		/// <summary>
		/// Ability event template for channeled abilities.
		/// </summary>
		public AbilityEvent ChanneledTemplate;

		/// <summary>
		/// Backing list for <see cref="OnCanManipulate"/> to avoid
		/// <see cref="System.Delegate.GetInvocationList"/> allocation per call.
		/// </summary>
		private readonly List<Func<bool>> canManipulateHandlers = new List<Func<bool>>();

		/// <summary>
		/// Event invoked to check if the character can manipulate abilities (e.g., not stunned).
		/// Backed by a list to avoid per-call delegate array allocation.
		/// </summary>
		/// <remarks>
		/// Handlers are iterated in REVERSE so that a handler that unsubscribes itself
		/// during its <c>Invoke()</c> does not cause the next entry to be skipped (forward
		/// iteration would shift the indices). Adding a new handler during iteration is
		/// still unsupported and will produce an extra invoke on the inserted handler.
		/// </remarks>
		public event Func<bool> OnCanManipulate
		{
			add { if (value != null) canManipulateHandlers.Add(value); }
			remove { canManipulateHandlers.Remove(value); }
		}

		/// <summary>
		/// Event for ability UI updates (e.g., cast bar, telegraphs).
		/// </summary>
		public event Action<string, float, float> OnUpdate;

		/// <summary>
		/// Event invoked when the current ability is interrupted.
		/// </summary>
		public event Action OnInterrupt;

		/// <summary>
		/// Event invoked when the current ability is cancelled.
		/// </summary>
		public event Action OnCancel;

		/// <summary>
		/// Fired when predicted ability objects are destroyed due to state mismatch.
		/// Provides the reconcile tick used for rollback so clients can trigger correction VFX/UI.
		/// For small timing mismatches (1-2 ticks), the ability is replayed automatically
		/// and no visual correction is needed. For larger mismatches, subscribers may
		/// want to hide/fade correction artifacts.
		/// </summary>
		public event Action<uint> OnPredictionMismatch;

		/// <summary>
		/// Fired when the server denies an ability the client already started predicting.
		/// The ability ID is the one the client predicted but the server rejected.
		/// Subscribers should show an "interrupted" flash, restore predicted resource costs, etc.
		/// </summary>
		public event Action<long> OnAbilityDenied;

		/// <summary>
		/// Event invoked to reset the ability UI.
		/// </summary>
		public event Action OnReset;

		/// <summary>
		/// True if an ability is currently being activated.
		/// </summary>
		public bool IsActivating { get { return currentAbilityID != NO_ABILITY; } }

		/// <summary>
		/// True if an ability is queued to activate after the current one.
		/// </summary>
		public bool AbilityQueued { get { return queuedAbilityID != NO_ABILITY; } }

		/// <summary>
		/// The remaining activation time for the current ability, or 0 if no ability is active.
		/// Converts internal tick count back to seconds for UI and NPC AI consumers.
		/// </summary>
		public float RemainingActivationTime => remainingTicks * (float)(base.TimeManager?.TickDelta ?? 0.0);

		/// <summary>
		/// Unity Awake callback. Initialises all known-ability and event collections.
		/// </summary>
		public override void OnAwake()
		{
			base.OnAwake();

			KnownAbilities = new SortedDictionary<long, Ability>();
			KnownBaseAbilities = new HashSet<int>();
			KnownAbilityEvents = new HashSet<int>();
			KnownAbilityOnTickEvents = new HashSet<int>();
			KnownAbilityOnHitEvents = new HashSet<int>();
			KnownAbilityOnPreSpawnEvents = new HashSet<int>();
			KnownAbilityOnSpawnEvents = new HashSet<int>();
			KnownAbilityOnDestroyEvents = new HashSet<int>();
			templateToAbilityID = new Dictionary<int, long>();
		}

		/// <summary>
		/// FishNet OnStartNetwork callback. Resolves component caches and initialises the
		/// ability seed generator.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			predictionController = GetComponent<CharacterPredictionController>();

			// Resolve component caches — these never change during gameplay.
			Character.TryGet(out cachedBuffController);
			Character.TryGet(out cachedCooldownController);
			Character.TryGet(out cachedDamageController);
			Character.TryGet(out cachedAttributeController);
			Character.TryGet(out cachedInventoryController);
			Character.TryGet(out cachedTargetController);
			Character.TryGet(out cachedAnimationController);

			/* Register the shared activation handler the first time any character starts on this
			 * client. Never unregistered, for the same reason as the resource handler: ClientManager
			 * does not clear handlers on stop, so a per-character unregister would have to be
			 * reference counted or the first despawn would switch off ability visuals for every
			 * remaining character. */
			if (base.IsClientStarted)
			{
				RegisterActivationBroadcast(base.NetworkManager);
			}

			// Eagerly initialize the deterministic RNG so the first WritePayload,
			// OnCreateReconcile, and ResetState paths all observe the same seed.
			// Without this, lazy initialization races could produce a one-tick seed mismatch.
			EnsureAbilitySeedGenerator();
		}

		/// <summary>
		/// FishNet OnStopNetwork callback. Releases component caches and clears events.
		/// </summary>
		public override void OnStopNetwork()
		{
			base.OnStopNetwork();

			// Release cached component references.
			cachedBuffController = null;
			cachedCooldownController = null;
			cachedDamageController = null;
			cachedAttributeController = null;
			cachedInventoryController = null;
			cachedTargetController = null;
			cachedAnimationController = null;

			OnUpdate = null;
			OnInterrupt = null;
			OnCancel = null;
			OnReset = null;
			OnPredictionMismatch = null;
			OnAbilityDenied = null;
			OnConsumableUsed = null;
			OnConsumableItemChanged = null;
			canManipulateHandlers.Clear();
		}

		/// <summary>
		/// Maps an <see cref="AbilityType"/> to the appropriate animation trigger on
		/// <see cref="ICharacterAnimationController"/>.
		/// Called from TryStartAbility on the first authoritative tick.
		/// </summary>
		private void TriggerAbilityAnimation(AbilityType abilityType)
		{
			if (cachedAnimationController == null) return;
			ActOut(cachedAnimationController, abilityType);
		}

		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			queuedAbilityID = NO_ABILITY;
			localInputFlags = 0;
			replicatedFlags = 0;
			// Clear the server-side denial sentinel so a respawn/possession does not
			// carry over a stale Denied bit into the next reconcile.
			stickyDenied = false;
			stickyNoSpawn = false;
			stickyBlankTicks = 0;
			serverTickWasBlank = false;
			wasDenied = false;
			serverSpawnedNothingTick = TimeManager.UNSET_TICK;
			Cancel();
			/* After Cancel, which is what would otherwise leave the tick of the activation this
			 * reset just ended behind for the next occupant of a pooled object. */
			castStartServerTick = TimeManager.UNSET_TICK;

			// Ensure no stale slot locks remain after state reset.
			consumableSlot = -1;
			predictedConsumableStarts.Clear();

			// Reset observer prediction sentinel.
			// Dispose is a no-op on CharacterReconcileData (struct, no unmanaged resources),
			// but called for IReconcileData contract compliance. If Dispose ever gains
			// a real implementation, this call ensures cleanup.
			lastCreatedData.Dispose();
			hasLastCreatedData = false;
			predictedStateHistory.Clear();

			// Detach all spawned ability objects before clearing references.
			// This allows in-flight projectiles to persist visually using their snapshots
			// instead of being immediately destroyed when the character disconnects.
			foreach (Ability ability in KnownAbilities.Values)
			{
				ability.DetachAllAbilityObjects();
			}

			/* Observer-learned abilities live in KnownAbilities too, so the loop above already
			 * detached their objects — there is no second set to sweep. */
			KnownAbilities.Clear();
			longestKnownAbilityRangeDirty = true;
			KnownBaseAbilities.Clear();
			KnownAbilityEvents.Clear();
			KnownAbilityOnTickEvents.Clear();
			KnownAbilityOnHitEvents.Clear();
			KnownAbilityOnPreSpawnEvents.Clear();
			KnownAbilityOnSpawnEvents.Clear();
			KnownAbilityOnDestroyEvents.Clear();
			templateToAbilityID?.Clear();
			pendingInFlightObjects.Clear();
			pendingHitClaims.Clear();
			recentlyEndedObjects.Clear();
			blankTicksInCast = 0;
			replicatedLaunch = default;

			// Force regeneration of the ability seed generator on every reset.
			// Without this, a non-null generator from a previous session (e.g.,
			// scene transfer) would skip re-initialization, leaving currentSeed
			// stale — the next WritePayload would create a new seed but currentSeed
			// would diverge from what the client receives via ReadPayload,
			// causing a one-tick seed mismatch and spurious reconcile.
			abilitySeedGenerator = null;
			EnsureAbilitySeedGenerator();
		}

		[Header("ECA - Abilities")]
		[Tooltip("Triggers invoked on the server when an ability activation begins.")]
		[SerializeField]
		private List<Trigger> onAbilityActivateTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked on the server when an ability activation completes.")]
		[SerializeField]
		private List<Trigger> onAbilityCompleteTriggers = new List<Trigger>();

		/// <inheritdoc/>
		public List<Trigger> OnAbilityActivateTriggers => onAbilityActivateTriggers;
		/// <inheritdoc/>
		public List<Trigger> OnAbilityCompleteTriggers => onAbilityCompleteTriggers;

		/// <inheritdoc/>
		public int Order => 100;

		/// <summary>
		/// Cached <see cref="CharacterPredictionController"/> used to resolve which peer writes
		/// this character's replicate input. Server-driven AI characters (monsters and pets)
		/// answer "the server"; player characters answer "the owning client".
		/// </summary>
		private CharacterPredictionController predictionController;

		/// <summary>
		/// True when this peer produces this character's ability input for the current tick.
		/// Falls back to <see cref="NetworkBehaviour.IsOwner"/> when no prediction controller is
		/// present, which preserves the original player-only behaviour.
		/// </summary>
		private bool HasInputAuthority => predictionController != null
			? predictionController.HasInputAuthority
			: base.IsOwner;

		/// <inheritdoc/>
		public void PopulateInput(ref CharacterReplicateData input)
		{
			AbilityActivationReplicateData abilityInput = HandleCharacterInput();
			input.ActivationFlags = abilityInput.ActivationFlags;
			input.QueuedAbilityID = abilityInput.QueuedAbilityID;

			PopulateAiAim(ref input);
			PopulateConvergence(ref input, abilityInput);
			PopulateHitClaim(ref input);
		}

		/// <summary>
		/// Owner: sends the launch line (the crosshair's own line, as three small numbers) on a tick a projectile may spawn.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Only then, because it means something on perhaps one tick in a hundred. The owner knows which: a cast it is
		/// starting this tick (an instant one finishes on the same tick), a cast whose countdown has a tick or less to
		/// run, and every tick of one that is held (a channel spawns each tick, a charge releases when the button does).
		/// And only for an ability that is launched toward the aim from the spawner; nothing else reads it.
		/// </para>
		/// <para>
		/// Guessing wrong costs accuracy, never agreement: a spawn on a tick without a launch line flies from the eye
		/// along the aim on the server and the owner alike (AimConvergence.LaunchLine).
		/// </para>
		/// </remarks>
		private void PopulateConvergence(ref CharacterReplicateData input, AbilityActivationReplicateData abilityInput)
		{
			if (PlayerCharacter == null || !base.IsOwner || base.IsServerStarted)
			{
				return;
			}
			KCCPlayer kccPlayer = PlayerCharacter.KCCPlayer;
			if (kccPlayer == null || !kccPlayer.OwnerLaunch.IsGiven)
			{
				return;
			}

			/* The cast that may LAUNCH on this tick decides, and that is the one in progress when there is one: an id
			 * queued behind it (only reachable when a correction re-installs a cast between the key press and the tick)
			 * starts nothing while the cast runs, and letting its template decide left the finishing cast without its
			 * line. */
			long abilityID = NO_ABILITY;
			if (IsActivating)
			{
				if (!replicatedFlags.IsFlagged(AbilityActivationFlags.IsConsumable) &&
					(remainingTicks <= 1u || replicatedFlags.IsFlagged(AbilityActivationFlags.IsHeld)))
				{
					abilityID = currentAbilityID;
				}
			}
			else if (abilityInput.QueuedAbilityID != NO_ABILITY &&
				!abilityInput.ActivationFlags.IsFlagged(AbilityActivationFlags.IsConsumable))
			{
				abilityID = abilityInput.QueuedAbilityID;
			}

			if (abilityID == NO_ABILITY ||
				!KnownAbilities.TryGetValue(abilityID, out Ability ability) ||
				ability.Template == null ||
				ability.Template.AbilitySpawnTarget != AbilitySpawnTarget.SpawnerWithCameraRotation)
			{
				return;
			}

			AimConvergence.AimLaunch launch = kccPlayer.OwnerLaunch;
			input.ConvergenceDistance = launch.Distance;
			input.AimOriginRight = launch.Right;
			input.AimOriginUp = launch.Up;
		}

		/// <summary>The launch line replicated for the tick being simulated; not given for none.</summary>
		private AimConvergence.AimLaunch replicatedLaunch;

		#region Hit claims
		/// <summary>
		/// False to stop sending and honouring hit claims (see <see cref="CharacterReplicateData.HitClaimVictim"/>): the
		/// server's own lag-compensated sweep is then the only judge of a hit, as it was before claims existed.
		/// </summary>
		public static bool HitClaims = true;

		/// <summary>Claims the server accepted since it started: hits its own sweep had not made.</summary>
		public static long HitClaimsGranted;

		/// <summary>Claims the server refused since it started, for any reason other than already agreeing.</summary>
		public static long HitClaimsRefused;

		/// <summary>A hit the owner's copy of one of its objects made, waiting for an input to carry it.</summary>
		private struct PendingHitClaim
		{
			public uint SpawnTick;
			public byte ObjectID;
			public int VictimObjectID;
			/// <summary>The ability whose object the claim names; 0 when it was not known.</summary>
			public long AbilityID;
		}

		/// <summary>Claims waiting to be sent, oldest first. One goes with each input.</summary>
		private readonly Queue<PendingHitClaim> pendingHitClaims = new Queue<PendingHitClaim>();

		/// <summary>The most claims kept waiting; a burst beyond it (an area object over a crowd) drops its oldest.</summary>
		private const int MaximumPendingHitClaims = 8;

		/// <summary>Owner: a copy of one of this character's ability objects hit <paramref name="victimObjectID"/>.</summary>
		internal void QueueHitClaim(PredictionTick spawnTick, byte objectID, int victimObjectID, long abilityID = 0)
		{
			if (!HitClaims || !base.IsOwner || base.IsServerStarted)
			{
				return;
			}
			while (pendingHitClaims.Count >= MaximumPendingHitClaims)
			{
				pendingHitClaims.Dequeue();
			}
			pendingHitClaims.Enqueue(new PendingHitClaim { SpawnTick = spawnTick.Value, ObjectID = objectID, VictimObjectID = victimObjectID, AbilityID = abilityID });
		}

		/// <summary>
		/// Owner: forgets the waiting claims of objects a correction has just taken back (spawned after
		/// <paramref name="tick"/>, or on it with <paramref name="includeTick"/>). The server never had those objects, so
		/// their claims could only be refused, and counted as refusals that were nothing of the kind.
		/// </summary>
		private void DropPendingHitClaims(uint tick, bool includeTick, long abilityID = 0, bool onlyTick = false, uint upToTick = 0u, bool bounded = false)
		{
			int count = pendingHitClaims.Count;
			for (int i = 0; i < count; ++i)
			{
				PendingHitClaim claim = pendingHitClaims.Dequeue();
				int after = unchecked((int)(claim.SpawnTick - tick));
				bool inScope = onlyTick ? after == 0 : (after > 0 || (includeTick && after == 0));
				if (inScope && bounded && unchecked((int)(claim.SpawnTick - upToTick)) > 0)
				{
					inScope = false;
				}
				// Only the ability whose objects were taken back, when the caller says which: another's claims are good.
				if (inScope && abilityID != 0 && claim.AbilityID != 0 && claim.AbilityID != abilityID)
				{
					inScope = false;
				}
				if (inScope)
				{
					continue;
				}
				pendingHitClaims.Enqueue(claim);
			}
		}

		/// <summary>Owner: attaches the oldest waiting claim to this tick's input.</summary>
		private void PopulateHitClaim(ref CharacterReplicateData input)
		{
			while (pendingHitClaims.Count > 0)
			{
				PendingHitClaim claim = pendingHitClaims.Dequeue();
				// The input is stamped with this tick (NetworkBehaviour.Replicate_Authoritative), which the age counts from.
				uint age = unchecked(base.TimeManager.LocalTick - claim.SpawnTick);
				if (age == 0u || age > ushort.MaxValue || claim.VictimObjectID < 0 || claim.VictimObjectID == int.MaxValue)
				{
					continue;
				}
				input.HitClaimVictim = claim.VictimObjectID + 1;
				input.HitClaimSpawnAge = (ushort)age;
				input.HitClaimObject = claim.ObjectID;
				return;
			}
		}

		/// <summary>
		/// Server: checks the claim an input carries and applies the hit if this character's object could have made it
		/// (AbilityObject.TryApplyClaimedHit).
		/// </summary>
		private void ApplyHitClaim(ref CharacterReplicateData input)
		{
			if (!HitClaims || !base.IsServerStarted || input.HitClaimVictim <= 0 || PlayerCharacter == null ||
				base.NetworkManager == null)
			{
				return;
			}

			uint spawnTick = unchecked(input.GetTick() - input.HitClaimSpawnAge);
			AbilityObject claimed = FindAbilityObject(spawnTick, input.HitClaimObject);
			// Not flying any more: one that ended a tick or two ago on scenery, or by running out, still answers.
			AbilityObject ended = claimed == null ? FindEndedObject(spawnTick, input.HitClaimObject) : null;
			if (claimed == null && ended == null)
			{
				// It ended on this very victim: the server made the hit itself. Otherwise gone, or it ended on somebody else.
				if (EndedOnThisVictim(spawnTick, input.HitClaimObject, input.HitClaimVictim - 1))
				{
					HitClaimsAgreed++;
				}
				else
				{
					HitClaimsRefused++;
				}
				return;
			}

			ICharacter victim = null;
			if (base.NetworkManager.ServerManager.Objects.Spawned.TryGetValue(input.HitClaimVictim - 1, out FishNet.Object.NetworkObject victimNob) &&
				victimNob != null)
			{
				victimNob.TryGetComponent(out victim);
			}
			if (victim == null || ReferenceEquals(victim, Character))
			{
				HitClaimsRefused++;
				return;
			}

			if (ended != null)
			{
				if (ended.TryApplyEndedClaim(victim, input.GetTick()))
				{
					HitClaimsGranted++;
				}
				else
				{
					HitClaimsRefused++;
				}
				return;
			}

			if (claimed.HasHit(victim))
			{
				// The server's own sweep made this hit already: the two agree, which is the usual case.
				HitClaimsAgreed++;
				return;
			}
			if (claimed.TryApplyClaimedHit(victim, input.GetTick()))
			{
				HitClaimsGranted++;
			}
			else
			{
				HitClaimsRefused++;
			}
		}

		/// <summary>Server: this character's objects that ended within the claim grace (AbilityObject.DestroyAbilityObjectInternal).</summary>
		private readonly List<AbilityObject> recentlyEndedObjects = new List<AbilityObject>();

		/// <summary>True while any of this character's abilities has an object in the world.</summary>
		internal bool HasLiveObjects
		{
			get
			{
				if (KnownAbilities == null)
				{
					return false;
				}
				foreach (Ability ability in KnownAbilities.Values)
				{
					if (ability?.Objects != null && ability.Objects.Count > 0)
					{
						return true;
					}
				}
				return false;
			}
		}

		/// <summary>Claims for a hit the server's own sweep had made already, since the server started.</summary>
		public static long HitClaimsAgreed;

		private struct EndedOnCharacter
		{
			public uint SpawnTick;
			public int ObjectID;
			public uint EndedTick;
			public int VictimA;
			public int VictimB;
			public int VictimC;
		}

		/// <summary>Server: recent objects of this character that ended on a character, and on whom (up to three).</summary>
		private readonly EndedOnCharacter[] endedOnCharacters = new EndedOnCharacter[8];
		private int endedOnCharacterNext;

		/// <summary>Server: an object of this character spent its last hit on a character; its claims are agreement, not refusals.</summary>
		internal void NoteEndedOnCharacter(uint spawnTick, int objectID, HashSet<GameObject> victims)
		{
			EndedOnCharacter entry = new EndedOnCharacter
			{
				SpawnTick = spawnTick,
				ObjectID = objectID,
				EndedTick = base.TimeManager != null ? base.TimeManager.LocalTick : 0u,
				VictimA = -1,
				VictimB = -1,
				VictimC = -1,
			};
			int filled = 0;
			foreach (GameObject victim in victims)
			{
				FishNet.Object.NetworkObject victimNob = victim != null ? victim.GetComponent<FishNet.Object.NetworkObject>() : null;
				if (victimNob == null)
				{
					continue;
				}
				if (filled == 0) entry.VictimA = victimNob.ObjectId;
				else if (filled == 1) entry.VictimB = victimNob.ObjectId;
				else entry.VictimC = victimNob.ObjectId;
				if (++filled >= 3)
				{
					break;
				}
			}
			endedOnCharacters[endedOnCharacterNext] = entry;
			endedOnCharacterNext = (endedOnCharacterNext + 1) % endedOnCharacters.Length;
		}

		/// <summary>True when the object named ended on <paramref name="victimObjectID"/> within the claim window.</summary>
		private bool EndedOnThisVictim(uint spawnTick, int objectID, int victimObjectID)
		{
			uint now = base.TimeManager != null ? base.TimeManager.LocalTick : 0u;
			for (int i = 0; i < endedOnCharacters.Length; ++i)
			{
				EndedOnCharacter entry = endedOnCharacters[i];
				if (entry.SpawnTick == spawnTick && entry.ObjectID == objectID && unchecked(now - entry.EndedTick) <= AbilityObject.ClaimStepWindow + 2u &&
					(entry.VictimA == victimObjectID || entry.VictimB == victimObjectID || entry.VictimC == victimObjectID))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Server: <paramref name="abilityObject"/> has ended but still answers a hit claim for a few ticks.</summary>
		internal void NoteEndedObject(AbilityObject abilityObject)
		{
			PruneEndedObjects();
			recentlyEndedObjects.Add(abilityObject);
		}

		/// <summary>Drops ended objects that Unity has destroyed, that have answered, or whose grace has run out.</summary>
		private void PruneEndedObjects()
		{
			uint now = base.TimeManager != null ? base.TimeManager.LocalTick : 0u;
			for (int i = recentlyEndedObjects.Count - 1; i >= 0; --i)
			{
				AbilityObject ended = recentlyEndedObjects[i];
				if (ended == null || !ended.AnswersClaimsAfterEnding ||
					unchecked(now - ended.EndedOnTick) > AbilityObject.ClaimStepWindow + 2u)
				{
					recentlyEndedObjects.RemoveAt(i);
				}
			}
		}

		/// <summary>This character's object from the cast of <paramref name="spawnTick"/> that ended within the claim grace, or null.</summary>
		private AbilityObject FindEndedObject(uint spawnTick, int objectID)
		{
			PruneEndedObjects();
			for (int i = 0; i < recentlyEndedObjects.Count; ++i)
			{
				AbilityObject ended = recentlyEndedObjects[i];
				if (ended.SpawnTick.Value == spawnTick && ended.ID == objectID)
				{
					return ended;
				}
			}
			return null;
		}

		/// <summary>This character's live ability object spawned by the cast of <paramref name="spawnTick"/>, by its index in the cast.</summary>
		private AbilityObject FindAbilityObject(uint spawnTick, int objectID)
		{
			foreach (Ability ability in KnownAbilities.Values)
			{
				if (ability == null || ability.Objects == null)
				{
					continue;
				}
				foreach (Dictionary<int, AbilityObject> container in ability.Objects.Values)
				{
					if (container != null &&
						container.TryGetValue(objectID, out AbilityObject abilityObject) &&
						abilityObject != null && !abilityObject.IsDestroyed &&
						abilityObject.SpawnTick.Value == spawnTick)
					{
						return abilityObject;
					}
				}
			}
			return null;
		}
		#endregion

		/// <summary>
		/// Writes an AI character's aim into the replicate stream.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Player characters get their aim from <c>KCCPlayer.PopulateInput</c> (Order 80, ahead of
		/// this controller's 100), so this only fills the gap for AI characters, which have no
		/// KCCPlayer at all — their movement is a server-side NavMeshAgent replicated by a
		/// NetworkTransform.
		/// </para>
		/// <para>
		/// Without this, nothing ever wrote those fields for an NPC and the aim a client used came
		/// from the AI controller — which never runs off the server. Every observing client
		/// therefore resolved an NPC's aim from a default-initialised controller and spawned its
		/// ability objects at the world origin pointing down +Z, while the server span them
		/// correctly. Replicating the aim is what lets a client reproduce the shot the server took.
		/// </para>
		/// </remarks>
		/// <param name="input">Replicate input being assembled for this tick.</param>
		private void PopulateAiAim(ref CharacterReplicateData input)
		{
			if (!HasInputAuthority || PlayerCharacter != null || Character == null)
			{
				return;
			}
			if (!Character.TryGet(out INPCBrain ai))
			{
				return;
			}

			/* Only the direction is replicated. The origin is derived from the motor on every
			 * peer -- see CharacterAimOrigin -- so an NPC and a player resolve it the same way. */
			// Quantised on the way in, for the same reason the player path quantises — see
			// AimDirectionCompression.
			input.AimDirection = AimDirectionCompression.Quantize(ai.AimRotation * Vector3.forward);
		}

		/// <summary>
		/// Aim origin replicated for the tick currently being simulated.
		/// </summary>
		private Vector3 replicatedAimOrigin;

		/// <summary>
		/// Aim direction replicated for the tick currently being simulated.
		/// </summary>
		/// <remarks>
		/// Cached from the replicate input rather than read back off the live controller when an
		/// ability spawns. Every peer — owner, server and observer — then traces from the value the
		/// wire actually carried for that tick, which is the whole point of a deterministic ability
		/// simulation. Reading the live controller instead meant the owner used its exact local
		/// camera while everyone else used the decoded one, and on an NPC it meant reading a
		/// controller that does not run on clients at all.
		/// </remarks>
		private Vector3 replicatedAimDirection = Vector3.forward;

		/// <summary>
		/// Handles local character input for ability activation, building the replicate data for the current tick.
		/// Only the owning client produces actual input data; non-owners return default.
		/// </summary>
		/// <returns>The replicate data representing the current input state.</returns>
		private AbilityActivationReplicateData HandleCharacterInput()
		{
			if (Character == null)
			{
				return default;
			}

			// AI characters are driven by the server-side brain, which calls Activate() /
			// Release() / Interrupt() directly. Without this the queued ability was never
			// drained into the replicate stream: AbilityQueued latched true forever and every
			// attacking state stopped the agent and waited on a cast that could never start.
			if (!HasInputAuthority)
			{
				return default;
			}

			// Copy current local input flags and add the IsActualData marker.
			int activationFlags = localInputFlags;
			activationFlags.EnableBit(AbilityActivationFlags.IsActualData);

			// Clear one-shot flags from localInputFlags after capturing them.
			// IsHeld and IsMount are persistent state flags cleared by Cancel().
			localInputFlags.DisableBit(AbilityActivationFlags.Interrupt);
			/* IsConsumable says what KIND of id this input queues, and nothing reads it on an input that queues none. It
			 * leaves with the id. It used to stay set until this peer ran a Cancel, which it never does when the SERVER ends
			 * or refuses the use (a stun, a death, an inventory disagreement: the owner learns by correction) or when the
			 * item is pressed while dead: every ability pressed afterwards went out marked as a consumable, matched no item
			 * on either peer, and silently did nothing until the Cancel key or the next successful potion. */
			if (queuedAbilityID != NO_ABILITY)
			{
				localInputFlags.DisableBit(AbilityActivationFlags.IsConsumable);
			}

			AbilityActivationReplicateData activationEventData = new AbilityActivationReplicateData(activationFlags,
																									queuedAbilityID);
			queuedAbilityID = NO_ABILITY;

			return activationEventData;
		}

		/// <summary>
		/// The last created replicate data from a Ticked replicate state on an observer.
		/// Used to predict held-state continuity for non-owner clients.
		/// </summary>
		private AbilityActivationReplicateData lastCreatedData;

		/// <summary>
		/// True once we have received at least one Ticked replicate on an observer.
		/// Prevents using the default-initialized lastCreatedData (tick 0) which would
		/// produce incorrect tickDiff values for the first several observer ticks.
		/// </summary>
		private bool hasLastCreatedData;

		/// <inheritdoc/>
		public void OnReplicate(ref CharacterReplicateData input, ReplicateState state, Channel channel)
		{
			ReplicateInternal(ref input, state);

			/* Record what this tick's simulation left behind, on every pass including replays —
			 * a replay re-simulates the tick and its result is what the next reconcile for that
			 * tick should be compared with.
			 *
			 * Owner only, matching the reconcile: the server has nothing to compare against, and a
			 * non-owner's reconcile path no longer reads this history at all (see OnReconcile), so
			 * filling it would be per-tick work whose only possible effect is a wrong correction
			 * if the gate is ever lost. */
			if (!base.IsServerStarted && base.IsOwner)
			{
				predictedStateHistory.Record(input.GetTick(), currentSeed, currentAbilityID);
			}
		}

		/// <summary>Consecutive ticks the server has run without input while this character was casting.</summary>
		private int blankTicksInCast;

		/// <summary>Ticks, since the server started, on which a cast waited because its caster's input had not arrived.</summary>
		public static long BlankCastTicks;

		/// <summary>Casts, since the server started, cancelled because input stayed missing past <see cref="MaximumBlankTicksInCast"/>.</summary>
		public static long BlankCastCancels;

		/// <summary>
		/// How many ticks in a row a cast waits for input the server does not have before the server cancels it: half a
		/// second at 30 Hz.
		/// </summary>
		/// <remarks>
		/// Long enough for any stall a playable connection produces (the input queue and the adaptive timing absorb far
		/// less than this), short enough that withholding input cannot bank a finished cast to release later. The owner
		/// learns of the cancel from the reconcile, like any other.
		/// </remarks>
		internal const int MaximumBlankTicksInCast = 15;

		/// <summary>
		/// Ticks without input a cast waits out before it is cancelled: <see cref="MaximumBlankTicksInCast"/>, plus the queue's
		/// own start delay, which runs default data after an underrun with the input already waiting and so is not the owner's.
		/// </summary>
		internal int BlankTicksAllowedInCast => MaximumBlankTicksInCast + (base.PredictionManager != null ? base.PredictionManager.StateInterpolation : 0);

		/// <summary>True where a tick without input freezes the ability state machine: the server, for anything it does not drive itself.</summary>
		internal static bool FreezesOnBlankTick(bool isServerStarted) => isServerStarted;

		/// <summary>
		/// The eye this tick's shots are fired from: a player's eye at the start of the tick (KCCPlayer.TickAimOrigin, when
		/// it was recorded for this very tick), otherwise the eye as it stands.
		/// </summary>
		private Vector3 ResolveTickAimOrigin(uint tick)
		{
			KCCPlayer kccPlayer = PlayerCharacter != null ? PlayerCharacter.KCCPlayer : null;
			if (kccPlayer != null && kccPlayer.TickAimOriginTick != KCCPlayer.NoTickAimOrigin && kccPlayer.TickAimOriginTick == tick)
			{
				return kccPlayer.TickAimOrigin;
			}
			return CharacterAimOrigin.Resolve(Character);
		}

		/// <summary>
		/// The body of <see cref="OnReplicate"/>; split out so every early return still lands in
		/// the per-tick history record.
		/// </summary>
		private void ReplicateInternal(ref CharacterReplicateData input, ReplicateState state)
		{
			// Convert to the internal type — all ability subsystem methods use AbilityActivationReplicateData.
			AbilityActivationReplicateData activationData = new AbilityActivationReplicateData(
				input.ActivationFlags, input.QueuedAbilityID);
			activationData.SetTick(input.GetTick());

			/* Aim for THIS tick. The direction is replicated; the origin is DERIVED, because a
			 * client-supplied origin was never validated against the caster's own position and so
			 * chose the point the server raycast from. See CharacterAimOrigin.
			 *
			 * For a player, the eye at the START of the tick, recorded by KCCPlayer (Order 80) before
			 * it moved the motor: the owner converged the aim on the crosshair's point from that eye
			 * (AimConvergence), so it is the only origin from which the shot reaches that point. The
			 * eye after the move, read here, put the shot on a parallel line one tick of movement away.
			 * An NPC has no KCCPlayer and keeps the eye as it stands. */
			replicatedAimOrigin = ResolveTickAimOrigin(input.GetTick());
			replicatedLaunch = new AimConvergence.AimLaunch(input.ConvergenceDistance, input.AimOriginRight, input.AimOriginUp);
			replicatedAimDirection = input.AimDirection.sqrMagnitude > 1e-12f
				? input.AimDirection
				// The QUANTISED fallback: KCCPlayer.PopulateInput writes that vector for a missing
				// aim, so substituting the raw one here would have this peer simulate a direction
				// slightly different from the one the owner committed to. See the remarks on it.
				: AimDirectionCompression.QuantizedFallbackDirection;

			HandlePrediction(ref activationData, state);

			bool hasActualData = activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsActualData);

			/* A tick the server ran with NO input (its queue for this player was empty: FishNet hands the body a blank
			 * one) does nothing to a cast. The countdown is counted in the owner's inputs, because the owner predicts it
			 * that way: N inputs after the one that started the cast, it finishes, with the aim of that input. A blank
			 * tick used to count as well, so every tick the queue ran dry during a cast finished it one input EARLIER on
			 * the server than the owner predicted. The owner was then corrected to "already finished" and never spawned
			 * its own projectile (a replayed tick does not spawn, and the server's activation message goes to everyone
			 * but the owner), and a countdown that ran out ON the blank tick fired with the blank input's aim, which is
			 * none: due north. Movement already waits out a blank tick (KCCPlayer.OnReplicate); now the cast does too,
			 * and finishes on the same input on both peers however late that input is.
			 *
			 * An input that never comes does not hold a cast for ever: see MaximumBlankTicksInCast. */
			if (!hasActualData && FreezesOnBlankTick(base.IsServerStarted))
			{
				serverTickWasBlank = true;
				if (IsActivating)
				{
					BlankCastTicks++;
					if (++blankTicksInCast > BlankTicksAllowedInCast)
					{
						blankTicksInCast = 0;
						BlankCastCancels++;
						Cancel(state);
						// Nothing renews the reconcile on a tick without input: the owner has to be told now, not when
						// its input comes back, by which time it has launched what the server cancelled.
						if (predictionController != null)
						{
							predictionController.RequestReconcileSends();
						}
					}
				}
				return;
			}
			blankTicksInCast = 0;
			serverTickWasBlank = false;

			ApplyHitClaim(ref input);

			// Off the server a tick without input is an observer's (state forwarding): held state carries over.
			if (!hasActualData)
			{
				activationData.QueuedAbilityID = NO_ABILITY;
				if (replicatedFlags.IsFlagged(AbilityActivationFlags.IsHeld))
				{
					activationData.ActivationFlags.EnableBit(AbilityActivationFlags.IsHeld);
				}
				else
				{
					activationData.ActivationFlags.DisableBit(AbilityActivationFlags.IsHeld);
				}
			}

			float deltaTime = (float)base.TimeManager.TickDelta;

			// If we have an interrupt queued
			if (ProcessInterrupt(activationData, state))
			{
				return;
			}

			// If we aren't activating anything, try to start a new ability or consumable
			if (!IsActivating)
			{
				bool tried = activationData.QueuedAbilityID != NO_ABILITY;
				bool started;

				if (!CanStartActivation())
				{
					/* Dead characters start nothing. Every server broadcast handler routes
					 * through CharacterStateValidation.CanAct, which refuses a dead character,
					 * but ability activation does not arrive by broadcast — it rides the
					 * predicted replicate stream and so bypassed that gate entirely. A player
					 * could keep casting from the floor, and after a reconnect-while-dead the
					 * character even stood upright while doing it.
					 *
					 * Placed here rather than at the top of OnReplicate on purpose: only the
					 * decision to *start* is refused. An already-active cast still processes
					 * (Kill cancels it server-side), and the deterministic cooldown and
					 * resource simulation further down keeps running, so cooldowns continue to
					 * tick while dead instead of freezing and desyncing on revive.
					 *
					 * Falls through to the shared `tried && !started` path below, so the server
					 * still records the denial and the client reconciles its prediction away. */
					started = false;
				}
				else if (activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsConsumable))
				{
					started = TryStartConsumable(activationData, state);
				}
				else
				{
					started = TryStartAbility(activationData, state);
				}

				// Server tracks denial so OnCreateReconcile can set the Denied flag.
				if (base.IsServerStarted && tried && !started)
				{
					wasDenied = true;

					/* A denied CONSUMABLE also corrects the owner's inventory. The owner predicted
					 * the whole use — including the stack decrement or slot clear in
					 * FinishConsumable — and a denial produces no other correcting message:
					 * inventory is not reconciled state, the replay skips the mutation, and
					 * OnConsumableUsed fires only on a server-side SUCCESS. Without this the
					 * owner's stack showed one fewer charge than the server holds until some
					 * unrelated system rewrote the slot. */
					if (activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsConsumable))
					{
						SendConsumableDenialCorrection(activationData.QueuedAbilityID);
					}
				}
			}

			// Process the active ability or consumable.
			// Always reached even if TryStart failed — prevents simulation skips
			// if future logic is added after ability processing.
			if (IsActivating)
			{
				if (replicatedFlags.IsFlagged(AbilityActivationFlags.IsConsumable))
				{
					ProcessActiveConsumable(activationData, state, deltaTime);
				}
				else
				{
					ProcessActiveAbility(activationData, state, deltaTime);
				}
			}
		}

		/// <summary>
		/// Sends the owner the authoritative state of the inventory slot a denied consumable
		/// prediction mutated. Server only; see the denial site in <c>ReplicateInternal</c>.
		/// </summary>
		/// <remarks>
		/// The slot is re-derived with the same deterministic search the prediction used
		/// (<see cref="FindConsumableItem"/> iterates slots in order on every peer), so when the
		/// two inventories agreed at prediction time — the normal case, since this denial is what
		/// would have made them disagree — the correction names exactly the item the owner
		/// touched. When the server holds no such item at all, the owner is showing an item the
		/// server does not have; there is no slot to name, and the next authoritative inventory
		/// write settles it. Reliable: this is a correction, not a cosmetic.
		/// </remarks>
		/// <param name="queuedAbilityID">The denied activation's template id, as replicated.</param>
		private void SendConsumableDenialCorrection(long queuedAbilityID)
		{
			if (!base.IsServerStarted ||
				base.Owner == null || !base.Owner.IsValid ||
				base.NetworkManager == null ||
				queuedAbilityID < int.MinValue || queuedAbilityID > int.MaxValue)
			{
				return;
			}

			ConsumableTemplate consumable = BaseItemTemplate.Get<ConsumableTemplate>((int)queuedAbilityID);
			if (consumable == null)
			{
				return;
			}

			Item item = FindConsumableItem(consumable.ID);
			if (item == null || item.Template == null)
			{
				return;
			}

			base.NetworkManager.ServerManager.Broadcast(base.Owner, new InventorySetItemBroadcast()
			{
				InstanceID = item.ID,
				TemplateID = item.Template.ID,
				Slot = item.Slot,
				Seed = item.IsGenerated ? item.Generator.Seed : 0,
				StackSize = item.IsStackable ? item.Stackable.Amount : 0,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Returns whether the character is in a state that may begin a new activation.
		/// </summary>
		/// <remarks>
		/// Tests health rather than <see cref="CharacterFlags.IsDead"/> because this runs inside
		/// the predicted replicate stream. Flags travel only in the spawn payload and are never
		/// re-synced, so a client's copy is stale from its first death onward — gating on it
		/// would make owner and server disagree about every activation for the rest of the
		/// session. Resource state is reconciled to the owner every tick, so both sides reach
		/// the same answer for the same tick and the client predicts the refusal correctly
		/// instead of casting and being rolled back.
		/// <para>
		/// A character with no damage controller has no health to lose and is not gated.
		/// </para>
		/// </remarks>
		private bool CanStartActivation()
		{
			return cachedDamageController == null || cachedDamageController.IsAlive;
		}

		/// <summary>
		/// Handles prediction state for non-owner clients. Predicts held state for future ticks
		/// and tracks the last created data for reconciliation.
		/// </summary>
		private void HandlePrediction(ref AbilityActivationReplicateData activationData, ReplicateState state)
		{
			if (!base.IsServerStarted && !base.IsOwner)
			{
				// Predict held state
				if (state.IsFuture())
				{
					// Only predict from real data; default-init data has tick 0.
					if (hasLastCreatedData)
					{
						uint thisTick = activationData.GetTick();
						uint lastCreatedTick = lastCreatedData.GetTick();

						// Guard: after a reconcile rewind, lastCreatedTick can briefly
						// exceed thisTick. Unsigned subtraction would wrap to a huge value,
						// silently disabling observer held-prediction for many ticks.
						if (lastCreatedTick <= thisTick)
						{
							uint tickDiff = thisTick - lastCreatedTick;
							if (tickDiff <= ObserverPredictionWindowTicks)
							{
								if (lastCreatedData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsHeld))
								{
									activationData.ActivationFlags.EnableBit(AbilityActivationFlags.IsHeld);
								}
								else
								{
									activationData.ActivationFlags.DisableBit(AbilityActivationFlags.IsHeld);
								}
							}
						}
					}
				}
				else if (state.ContainsTicked() && activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsActualData))
				{
					// Only cache lastCreatedData if this tick's input is actual data (not default/empty),
					// matching KCCPlayer observer prediction pattern.
					lastCreatedData.Dispose();
					lastCreatedData = activationData;
					hasLastCreatedData = true;
				}
			}
		}

		/// <inheritdoc/>
		public void OnCreateReconcile(ref CharacterReconcileData reconcileData)
		{
			// Include server-authoritative denial flag if the last tick denied an activation.
			int flags = replicatedFlags;
			/* A one-shot report rides every reconcile until an input runs after it. The reconcile of a tick with no input is
			 * stamped with the last input's tick too (PredictionManager.SendStateUpdate), so the owner can receive both in one
			 * state and keeps the later: a report that rode only the first was lost. */
			bool freshReport = false;
			if (wasDenied)
			{
				stickyDenied = true;
				wasDenied = false;
				freshReport = true;
			}
			/* Only when the no-spawn belongs to the tick this reconcile describes. A reconcile is the
			 * state after the LAST replicate the server ran this tick, so that tick — and no other —
			 * is what the owner will compare it against. Consumed either way: a stale report has
			 * missed its reconcile and must not be carried into the next one. */
			if (serverSpawnedNothingTick != TimeManager.UNSET_TICK)
			{
				uint reconcileTick = predictionController != null
					? predictionController.CurrentReplicateTickSnapshot
					: TimeManager.UNSET_TICK;

				if (ShouldFlagNoSpawn(serverSpawnedNothingTick, reconcileTick))
				{
					stickyNoSpawn = true;
					freshReport = true;
				}
				serverSpawnedNothingTick = TimeManager.UNSET_TICK;
			}
			if (freshReport)
			{
				stickyBlankTicks = 0;
			}
			else if (!serverTickWasBlank)
			{
				// An input ran after the one the report was about: this reconcile is that input's, and the report is spent.
				stickyDenied = false;
				stickyNoSpawn = false;
			}
			else if ((stickyDenied || stickyNoSpawn) && ++stickyBlankTicks > StickyReportBlankTicks)
			{
				stickyDenied = false;
				stickyNoSpawn = false;
			}
			if (stickyDenied)
			{
				flags.EnableBit(AbilityActivationFlags.Denied);
			}
			if (stickyNoSpawn)
			{
				flags.EnableBit(AbilityActivationFlags.NoSpawn);
			}

			if ((flags & ~0xFFFF) != 0)
				Log.Warning("AbilityController", $"replicatedFlags 0x{flags:X} exceeds 16-bit Pack range");

			EnsureAbilitySeedGenerator();

			/* The generator travels whole, to whoever this reconcile reaches.
			 *
			 * That audience is a project setting, not a choice made here: FishNet writes one
			 * reconcile buffer per send (Server_SendReconcileRpc) — to the owner alone with state
			 * forwarding off, and to every observer with it on. There is no per-connection shape to
			 * write, which is why the gate lives at the CONSUMER instead; see
			 * InstallsReconciledRngState, which also explains why zeroing these words here would
			 * blind the owner in one mode and every simulating observer in the other. */
			abilitySeedGenerator.CaptureState(out uint rngS0, out uint rngS1, out uint rngS2, out uint rngS3);

			reconcileData.AbilityID = currentAbilityID;
			reconcileData.RemainingTicks = remainingTicks;
			reconcileData.ChargedHoldTicks = chargedHoldTicks;
			reconcileData.Seed = currentSeed;
			reconcileData.PackedFlagsAndSlot = CharacterReconcileData.Pack(flags, consumableSlot);
			reconcileData.RngS0 = rngS0;
			reconcileData.RngS1 = rngS1;
			reconcileData.RngS2 = rngS2;
			reconcileData.RngS3 = rngS3;
		}

		/// <inheritdoc/>
		public void OnReconcile(CharacterReconcileData rd, Channel channel)
		{
			//Log.Debug($"Reconciled: {rd.GetTick()}");
			uint reconcileTick = rd.GetTick();

			/* Predicted-history reconciliation is OWNER-ONLY.
			 *
			 * Everything in ReconcilePredictedHistory judges the server's state against what THIS
			 * peer predicted for the same tick, and only the owner predicts: it is the only peer
			 * whose replicate stream carries real input, the only one that spawns ability objects
			 * ahead of the server, and the only one whose UI shows a cast bar to clear. A
			 * non-owner that reached this code compared the server's seed against a history it had
			 * never populated (or, with state forwarding on, one built from relayed input) and
			 * could destroy ability objects it had faithfully reproduced from a broadcast, fire
			 * OnAbilityDenied for a denial that was not its own, and clear a cast bar it never
			 * drew. Non-owners take the authoritative fields below and nothing else. */
			if (base.IsOwner)
			{
				ReconcilePredictedHistory(rd, reconcileTick);
			}

			ApplyAuthoritativeReconcileState(rd);
		}

		/// <summary>
		/// Owner-only half of <see cref="OnReconcile"/>: compares the server's state for the
		/// reconcile tick against what this client predicted for that same tick and corrects the
		/// difference.
		/// </summary>
		private void ReconcilePredictedHistory(CharacterReconcileData rd, uint reconcileTick)
		{
			bool denied = rd.UnpackFlags.IsFlagged(AbilityActivationFlags.Denied);

			/* Compare like with like. This reconcile describes the server's state AFTER tick
			 * reconcileTick; the client's live fields describe its state after its latest local
			 * tick, which is always several ticks later (FishNet applies a reconcile only once it
			 * is stateInterpolation+1 ticks behind local). Any cast predicted in between had
			 * advanced currentSeed, so comparing against the live value declared a mismatch on
			 * every cast and destroyed every owner-predicted projectile a tick after it spawned.
			 * The history holds what the client had at reconcileTick itself. Without an entry
			 * (first reconciles after spawn, or one older than the ring) there is nothing to
			 * judge against and no correction is attempted. */
			bool havePredicted = predictedStateHistory.TryGet(reconcileTick, out int predictedSeed, out long predictedAbilityID);

			/* The state this client held going INTO the reconcile tick. Needed to tell apart the
			 * two ways a seed can disagree at tick T — see ShouldDestroySpawnsAtReconcileTick. */
			bool havePrevious = predictedStateHistory.TryGet(reconcileTick - 1u, out int previousSeed, out _);

			/* True when the server demonstrably did NOT spawn at the reconcile tick while this
			 * client did, so the object spawned exactly ON that tick has to go too. */
			bool serverSpawnedNothingAtTick = rd.UnpackFlags.IsFlagged(AbilityActivationFlags.NoSpawn);
			bool destroyAtTick = ShouldDestroySpawnsAtReconcileTick(denied, havePredicted, predictedSeed,
				havePrevious, previousSeed, rd.Seed, serverSpawnedNothingAtTick);

			// Detect prediction mismatch: the client's simulation of this tick produced a
			// different seed than the server's, so the client spawned something the server did
			// not (or vice versa). Destroy ability objects spawned after the reconcile tick.
			// If a specific ability was being activated, restrict destruction to that ability;
			// otherwise fall back to a full cleanup.
			if ((havePredicted && predictedSeed != rd.Seed) || destroyAtTick)
			{
				OnPredictionMismatch?.Invoke(reconcileTick);

				/* NoSpawn with AGREEING seeds corrects exactly one tick. The flag says the server ran
				 * the activation at T and produced no object, so the client's spawn AT T is
				 * unconfirmed — but matching seeds say nothing else diverged, so every spawn on a
				 * later tick is still confirmed and must survive. The >= sweep here destroyed those
				 * later objects too, which the replay cannot restore (it skips spawns): a
				 * RequiresTarget miss at T erased a confirmed projectile from T+1 permanently. Only a
				 * genuine seed mismatch (or denial, which implies one at T) justifies the wider sweep. */
				bool onlyAtTick = destroyAtTick && havePredicted && predictedSeed == rd.Seed;
				long droppedAbility = predictedAbilityID != NO_ABILITY && KnownAbilities.ContainsKey(predictedAbilityID) ? predictedAbilityID : 0;
				if (onlyAtTick)
				{
					DropPendingHitClaims(reconcileTick, includeTick: true, droppedAbility, onlyTick: true);
				}
				else
				{
					DropPendingHitClaims(reconcileTick, destroyAtTick, droppedAbility);
				}

				if (predictedAbilityID != NO_ABILITY && KnownAbilities.TryGetValue(predictedAbilityID, out Ability mismatchedAbility))
				{
					// Only destroy objects from the ability whose activation caused the seed divergence
					if (onlyAtTick)
					{
						mismatchedAbility.DestroyAbilityObjectsAtTick(reconcileTick);
					}
					else
					{
						mismatchedAbility.DestroyAbilityObjectsAfterTick(reconcileTick, destroyAtTick);
					}
				}
				else
				{
					// Instant abilities have already cleared their ID by the end of the tick, so
					// this is the common path for them, not a sign of deeper desync.
					foreach (Ability ability in KnownAbilities.Values)
					{
						if (onlyAtTick)
						{
							ability.DestroyAbilityObjectsAtTick(reconcileTick);
						}
						else
						{
							ability.DestroyAbilityObjectsAfterTick(reconcileTick, destroyAtTick);
						}
					}
				}
			}

			// The denial flag is authoritative and independent from RNG state.
			// A rejected activation can occur before any seed advance, so tying
			// this callback to seed mismatch drops legitimate denial corrections.
			// Judged against the predicted state at the denied tick so instant abilities —
			// whose live ID is already cleared by the time the denial arrives — still report.
			if (denied && havePredicted && predictedAbilityID != NO_ABILITY)
			{
				OnAbilityDenied?.Invoke(predictedAbilityID);
			}

			// When the server had no active ability at this tick but the client predicted one,
			// the cast bar must be cleared: the server completed, interrupted, or never started
			// the ability (e.g. the replicate was lost). Judged at the reconcile tick — against
			// the live ID this fired on every reconcile for the first RTT of every timed cast,
			// while the server simply had not received the activation yet.
			if (!denied && havePredicted && predictedAbilityID != NO_ABILITY && rd.AbilityID == NO_ABILITY)
			{
				OnCancel?.Invoke();

				/* And whatever that cast has launched SINCE is taken back. The seeds can agree here (the server ended the
				 * cast without finishing it: an interrupt, a stun, a death, input that never came), so the mismatch sweep
				 * above did not run, and the replay from this state does not finish the cast. The projectile the owner
				 * launched after this tick therefore had no server counterpart and flew its whole life as a ghost, showing
				 * hits nobody else saw. Objects from an EARLIER cast of the same ability (at or before this tick) stay. */
				if (KnownAbilities.TryGetValue(predictedAbilityID, out Ability endedAbility))
				{
					/* Only that cast's: up to the tick this client's own prediction ended it. A later cast of the same ability
					 * is one the server runs too (it never finished the first, so nothing is cooling down) and must stay. */
					uint castEnd = PredictedCastEndAfter(reconcileTick, predictedAbilityID);
					endedAbility.DestroyAbilityObjectsBetween(reconcileTick, castEnd);
					DropPendingHitClaims(reconcileTick, includeTick: false, predictedAbilityID, upToTick: castEnd, bounded: true);
				}

				/* And the persistent animation state with it. Cancel() clears the Block bool, but a
				 * SERVER-side interrupt runs Cancel on the server only — where SetBlocking compiles
				 * out — and the owner learns of it exactly here, through the reconcile. Without this
				 * the owner's animator held the blocking pose (and the client-authoritative
				 * NetworkAnimator relayed it to every observer) until the player's next locally
				 * cancelled ability. Trigger-based animations are transient and need no clear. */
				cachedAnimationController?.SetBlocking(false);
			}
		}

		/// <summary>
		/// The first tick after <paramref name="afterTick"/> on which this client's own prediction no longer had
		/// <paramref name="abilityID"/> in progress (its cast ended there, finishing or not), or the current tick.
		/// </summary>
		private uint PredictedCastEndAfter(uint afterTick, long abilityID)
		{
			uint now = base.TimeManager != null ? base.TimeManager.LocalTick : afterTick;
			for (uint t = afterTick + 1u; (int)(now - t) >= 0; ++t)
			{
				if (!predictedStateHistory.TryGet(t, out _, out long predicted))
				{
					continue;
				}
				if (predicted != abilityID)
				{
					return t;
				}
			}
			return now;
		}

		/// <summary>
		/// Whether a recorded no-spawn may travel in the reconcile currently being built.
		/// </summary>
		/// <remarks>
		/// <para>
		/// True only when the two ticks are the same one. The reconcile describes the state after the
		/// last replicate the server ran this tick, and
		/// <see cref="ShouldDestroySpawnsAtReconcileTick"/> on the owner judges the flag against
		/// exactly that tick — so a flag raised on any other tick names an object the owner will not
		/// look for, or worse, a different object that it will.
		/// </para>
		/// <para>
		/// <b>Refusing is the safe direction and that is deliberate.</b> Dropping a mismatched report
		/// leaves the owner with a ghost projectile that flies out its lifetime: a visual defect, and
		/// self-correcting. Stamping it onto the wrong tick destroys an object the server really did
		/// spawn, which the replay cannot restore because it starts at T+1. Between a wrong visual and
		/// an unrecoverable one, the reconcile says nothing.
		/// </para>
		/// <para>
		/// Separated from <see cref="OnCreateReconcile"/> so the rule can be pinned by a test rather
		/// than inferred from the one call site, exactly as
		/// <see cref="ShouldDestroySpawnsAtReconcileTick"/> is.
		/// </para>
		/// </remarks>
		/// <param name="spawnedNothingTick">Replicate tick the server resolved no object on.</param>
		/// <param name="reconcileTick">Replicate tick this reconcile describes.</param>
		internal static bool ShouldFlagNoSpawn(uint spawnedNothingTick, uint reconcileTick)
		{
			if (spawnedNothingTick == TimeManager.UNSET_TICK ||
				reconcileTick == TimeManager.UNSET_TICK)
			{
				return false;
			}
			return spawnedNothingTick == reconcileTick;
		}

		/// <summary>
		/// Decides whether ability objects spawned exactly ON the reconcile tick must be destroyed.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why this exists.</b> An instant cast (<c>ActivationTime</c> 0) starts and finishes
		/// inside one replicate call, so the history entry for that tick records
		/// <c>NO_ABILITY</c> and only the seed advance survives. Cleanup used a strict
		/// <c>&gt; tick</c> comparison, so a projectile predicted AT tick T was never destroyed
		/// when the server refused the cast at T, and it flew on as a ghost for its whole
		/// lifetime.
		/// </para>
		/// <para>
		/// <b>Why it cannot simply be "destroy at T on any mismatch".</b> FishNet replays from
		/// <c>T + 1</c>, so an object spawned at T is never re-created by the replay. Deleting one
		/// the server really did spawn removes it permanently. The seed is what distinguishes the
		/// cases: <c>ResolveTargetAndSpawn</c> advances the seed exactly once per spawn attempt on
		/// every peer, so "did this peer spawn at T" is readable as "did its seed advance at T".
		/// </para>
		/// <para><b>Truth table.</b> P = this client's predicted seed after T,
		/// P₋ = its predicted seed after T-1, S = the server's seed after T.</para>
		/// <list type="table">
		/// <item><description>
		/// <b>Server spawned at T, client spawned at T</b> — P == S. No correction; the object at
		/// T is confirmed and must survive. Returns false.
		/// </description></item>
		/// <item><description>
		/// <b>Server DENIED the activation at T, client spawned at T</b> — the Denied flag is
		/// authoritative and independent of the RNG, and a denial means nothing started, so
		/// nothing spawned. Returns true. This is the instant-cast ghost.
		/// </description></item>
		/// <item><description>
		/// <b>Server did not spawn at T for another reason (input lost), client spawned at T</b> —
		/// S == P₋ (the server's seed never moved at T) while P != P₋ (the client's did). Returns
		/// true.
		/// </description></item>
		/// <item><description>
		/// <b>Server spawned at T, client did not</b> — P == P₋ and S != P. The client has no
		/// object at T to remove, and the server's copy is not reproducible here. Returns false;
		/// the ordinary <c>&gt; T</c> cleanup still runs.
		/// </description></item>
		/// <item><description>
		/// <b>Divergence began before T</b> — S matches neither P nor P₋, so who spawned at T
		/// cannot be established. Returns false and leaves tick T alone rather than risk deleting
		/// a confirmed object; the <c>&gt; T</c> cleanup still corrects everything after it.
		/// </description></item>
		/// <item><description>
		/// <b>No history for T</b> (first reconciles after spawn, or older than the ring) — nothing
		/// to judge against. Returns false.
		/// </description></item>
		/// </list>
		/// </remarks>
		/// <param name="denied">The server set <see cref="AbilityActivationFlags.Denied"/> for this tick.</param>
		/// <param name="havePredicted">A history entry exists for the reconcile tick.</param>
		/// <param name="predictedSeed">The client's seed after simulating the reconcile tick.</param>
		/// <param name="havePrevious">A history entry exists for the tick before the reconcile tick.</param>
		/// <param name="previousSeed">The client's seed after simulating the tick before the reconcile tick.</param>
		/// <param name="serverSeed">The server's seed after the reconcile tick.</param>
		/// <param name="serverSpawnedNothing">
		/// The server set <see cref="AbilityActivationFlags.NoSpawn"/> for this tick: it completed an
		/// activation and produced no object. The seeds AGREE in this case — the seed advances
		/// whether or not a spawn happened — so this has to be judged before the matching-seed exit
		/// below, or the owner's object is confirmed by the very fact that both sides advanced.
		/// </param>
		internal static bool ShouldDestroySpawnsAtReconcileTick(
			bool denied,
			bool havePredicted,
			int predictedSeed,
			bool havePrevious,
			int previousSeed,
			int serverSeed,
			bool serverSpawnedNothing = false)
		{
			// Nothing to compare against.
			if (!havePredicted)
			{
				return false;
			}

			/* The server ran the activation and spawned nothing. Anything the client spawned at T is
			 * unconfirmed regardless of seed agreement. */
			if (serverSpawnedNothing)
			{
				return true;
			}

			/* Client and server agree about tick T. Whatever happened at T happened on both, so
			 * the object spawned there — if any — is confirmed. */
			if (predictedSeed == serverSeed)
			{
				return false;
			}

			/* An authoritative refusal. TryStartAbility failed on the server, so no spawn can have
			 * occurred at T, whatever the seeds say about earlier ticks. */
			if (denied)
			{
				return true;
			}

			/* The server's seed is still exactly what this client had going into T: the server
			 * advanced nothing at T. Combined with the client's own seed having moved, this
			 * client spawned something the server did not. */
			return havePrevious && previousSeed == serverSeed && predictedSeed != previousSeed;
		}

		/// <summary>
		/// Applies the server's authoritative ability state from a reconcile. Runs on every peer
		/// that receives one, owner or not.
		/// </summary>
		private void ApplyAuthoritativeReconcileState(CharacterReconcileData rd)
		{
			currentAbilityID = rd.AbilityID;
			remainingTicks = rd.RemainingTicks;
			// The countdown restored is the one AFTER the corrected tick: the replay counts on from that number.
			lastCastTick = rd.GetTick();
			hasLastCastTick = true;
			/* Restored like every other predicted counter. The owner increments this once per
			 * invocation of the replicate body, and a reconcile replays every tick since the
			 * correction — so left to itself it runs at several times the server's rate and the
			 * owner cancels its own charge early. See CharacterReconcileData.ChargedHoldTicks. */
			chargedHoldTicks = rd.ChargedHoldTicks;

			/* THE DETERMINISTIC GENERATOR IS NOT RESTORED UNCONDITIONALLY.
			 *
			 * Every broadcast site in this project asks ObserverSyncMode whose turn it is; this
			 * reconcile consumer did not, and it is the one that installs the whole 128-bit
			 * xoshiro128** state plus the current per-cast seed. WritePayload refuses to hand that
			 * to a non-owner and says why at length: 128 bits is the entire generator, so a
			 * modified client holding a peer's state can compute every seed that peer will ever
			 * cast with, and anything rolled from those seeds becomes predictable for somebody
			 * else's character.
			 *
			 * The rule below is the same question every other consumer asks. In the shipped
			 * interpolated mode it is exactly `IsOwner`, and no non-owner can reach it anyway —
			 * FishNet writes the reconcile to the owner alone when forwarding is off — so this
			 * costs the owner's replay nothing and closes the path if the gate above it is ever
			 * relaxed. In the forwarded mode the answer is TRUE for a non-owner, because there an
			 * observer runs the caster's replicate from the relayed input and spawns the objects
			 * itself: currentSeed is what ResolveTargetAndSpawn hands to AbilityObject.Spawn, the
			 * generator is what advances it, and BroadcastAbilityActivated is deliberately silent
			 * in that mode. So the state is not a disclosure there, it is the authority the mode
			 * runs on.
			 *
			 * This is why the words are NOT zeroed at the sender instead. One writer serves the
			 * whole audience (NetworkBehaviour.Server_SendReconcileRpc writes once, to the owner
			 * when forwarding is off and to every observer when it is on), so a sender-side zero
			 * cannot be aimed: with forwarding off it would blind the owner's own replay, and with
			 * forwarding on it would blind the very peers that simulate from it. Shaping the
			 * reconcile per connection is not possible without changing FishNet. */
			if (InstallsReconciledRngState(base.IsOwner, ObserverSyncMode.ObserversConsumeReconcile(base.NetworkObject)))
			{
				currentSeed = rd.Seed;

				// Restore the full xoshiro128** generator state so that subsequent
				// Next() calls during replay produce identical values to the server.
				// Without this, a single prediction mismatch permanently desynchronizes
				// the 128-bit generator state, causing a cascade of mismatches on
				// every subsequent ability activation.
				// RestoreState reuses the existing instance to avoid a per-reconcile
				// allocation (30 Hz × N clients = significant GC pressure at scale).
				if (abilitySeedGenerator == null)
				{
					abilitySeedGenerator = new DeterministicRNG(rd.RngS0, rd.RngS1, rd.RngS2, rd.RngS3);
				}
				else
				{
					abilitySeedGenerator.RestoreState(rd.RngS0, rd.RngS1, rd.RngS2, rd.RngS3);
				}
			}

			// Restore only the replicated flags from the server.
			// localInputFlags is NOT touched — any input queued since the last tick
			// (e.g. Release() or Interrupt()) is preserved.
			replicatedFlags = rd.UnpackFlags;
			replicatedFlags.DisableBit(AbilityActivationFlags.Denied);

			// Restore consumable slot lock state from server.
			// Unlock the old predicted slot (may differ from server) and lock the authoritative one.
			int serverSlot = rd.UnpackConsumableSlot;
			if (cachedInventoryController != null)
			{
				if (consumableSlot >= 0)
				{
					cachedInventoryController.UnlockSlot(consumableSlot);
				}
				if (serverSlot >= 0)
				{
					cachedInventoryController.LockSlot(serverSlot);
				}
			}
			consumableSlot = serverSlot;

		}

		/// <summary>
		/// Whether this peer may install the deterministic ability generator a reconcile carries.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The rule <see cref="ApplyAuthoritativeReconcileState"/> applies to the four xoshiro128**
		/// state words and <c>currentSeed</c>, as a pure function so the table can be asserted
		/// without a NetworkManager — in the style of <c>ServerCancelsDirectly</c> and
		/// <c>AbilityObject.ResolvesHitsOnThisPeer</c>.
		/// </para>
		/// <list type="table">
		/// <item><description>owner, forwarding off → INSTALL (the shipped path; its replay is load-bearing for deterministic ability RNG)</description></item>
		/// <item><description>owner, forwarding on → INSTALL</description></item>
		/// <item><description>non-owner, forwarding off → REFUSE (unreachable — the reconcile goes to the owner alone — so this is the gate, not the behaviour)</description></item>
		/// <item><description>non-owner, forwarding on → INSTALL (that peer runs the caster's replicate from relayed input and spawns from this generator)</description></item>
		/// </list>
		/// </remarks>
		/// <param name="isOwner">Whether this peer owns the casting character.</param>
		/// <param name="observersConsumeReconcile">
		/// <c>ObserverSyncMode.ObserversConsumeReconcile</c> for this object: true only while state
		/// forwarding is on.
		/// </param>
		/// <returns>True when the reconciled generator state is this peer's authority.</returns>
		internal static bool InstallsReconciledRngState(bool isOwner, bool observersConsumeReconcile)
		{
			return isOwner || observersConsumeReconcile;
		}

		/// <summary>
		/// Ensures the ability seed generator is initialized. The server allocates a new
		/// per-character seed from <see cref="playerSeedGenerator"/>; clients initialize
		/// from the last payload/reconcile seed without advancing the shared static RNG.
		/// Sets <see cref="currentSeed"/> to the first generated value.
		/// Called from <c>OnStartNetwork</c>, <see cref="ResetState"/>, <c>OnCreateReconcile</c> and
		/// <c>WritePayload</c> — every path must produce identical results when the generator is
		/// null, so the logic is centralized here.
		/// </summary>
		private void EnsureAbilitySeedGenerator()
		{
			if (abilitySeedGenerator == null)
			{
				if (base.IsServerStarted)
				{
					abilitySeed = playerSeedGenerator.Next();
				}
				abilitySeedGenerator = new DeterministicRNG(abilitySeed);
				currentSeed = abilitySeedGenerator.Next();
			}
		}

	}
}