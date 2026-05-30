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
		/// Cached <see cref="EventData"/> for <see cref="Ability.MeetsActivationConditions"/>.
		/// Avoids allocating a new instance every tick when no ability is active
		/// (CanActivate is called every tick from Replicate).
		/// </summary>
		private EventData cachedCheckEventData;

		// Cached component references resolved once in OnStartNetwork.
		// Avoids Dictionary<Type, ICharacterBehaviour> lookups per tick per character.
		private IBuffController cachedBuffController;
		private ICooldownController cachedCooldownController;
		private ICharacterDamageController cachedDamageController;
		private ICharacterAttributeController cachedAttributeController;
		private IInventoryController cachedInventoryController;
		private ITargetController cachedTargetController;

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

		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			// Resolve component caches — these never change during gameplay.
			Character.TryGet(out cachedBuffController);
			Character.TryGet(out cachedCooldownController);
			Character.TryGet(out cachedDamageController);
			Character.TryGet(out cachedAttributeController);
			Character.TryGet(out cachedInventoryController);
			Character.TryGet(out cachedTargetController);

			// Eagerly initialize the deterministic RNG so the first WritePayload,
			// OnCreateReconcile, and ResetState paths all observe the same seed.
			// Without this, lazy initialization races could produce a one-tick seed mismatch.
			EnsureAbilitySeedGenerator();
		}

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

			OnUpdate = null;
			OnInterrupt = null;
			OnCancel = null;
			OnReset = null;
			OnPredictionMismatch = null;
			OnAbilityDenied = null;
			OnConsumableUsed = null;
			canManipulateHandlers.Clear();
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

			// Detach all spawned ability objects before clearing references.
			// This allows in-flight projectiles to persist visually using their snapshots
			// instead of being immediately destroyed when the character disconnects.
			foreach (Ability ability in KnownAbilities.Values)
			{
				ability.DetachAllAbilityObjects();
			}

			KnownAbilities.Clear();
			KnownBaseAbilities.Clear();
			KnownAbilityEvents.Clear();
			KnownAbilityOnTickEvents.Clear();
			KnownAbilityOnHitEvents.Clear();
			KnownAbilityOnPreSpawnEvents.Clear();
			KnownAbilityOnSpawnEvents.Clear();
			KnownAbilityOnDestroyEvents.Clear();
			templateToAbilityID?.Clear();

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

		/// <inheritdoc/>
		public void PopulateInput(ref CharacterReplicateData input)
		{
			AbilityActivationReplicateData abilityInput = HandleCharacterInput();
			input.ActivationFlags = abilityInput.ActivationFlags;
			input.QueuedAbilityID = abilityInput.QueuedAbilityID;
		}

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

			if (!base.IsOwner)
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
			// Convert to the internal type — all ability subsystem methods use AbilityActivationReplicateData.
			AbilityActivationReplicateData activationData = new AbilityActivationReplicateData(
				input.ActivationFlags, input.QueuedAbilityID);
			activationData.SetTick(input.GetTick());

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

				if (activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsConsumable))
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
			long previousAbilityID = currentAbilityID;
			bool denied = rd.UnpackFlags.IsFlagged(AbilityActivationFlags.Denied);

			// Detect prediction mismatch: if the server's seed differs from the client's,
			// the client mispredicted an ability activation. Destroy any ability objects
			// spawned after the reconcile tick since they will be replayed.
			if (rd.Seed != currentSeed)
			{
				uint reconcileTick = rd.GetTick();
				OnPredictionMismatch?.Invoke(reconcileTick);

				foreach (Ability ability in KnownAbilities.Values)
				{
					ability.DestroyAbilityObjectsAfterTick(reconcileTick);
				}
			}

			// The denial flag is authoritative and independent from RNG state.
			// A rejected activation can occur before any seed advance, so tying
			// this callback to seed mismatch drops legitimate denial corrections.
			if (denied && previousAbilityID != NO_ABILITY)
			{
				OnAbilityDenied?.Invoke(previousAbilityID);
			}

			// When the server's authoritative state has no active ability but the client
			// was still predicting one, the cast bar must be cleared. This fires for
			// cases where the server completed, interrupted, or never started the ability
			// (e.g., ability completed in one tick before the client prediction caught up,
			// or replicate was lost and the server never received the activation input).
			// The denial case is handled above; this covers every other server-side ending.
			if (!denied && previousAbilityID != NO_ABILITY && rd.AbilityID == NO_ABILITY)
			{
				OnCancel?.Invoke();
			}

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

		/// <summary>
		/// Resolves the virtual camera position and rotation for the given character.
		/// PCs use <see cref="KCCController"/>, NPCs use <see cref="IAIController"/>,
		/// fallback is the character's transform.
		/// </summary>
		/// <param name="character">The character to resolve camera data for.</param>
		/// <param name="playerCharacter">Cached player character reference, or null for NPCs.</param>
		/// <param name="cameraPosition">The resolved camera position.</param>
		/// <param name="cameraRotation">The resolved camera rotation.</param>
		internal static void ResolveCameraData(ICharacter character, IPlayerCharacter playerCharacter, out Vector3 cameraPosition, out Quaternion cameraRotation)
		{
			if (playerCharacter != null)
			{
				cameraPosition = playerCharacter.CharacterController.VirtualCameraPosition;
				cameraRotation = playerCharacter.CharacterController.VirtualCameraRotation;
			}
			else if (character.TryGet(out IAIController ai))
			{
				cameraPosition = ai.VirtualCameraPosition;
				cameraRotation = ai.VirtualCameraRotation;
			}
			else
			{
				cameraPosition = character.Transform.position;
				cameraRotation = character.Transform.rotation;
			}
		}
	}
}