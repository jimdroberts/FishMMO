﻿using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	public class AbilityController : CharacterBehaviour, IAbilityController
	{
		private static System.Random playerSeedGenerator = new System.Random();

		public const long NO_ABILITY = 0;

		private long currentAbilityID;
		private bool interruptQueued;
		private long queuedAbilityID;
		private float remainingTime;
		private KeyCode heldKey;

		private System.Random abilitySeedGenerator;
		private int abilitySeed = 0;
		private int currentSeed = 0;

		public Transform AbilitySpawner;
		public CharacterAttributeTemplate AttackSpeedReductionTemplate;
		public CharacterAttributeTemplate CastSpeedReductionTemplate;
		public CharacterAttributeTemplate CooldownReductionTemplate;
		public AbilityEvent BloodResourceConversionTemplate;
		public AbilityEvent ChargedTemplate;
		public AbilityEvent ChanneledTemplate;

		public event Func<bool> OnCanManipulate;

		// Handle ability updates here, display cast bar, display hitbox telegraphs, etc
		public event Action<string, float, float> OnUpdate;
		// Invoked when the current ability is Interrupted.
		public event Action OnInterrupt;
		// Invoked when the current ability is Cancelled.
		public event Action OnCancel;
		// UI
		public event Action OnReset;
		public event Action<Ability> OnAddAbility;
		public event Action<BaseAbilityTemplate> OnAddKnownAbility;

		public Dictionary<long, Ability> KnownAbilities { get; private set; }
		public HashSet<int> KnownBaseAbilities { get; private set; }
		public HashSet<int> KnownEvents { get; private set; }
		public HashSet<int> KnownSpawnEvents { get; private set; }
		public HashSet<int> KnownHitEvents { get; private set; }
		public HashSet<int> KnownMoveEvents { get; private set; }
		public bool IsActivating { get { return currentAbilityID != NO_ABILITY; } }
		public bool AbilityQueued { get { return queuedAbilityID != NO_ABILITY; } }

		public override void OnAwake()
		{
			base.OnAwake();

			KnownAbilities = new Dictionary<long, Ability>();
			KnownBaseAbilities = new HashSet<int>();
			KnownEvents = new HashSet<int>();
			KnownSpawnEvents = new HashSet<int>();
			KnownHitEvents = new HashSet<int>();
			KnownMoveEvents = new HashSet<int>();

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
			KnownEvents.Clear();
			KnownSpawnEvents.Clear();
			KnownHitEvents.Clear();
			KnownMoveEvents.Clear();
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
			KnownEvents.Clear();
			KnownSpawnEvents.Clear();
			KnownHitEvents.Clear();
			KnownMoveEvents.Clear();

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

		private void TimeManager_OnTick()
		{
			Replicate(HandleCharacterInput());
			CreateReconcile();
		}

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

		public AbilityType GetCurrentAbilityType()
		{
			if (currentAbilityID != NO_ABILITY &&
				KnownAbilities.TryGetValue(currentAbilityID, out Ability currentAbility))
			{
				return currentAbility.TypeOverride != null ? currentAbility.TypeOverride.OverrideAbilityType : currentAbility.Template.Type;
			}
			return AbilityType.None;
		}

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

		public void Interrupt(ICharacter attacker)
		{
			interruptQueued = true;
		}

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

		internal void Cancel()
		{
			//Log.Debug("Cancel");
			currentAbilityID = NO_ABILITY;
			remainingTime = 0.0f;
			heldKey = KeyCode.None;

			OnCancel?.Invoke();
		}

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

		public void RemoveAbility(int referenceID)
		{
			KnownAbilities.Remove(referenceID);
		}

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

		public bool KnowsAbility(int abilityID)
		{
			if ((KnownBaseAbilities != null && KnownBaseAbilities.Contains(abilityID)) ||
				(KnownEvents != null && KnownEvents.Contains(abilityID)))
			{
				return true;
			}
			return false;
		}

		public bool LearnBaseAbilities(List<BaseAbilityTemplate> abilityTemplates = null)
		{
			if (abilityTemplates == null)
			{
				return false;
			}

			for (int i = 0; i < abilityTemplates.Count; ++i)
			{
				// If the template is an ability event we add them to their mapped containers
				AbilityEvent abilityEvent = abilityTemplates[i] as AbilityEvent;
				if (abilityEvent != null)
				{
					// Add the event to the global events map
					if (!KnownEvents.Contains(abilityEvent.ID))
					{
						KnownEvents.Add(abilityEvent.ID);
					}

					switch (abilityEvent)
					{
						case HitEvent:
							if (!KnownHitEvents.Contains(abilityEvent.ID))
							{
								KnownHitEvents.Add(abilityEvent.ID);
							}
							break;
						case MoveEvent:
							if (!KnownMoveEvents.Contains(abilityEvent.ID))
							{
								KnownMoveEvents.Add(abilityEvent.ID);
							}
							break;
						case SpawnEvent:
							if (!KnownSpawnEvents.Contains(abilityEvent.ID))
							{
								KnownSpawnEvents.Add(abilityEvent.ID);
							}
							break;
					}
				}
				else
				{
					AbilityTemplate abilityTemplate = abilityTemplates[i] as AbilityTemplate;
					if (abilityTemplate != null)
					{
						if (!KnownBaseAbilities.Contains(abilityTemplate.ID))
						{
							KnownBaseAbilities.Add(abilityTemplate.ID);
						}
					}
				}
			}
			return true;
		}

		public bool KnowsLearnedAbility(int templateID)
		{
			if (KnownAbilities == null)
			{
				return false;
			}
			KeyValuePair<long, Ability>? found = KnownAbilities.Where(x => x.Value.Template.ID == templateID)
															   .Select(x => (KeyValuePair<long, Ability>?)x)
															   .FirstOrDefault();
			return found != null;
		}

		public void LearnAbility(Ability ability, float remainingCooldown = 0.0f)
		{
			if (ability == null)
			{
				return;
			}
			KnownAbilities[ability.ID] = ability;

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