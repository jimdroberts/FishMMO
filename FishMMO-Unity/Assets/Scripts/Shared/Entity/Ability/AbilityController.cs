using FishNet.Connection;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls the activation, management, and synchronization of abilities for a character, including known abilities, events, and network state.
	/// Handles ability casting, queuing, cooldowns, and client/server synchronization.
	/// </summary>
	public class AbilityController : CharacterBehaviour, IAbilityController
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
		/// True if an interrupt is queued for the current ability.
		/// </summary>
		private bool interruptQueued;

		/// <summary>
		/// The ID of the next ability to activate after the current one, or NO_ABILITY if none.
		/// </summary>
		private long queuedAbilityID;

		/// <summary>
		/// Remaining time for the current ability activation or cooldown.
		/// </summary>
		private float remainingTime;

		/// <summary>
		/// The key currently held for ability activation.
		/// </summary>
		private KeyCode heldKey;

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
		/// Event invoked when a new ability is added to the character.
		/// </summary>
		public event Action<Ability> OnAddAbility;

		/// <summary>
		/// Event invoked when a new base ability is learned.
		/// </summary>
		public event Action<BaseAbilityTemplate> OnAddKnownAbility;

		/// <summary>
		/// Event invoked when a new ability event is learned.
		/// </summary>
		public event Action<AbilityEvent> OnAddKnownAbilityEvent;

		/// <summary>
		/// All known abilities for this character, indexed by ability ID.
		/// </summary>
		public Dictionary<long, Ability> KnownAbilities { get; private set; }

		/// <summary>
		/// All known base ability template IDs for this character.
		/// </summary>
		public HashSet<int> KnownBaseAbilities { get; private set; }

		/// <summary>
		/// All known ability event template IDs for this character.
		/// </summary>
		public HashSet<int> KnownAbilityEvents { get; private set; }

		/// <summary>
		/// All known OnTick event template IDs for this character.
		/// </summary>
		public HashSet<int> KnownAbilityOnTickEvents { get; private set; }

		/// <summary>
		/// All known OnHit event template IDs for this character.
		/// </summary>
		public HashSet<int> KnownAbilityOnHitEvents { get; private set; }

		/// <summary>
		/// All known OnPreSpawn event template IDs for this character.
		/// </summary>
		public HashSet<int> KnownAbilityOnPreSpawnEvents { get; private set; }

		/// <summary>
		/// All known OnSpawn event template IDs for this character.
		/// </summary>
		public HashSet<int> KnownAbilityOnSpawnEvents { get; private set; }

		/// <summary>
		/// All known OnDestroy event template IDs for this character.
		/// </summary>
		public HashSet<int> KnownAbilityOnDestroyEvents { get; private set; }

		/// <summary>
		/// True if an ability is currently being activated.
		/// </summary>
		public bool IsActivating { get { return currentAbilityID != NO_ABILITY; } }

		/// <summary>
		/// True if an ability is queued to activate after the current one.
		/// </summary>
		public bool AbilityQueued { get { return queuedAbilityID != NO_ABILITY; } }

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

#if UNITY_SERVER
			// Check if we already instantiated an RNG for this ability controller
			if (abilitySeedGenerator == null)
			{
				// Generate an AbilitySeedGenerator Seed
				abilitySeed = playerSeedGenerator.Next();

				// Instantiate the AbilitySeedGenerator on the server
				abilitySeedGenerator = new System.Random(abilitySeed);

				// Set the initial seed
				currentSeed = abilitySeedGenerator.Next();
			}
#endif
		}

		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		public override void OnStopNetwork()
		{
			base.OnStopNetwork();

			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}
		}

		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			queuedAbilityID = NO_ABILITY;
			Cancel();

			KnownAbilities.Clear();
			KnownBaseAbilities.Clear();
			KnownAbilityEvents.Clear();
			KnownAbilityOnTickEvents.Clear();
			KnownAbilityOnHitEvents.Clear();
			KnownAbilityOnPreSpawnEvents.Clear();
			KnownAbilityOnSpawnEvents.Clear();
			KnownAbilityOnDestroyEvents.Clear();
		}

#if !UNITY_SERVER
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				this.enabled = false;
			}
			else
			{
				ClientManager.RegisterBroadcast<KnownAbilityAddBroadcast>(OnClientKnownAbilityAddBroadcastReceived);
				ClientManager.RegisterBroadcast<KnownAbilityAddMultipleBroadcast>(OnClientKnownAbilityAddMultipleBroadcastReceived);
				ClientManager.RegisterBroadcast<KnownAbilityEventAddBroadcast>(OnClientKnownAbilityEventAddBroadcastReceived);
				ClientManager.RegisterBroadcast<KnownAbilityEventAddMultipleBroadcast>(OnClientKnownAbilityEventAddMultipleBroadcastReceived);
				ClientManager.RegisterBroadcast<AbilityAddBroadcast>(OnClientAbilityAddBroadcastReceived);
				ClientManager.RegisterBroadcast<AbilityAddMultipleBroadcast>(OnClientAbilityAddMultipleBroadcastReceived);

				// invoke client reset event
				OnReset?.Invoke();

				foreach (Ability ability in KnownAbilities.Values)
				{
					// update our client with abilities
					OnAddAbility?.Invoke(ability);
				}
			}
		}

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<KnownAbilityAddBroadcast>(OnClientKnownAbilityAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<KnownAbilityAddMultipleBroadcast>(OnClientKnownAbilityAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<KnownAbilityEventAddBroadcast>(OnClientKnownAbilityEventAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<KnownAbilityEventAddMultipleBroadcast>(OnClientKnownAbilityEventAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<AbilityAddBroadcast>(OnClientAbilityAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<AbilityAddMultipleBroadcast>(OnClientAbilityAddMultipleBroadcastReceived);
			}
		}

		/// <summary>
		/// Server sent an add known ability broadcast.
		/// </summary>
		private void OnClientKnownAbilityAddBroadcastReceived(KnownAbilityAddBroadcast msg, Channel channel)
		{
			BaseAbilityTemplate baseAbilityTemplate = BaseAbilityTemplate.Get<BaseAbilityTemplate>(msg.TemplateID);
			if (baseAbilityTemplate != null)
			{
				LearnBaseAbilities(new List<BaseAbilityTemplate>() { baseAbilityTemplate });

				OnAddKnownAbility?.Invoke(baseAbilityTemplate);
			}
		}

		/// <summary>
		/// Server sent an add known ability broadcast.
		/// </summary>
		private void OnClientKnownAbilityAddMultipleBroadcastReceived(KnownAbilityAddMultipleBroadcast msg, Channel channel)
		{
			List<BaseAbilityTemplate> templates = new List<BaseAbilityTemplate>();
			foreach (KnownAbilityAddBroadcast knownAbility in msg.Abilities)
			{
				BaseAbilityTemplate baseAbilityTemplate = BaseAbilityTemplate.Get<BaseAbilityTemplate>(knownAbility.TemplateID);
				if (baseAbilityTemplate != null)
				{
					templates.Add(baseAbilityTemplate);

					OnAddKnownAbility?.Invoke(baseAbilityTemplate);
				}
			}
			LearnBaseAbilities(templates);
		}

		/// <summary>
		/// Server sent an add known ability event broadcast.
		/// </summary>
		private void OnClientKnownAbilityEventAddBroadcastReceived(KnownAbilityEventAddBroadcast msg, Channel channel)
		{
			AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(msg.TemplateID);
			if (abilityEvent != null)
			{
				LearnAbilityEvents(new List<AbilityEvent>() { abilityEvent });

				OnAddKnownAbilityEvent?.Invoke(abilityEvent);
			}
		}

		/// <summary>
		/// Server sent an add known ability broadcast.
		/// </summary>
		private void OnClientKnownAbilityEventAddMultipleBroadcastReceived(KnownAbilityEventAddMultipleBroadcast msg, Channel channel)
		{
			List<AbilityEvent> events = new List<AbilityEvent>();
			foreach (KnownAbilityEventAddBroadcast knownAbilityEvent in msg.AbilityEvents)
			{
				AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(knownAbilityEvent.TemplateID);
				if (abilityEvent != null)
				{
					events.Add(abilityEvent);

					OnAddKnownAbilityEvent?.Invoke(abilityEvent);
				}
			}
			LearnAbilityEvents(events);
		}

		/// <summary>
		/// Server sent an add ability broadcast.
		/// </summary>
		private void OnClientAbilityAddBroadcastReceived(AbilityAddBroadcast msg, Channel channel)
		{
			AbilityTemplate abilityTemplate = AbilityTemplate.Get<AbilityTemplate>(msg.TemplateID);
			if (abilityTemplate != null)
			{
				Ability newAbility = new Ability(msg.ID, abilityTemplate, msg.Events);
				LearnAbility(newAbility);

				OnAddAbility?.Invoke(newAbility);
			}
		}

		/// <summary>
		/// Server sent an add multiple ability broadcast.
		/// </summary>
		private void OnClientAbilityAddMultipleBroadcastReceived(AbilityAddMultipleBroadcast msg, Channel channel)
		{
			foreach (AbilityAddBroadcast ability in msg.Abilities)
			{
				AbilityTemplate abilityTemplate = AbilityTemplate.Get<AbilityTemplate>(ability.TemplateID);
				if (abilityTemplate != null)
				{
					Ability newAbility = new Ability(ability.ID, abilityTemplate, ability.Events);
					LearnAbility(newAbility);

					OnAddAbility?.Invoke(newAbility);
				}
			}
		}
#endif

		/// <summary>
		/// Reads the ability controller's state from the network payload, including ability RNG seed, known abilities, and cooldowns.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="reader">The network reader to read from.</param>
		public override void ReadPayload(NetworkConnection conn, Reader reader)
		{
			// Read the AbilitySeedGenerator seed
			abilitySeed = reader.ReadInt32();

			// Instantiate the AbilitySeedGenerator
			abilitySeedGenerator = new System.Random(abilitySeed);

			// Set the initial seed
			currentSeed = abilitySeedGenerator.Next();

			//Log.Debug($"Received AbilitySeedGenerator Seed {abilitySeed}\r\nCurrent Seed {currentSeed}");

			int abilityCount = reader.ReadInt32();
			if (abilityCount < 1)
			{
				return;
			}
			KnownAbilities.Clear();
			KnownBaseAbilities.Clear();
			KnownAbilityEvents.Clear();
			KnownAbilityOnTickEvents.Clear();
			KnownAbilityOnHitEvents.Clear();
			KnownAbilityOnPreSpawnEvents.Clear();
			KnownAbilityOnSpawnEvents.Clear();
			KnownAbilityOnDestroyEvents.Clear();

			for (int i = 0; i < abilityCount; ++i)
			{
				long abilityID = reader.ReadInt64();
				int abilityTemplateID = reader.ReadInt32();

				List<int> abilityEvents = new List<int>();
				int abilityEventsCount = reader.ReadInt32();
				for (int j = 0; j < abilityEventsCount; ++j)
				{
					abilityEvents.Add(reader.ReadInt32());
				}
				Ability ability = new Ability(abilityID, abilityTemplateID, abilityEvents);

				LearnAbility(ability);
			}

			if (Character.TryGet(out ICooldownController cooldownController))
			{
				cooldownController.Read(reader);
			}
		}

		/// <summary>
		/// Writes the ability controller's state to the network payload, including ability RNG seed, known abilities, and cooldowns.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="writer">The network writer to write to.</param>
		public override void WritePayload(NetworkConnection conn, Writer writer)
		{
			// Write the ability RNG seed for the clients
			writer.WriteInt32(abilitySeed);

			//Log.Debug($"Writing AbilitySeedGenerator Seed {abilitySeed}\r\nCurrent Seed {currentSeed}");

			// Write the abilities for the clients
			writer.WriteInt32(KnownAbilities.Count);
			foreach (Ability ability in KnownAbilities.Values)
			{
				writer.WriteInt64(ability.ID);
				writer.WriteInt32(ability.Template.ID);

				writer.WriteInt32(ability.AbilityEvents.Count);
				foreach (int abilityEvent in ability.AbilityEvents.Keys)
				{
					writer.WriteInt32(abilityEvent);
				}
			}

			if (Character.TryGet(out ICooldownController cooldownController))
			{
				cooldownController.Write(writer);
			}
		}

		/// <summary>
		/// Called on each network tick to replicate input and reconcile state.
		/// </summary>
		private void TimeManager_OnTick()
		{
			Replicate(HandleCharacterInput());
			CreateReconcile();
		}

		/// <summary>
		/// Creates and sends a reconcile state for the ability controller to synchronize client/server state.
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
													 attributeController.GetResourceState());
				}
				Reconcile(state);
			}
		}

		/// <summary>
		/// Gets the current ability type, considering any type override.
		/// </summary>
		/// <returns>The current <see cref="AbilityType"/> if an ability is active, otherwise <see cref="AbilityType.None"/>.</returns>
		public AbilityType GetCurrentAbilityType()
		{
			if (currentAbilityID != NO_ABILITY &&
				KnownAbilities.TryGetValue(currentAbilityID, out Ability currentAbility))
			{
				return currentAbility.TypeOverride != null ? currentAbility.TypeOverride.OverrideAbilityType : currentAbility.Template.Type;
			}
			return AbilityType.None;
		}

		/// <summary>
		/// Checks if the current ability type is an aerial type (AerialPhysical or AerialMagic).
		/// </summary>
		/// <returns>True if the current ability is aerial, false otherwise.</returns>
		public bool IsCurrentAbilityTypeAerial()
		{
			AbilityType abilityType = GetCurrentAbilityType();
			switch (abilityType)
			{
				case AbilityType.AerialPhysical:
				case AbilityType.AerialMagic:
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// Gets the appropriate attribute template for activation speed reduction based on the ability type.
		/// </summary>
		/// <param name="ability">The ability to check.</param>
		/// <returns>The attribute template for speed reduction.</returns>
		public CharacterAttributeTemplate GetActivationAttributeTemplate(Ability ability)
		{
			AbilityType abilityType = ability.TypeOverride != null ? ability.TypeOverride.OverrideAbilityType : ability.Template.Type;

			switch (abilityType)
			{
				case AbilityType.None:
				case AbilityType.Physical:
				case AbilityType.GroundedPhysical:
				case AbilityType.AerialPhysical:
					return AttackSpeedReductionTemplate;
				default:
					return CastSpeedReductionTemplate;
			}
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

			int activationFlags = 0;

			activationFlags.EnableBit(AbilityActivationFlags.IsActualData);
			if (interruptQueued)
			{
				activationFlags.EnableBit(AbilityActivationFlags.Interrupt);

				interruptQueued = false;
			}

			AbilityActivationReplicateData activationEventData = new AbilityActivationReplicateData(activationFlags,
																									 queuedAbilityID,
																									 heldKey);
			// Clear the locally queued data
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
						activationData.HeldKey = lastCreatedData.HeldKey;
					}
				}
				else if (state.ContainsTicked())
				{
					lastCreatedData.Dispose();
					lastCreatedData = activationData;
				}
			}

			float deltaTime = (float)base.TimeManager.TickDelta;

			if (Character.TryGet(out ICharacterAttributeController attributeController))
			{
				attributeController.Regenerate(deltaTime);
			}

			// If we have an interrupt queued
			if (activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.Interrupt))
			{
				Log.Debug("AbilityController", "Interrupting");
				OnInterrupt?.Invoke();
				Cancel();
				return;
			}

			// If we aren't activating anything
			if (!IsActivating)
			{
				// Try to activate the queued ability
				if (CanActivate(activationData.QueuedAbilityID, out Ability newAbility))
				{
					//Log.Debug($"1 New Ability Activation:{newAbility.ID} State:{state} Tick:{activationData.GetTick()}");
					currentAbilityID = newAbility.ID;
					remainingTime = newAbility.ActivationTime * CalculateSpeedReduction(GetActivationAttributeTemplate(newAbility));

					heldKey = activationData.HeldKey;
				}
				else
				{
					return;
				}
			}

			// Process ability activation
			if (IsActivating && CanActivate(currentAbilityID, out Ability validatedAbility))
			{
				if (remainingTime > 0.0f)
				{
					//Log.Debug($"2 Activating {validatedAbility.ID} State: {state}");

					// Handle ability updates here, display cast bar, display hitbox telegraphs, etc
					if (state.IsTickedCreated())
					{
						OnUpdate?.Invoke(validatedAbility.Name, remainingTime, validatedAbility.ActivationTime * CalculateSpeedReduction(GetActivationAttributeTemplate(validatedAbility)));
					}

					// Handle held ability updates
					if (heldKey != KeyCode.None)
					{
						// The Held ability hotkey was released or the character can no longer activate the ability
						if (activationData.HeldKey == KeyCode.None)
						{
							// Add ability to cooldowns
							AddCooldown(validatedAbility);

							// Reset ability data
							Cancel();
							return;
						}

						// Channeled abilities like beam effects or a charge rush that are continuously updating or spawning objects should be handled here
						if (ChanneledTemplate != null &&
							validatedAbility.HasAbilityEvent(ChanneledTemplate.ID))
						{
							// Handle PC targetting and ability spawning
							if (PlayerCharacter != null &&
								Character.TryGet(out ITargetController t))
							{
								// Get target info
								TargetInfo targetInfo = t.UpdateTarget(PlayerCharacter.CharacterController.VirtualCameraPosition,
																		   PlayerCharacter.CharacterController.VirtualCameraRotation * Vector3.forward,
																		   validatedAbility.Range);

								// Spawn the ability object
								AbilityObject.Spawn(validatedAbility, PlayerCharacter, AbilitySpawner, targetInfo, currentSeed);

								// Generate a new seed
								currentSeed = abilitySeedGenerator.Next();

								//Log.Debug($"3 New Ability Seed {currentSeed}");

								// Channeled abilities consume resources during activation

								//Log.Debug($"4 Consumed On Tick: {activationData.GetTick()} State: {state}");
								validatedAbility.ConsumeResources(Character, BloodResourceConversionTemplate);
							}
							// Handle NPC targetting and ability spawning
							else
							{

							}
						}
					}

					remainingTime -= deltaTime;
					return;
				}

				// Return immediately if we are charging our attack
				if (ChargedTemplate != null &&
					validatedAbility.HasAbilityEvent(ChargedTemplate.ID) &&
					heldKey != KeyCode.None &&
					activationData.HeldKey != KeyCode.None)
				{
					return;
				}

				// Handle PC targetting and ability spawning
				if (PlayerCharacter != null &&
					Character.TryGet(out ITargetController tc))
				{
					// Get target info
					TargetInfo targetInfo = tc.UpdateTarget(PlayerCharacter.CharacterController.VirtualCameraPosition,
														   PlayerCharacter.CharacterController.VirtualCameraRotation * Vector3.forward,
														   validatedAbility.Range);

					// Spawn the ability object
					AbilityObject.Spawn(validatedAbility, PlayerCharacter, AbilitySpawner, targetInfo, currentSeed);

					// Generate a new seed
					currentSeed = abilitySeedGenerator.Next();

					//Log.Debug($"5 New Ability Seed {currentSeed}");
				}
				// Handle NPC targetting and ability spawning
				else
				{

				}

				// Consume resources
				//Log.Debug($"6 Consumed On Tick: {activationData.GetTick()} State: {state}");
				validatedAbility.ConsumeResources(Character, BloodResourceConversionTemplate);

				// Add ability to cooldowns
				AddCooldown(validatedAbility);

				// Reset ability data
				Cancel();
			}
		}

		/// <summary>
		/// Reconciles the ability controller's state from the server, applying ability and resource state.
		/// </summary>
		/// <param name="rd">The reconcile data from the server.</param>
		/// <param name="channel">The network channel.</param>
		[Reconcile]
		private void Reconcile(AbilityReconcileData rd, Channel channel = Channel.Unreliable)
		{
			//Log.Debug($"Reconciled: {rd.GetTick()}");
			currentAbilityID = rd.AbilityID;
			remainingTime = rd.RemainingTime;

			if (Character.TryGet(out ICharacterAttributeController attributeController))
			{
				attributeController.ApplyResourceState(rd.ResourceState);
			}
		}

		/// <summary>
		/// Calculates the speed reduction factor for ability activation based on the given attribute.
		/// </summary>
		/// <param name="attribute">The attribute template to use for calculation.</param>
		/// <returns>The speed reduction multiplier (1.0 = no reduction).</returns>
		public float CalculateSpeedReduction(CharacterAttributeTemplate attribute)
		{
			if (attribute != null &&
				Character.TryGet(out ICharacterAttributeController attributeController))
			{
				CharacterAttribute speedReduction;
				if (attributeController.TryGetAttribute(attribute.ID, out speedReduction))
				{
					return 1.0f - (attribute.InitialValueAsPct - speedReduction.FinalValueAsPct.Clamp(0.0f, 0.9f));
				}
			}
			return 1.0f;
		}

		/// <summary>
		/// Queues an interrupt for the current ability, to be processed on the next tick.
		/// </summary>
		/// <param name="attacker">The character causing the interrupt (not used).</param>
		public void Interrupt(ICharacter attacker)
		{
			interruptQueued = true;
		}

		/// <summary>
		/// Attempts to activate an ability by reference ID and held key, if all conditions are met.
		/// </summary>
		/// <param name="referenceID">The ability reference ID to activate.</param>
		/// <param name="heldKey">The key currently held for activation.</param>
		public void Activate(long referenceID, KeyCode heldKey)
		{
			if (!CanActivate(referenceID, out Ability validatedAbility))
			{
				return;
			}

			// Don't activate spells when hovering over UI controls.
			if (!(OnCanManipulate == null ? true : (bool)OnCanManipulate?.Invoke()))
			{
				//Log.Debug("Cannot activate");
				return;
			}

			// Ensure we are not already activating an ability or an interrupt is waiting to be processed
			if (!AbilityQueued &&
				!IsActivating &&
				!interruptQueued)
			{
				//Log.Debug("Activating " + referenceID);
				queuedAbilityID = referenceID;
				this.heldKey = heldKey;
			}
		}

		/// <summary>
		/// Validates that we can manipulate the ability controller, we know the ability, and that we meet the requirements to use the ability.
		/// </summary>
		private bool CanActivate(long abilityID, out Ability validatedAbility)
		{
			validatedAbility = null;

			if (abilityID == NO_ABILITY)
			{
				//Log.Debug("NO Ability.");
				return false;
			}
			if (!CanManipulate())
			{
				//Log.Debug("Can't manipulate.");
				return false;
			}
			if (!KnownAbilities.TryGetValue(abilityID, out validatedAbility))
			{
				//Log.Debug("Trying to activate an unknown ability.");
				return false;
			}
			if (!Character.TryGet(out ICharacterDamageController damageController) ||
				!damageController.IsAlive)
			{
				//Log.Debug("Cannot activate an ability while dead.");
				return false;
			}
			if (!Character.TryGet(out ICooldownController cooldownController) ||
				cooldownController.IsOnCooldown(validatedAbility.ID))
			{
				//Log.Debug("Ability is cooling down.");
				return false;
			}

			AbilityType abilityType = validatedAbility.TypeOverride != null ? validatedAbility.TypeOverride.OverrideAbilityType : validatedAbility.Template.Type;
			switch (abilityType)
			{
				case AbilityType.GroundedMagic:
				case AbilityType.GroundedPhysical:
					if (PlayerCharacter != null &&
						!PlayerCharacter.Motor.GroundingStatus.IsStableOnGround)
					{
						return false;
					}
					break;
				case AbilityType.AerialMagic:
				case AbilityType.AerialPhysical:
					if (PlayerCharacter != null &&
						PlayerCharacter.Motor.GroundingStatus.IsStableOnGround)
					{
						return false;
					}
					break;
				default: break;
			}

			// Check if the character already has a pet
			PetAbilityTemplate petAbilityTemplate = validatedAbility.Template as PetAbilityTemplate;
			if (petAbilityTemplate != null &&
				Character.TryGet(out IPetController petController) &&
				petController.Pet != null)
			{
				return false;
			}

			if (!validatedAbility.MeetsRequirements(Character) ||
				!validatedAbility.HasResource(Character, BloodResourceConversionTemplate))
			{
				//Log.Debug("Not enough resources.");
				return false;
			}
			return true;
		}

		/// <summary>
		/// Cancels the current ability activation and resets all related state.
		/// </summary>
		internal void Cancel()
		{
			//Log.Debug("Cancel");
			currentAbilityID = NO_ABILITY;
			remainingTime = 0.0f;
			heldKey = KeyCode.None;

			OnCancel?.Invoke();
		}

		/// <summary>
		/// Adds a cooldown for the given ability using the cooldown controller.
		/// </summary>
		/// <param name="ability">The ability to add a cooldown for.</param>
		internal void AddCooldown(Ability ability)
		{
			if (ability.Cooldown > 0.0f &&
				Character.TryGet(out ICooldownController cooldownController))
			{
				float cooldownReduction = CalculateSpeedReduction(CooldownReductionTemplate);
				float cooldown = ability.Cooldown * cooldownReduction;

				cooldownController.AddCooldown(ability.ID, new CooldownInstance(cooldown));
			}
		}

		/// <summary>
		/// Removes the ability with the given reference ID from the known abilities set.
		/// </summary>
		/// <param name="referenceID">The ability reference ID to remove.</param>
		public void RemoveAbility(int referenceID)
		{
			KnownAbilities.Remove(referenceID);
		}

		/// <summary>
		/// Checks if the character is in a valid state to manipulate abilities (not teleporting, not despawned, etc).
		/// </summary>
		/// <returns>True if the character can manipulate abilities, false otherwise.</returns>
		public bool CanManipulate()
		{
			if (Character == null ||
				Character.IsTeleporting ||
				!Character.IsSpawned)
				return false;

			/*if (!character.IsSafeZone &&
				  (character.State == CharacterState.Idle ||
				  character.State == CharacterState.Moving) &&
				  character.State != CharacterState.UsingObject &&
				  character.State != CharacterState.IsFrozen &&
				  character.State != CharacterState.IsStunned &&
				  character.State != CharacterState.IsMesmerized) return true;
			*/
			return true;
		}

		/// <summary>
		/// Checks if the controller knows a base ability with the given template ID.
		/// </summary>
		/// <param name="templateID">The base ability template ID to check.</param>
		/// <returns>True if the base ability is known, false otherwise.</returns>
		public bool KnowsAbility(int templateID)
		{
			return KnownBaseAbilities?.Contains(templateID) ?? false;
		}

		/// <summary>
		/// Adds the provided base ability templates to the known base abilities set.
		/// </summary>
		/// <param name="abilityTemplates">List of base ability templates to learn.</param>
		/// <returns>True if any templates were learned, false if input is null.</returns>
		public bool LearnBaseAbilities(List<BaseAbilityTemplate> abilityTemplates = null)
		{
			if (abilityTemplates == null)
			{
				return false;
			}

			for (int i = 0; i < abilityTemplates.Count; ++i)
			{
				BaseAbilityTemplate template = abilityTemplates[i];
				if (template != null)
				{
					if (!KnownBaseAbilities.Contains(template.ID))
					{
						KnownBaseAbilities.Add(template.ID);
					}
				}
			}
			return true;
		}

		/// <summary>
		/// Checks if the controller knows the specified ability event by event ID.
		/// </summary>
		/// <param name="eventID">The event template ID to check.</param>
		/// <returns>True if the event is known, false otherwise.</returns>
		public bool KnowsAbilityEvent(int eventID)
		{
			return KnownAbilityEvents?.Contains(eventID) ?? false;
		}

		/// <summary>
		/// Adds the provided ability events to the known event sets, categorizing them by event type.
		/// </summary>
		/// <param name="abilityEvents">List of ability events to learn.</param>
		/// <returns>True if any events were learned, false if input is null.</returns>
		public bool LearnAbilityEvents(List<AbilityEvent> abilityEvents = null)
		{
			if (abilityEvents == null)
			{
				return false;
			}

			foreach (var abilityEvent in abilityEvents)
			{
				if (abilityEvent == null) continue;

				KnownAbilityEvents.Add(abilityEvent.ID);

				// Categorize the event by its specific type for fast lookup.
				switch (abilityEvent)
				{
					case AbilityOnTickEvent _:
						KnownAbilityOnTickEvents.Add(abilityEvent.ID);
						break;
					case AbilityOnHitEvent _:
						KnownAbilityOnHitEvents.Add(abilityEvent.ID);
						break;
					case AbilityOnPreSpawnEvent _:
						KnownAbilityOnPreSpawnEvents.Add(abilityEvent.ID);
						break;
					case AbilityOnSpawnEvent _:
						KnownAbilityOnSpawnEvents.Add(abilityEvent.ID);
						break;
					case AbilityOnDestroyEvent _:
						KnownAbilityOnDestroyEvents.Add(abilityEvent.ID);
						break;
				}
			}
			return true;
		}

		/// <summary>
		/// Checks if the controller knows an ability instance with the given template ID.
		/// </summary>
		/// <param name="templateID">The template ID to check.</param>
		/// <returns>True if the ability is known, false otherwise.</returns>
		public bool KnowsLearnedAbility(int templateID)
		{
			return KnownAbilities?.ContainsKey(templateID) ?? false;
		}

		/// <summary>
		/// Adds the given ability to the known abilities set, and applies any remaining cooldown if specified.
		/// </summary>
		/// <param name="ability">The ability to learn.</param>
		/// <param name="remainingCooldown">Optional remaining cooldown to apply to the ability.</param>
		public void LearnAbility(Ability ability, float remainingCooldown = 0.0f)
		{
			if (ability == null)
			{
				return;
			}
			KnownAbilities[ability.ID] = ability;

			// If a cooldown is specified, apply it to the cooldown controller.
			if (remainingCooldown > 0.0f &&
				Character.TryGet(out ICooldownController cooldownController))
			{
				float cooldownReduction = CalculateSpeedReduction(CooldownReductionTemplate);
				float cooldown = ability.Cooldown * cooldownReduction;

				cooldownController.AddCooldown(ability.ID, new CooldownInstance(cooldown, remainingCooldown));
			}
		}
	}
}