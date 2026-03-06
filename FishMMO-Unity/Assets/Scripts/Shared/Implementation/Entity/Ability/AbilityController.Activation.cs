using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Partial class for AbilityController handling activation logic, including starting, processing,
	/// spawning, validating, cancelling abilities, and consumable activation.
	/// </summary>
	public partial class AbilityController
	{
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
		/// Returns true if the given ability requires held input (channeled or charged).
		/// AI should pass the result as the isHeld parameter when calling Activate().
		/// </summary>
		/// <param name="abilityID">The ability ID to check.</param>
		public bool RequiresHeld(long abilityID)
		{
			if (!KnownAbilities.TryGetValue(abilityID, out Ability ability))
				return false;

			if (ChanneledTemplate != null && ability.HasAbilityEvent(ChanneledTemplate.ID))
				return true;

			if (ChargedTemplate != null && ability.HasAbilityEvent(ChargedTemplate.ID))
				return true;

			return false;
		}

		/// <summary>
		/// Regenerates character attributes (mana, stamina, etc.) each tick.
		/// </summary>
		private void RegenerateAttributes(float deltaTime)
		{
			if (Character.TryGet(out ICharacterAttributeController attributeController))
			{
				attributeController.Regenerate(deltaTime);
			}
		}

		/// <summary>
		/// Processes an interrupt flag from the activation data. Returns true if an interrupt
		/// was processed and the caller should return immediately.
		/// </summary>
		private bool ProcessInterrupt(AbilityActivationReplicateData activationData)
		{
			if (activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.Interrupt))
			{
				Log.Debug("AbilityController", "Interrupting");
				OnInterrupt?.Invoke();
				Cancel();
				return true;
			}
			return false;
		}

		/// <summary>
		/// Attempts to start a new ability from the queued activation data. Sets the current
		/// ability, remaining time, and held state. Returns true if an ability was started.
		/// </summary>
		private bool TryStartAbility(AbilityActivationReplicateData activationData)
		{
			if (CanActivate(activationData.QueuedAbilityID, out Ability newAbility))
			{
				//Log.Debug($"1 New Ability Activation:{newAbility.ID} State:{state} Tick:{activationData.GetTick()}");
				currentAbilityID = newAbility.ID;
				remainingTime = newAbility.ActivationTime * CalculateSpeedReduction(GetActivationAttributeTemplate(newAbility));
				if (activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsHeld))
				{
					inputFlags.EnableBit(AbilityActivationFlags.IsHeld);
				}
				else
				{
					inputFlags.DisableBit(AbilityActivationFlags.IsHeld);
				}
				return true;
			}
			return false;
		}

		/// <summary>
		/// Processes the currently active ability each tick. Handles the activation countdown,
		/// held/channeled ability updates, charged ability hold, and final ability spawning.
		/// </summary>
		private void ProcessActiveAbility(AbilityActivationReplicateData activationData, ReplicateState state, float deltaTime)
		{
			if (!IsActivating || !CanActivate(currentAbilityID, out Ability validatedAbility))
			{
				return;
			}

			if (remainingTime > 0.0f)
			{
				UpdateActivation(activationData, state, validatedAbility, deltaTime);
				return;
			}

			// Return immediately if we are charging our attack
			if (ChargedTemplate != null &&
				validatedAbility.HasAbilityEvent(ChargedTemplate.ID) &&
				inputFlags.IsFlagged(AbilityActivationFlags.IsHeld) &&
				activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsHeld))
			{
				return;
			}

			// Activation complete — spawn the ability and finish
			FinishAbility(validatedAbility, activationData);
		}

		/// <summary>
		/// Updates an ability that is still activating (remainingTime > 0). Handles UI updates,
		/// held ability release checks, and channeled ability spawning during activation.
		/// </summary>
		private void UpdateActivation(AbilityActivationReplicateData activationData, ReplicateState state, Ability validatedAbility, float deltaTime)
		{
			//Log.Debug($"2 Activating {validatedAbility.ID} State: {state}");

			// Handle ability updates here, display cast bar, display hitbox telegraphs, etc
			if (state.IsTickedCreated())
			{
				OnUpdate?.Invoke(validatedAbility.Name, remainingTime, validatedAbility.ActivationTime * CalculateSpeedReduction(GetActivationAttributeTemplate(validatedAbility)));
			}

			// Handle held ability updates
			if (inputFlags.IsFlagged(AbilityActivationFlags.IsHeld))
			{
				// The Held ability hotkey was released or the character can no longer activate the ability
				if (!activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsHeld))
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
					SpawnChanneledAbility(validatedAbility, activationData);
				}
			}

			remainingTime -= deltaTime;
		}

		/// <summary>
		/// Spawns a channeled ability object during activation (e.g., beam effects, continuous damage).
		/// Handles both PC and NPC targeting paths.
		/// </summary>
		private void SpawnChanneledAbility(Ability validatedAbility, AbilityActivationReplicateData activationData)
		{
			// Handle PC targetting and ability spawning
			if (PlayerCharacter != null &&
				Character.TryGet(out ITargetController t))
			{
				// Read camera from KCCController. Guaranteed fresh because AbilityController runs on OnPostTick after KCCPlayer processes on OnTick.
				Vector3 cameraPosition = PlayerCharacter.CharacterController.VirtualCameraPosition;
				Quaternion cameraRotation = PlayerCharacter.CharacterController.VirtualCameraRotation;

				TargetInfo targetInfo = t.UpdateTarget(cameraPosition,
														   cameraRotation * Vector3.forward,
														   validatedAbility.Range);

				AbilityObject.Spawn(validatedAbility, PlayerCharacter, AbilitySpawner, targetInfo, currentSeed, activationData.GetTick());

				currentSeed = abilitySeedGenerator.Next();

				//Log.Debug($"3 New Ability Seed {currentSeed}");

				// Channeled abilities consume resources during activation

				//Log.Debug($"4 Consumed On Tick: {activationData.GetTick()} State: {state}");
				validatedAbility.ConsumeResources(Character, BloodResourceConversionTemplate);
			}
			// Handle NPC targetting and ability spawning
			else if (Character.TryGet(out IAIController channelAI))
			{
				AIController channelAIController = channelAI as AIController;
				if (channelAIController != null && Character.TryGet(out ITargetController channelTC))
				{
					TargetInfo targetInfo = channelTC.UpdateTarget(
						channelAIController.VirtualCameraPosition,
						channelAIController.VirtualCameraRotation * Vector3.forward,
						validatedAbility.Range);

					AbilityObject.SpawnNPC(validatedAbility, Character, AbilitySpawner, targetInfo, currentSeed, activationData.GetTick());

					currentSeed = abilitySeedGenerator.Next();

					// Channeled abilities consume resources during activation
					validatedAbility.ConsumeResources(Character, BloodResourceConversionTemplate);
				}
			}
		}

		/// <summary>
		/// Completes an ability activation: spawns the final ability object, consumes resources,
		/// adds cooldown, and resets ability state.
		/// </summary>
		private void FinishAbility(Ability validatedAbility, AbilityActivationReplicateData activationData)
		{
			SpawnAbilityObject(validatedAbility, activationData);

			// Consume resources
			//Log.Debug($"6 Consumed On Tick: {activationData.GetTick()} State: {state}");
			validatedAbility.ConsumeResources(Character, BloodResourceConversionTemplate);

			// Add ability to cooldowns
			AddCooldown(validatedAbility);

			// Reset ability data
			Cancel();
		}

		/// <summary>
		/// Spawns the final ability object on activation completion. Handles both PC (camera-based)
		/// and NPC (virtual-camera-based) targeting paths.
		/// </summary>
		private void SpawnAbilityObject(Ability validatedAbility, AbilityActivationReplicateData activationData)
		{
			// Handle PC targetting and ability spawning
			if (PlayerCharacter != null &&
				Character.TryGet(out ITargetController tc))
			{
				// Read camera from KCCController. Guaranteed fresh because AbilityController runs on OnPostTick after KCCPlayer processes on OnTick.
				Vector3 cameraPosition = PlayerCharacter.CharacterController.VirtualCameraPosition;
				Quaternion cameraRotation = PlayerCharacter.CharacterController.VirtualCameraRotation;

				TargetInfo targetInfo = tc.UpdateTarget(cameraPosition,
													   cameraRotation * Vector3.forward,
													   validatedAbility.Range);

				AbilityObject.Spawn(validatedAbility, PlayerCharacter, AbilitySpawner, targetInfo, currentSeed, activationData.GetTick());

				currentSeed = abilitySeedGenerator.Next();

				//Log.Debug($"5 New Ability Seed {currentSeed}");
			}
			// Handle NPC targetting and ability spawning
			else if (Character.TryGet(out IAIController spawnAI))
			{
				AIController spawnAIController = spawnAI as AIController;
				if (spawnAIController != null && Character.TryGet(out ITargetController npcTC))
				{
					TargetInfo targetInfo = npcTC.UpdateTarget(
						spawnAIController.VirtualCameraPosition,
						spawnAIController.VirtualCameraRotation * Vector3.forward,
						validatedAbility.Range);

					AbilityObject.SpawnNPC(validatedAbility, Character, AbilitySpawner, targetInfo, currentSeed, activationData.GetTick());

					currentSeed = abilitySeedGenerator.Next();
				}
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

			if (!validatedAbility.MeetsActivationConditions(Character) ||
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
			inputFlags.DisableBit(AbilityActivationFlags.IsHeld);

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
		/// Queues a consumable item for activation through the replicate pipeline.
		/// Sets the IsConsumable flag and stores the consumable template ID in the ability queue.
		/// </summary>
		/// <param name="item">The consumable item to activate.</param>
		public void ActivateConsumable(Item item)
		{
			if (item == null) return;

			ConsumableTemplate consumable = item.Template as ConsumableTemplate;
			if (consumable == null) return;

			if (!CanManipulate()) return;

			// Don't activate when hovering over UI controls.
			if (OnCanManipulate != null && !OnCanManipulate.Invoke()) return;

			// Ensure we are not already activating an ability or an interrupt is waiting to be processed
			if (!AbilityQueued &&
				!IsActivating &&
				!inputFlags.IsFlagged(AbilityActivationFlags.Interrupt))
			{
				queuedAbilityID = consumable.ID;
				inputFlags.EnableBit(AbilityActivationFlags.IsConsumable);
			}
		}

		/// <summary>
		/// Queues an interrupt for the current ability, to be processed on the next tick.
		/// </summary>
		/// <param name="attacker">The character causing the interrupt (not used).</param>
		public void Interrupt(ICharacter attacker)
		{
			inputFlags.EnableBit(AbilityActivationFlags.Interrupt);
		}

		/// <summary>
		/// Releases the held state for the current ability. For charged abilities this
		/// triggers the release (fire). For channeled abilities this stops the channel early.
		/// </summary>
		public void Release()
		{
			inputFlags.DisableBit(AbilityActivationFlags.IsHeld);
		}

		/// <summary>
		/// Attempts to activate an ability by reference ID and held state, if all conditions are met.
		/// </summary>
		/// <param name="referenceID">The ability reference ID to activate.</param>
		/// <param name="isHeld">Whether the activation key is held.</param>
		public void Activate(long referenceID, bool isHeld)
		{
			if (!CanActivate(referenceID, out Ability validatedAbility))
			{
				return;
			}

			// Don't activate spells when hovering over UI controls.
			if (OnCanManipulate != null && !OnCanManipulate.Invoke())
			{
				//Log.Debug("Cannot activate");
				return;
			}

			// Ensure we are not already activating an ability or an interrupt is waiting to be processed
			if (!AbilityQueued &&
				!IsActivating &&
				!inputFlags.IsFlagged(AbilityActivationFlags.Interrupt))
			{
				//Log.Debug("Activating " + referenceID);
				queuedAbilityID = referenceID;
				if (isHeld)
				{
					inputFlags.EnableBit(AbilityActivationFlags.IsHeld);
				}
				else
				{
					inputFlags.DisableBit(AbilityActivationFlags.IsHeld);
				}
			}
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

			return true;
		}
	}
}