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
	public partial class AbilityController : CharacterBehaviour, IAbilityController
	{
		/// <summary>
		/// Static RNG for generating player ability seeds (server-side).
		/// </summary>
		private static System.Random playerSeedGenerator = new System.Random();

		/// <summary>
		/// Constant representing no active ability.
		/// </summary>
		public const long NO_ABILITY = 0;

		/// <summary>
		/// The ID of the currently activating ability, or NO_ABILITY if none.
		/// </summary>
		private long currentAbilityID;

		/// <summary>
		/// Bit field tracking local input state (held, interrupt, consumable, mount).
		/// Uses AbilityActivationFlags for flag positions.
		/// </summary>
		private int inputFlags;

		/// <summary>
		/// The ID of the next ability to activate after the current one, or NO_ABILITY if none.
		/// </summary>
		private long queuedAbilityID;

		/// <summary>
		/// Remaining time for the current ability activation or cooldown.
		/// </summary>
		private float remainingTime;

		/// <summary>
		/// RNG for ability-specific randomization (server-side).
		/// </summary>
		private System.Random abilitySeedGenerator;

		/// <summary>
		/// The seed used to initialize the ability RNG.
		/// </summary>
		private int abilitySeed = 0;

		/// <summary>
		/// The current seed value for ability RNG.
		/// </summary>
		private int currentSeed = 0;

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
		/// Event invoked to check if the character can manipulate abilities (e.g., not stunned).
		/// </summary>
		public event Func<bool> OnCanManipulate;

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
		/// </summary>
		public float RemainingActivationTime => remainingTime;

		public override void OnAwake()
		{
			base.OnAwake();

			KnownAbilities = new Dictionary<long, Ability>();
			KnownBaseAbilities = new HashSet<int>();
			KnownAbilityEvents = new HashSet<int>();
			KnownAbilityOnTickEvents = new HashSet<int>();
			KnownAbilityOnHitEvents = new HashSet<int>();
			KnownAbilityOnPreSpawnEvents = new HashSet<int>();
			KnownAbilityOnSpawnEvents = new HashSet<int>();
			KnownAbilityOnDestroyEvents = new HashSet<int>();
		}

		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnPostTick += TimeManager_OnPostTick;
			}
		}

		public override void OnStopNetwork()
		{
			base.OnStopNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnPostTick -= TimeManager_OnPostTick;
			}
		}

		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			queuedAbilityID = NO_ABILITY;
			inputFlags = 0;
			Cancel();

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

			abilitySeedGenerator = null;
		}

		/// <summary>
		/// Called after each network tick to replicate input and reconcile state.
		/// Runs on OnPostTick so that KCCPlayer has already processed on OnTick,
		/// guaranteeing VirtualCameraPosition/VirtualCameraRotation are fresh.
		/// </summary>
		private void TimeManager_OnPostTick()
		{
			Replicate(HandleCharacterInput());
			CreateReconcile();
		}

		/// <summary>
		/// Handles local character input for ability activation, building the replicate data for the current tick.
		/// </summary>
		/// <returns>The replicate data representing the current input state.</returns>
		private AbilityActivationReplicateData HandleCharacterInput()
		{
			if (Character == null)
			{
				return default;
			}

			float deltaTime = (float)base.TimeManager.TickDelta;
			if (Character.TryGet(out ICooldownController cooldownController))
			{
				cooldownController.OnTick(deltaTime);
			}

			if (!base.IsOwner)
			{
				return default;
			}

			// Copy current input flags and add the IsActualData marker
			int activationFlags = inputFlags;
			activationFlags.EnableBit(AbilityActivationFlags.IsActualData);

			// Clear one-shot flags from inputFlags after capturing them
			inputFlags.DisableBit(AbilityActivationFlags.Interrupt);
			inputFlags.DisableBit(AbilityActivationFlags.IsConsumable);
			inputFlags.DisableBit(AbilityActivationFlags.IsMount);

			AbilityActivationReplicateData activationEventData = new AbilityActivationReplicateData(activationFlags,
																									 queuedAbilityID);
			queuedAbilityID = NO_ABILITY;

			return activationEventData;
		}

		private AbilityActivationReplicateData lastCreatedData;

		/// <summary>
		/// Replicates ability activation input and state across the network, handling prediction, interrupts, and ability activation.
		/// </summary>
		/// <param name="activationData">The replicate data for this tick.</param>
		/// <param name="state">The prediction state.</param>
		/// <param name="channel">The network channel.</param>
		[Replicate]
		private void Replicate(AbilityActivationReplicateData activationData, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
		{
			// Ignore default data
			// FishNet sends default replicate data occassionally
			if (!activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsActualData))
			{
				return;
			}

			HandlePrediction(ref activationData, state);

			float deltaTime = (float)base.TimeManager.TickDelta;

			RegenerateAttributes(deltaTime);

			// If we have an interrupt queued
			if (ProcessInterrupt(activationData))
			{
				return;
			}

			// If we aren't activating anything, try to start a new ability
			if (!IsActivating)
			{
				if (!TryStartAbility(activationData))
				{
					return;
				}
			}

			// Process the active ability (casting, channeling, spawning)
			ProcessActiveAbility(activationData, state, deltaTime);
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
					uint lastCreatedTick = lastCreatedData.GetTick();
					uint thisTick = activationData.GetTick();
					uint tickDiff = lastCreatedTick - thisTick;
					if (tickDiff <= 1)
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
				else if (state.ContainsTicked())
				{
					lastCreatedData.Dispose();
					lastCreatedData = activationData;
				}
			}
		}

		/// <summary>
		/// Creates and sends a reconcile state for the ability controller to synchronize client/server state.
		/// Includes the current RNG seed so the client can detect prediction mismatches.
		/// </summary>
		public override void CreateReconcile()
		{
			if (base.IsServerStarted)
			{
				AbilityReconcileData state = default;
				if (Character.TryGet(out ICharacterAttributeController attributeController))
				{
					state = new AbilityReconcileData(currentAbilityID,
													 remainingTime,
													 currentSeed,
													 attributeController.GetResourceState());
				}
				Reconcile(state);
			}
		}

		/// <summary>
		/// Reconciles the ability controller's state from the server, applying ability and resource state.
		/// Detects prediction mismatches via seed comparison and destroys erroneously predicted ability objects.
		/// </summary>
		/// <param name="rd">The reconcile data from the server.</param>
		/// <param name="channel">The network channel.</param>
		[Reconcile]
		private void Reconcile(AbilityReconcileData rd, Channel channel = Channel.Unreliable)
		{
			//Log.Debug($"Reconciled: {rd.GetTick()}");

			// Detect prediction mismatch: if the server's seed differs from the client's,
			// the client mispredicted an ability activation. Destroy any ability objects
			// spawned after the reconcile tick since they will be replayed.
			if (rd.Seed != currentSeed)
			{
				uint reconcileTick = rd.GetTick();
				foreach (Ability ability in KnownAbilities.Values)
				{
					ability.DestroyAbilityObjectsAfterTick(reconcileTick);
				}
			}

			currentAbilityID = rd.AbilityID;
			remainingTime = rd.RemainingTime;
			currentSeed = rd.Seed;

			if (Character.TryGet(out ICharacterAttributeController attributeController))
			{
				attributeController.ApplyResourceState(rd.ResourceState);
			}
		}

	}
}