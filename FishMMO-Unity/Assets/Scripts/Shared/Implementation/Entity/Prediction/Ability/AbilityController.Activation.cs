using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishNet.Object.Prediction;
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
		/// Invoked on both client and server after a consumable is successfully consumed via this
		/// controller's activation pipeline. Server-side subscribers should send the appropriate
		/// <see cref="InventorySetItemBroadcast"/> or <see cref="InventoryRemoveItemBroadcast"/>
		/// to the owning client. Client-side subscribers can use this for UI feedback or validation.
		/// Parameters: (character, consumableTemplateID, inventorySlot).
		/// </summary>
		public event Action<ICharacter, int, int> OnConsumableUsed;

		/// <summary>
		/// Gets the current ability type, considering any type override.
		/// </summary>
		/// <returns>The current <see cref="AbilityType"/> if an ability is active, otherwise <see cref="AbilityType.None"/>.</returns>
		public AbilityType GetCurrentAbilityType()
		{
			if (currentAbilityID != NO_ABILITY &&
				KnownAbilities.TryGetValue(currentAbilityID, out Ability currentAbility))
			{
				return currentAbility.EffectiveType;
			}
			return AbilityType.None;
		}

		/// <summary>
		/// Gets the appropriate attribute template for activation speed reduction based on the ability type.
		/// Physical variants (including <see cref="AbilityType.None"/>) use
		/// <see cref="AttackSpeedReductionTemplate"/>; all other variants use
		/// <see cref="CastSpeedReductionTemplate"/>.
		/// </summary>
		/// <param name="ability">The ability to check.</param>
		/// <returns>The attribute template for speed reduction.</returns>
		public CharacterAttributeTemplate GetActivationAttributeTemplate(Ability ability)
		{
			switch (ability.EffectiveType)
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
		/// <remarks>
		/// DESYNC RISK: This method reads live attribute values from
		/// <see cref="ICharacterAttributeController"/>. During prediction replay,
		/// the attribute controller may have reconciled in a different order than
		/// the ability controller. If a buff/debuff modifies cast/attack speed
		/// mid-cast, the <c>remainingTicks</c> computed here during prediction may
		/// differ from the server's value. This is corrected by the reconcile data
		/// which includes <see cref="CharacterReconcileData.RemainingTicks"/> — the
		/// desync is limited to the prediction window.
		/// </remarks>
		public float CalculateSpeedReduction(CharacterAttributeTemplate attribute)
		{
			if (attribute != null &&
				cachedAttributeController != null)
			{
				CharacterAttribute speedReduction;
				if (cachedAttributeController.TryGetAttribute(attribute.ID, out speedReduction))
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
		/// Processes an interrupt flag from the activation data. Returns true if an interrupt
		/// was processed and the caller should return immediately.
		/// OnCancel is suppressed during interrupt because <see cref="OnInterrupt"/> already
		/// fires — UI subscribers that handle both events would otherwise flicker or double-reset.
		/// </summary>
		private bool ProcessInterrupt(AbilityActivationReplicateData activationData, ReplicateState state)
		{
			if (activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.Interrupt))
			{
				Log.Debug("AbilityController", "Interrupting");
				// Only fire UI/audio events on the first execution, not during reconcile replay.
				if (state.IsTickedCreated())
				{
					OnInterrupt?.Invoke();
				}
				Cancel(state, true);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Attempts to start a new ability from the queued activation data. Sets the current
		/// ability, remaining time, and held state. Returns true if an ability was started.
		/// </summary>
		/// <remarks>
		/// <c>remainingTicks</c> is computed once here using the current speed attribute
		/// and never updated mid-cast. If a speed buff is applied or expires during the
		/// cast, the actual cast duration won't change — only the UI cast bar in
		/// <see cref="UpdateActivation"/> will show a different <c>totalTime</c> because
		/// it recomputes from the current attribute each tick. This is intentional:
		/// locking the tick count at cast start keeps client and server deterministic
		/// without needing to reconcile a changing duration.
		/// </remarks>
		private bool TryStartAbility(AbilityActivationReplicateData activationData, ReplicateState state)
		{
			if (CanActivate(activationData.QueuedAbilityID, activationData.GetTick(), out Ability newAbility))
			{
				//Log.Debug($"1 New Ability Activation:{newAbility.ID} State:{state} Tick:{activationData.GetTick()}");
				currentAbilityID = newAbility.ID;
				remainingTicks = (uint)Mathf.CeilToInt(newAbility.ActivationTime * CalculateSpeedReduction(GetActivationAttributeTemplate(newAbility)) / (float)base.TimeManager.TickDelta);
				if (activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsHeld))
				{
					replicatedFlags.EnableBit(AbilityActivationFlags.IsHeld);
				}
				else
				{
					replicatedFlags.DisableBit(AbilityActivationFlags.IsHeld);
				}
				// Only fire the activate ECA on the authoritative first-execution tick.
				// Without the !ContainsReplayed gate, every reconcile replay would re-invoke
				// the trigger graph, double-firing achievements, sound, or DB writes wired
				// into onAbilityActivateTriggers.
				if (base.IsServerStarted && !state.ContainsReplayed())
				{
					AbilityEventData aed = new AbilityEventData(Character, currentAbilityID);
					aed.Add(new TickEventData(Character, activationData.GetPredictionTick()));
					Character.Invoke(onAbilityActivateTriggers, aed);
				}
				return true;
			}
			return false;
		}

		/// <summary>
		/// Validates that the character is still in a valid state to continue an already-started
		/// ability activation. Lighter than <see cref="CanActivate"/> — skips cooldown, resource,
		/// pet, and activation-condition checks that are only relevant at start.
		/// Retains the grounding check because the player can jump/fall mid-cast.
		/// </summary>
		/// <remarks>
		/// NOTE: Resource depletion mid-channel does not interrupt the cast by design.
		/// Channeled abilities consume resources each tick via <see cref="SpawnChanneledAbility"/>,
		/// but this method intentionally does not re-check resource availability. A character
		/// that runs out of mana mid-channel will continue channeling. If this behavior should
		/// change, add a resource check here and in <see cref="ProcessActiveAbility"/>.
		/// // NOTE(cross-ref): keep this in sync with ProcessActiveAbility — if either
		/// // gains a mid-cast resource check, the other must be updated to match.
		/// </remarks>
		private bool ValidateActiveCast(out Ability validatedAbility)
		{
			validatedAbility = null;
			if (currentAbilityID == NO_ABILITY) return false;
			if (!CanManipulate()) return false;
			if (!KnownAbilities.TryGetValue(currentAbilityID, out validatedAbility)) return false;
			if (cachedDamageController == null || !cachedDamageController.IsAlive) return false;

			if (!PassesGroundingCheck(validatedAbility.EffectiveType)) return false;

			return true;
		}

		/// <summary>
		/// Processes the currently active ability each tick. Handles the activation countdown,
		/// held/channeled ability updates, charged ability hold, and final ability spawning.
		/// </summary>
		private void ProcessActiveAbility(AbilityActivationReplicateData activationData, ReplicateState state, float deltaTime)
		{
			if (!IsActivating)
			{
				return;
			}

			if (!ValidateActiveCast(out Ability validatedAbility))
			{
				Cancel(state);
				return;
			}

			if (remainingTicks > 0)
			{
				UpdateActivation(activationData, state, validatedAbility, deltaTime);
				return;
			}

			// Return immediately if we are charging our attack.
			// Both replicatedFlags (previous tick's authoritative state) and
			// activationData.ActivationFlags (current tick's input) must have
			// IsHeld set. This is not redundant — each comes from a different
			// state source. Requiring both prevents a one-tick window where
			// the flag was cleared in one source but not the other from
			// accidentally continuing the hold.
			if (ChargedTemplate != null &&
				validatedAbility.HasAbilityEvent(ChargedTemplate.ID) &&
				replicatedFlags.IsFlagged(AbilityActivationFlags.IsHeld) &&
				activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsHeld))
			{
				return;
			}

			// Activation complete — spawn the ability and finish
			FinishAbility(validatedAbility, activationData, state);
		}

		/// <summary>
		/// Updates an ability that is still activating (remainingTicks > 0). Handles UI updates,
		/// held ability release checks, and channeled ability spawning during activation.
		/// </summary>
		private void UpdateActivation(AbilityActivationReplicateData activationData, ReplicateState state, Ability validatedAbility, float deltaTime)
		{
			//Log.Debug($"2 Activating {validatedAbility.ID} State: {state}");

			// Handle ability updates here, display cast bar, display hitbox telegraphs, etc
			// NOTE: totalTime is recomputed from the current speed attribute, while
			// remainingTicks was locked at cast start. If speed changed mid-cast the
			// bar progress will drift. To fix, cache totalTicks in TryStartAbility
			// and use (remainingTicks / (float)totalTicks) * totalTime here.
			if (state.IsTickedCreated())
			{
				float tickDelta = (float)base.TimeManager.TickDelta;
				float totalTime = validatedAbility.ActivationTime * CalculateSpeedReduction(GetActivationAttributeTemplate(validatedAbility));
				OnUpdate?.Invoke(validatedAbility.Name, remainingTicks * tickDelta, totalTime);
			}

			// Handle held ability updates
			if (replicatedFlags.IsFlagged(AbilityActivationFlags.IsHeld))
			{
				// The Held ability hotkey was released or the character can no longer activate the ability
				if (!activationData.ActivationFlags.IsFlagged(AbilityActivationFlags.IsHeld))
				{
					// Cooldown only on non-replay execution — during reconcile replay
					// the server's authoritative cooldown state is already restored.
					if (!state.ContainsReplayed())
					{
						AddCooldown(validatedAbility, activationData.GetTick());
					}
					// Reset ability data
					Cancel(state);
					return;
				}

				// Channeled abilities like beam effects or a charge rush that are continuously updating or spawning objects should be handled here
				if (ChanneledTemplate != null &&
					validatedAbility.HasAbilityEvent(ChanneledTemplate.ID))
				{
					SpawnChanneledAbility(validatedAbility, activationData, state);
				}
			}

			remainingTicks = remainingTicks > 0 ? remainingTicks - 1 : 0;
		}

		/// <summary>
		/// Resolves targeting, spawns the ability object, and unconditionally advances
		/// the deterministic RNG seed. Camera data is resolved via <see cref="ResolveCameraData"/>.
		/// During reconcile replay the visual spawn is skipped but the seed is still
		/// advanced to keep client/server RNG in lockstep.
		/// </summary>
		private void ResolveTargetAndSpawn(Ability ability, AbilityActivationReplicateData activationData, ReplicateState state)
		{
			// During reconcile replay, spawned objects were already destroyed by
			// DestroyAbilityObjectsAfterTick. Skip the visual spawn but still
			// advance the seed below so RNG state stays deterministic.
			if (!state.ContainsReplayed())
			{
				if (cachedTargetController != null)
				{
					ResolveCameraData(Character, PlayerCharacter, out Vector3 cameraPosition, out Quaternion cameraRotation);

					TargetInfo targetInfo = cachedTargetController.UpdateTarget(cameraPosition,
																			cameraRotation * Vector3.forward,
																			ability.Range);

					AbilityObject.Spawn(ability, Character, AbilitySpawner, targetInfo, currentSeed, activationData.GetTick());
				}
			}

			// Always advance seed regardless of replay state, which path was taken,
			// or whether a spawn actually occurred — maintains deterministic RNG
			// state across client and server.
			currentSeed = abilitySeedGenerator.Next();
		}

		/// <summary>
		/// Spawns a channeled ability object during activation (e.g., beam effects, continuous damage).
		/// Delegates to <see cref="ResolveTargetAndSpawn"/> which handles replay skipping and seed advance.
		/// </summary>
		private void SpawnChanneledAbility(Ability validatedAbility, AbilityActivationReplicateData activationData, ReplicateState state)
		{
			ResolveTargetAndSpawn(validatedAbility, activationData, state);

			// Channeled abilities consume resources every tick, but only on non-replay execution.
			if (!state.ContainsReplayed())
			{
				validatedAbility.ConsumeResources(Character, BloodResourceConversionTemplate);
			}
		}

		/// <summary>
		/// Completes an ability activation: spawns the final ability object, consumes resources,
		/// adds cooldown, and resets ability state.
		/// Resource consumption and cooldown are skipped during reconcile replay because the
		/// server's authoritative state was already restored — re-applying them would
		/// double-subtract resources and restart the cooldown timer.
		/// </summary>
		private void FinishAbility(Ability validatedAbility, AbilityActivationReplicateData activationData, ReplicateState state)
		{
			// Spawn the final ability object (skipped during replay; seed still advanced).
			ResolveTargetAndSpawn(validatedAbility, activationData, state);

			// Resource consumption and cooldown only on non-replay execution.
			if (!state.ContainsReplayed())
			{
				//Log.Debug($"6 Consumed On Tick: {activationData.GetTick()} State: {state}");
				validatedAbility.ConsumeResources(Character, BloodResourceConversionTemplate);
				AddCooldown(validatedAbility, activationData.GetTick());
				if (base.IsServerStarted)
				{
					AbilityEventData aed = new AbilityEventData(Character, validatedAbility.ID);
					aed.Add(new TickEventData(Character, activationData.GetPredictionTick()));
					Character.Invoke(onAbilityCompleteTriggers, aed);
				}
			}

			// Reset ability data
			Cancel(state);
		}

		/// <summary>
		/// Validates that the currently active consumable is still in a valid state to continue.
		/// Resolves the consumable template from <see cref="currentAbilityID"/>.
		/// Named to match the <see cref="ValidateActiveCast"/> convention for abilities.
		/// </summary>
		/// <param name="consumable">The resolved consumable template, or null if validation fails.</param>
		/// <returns>True if the active consumable is still valid, false otherwise.</returns>
		private bool ValidateActiveConsumable(out ConsumableTemplate consumable)
		{
			consumable = null;

			// This method is only called from ProcessActiveConsumable, which already
			// returns early if !IsActivating. No need to check or warn again here.

			if (!CanManipulate())
			{
				return false;
			}

			if (currentAbilityID < int.MinValue || currentAbilityID > int.MaxValue)
			{
				return false;
			}

			consumable = BaseItemTemplate.Get<ConsumableTemplate>((int)currentAbilityID);
			if (consumable == null)
			{
				return false;
			}

			if (cachedDamageController == null ||
				!cachedDamageController.IsAlive)
			{
				return false;
			}

			return true;
		}

		/// <summary>
		/// Attempts to start a consumable activation from the queued replicate data.
		/// Validates the consumable template, cooldown, and item availability.
		/// Locks the inventory slot for the duration of activation.
		/// </summary>
		/// <param name="activationData">The replicate data for this tick.</param>
		/// <returns>True if the consumable activation was started, false otherwise.</returns>
		private bool TryStartConsumable(AbilityActivationReplicateData activationData)
		{
			if (activationData.QueuedAbilityID < int.MinValue || activationData.QueuedAbilityID > int.MaxValue)
			{
				return false;
			}

			int templateID = (int)activationData.QueuedAbilityID;
			if (templateID == NO_ABILITY)
			{
				return false;
			}

			// Consumables are only supported for player characters.
			// NPCs do not have an inventory; FinishConsumable requires PlayerCharacter.
			if (PlayerCharacter == null)
			{
				return false;
			}

			// Deterministic path: only CanManipulate(), not CanManipulateAuthoritative().
			// External handlers (UI MouseMode, etc.) are non-deterministic and must not
			// run during Replicate/replay. The client pre-filter in ActivateConsumable()
			// already checks external handlers before queuing.
			if (!CanManipulate())
			{
				return false;
			}

			ConsumableTemplate consumable = BaseItemTemplate.Get<ConsumableTemplate>(templateID);
			if (consumable == null)
			{
				return false;
			}

			if (cachedDamageController == null ||
				!cachedDamageController.IsAlive)
			{
				return false;
			}

			if (cachedCooldownController != null &&
				cachedCooldownController.IsOnCooldown(consumable.ID, activationData.GetTick()))
			{
				return false;
			}

			// Both client and server validate the item exists in inventory.
			// Deterministic: both iterate the same slot order, finding the same item.
			Item item = FindConsumableItem(consumable.ID);
			if (item == null)
			{
				return false;
			}

			// SECURITY: Re-validate via CanConsume on both client and server.
			// ActivateConsumable (client-side queue) already calls CanConsume,
			// but the server must also check because a crafted replicate packet
			// could bypass the client-side check. CanConsume verifies stackable
			// status, sufficient charges, and cooldown state.
			if (!consumable.CanConsume(PlayerCharacter, item, activationData.GetTick()))
			{
				return false;
			}

			// Set all logical state before the side-effecting LockSlot call.
			// If an exception occurred between LockSlot and setting currentAbilityID,
			// Cancel() would see consumableSlot set but currentAbilityID == NO_ABILITY,
			// leaving the controller in an inconsistent state.
			currentAbilityID = templateID;
			replicatedFlags.EnableBit(AbilityActivationFlags.IsConsumable);
			remainingTicks = (uint)Mathf.CeilToInt(consumable.ActivationTime / (float)base.TimeManager.TickDelta);

			// Lock the inventory slot to prevent movement during activation.
			// consumableSlot is assigned AFTER LockSlot so that if LockSlot throws,
			// Cancel() won't try to unlock a slot that was never locked.
			if (cachedInventoryController != null)
			{
				cachedInventoryController.LockSlot(item.Slot);
			}
			consumableSlot = item.Slot;
			return true;
		}

		/// <summary>
		/// Processes the currently active consumable each tick. Handles the activation countdown
		/// and finishes the consumable when activation time expires.
		/// Consumables do not support channeling, charging, or held-state early cancellation.
		/// Unlike abilities (which can be released mid-cast via the IsHeld flag in
		/// <see cref="UpdateActivation"/>), consumables always run to completion once started.
		/// This is intentional: potions and similar items should not be interruptible by
		/// releasing the activation key. Interrupts via <see cref="ProcessInterrupt"/> still
		/// apply (e.g., stun, explicit cancel).
		/// </summary>
		/// <param name="activationData">The replicate data for this tick.</param>
		/// <param name="state">The prediction state.</param>
		/// <param name="deltaTime">Tick delta time.</param>
		private void ProcessActiveConsumable(AbilityActivationReplicateData activationData, ReplicateState state, float deltaTime)
		{
			if (!IsActivating)
			{
				return;
			}

			if (!ValidateActiveConsumable(out ConsumableTemplate consumable))
			{
				Cancel(state);
				return;
			}

			// NOTE: Interrupt is already handled by ProcessInterrupt in Replicate()
			// before this method is called. No duplicate check needed here.

			if (remainingTicks > 0)
			{
				if (state.IsTickedCreated())
				{
					float tickDelta = (float)base.TimeManager.TickDelta;
					OnUpdate?.Invoke(consumable.Name, remainingTicks * tickDelta, consumable.ActivationTime);
				}
				remainingTicks--;
				return;
			}

			// Activation complete — consume the item and finish
			FinishConsumable(consumable, activationData, state);
		}

		/// <summary>
		/// Completes a consumable activation. Both client and server execute the same code path
		/// for prediction parity: invoke the consumable (handles cooldown, charge reduction,
		/// item destruction, and the consumable effect), then clean up the inventory slot.
		/// Server-side subscribers of <see cref="OnConsumableUsed"/> send the authoritative
		/// inventory broadcast to correct any client misprediction.
		/// During reconcile replay the effect, inventory mutation, and event are skipped because
		/// the server's authoritative state was already restored — re-applying them would
		/// double-consume the item and fire duplicate events.
		/// </summary>
		/// <param name="consumable">The consumable template being activated.</param>
		/// <param name="activationData">The replicate data for this tick, used for tick-based cooldown checks.</param>
		/// <param name="state">The prediction state, forwarded to Cancel for replay guarding.</param>
		private void FinishConsumable(ConsumableTemplate consumable, AbilityActivationReplicateData activationData, ReplicateState state)
		{
			// During reconcile replay, skip the entire effect body.
			// Cooldown re-validation, Invoke, inventory slot removal, and OnConsumableUsed
			// are all authoritative operations that must not be replayed.
			// NOTE: The cooldown re-validation below could produce a divergent Cancel
			// during replay (server cooldown state may differ from the predicted state),
			// but the replay guard makes this moot — the check is never reached.
			if (!state.ContainsReplayed())
			{
				// Re-validate: an external effect could have put this consumable on
				// cooldown during the activation window (e.g., debuff or another source).
				if (cachedCooldownController != null &&
					cachedCooldownController.IsOnCooldown(consumable.ID, activationData.GetTick()))
				{
					Cancel(state);
					return;
				}

				if (PlayerCharacter != null &&
					consumableSlot >= 0 &&
					cachedInventoryController != null &&
					cachedInventoryController.TryGetItem(consumableSlot, out Item item) &&
					item.Template.ID == consumable.ID)
				{
					int slot = consumableSlot;
					if (consumable.Invoke(PlayerCharacter, item, activationData.GetTick()))
					{
						// If the item was fully consumed, clean up the inventory slot.
						// Stackable is not nulled by Item.Destroy(), so this check is safe.
						if (!item.IsStackable || item.Stackable.Amount < 1)
						{
							// Unlock before setting null so the SetItemSlot call is not blocked.
							cachedInventoryController.UnlockSlot(slot);
							// Prevent double-unlock: Cancel() also unlocks consumableSlot.
							// Setting to -1 here ensures the subsequent Cancel() is a no-op
							// for slot unlocking. If SetItemSlot below throws,
							// currentAbilityID is still set — the subsequent Cancel(state)
							// will clear it, but the slot is already unlocked and cleaned
							// up correctly. This ordering is intentional.
							consumableSlot = -1;
							cachedInventoryController.SetItemSlot(null, slot);
						}

						OnConsumableUsed?.Invoke(Character, consumable.ID, slot);
					}
				}
			}

			Cancel(state);
		}

		/// <summary>
		/// Searches the character's inventory for the first item matching the given template ID.
		/// </summary>
		/// <param name="templateID">The consumable template ID to search for.</param>
		/// <returns>The first matching <see cref="Item"/>, or null if not found.</returns>
		/// <remarks>
		/// DETERMINISM: This method iterates <see cref="IInventoryController.Items"/> by
		/// index (0..Count-1). Both client and server must iterate the same backing
		/// collection in the same order. If <c>Items</c> is backed by a slot-indexed
		/// <c>List&lt;Item&gt;</c> where index == slot, this is deterministic. If the
		/// backing collection ever changes to a non-deterministic type (e.g.,
		/// <c>Dictionary.Values</c>), client and server may find different items,
		/// causing a reconcile mismatch on every consumable use.
		/// </remarks>
		private Item FindConsumableItem(int templateID)
		{
			if (cachedInventoryController == null)
			{
				return null;
			}

			// IItemContainer.Items is declared as List<Item> — the compile-time type
			// guarantees index-stable iteration required for deterministic lookup.
			// Runtime assertion: if the interface ever returns a different collection type
			// (e.g., via a shim or mock), catch the determinism break immediately.
			List<Item> items = cachedInventoryController.Items;
			if (items == null)
			{
				Log.Warning("AbilityController", "FindConsumableItem: IInventoryController.Items returned null — determinism contract violated.");
				return null;
			}
			for (int i = 0; i < items.Count; ++i)
			{
				Item item = items[i];
				if (item != null && item.Template.ID == templateID)
				{
					return item;
				}
			}
			return null;
		}

		/// <summary>
		/// Deterministic activation check used inside <see cref="Replicate"/> on both client
		/// and server. Every condition here MUST read only from reconciled or deterministic
		/// state so that client replay produces the same result as the server.
		///
		/// Determinism classification of each check:
		///   - CanManipulate: reads Character null/IsTeleporting/IsSpawned — safe (server-set flags)
		///   - KnownAbilities: synced via payload — safe
		///   - IsAlive: derived from health attribute CurrentValue — reconciled
		///   - Cooldowns: tick-based, reconciled via CooldownReconcileEntry[]
		///   - PassesGroundingCheck: reads KCC motor state, predicted on OnTick before OnPostTick — safe
		///   - MeetsActivationConditions/HasResource: reads attributes/buffs — reconciled
		///
		/// NOT included here (non-deterministic / client-only):
		///   - canManipulateHandlers (external Func delegates, e.g. UI MouseMode) — use CanActivateOptimistic
		///   - Pet check (broadcast-synced, not reconciled) — use CanActivateOptimistic
		/// </summary>
		private bool CanActivate(long abilityID, uint currentTick, out Ability validatedAbility)
		{
			validatedAbility = null;

			if (abilityID == NO_ABILITY)
			{
				return false;
			}
			if (!CanManipulate())
			{
				return false;
			}
			if (!KnownAbilities.TryGetValue(abilityID, out validatedAbility))
			{
				return false;
			}
			if (cachedDamageController == null ||
				!cachedDamageController.IsAlive)
			{
				return false;
			}
			if (cachedCooldownController != null &&
				cachedCooldownController.IsOnCooldown(validatedAbility.ID, currentTick))
			{
				return false;
			}

			AbilityType abilityType = validatedAbility.EffectiveType;
			if (!PassesGroundingCheck(abilityType))
			{
				return false;
			}

			if (!validatedAbility.MeetsActivationConditions(Character, ref cachedCheckEventData) ||
				!validatedAbility.HasResource(Character, BloodResourceConversionTemplate))
			{
				return false;
			}
			return true;
		}

		/// <summary>
		/// Client-side pre-filter: cheap, best-effort, not authoritative.
		/// Runs all deterministic checks from <see cref="CanActivate"/> PLUS non-deterministic
		/// checks that should prevent obviously invalid activations from being queued:
		///   - External canManipulateHandlers (e.g. UI MouseMode gating)
		///   - Pet existence (broadcast-synced, not reconciled)
		///
		/// If this passes but the server denies, reconcile will correct the prediction.
		/// The important insight: letting the client be wrong occasionally is fine —
		/// that's what reconcile is for. Avoiding the client being so conservative
		/// it never predicts is the goal.
		/// </summary>
		private bool CanActivateOptimistic(long abilityID, uint currentTick, out Ability validatedAbility)
		{
			if (!CanActivate(abilityID, currentTick, out validatedAbility))
			{
				return false;
			}

			// Non-deterministic external handlers (e.g., UI MouseMode gating).
			// These are client-only checks that the server never evaluates.
			// Excluding them from CanActivate prevents divergence during replay.
			for (int i = 0; i < canManipulateHandlers.Count; i++)
			{
				if (!canManipulateHandlers[i].Invoke())
				{
					return false;
				}
			}

			// Pet state is broadcast-synced, not reconciled. During replay the pet
			// reference may be stale. Check here as a best-effort pre-filter;
			// the server will reject if the character already has a pet.
			if (validatedAbility != null)
			{
				PetAbilityTemplate petAbilityTemplate = validatedAbility.Template as PetAbilityTemplate;
				if (petAbilityTemplate != null &&
					Character.TryGet(out IPetController petController) &&
					petController.Pet != null)
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Cancels the current ability activation and resets all related state.
		/// Unlocks the consumable inventory slot if one was locked.
		/// Clears persistent flags from both local input and replicated state.
		/// </summary>
		/// <param name="state">
		/// Optional replicate state. When provided, UI events are suppressed during
		/// reconcile replay to prevent flickering. Callers without a state (e.g.,
		/// ResetState) omit this parameter and events fire normally.
		/// </param>
		/// <param name="suppressCancelEvent">
		/// When true, <see cref="OnCancel"/> is not invoked. Used by
		/// <see cref="ProcessInterrupt"/> which fires <see cref="OnInterrupt"/> instead,
		/// preventing double UI events on the interrupt path.
		/// </param>
		internal void Cancel(ReplicateState state = ReplicateState.Invalid, bool suppressCancelEvent = false)
		{
			//Log.Debug("Cancel");

			// Unlock the consumable slot before resetting state.
			if (consumableSlot >= 0 &&
				Character != null &&
				cachedInventoryController != null)
			{
				cachedInventoryController.UnlockSlot(consumableSlot);
			}
			consumableSlot = -1;

			currentAbilityID = NO_ABILITY;
			remainingTicks = 0;

			// Clear persistent activation flags from replicated state.
			replicatedFlags.DisableBit(AbilityActivationFlags.IsHeld);
			replicatedFlags.DisableBit(AbilityActivationFlags.IsConsumable);
			replicatedFlags.DisableBit(AbilityActivationFlags.IsMount);

			// Only clear persistent bits from local input on non-replay execution.
			// During replay these bits are not cleared, so any input queued
			// between ticks (e.g., a consumable activation) naturally persists
			// until the next real tick processes it. Clearing here during
			// non-replay is what actually consumes the flags.
			if (!state.ContainsReplayed())
			{
				localInputFlags.DisableBit(AbilityActivationFlags.IsHeld);
				localInputFlags.DisableBit(AbilityActivationFlags.IsConsumable);
				localInputFlags.DisableBit(AbilityActivationFlags.IsMount);
			}

			// Only fire UI events on the first execution or non-prediction callers.
			// During reconcile replay the UI should not flicker.
			// Suppressed during interrupt (ProcessInterrupt fires OnInterrupt instead).
			if (!suppressCancelEvent && (state == ReplicateState.Invalid || state.IsTickedCreated()))
			{
				OnCancel?.Invoke();
			}
		}

		/// <summary>
		/// Adds a cooldown for the given ability using the cooldown controller.
		/// </summary>
		/// <param name="ability">The ability to add a cooldown for.</param>
		internal void AddCooldown(Ability ability, uint currentTick)
		{
			if (ability.Cooldown > 0.0f &&
				cachedCooldownController != null)
			{
				float cooldownReduction = CalculateSpeedReduction(CooldownReductionTemplate);
				float cooldown = ability.Cooldown * cooldownReduction;

				cachedCooldownController.AddCooldown(ability.ID, new CooldownInstance(currentTick, cooldown, (float)base.TimeManager.TickDelta));
			}
		}

		/// <summary>
		/// Queues a consumable item for activation through the replicate pipeline.
		/// Sets the IsConsumable flag and stores the consumable template ID in the ability queue.
		/// Validates that the consumable can be used before queuing.
		/// </summary>
		/// <param name="item">The consumable item to activate.</param>
		public void ActivateConsumable(Item item)
		{
			if (item == null) return;

			ConsumableTemplate consumable = item.Template as ConsumableTemplate;
			if (consumable == null) return;

			if (!CanManipulate()) return;

			// Client-side pre-filter: check external handlers (e.g. UI MouseMode).
			for (int i = 0; i < canManipulateHandlers.Count; i++)
			{
				if (!canManipulateHandlers[i].Invoke())
				{
					return;
				}
			}


			// Validate the consumable can be used (charges, cooldown, etc.)
			// LocalTick is correct here: this is a client-side input pre-filter that QUEUES
			// the activation for the next replicate tick. The server re-validates authoritatively
			// in OnReplicate using the simulated tick — so this is not part of the deterministic
			// state machine and may use the live wall-clock tick safely.
			if (!consumable.CanConsume(PlayerCharacter, item, base.TimeManager.LocalTick)) return;

			// Ensure we are not already activating an ability or an interrupt is waiting to be processed
			if (!AbilityQueued &&
				!IsActivating &&
				!localInputFlags.IsFlagged(AbilityActivationFlags.Interrupt))
			{
				queuedAbilityID = consumable.ID;
				localInputFlags.EnableBit(AbilityActivationFlags.IsConsumable);
			}
		}

		/// <summary>
		/// Queues an interrupt for the current ability, to be processed on the next tick.
		/// </summary>
		/// <param name="attacker">The character causing the interrupt (not used).</param>
		public void Interrupt(ICharacter attacker)
		{
			localInputFlags.EnableBit(AbilityActivationFlags.Interrupt);
		}

		/// <summary>
		/// Releases the held state for the current ability. For charged abilities this
		/// triggers the release (fire). For channeled abilities this stops the channel early.
		/// </summary>
		public void Release()
		{
			localInputFlags.DisableBit(AbilityActivationFlags.IsHeld);
		}

		/// <summary>
		/// Attempts to activate an ability by reference ID and held state, if all conditions are met.
		/// </summary>
		/// <param name="referenceID">The ability reference ID to activate.</param>
		/// <param name="isHeld">Whether the activation key is held.</param>
		public void Activate(long referenceID, bool isHeld)
		{
			// Client-side pre-filter: cheap, best-effort, not authoritative.
			// The server will re-validate in Replicate(). If this passes but the
			// server denies, reconcile will correct the prediction.
			if (!CanActivateOptimistic(referenceID, base.TimeManager.LocalTick, out _))
			{
				return;
			}

			// Ensure we are not already activating an ability or an interrupt is waiting to be processed
			if (!AbilityQueued &&
				!IsActivating &&
				!localInputFlags.IsFlagged(AbilityActivationFlags.Interrupt))
			{
				//Log.Debug("Activating " + referenceID);
				queuedAbilityID = referenceID;
				if (isHeld)
				{
					localInputFlags.EnableBit(AbilityActivationFlags.IsHeld);
				}
				else
				{
					localInputFlags.DisableBit(AbilityActivationFlags.IsHeld);
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

		/// <summary>
		/// Checks whether the player character's grounding status is compatible with
		/// the given ability type. Grounded abilities require stable ground, aerial
		/// abilities require the character to be airborne. NPCs (no PlayerCharacter)
		/// always pass this check.
		/// </summary>
		/// <param name="abilityType">The effective ability type to validate.</param>
		/// <returns>True if grounding requirements are met, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool PassesGroundingCheck(AbilityType abilityType)
		{
			if (PlayerCharacter == null)
			{
				return true;
			}

			if (abilityType == AbilityType.GroundedPhysical ||
				abilityType == AbilityType.GroundedMagic)
			{
				return PlayerCharacter.Motor.GroundingStatus.IsStableOnGround;
			}
			if (abilityType == AbilityType.AerialPhysical ||
				abilityType == AbilityType.AerialMagic)
			{
				return !PlayerCharacter.Motor.GroundingStatus.IsStableOnGround;
			}
			return true;
		}
	}
}