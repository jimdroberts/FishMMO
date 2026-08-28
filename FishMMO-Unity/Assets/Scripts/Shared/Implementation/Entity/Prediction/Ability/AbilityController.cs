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

			switch (abilityType)
			{
				case AbilityType.Physical:
				case AbilityType.GroundedPhysical:
				case AbilityType.AerialPhysical:
					cachedAnimationController.TriggerAttack();
					break;
				case AbilityType.Magic:
				case AbilityType.GroundedMagic:
				case AbilityType.AerialMagic:
					cachedAnimationController.TriggerCast();
					break;
				case AbilityType.Block:
					cachedAnimationController.SetBlocking(true);
					break;
				case AbilityType.Roll:
					cachedAnimationController.TriggerRoll();
					break;
				default:
					break;
			}
		}

		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			queuedAbilityID = NO_ABILITY;
			localInputFlags = 0;
			replicatedFlags = 0;
			// Clear the server-side denial sentinel so a respawn/possession does not
			// carry over a stale Denied bit into the next reconcile.
			wasDenied = false;
			Cancel();

			// Ensure no stale slot locks remain after state reset.
			consumableSlot = -1;

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

			// Observer-only abilities hold spawned objects too; detach and forget them the same way.
			ClearObservedAbilities();

			KnownAbilities.Clear();
			KnownBaseAbilities.Clear();
			KnownAbilityEvents.Clear();
			KnownAbilityOnTickEvents.Clear();
			KnownAbilityOnHitEvents.Clear();
			KnownAbilityOnPreSpawnEvents.Clear();
			KnownAbilityOnSpawnEvents.Clear();
			KnownAbilityOnDestroyEvents.Clear();
			templateToAbilityID?.Clear();
			pendingInFlightObjects.Clear();

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
		}

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
		/// from <c>AIController</c> — which disables itself off the server. Every observing client
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
			if (!Character.TryGet(out IAIController ai))
			{
				return;
			}

			/* Only the direction is replicated. The origin is derived from the motor on every
			 * peer -- see CharacterAimOrigin -- so an NPC and a player resolve it the same way. */
			// Quantised on the way in, for the same reason the player path quantises — see
			// AimDirectionCompression.
			input.AimDirection = AimDirectionCompression.Quantize(ai.VirtualCameraRotation * Vector3.forward);
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
			// IsHeld, IsConsumable, and IsMount are persistent state flags cleared by Cancel().
			localInputFlags.DisableBit(AbilityActivationFlags.Interrupt);

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
			 * chose the point the server raycast from. See CharacterAimOrigin. KCCPlayer (Order 80)
			 * has already advanced the motor for this tick by the time this runs (Order 100). */
			replicatedAimOrigin = CharacterAimOrigin.Resolve(Character);
			replicatedAimDirection = input.AimDirection.sqrMagnitude > 1e-12f
				? input.AimDirection
				: AimDirectionCompression.FallbackDirection;

			HandlePrediction(ref activationData, state);

			bool hasActualData = activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsActualData);

			// FishNet may occasionally provide default replicate data. That means
			// no new input arrived for this tick, not that simulation should stop.
			// Preserve held-state continuity for active casts while still running
			// deterministic cooldown/resource simulation below.
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
					started = TryStartConsumable(activationData);
				}
				else
				{
					started = TryStartAbility(activationData, state);
				}

				// Server tracks denial so OnCreateReconcile can set the Denied flag.
				if (base.IsServerStarted && tried && !started)
				{
					wasDenied = true;
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
			if (wasDenied)
			{
				flags.EnableBit(AbilityActivationFlags.Denied);
				wasDenied = false;
			}

			if ((flags & ~0xFFFF) != 0)
				Log.Warning("AbilityController", $"replicatedFlags 0x{flags:X} exceeds 16-bit Pack range");

			EnsureAbilitySeedGenerator();

			abilitySeedGenerator.CaptureState(out uint rngS0, out uint rngS1, out uint rngS2, out uint rngS3);

			reconcileData.AbilityID = currentAbilityID;
			reconcileData.RemainingTicks = remainingTicks;
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
			bool destroyAtTick = ShouldDestroySpawnsAtReconcileTick(denied, havePredicted, predictedSeed,
				havePrevious, previousSeed, rd.Seed);

			// Detect prediction mismatch: the client's simulation of this tick produced a
			// different seed than the server's, so the client spawned something the server did
			// not (or vice versa). Destroy ability objects spawned after the reconcile tick.
			// If a specific ability was being activated, restrict destruction to that ability;
			// otherwise fall back to a full cleanup.
			if ((havePredicted && predictedSeed != rd.Seed) || destroyAtTick)
			{
				OnPredictionMismatch?.Invoke(reconcileTick);

				if (predictedAbilityID != NO_ABILITY && KnownAbilities.TryGetValue(predictedAbilityID, out Ability mismatchedAbility))
				{
					// Only destroy objects from the ability whose activation caused the seed divergence
					mismatchedAbility.DestroyAbilityObjectsAfterTick(reconcileTick, destroyAtTick);
				}
				else
				{
					// Instant abilities have already cleared their ID by the end of the tick, so
					// this is the common path for them, not a sign of deeper desync.
					foreach (Ability ability in KnownAbilities.Values)
					{
						ability.DestroyAbilityObjectsAfterTick(reconcileTick, destroyAtTick);
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
			}
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
		internal static bool ShouldDestroySpawnsAtReconcileTick(
			bool denied,
			bool havePredicted,
			int predictedSeed,
			bool havePrevious,
			int previousSeed,
			int serverSeed)
		{
			// Nothing to compare against.
			if (!havePredicted)
			{
				return false;
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
		/// Ensures the ability seed generator is initialized. The server allocates a new
		/// per-character seed from <see cref="playerSeedGenerator"/>; clients initialize
		/// from the last payload/reconcile seed without advancing the shared static RNG.
		/// Sets <see cref="currentSeed"/> to the first generated value.
		/// Called from <see cref="ResetState"/>, <see cref="CreateReconcile"/>, and
		/// <see cref="WritePayload"/> — all three paths must produce identical results
		/// when the generator is null, so the logic is centralized here.
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