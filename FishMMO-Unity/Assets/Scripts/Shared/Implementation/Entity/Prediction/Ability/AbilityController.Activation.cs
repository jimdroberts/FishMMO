using FishNet.Object;
using FishNet.Transporting;
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
				// DETERMINISM: (int)Math.Ceiling avoids platform-specific float rounding differences
				// between x86 and ARM that Mathf.CeilToInt is susceptible to.
				float activationTimeSec = newAbility.ActivationTime * CalculateSpeedReduction(GetActivationAttributeTemplate(newAbility));
				remainingTicks = (uint)(int)Math.Ceiling(activationTimeSec / (double)base.TimeManager.TickDelta);
				remainingTicks = ApplyChannelActivationFloor(newAbility, activationData.ActivationFlags, remainingTicks);
				// A fresh activation owes its observers a reliable opening message; see
				// BroadcastAbilityActivated.
				channelSpawnsBroadcast = 0u;
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

				// Trigger animation for this ability type on the first authoritative tick.
				// Avoids replay flicker; observers see animation via NetworkAnimator sync.
				if (!state.ContainsReplayed())
				{
					TriggerAbilityAnimation(newAbility.EffectiveType);
				}

				return true;
			}
			return false;
		}

		/// <summary>
		/// Raises a channelled ability's activation window to at least one tick.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A channel's per-tick spawns happen in <see cref="UpdateActivation"/>, which is reached
		/// only while <c>remainingTicks &gt; 0</c>. An ability authored with
		/// <c>ActivationTime = 0</c> and a Channeled event therefore never reached it at all: the
		/// activation completed inside the same replicate call that started it and the whole
		/// channel collapsed to the single closing spawn from <see cref="FinishAbility"/>, with no
		/// error and nothing to see. The author asked for a channel; this gives them the shortest
		/// one that exists rather than silently turning it into an instant.
		/// </para>
		/// <para>
		/// Pure and deterministic — it reads only the ability's events and this tick's input
		/// flags, both of which the server and the owner agree on — so both sides compute the same
		/// <c>remainingTicks</c> and the reconcile stays quiet. Applies only while the ability is
		/// actually held: a channelled ability activated without the hold flag is a one-shot cast
		/// and keeps its instant timing.
		/// </para>
		/// </remarks>
		/// <param name="ability">The ability being started.</param>
		/// <param name="activationFlags">This tick's activation flags.</param>
		/// <param name="ticks">The activation window computed from the ability's activation time.</param>
		/// <returns>The activation window to use.</returns>
		private uint ApplyChannelActivationFloor(Ability ability, int activationFlags, uint ticks)
		{
			if (ticks > 0u ||
				ChanneledTemplate == null ||
				!activationFlags.IsFlagged(AbilityActivationFlags.IsHeld) ||
				!ability.HasAbilityEvent(ChanneledTemplate.ID))
			{
				return ticks;
			}
			return 1u;
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
				// Enforce a maximum hold duration (2x normal activation time, floored — see
				// ComputeMaxHoldTicks) to prevent clients from holding charged abilities
				// indefinitely. Track hold ticks separately since remainingTicks is already 0
				// when the ability finished charging.
				chargedHoldTicks++;
				uint maxHoldTicks = ComputeMaxHoldTicks(validatedAbility.ActivationTime, (float)base.TimeManager.TickDelta);
				if (chargedHoldTicks >= maxHoldTicks)
				{
					chargedHoldTicks = 0;
					// Pass the real state: with Invalid, a replayed tick would clear the
					// player's live localInputFlags and reset the animator mid-replay.
					Cancel(state, true);
				}
				return;
			}
			chargedHoldTicks = 0;

			// Activation complete — spawn the ability and finish
			FinishAbility(validatedAbility, activationData, state);
		}

		/// <summary>
		/// Minimum charged-hold window, in seconds, applied when 2x the ability's activation
		/// time would be shorter. See <see cref="ComputeMaxHoldTicks"/>.
		/// </summary>
		internal const float MinimumChargedHoldSeconds = 1f;

		/// <summary>
		/// Ticks a charged ability may be held past full charge before the server cancels it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The cap is 2x the activation time, floored at <see cref="MinimumChargedHoldSeconds"/>
		/// and never below one tick. The floor exists because the raw formula collapses for
		/// instant abilities: an ability authored at <c>ActivationTime = 0</c> (Punch is) produced
		/// <c>maxHoldTicks = 0</c>, and since the hold counter increments before the comparison,
		/// the charge was cancelled on the very first held tick — a charged event on a fast
		/// ability was silently unusable rather than briefly holdable.
		/// </para>
		/// <para>
		/// Pure and static, with <c>Math.Ceiling</c> rather than <c>Mathf</c>, because this runs
		/// inside the predicted replicate on every peer — both sides must compute the identical
		/// tick count or the client predicts a cancel the server does not perform.
		/// </para>
		/// </remarks>
		/// <param name="activationTime">The ability's activation (charge) time in seconds.</param>
		/// <param name="tickDelta">Fixed seconds per tick.</param>
		/// <returns>The hold cap in ticks, always at least 1.</returns>
		internal static uint ComputeMaxHoldTicks(float activationTime, float tickDelta)
		{
			if (tickDelta <= 0f)
			{
				return 1u;
			}

			float capSeconds = Mathf.Max(activationTime * 2f, MinimumChargedHoldSeconds);
			uint ticks = (uint)(int)Math.Ceiling(capSeconds / (double)tickDelta);
			return ticks < 1u ? 1u : ticks;
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
					// Applied on replay too — see the remarks on FinishAbility: a reconcile for a
					// tick before the release wipes this cooldown, and only the replay restores it.
					AddCooldown(validatedAbility, activationData.GetPredictionTick());
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
		/// the deterministic RNG seed. Aim comes from the REPLICATED input cached in
		/// <c>ReplicateInternal</c> (<c>replicatedAimOrigin</c>/<c>replicatedAimDirection</c>), never
		/// from a live controller — see the note in the body. During reconcile replay the visual
		/// spawn is skipped but the seed is still advanced to keep client/server RNG in lockstep.
		/// </summary>
		private void ResolveTargetAndSpawn(Ability ability, AbilityActivationReplicateData activationData, ReplicateState state, bool isChannelTick = false)
		{
			// During reconcile replay, spawned objects were already destroyed by
			// DestroyAbilityObjectsAfterTick. Skip the visual spawn but still
			// advance the seed below so RNG state stays deterministic.
			if (!state.ContainsReplayed())
			{
				if (cachedTargetController != null)
				{
					/* Trace from the aim that was REPLICATED for this tick, not from whatever the
					 * live controller holds right now.
					 *
					 * Reading the controller was wrong on both sides of the wire. For a player it
					 * meant the owner traced with its exact local camera while the server and every
					 * observer traced with the value that survived quantisation, so a deterministic
					 * simulation produced a different shot on each peer. For an NPC it meant reading
					 * AIController, which disables itself off the server, so every client traced
					 * from a default-initialised controller — origin at the world origin, direction
					 * +Z — and NPC shots went nowhere near their targets. */
					TargetInfo targetInfo = cachedTargetController.UpdateTarget(replicatedAimOrigin,
																			replicatedAimDirection,
																			ability.Range);

					AbilityObject spawned = AbilityObject.Spawn(ability, Character, AbilitySpawner, targetInfo,
						replicatedAimOrigin, replicatedAimDirection, currentSeed, activationData.GetPredictionTick());

					/* The server resolved no object where the owner may well have.
					 *
					 * Spawn returns null when the ability requires a target and this peer's trace
					 * found none. The owner traces against its own view and can easily disagree, and
					 * nothing else in the reconcile expresses the difference: the seed still advances
					 * below, the cost and cooldown still apply, and no denial is raised — so the
					 * owner's object had no reason to be rolled back and flew its full lifetime
					 * hitting nothing. Flagging it lets the reconcile take that object back. */
					if (spawned == null && base.IsServerStarted)
					{
						/* Stamped with the tick it happened on, not raised as a bare flag. The
						 * reconcile built after this tick's replicates only carries it if the two
						 * ticks match — see ShouldFlagNoSpawn for why a mismatch must be dropped
						 * rather than reported against whatever tick the reconcile ends up on. */
						serverSpawnedNothingTick = activationData.GetPredictionTick().Value;
					}

					/* Tell observers what happened, as one message per cast.
					 *
					 * Observers currently learn about this spawn only because the owner's whole
					 * input stream is relayed to them thirty times a second. That relay is what
					 * makes 100-200 players unaffordable, and it is switched off by disabling state
					 * forwarding — at which point this broadcast is the only thing that reaches
					 * them. The ability simulation is deterministic, so the tuple below is all an
					 * observer needs to reproduce the identical object for its whole lifetime.
					 *
					 * Safe to send while forwarding is still on: an observer receiving both paths
					 * spawns once, because AbilityContainerAllocator treats a matching seed+tick as
					 * a duplicate and replaces it rather than adding a second object. */
					BroadcastAbilityActivated(ability, targetInfo, activationData.GetPredictionTick(), isChannelTick);
				}
				else if (base.IsServerStarted && !warnedMissingTargetController)
				{
					/* Silent otherwise: the seed still advances below, resources are still
					 * consumed and the cooldown still starts, but nothing ever spawns and
					 * observers are never told. Say so once per controller. */
					warnedMissingTargetController = true;
					Log.Warning("AbilityController",
						$"'{gameObject.name}' has no ITargetController; its abilities will never spawn objects.");
				}
			}

			// Always advance seed regardless of replay state, which path was taken,
			// or whether a spawn actually occurred — maintains deterministic RNG
			// state across client and server.
			currentSeed = abilitySeedGenerator.Next();
		}

		/// <summary>True once the missing-target-controller warning has been logged for this controller.</summary>
		private bool warnedMissingTargetController;

		/// <summary>
		/// Sends this activation to everyone observing the caster.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Server only, and server authored. The client supplied a direction through its replicate
		/// input; the server validated the activation, resolved the target with its own raycast, and
		/// this reports the result. Nothing in the message is taken from the client on trust —
		/// <c>TargetObjectID</c> in particular is the server's resolution, never a victim the client
		/// named.
		/// </para>
		/// <para>
		/// Scoped to the caster's observers and excluding its owner, so the interest management
		/// already in place bounds this traffic without any extra work. A one-off cast and the
		/// FIRST spawn of a channel go reliably — a cast is a rare, event-driven message and the
		/// ability object is client-local with no state channel behind it, so a dropped activation
		/// was a projectile that observer never saw at all. The second and later per-tick spawns of
		/// a channel go unreliably; see the channel-cost note in the body. Either way a late
		/// message still lands correctly, because the receiver fast-forwards by the measured
		/// transit delay rather than assuming the message was instant.
		/// </para>
		/// </remarks>
		/// <param name="isChannelTick">
		/// True when this spawn is one of a channelled ability's per-tick spawns rather than a
		/// one-off cast. See the channel-cost note below.
		/// </param>
		private void BroadcastAbilityActivated(Ability ability, TargetInfo targetInfo, PredictionTick spawnTick, bool isChannelTick = false)
		{
			if (!base.IsServerStarted || base.NetworkManager == null || base.NetworkObject == null)
			{
				return;
			}

			/* Nothing to reproduce: pets, self-target and prefab-less abilities spawn no world
			 * object, and a required target that was not found spawned nothing here either. */
			if (!AbilityObject.SpawnsWorldObject(ability.Template) ||
				(ability.Template.RequiresTarget && targetInfo.Target == null))
			{
				return;
			}

			/* Forwarded objects reproduce the cast from the relayed input instead.
			 *
			 * Their observers run the replicate and spawn the object themselves, so this message
			 * would be the same spawn a second time. The allocator would collapse the duplicate, but
			 * the receiving handler also fast-forwards what it is given, and fast-forwarding an
			 * object that is already simulating jumps it ahead of the server's copy.
			 * See ObserverSyncMode. */
			if (!ObserverSyncMode.ShouldBroadcastToObservers(base.NetworkObject))
			{
				return;
			}

			int targetObjectID = -1;
			if (targetInfo.Target != null)
			{
				NetworkObject targetNob = targetInfo.Target.GetComponentInParent<NetworkObject>();
				if (targetNob != null)
				{
					targetObjectID = targetNob.ObjectId;
				}
			}

			// The pose the server spawned with — see AbilitySpawnPose for why it travels.
			AbilitySpawnPose pose = AbilityObject.ResolveSpawnPose(Character, ability, AbilitySpawner, targetInfo,
				replicatedAimOrigin, replicatedAimDirection);

			/* To observers only.
			 *
			 * The owner is always one of its own object's observers, and used to receive this and
			 * discard it on arrival (it predicted the cast and owns the authoritative copy through
			 * the reconcile).
			 *
			 * CHANNEL COST. A one-off cast goes reliably: it is a rare, event-driven message with
			 * no recovery path of its own — the object is client-local, so a dropped activation
			 * used to be a projectile that observer never saw, for its whole lifetime. A
			 * channelled ability is a different shape. It re-broadcasts every tick it is held,
			 * thirty a second, and each of those spawns is one short-lived visual among many; a
			 * lost one is a gap in a beam nobody can point to, while paying reliable delivery for
			 * all of them puts a retransmit queue behind a stream that is already continuous. So
			 * the FIRST spawn of a channel is reliable — that is the one that tells an observer a
			 * channel started at all — and the per-tick spawns after it are unreliable. The
			 * counter resets in TryStartAbility and Cancel, so every new channel pays for its own
			 * opening message.
			 *
			 * The fast-forward on the receiving side already accounts for however long a message
			 * took to arrive, so a retransmitted or late one still lands in the right place. */
			Channel channel = Channel.Reliable;
			if (isChannelTick)
			{
				if (channelSpawnsBroadcast > 0u)
				{
					channel = Channel.Unreliable;
				}
				channelSpawnsBroadcast++;
			}

			ObserverBroadcastScope.BroadcastToObserversExceptOwner(base.NetworkObject, new AbilityActivatedBroadcast
			{
				CasterObjectID = base.NetworkObject.ObjectId,
				AbilityID = ability.ID,
				Seed = currentSeed,
				SpawnTick = spawnTick.Value,
				SpawnMode = (byte)ability.Template.AbilitySpawnTarget,
				AimOrigin = replicatedAimOrigin,
				PackedAimDirection = AimDirectionCompression.Encode(replicatedAimDirection),
				TargetObjectID = targetObjectID,
				SpawnPosition = pose.Position,
				SpawnRotation = pose.Rotation,
				ServerTick = base.TimeManager.LocalTick,
			}, channel);
		}

		/// <summary>
		/// Activation broadcasts already sent for the channel currently being held.
		/// </summary>
		/// <remarks>
		/// Zero means the next channel spawn is the first of its channel and must go reliably.
		/// Server-side only — it exists solely to pick a send channel. See
		/// <see cref="BroadcastAbilityActivated"/>.
		/// </remarks>
		private uint channelSpawnsBroadcast;

		/// <summary>
		/// Ticks an observer should fast-forward a freshly reproduced ability object by, so it
		/// sits where the server's copy is <i>as the observer renders its peers</i>.
		/// </summary>
		/// <remarks>
		/// Observers render other characters <paramref name="interpolationTicks"/> behind the
		/// server, so the object is placed that far behind the server's copy too — a projectile
		/// that ran level with the server would visibly lead the interpolated caster that fired
		/// it. Negative differences (an estimate that lags the spawn) clamp to zero.
		/// </remarks>
		/// <param name="estimatedServerTick">The observer's estimate of the current server tick (<c>TimeManager.Tick</c>).</param>
		/// <param name="serverSpawnTick">Server tick the object spawned on.</param>
		/// <param name="interpolationTicks">Ticks the observer renders its peers behind the server.</param>
		internal static uint ComputeObserverFastForwardTicks(uint estimatedServerTick, uint serverSpawnTick, uint interpolationTicks)
		{
			long elapsed = (int)(estimatedServerTick - serverSpawnTick);
			elapsed -= interpolationTicks;
			return elapsed > 0 ? (uint)elapsed : 0u;
		}

		/// <summary>True once this client has registered the shared activation handler.</summary>
		/// <remarks>
		/// Registered once per client rather than per character, for the same reason the resource
		/// handler is: a per-character registration would run one delegate per character in the
		/// scene for every cast anyone makes.
		/// </remarks>
		private static bool activationBroadcastRegistered;

		/// <summary>Registers the shared activation handler for this client.</summary>
		internal static void RegisterActivationBroadcast(FishNet.Managing.NetworkManager networkManager)
		{
			if (activationBroadcastRegistered || networkManager == null)
			{
				return;
			}
			networkManager.ClientManager.RegisterBroadcast<AbilityActivatedBroadcast>(OnAbilityActivatedBroadcast);
			networkManager.ClientManager.RegisterBroadcast<AbilityObjectHitBroadcast>(OnAbilityObjectHitBroadcast);
			networkManager.ClientManager.RegisterBroadcast<AbilityObjectDestroyedBroadcast>(OnAbilityObjectDestroyedBroadcast);
			networkManager.ClientManager.RegisterBroadcast<AbilityLearnedObserverBroadcast>(OnAbilityLearnedObserverBroadcast);
			activationBroadcastRegistered = true;
		}

		/// <summary>
		/// Applies a hit the server resolved to this client's copy of an ability object.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The counterpart to <c>AbilityObject.ResolveSweptHits</c>'s peer gate: a third-party
		/// observer no longer decides what a projectile hit, because it holds every character
		/// interpolated against its own latency and the server resolves inside a rewind to the
		/// CASTER'S view. It is told instead, and plays the impact where the server measured it.
		/// </para>
		/// <para>
		/// The victim is resolved back through the spawned map rather than re-traced, for the same
		/// reason <see cref="OnAbilityActivatedBroadcast"/> resolves its target that way: re-tracing
		/// would reintroduce exactly the divergence this removes. A victim id of zero is a hit on
		/// scenery and is applied with no target character, which is what an authored impact decal
		/// or sound needs; a NON-zero id this client cannot resolve is a victim it is not observing,
		/// and is dropped rather than downgraded to a scenery hit, which would fire target-less
		/// effects for a character that is really there.
		/// </para>
		/// <para>
		/// A copy already gone — the lifetime got there first, or a destroy message overtook this —
		/// is simply not found, exactly as for the destroy broadcast.
		/// </para>
		/// </remarks>
		private static void OnAbilityObjectHitBroadcast(AbilityObjectHitBroadcast msg, Channel channel)
		{
			FishNet.Managing.NetworkManager nm = FishNet.InstanceFinder.NetworkManager;
			if (nm == null || nm.ClientManager == null || nm.IsServerStarted)
			{
				return;
			}
			if (!nm.ClientManager.Objects.Spawned.TryGetValue(msg.CasterObjectID, out NetworkObject casterNob) ||
				casterNob == null)
			{
				return;
			}

			AbilityController controller = casterNob.GetComponent<AbilityController>();
			if (controller == null ||
				!controller.TryGetAbilityForVisuals(msg.AbilityID, out Ability ability) ||
				ability.Objects == null ||
				!ability.Objects.TryGetValue(msg.ContainerID, out Dictionary<int, AbilityObject> container) ||
				!container.TryGetValue(msg.ObjectID, out AbilityObject abilityObject) ||
				abilityObject == null)
			{
				return;
			}

			ICharacter hitCharacter = null;
			if (msg.VictimObjectID != 0)
			{
				if (!nm.ClientManager.Objects.Spawned.TryGetValue(msg.VictimObjectID, out NetworkObject victimNob) ||
					victimNob == null ||
					!victimNob.TryGetComponent(out hitCharacter))
				{
					// A character this client is not observing. Dropping the hit is right: applying
					// it target-less would run the OnHit events as though it had struck scenery.
					return;
				}
			}

			abilityObject.ApplyObservedHit(hitCharacter, msg.Point, msg.Normal);
		}

		/// <summary>
		/// Files an ability a peer learned while this client was already observing it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Without this, an observer's knowledge of a peer's abilities was frozen at the moment it
		/// started observing: <c>ReadPayload</c> was the only source, so every cast of an ability
		/// crafted afterwards resolved to nothing and drew nothing, permanently.
		/// </para>
		/// <para>
		/// The ability lands in <c>KnownAbilities</c>, the same container the owner uses — see
		/// <c>AbilityController.RegisterObservedAbility</c>, which explains why the parallel
		/// observer-only store it used to go into was the wrong shape. The security boundary is
		/// that method's OWNER CHECK, not a container split: this message only ever describes
		/// somebody else, so it refuses on our own character. The owner is excluded by the sender
		/// and skipped again here; it has its own <c>AbilityAddBroadcast</c>.
		/// </para>
		/// </remarks>
		private static void OnAbilityLearnedObserverBroadcast(AbilityLearnedObserverBroadcast msg, Channel channel)
		{
			FishNet.Managing.NetworkManager nm = FishNet.InstanceFinder.NetworkManager;
			if (nm == null || nm.ClientManager == null || nm.IsServerStarted)
			{
				return;
			}
			if (!nm.ClientManager.Objects.Spawned.TryGetValue(msg.CasterObjectID, out NetworkObject casterNob) ||
				casterNob == null || casterNob.IsOwner)
			{
				return;
			}

			AbilityController controller = casterNob.GetComponent<AbilityController>();
			if (controller == null)
			{
				return;
			}

			AbilityTemplate template = AbilityTemplate.Get<AbilityTemplate>(msg.TemplateID);
			if (template == null)
			{
				Log.Warning("AbilityController",
					$"Observed learn for ability {msg.AbilityID} names unknown template {msg.TemplateID}; " +
					"its casts will not be drawn.");
				return;
			}

			List<int> events = msg.Events != null && msg.Events.Length > 0 ? new List<int>(msg.Events) : null;
			controller.RegisterObservedAbility(msg.AbilityID, template, events);
		}

		/// <summary>
		/// Destroys the local copy of an ability object the server ended by collision.
		/// </summary>
		/// <remarks>
		/// Runs for the owner as well as observers — the owner's predicted collision can miss a
		/// hit the server landed just as an observer's can. A copy already gone (the local
		/// collision or lifetime got there first) is simply not found.
		/// </remarks>
		private static void OnAbilityObjectDestroyedBroadcast(AbilityObjectDestroyedBroadcast msg, Channel channel)
		{
			FishNet.Managing.NetworkManager nm = FishNet.InstanceFinder.NetworkManager;
			if (nm == null || nm.ClientManager == null || nm.IsServerStarted)
			{
				return;
			}
			if (!nm.ClientManager.Objects.Spawned.TryGetValue(msg.CasterObjectID, out NetworkObject casterNob) ||
				casterNob == null)
			{
				return;
			}

			AbilityController controller = casterNob.GetComponent<AbilityController>();
			if (controller == null ||
				!controller.TryGetAbilityForVisuals(msg.AbilityID, out Ability ability) ||
				ability.Objects == null ||
				!ability.Objects.TryGetValue(msg.ContainerID, out Dictionary<int, AbilityObject> container) ||
				!container.TryGetValue(msg.ObjectID, out AbilityObject abilityObject) ||
				abilityObject == null)
			{
				return;
			}

			abilityObject.DestroyAbilityObjectInternal();
		}

		/// <summary>
		/// Reproduces a broadcast activation on an observing client.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Skipped on the server, which already spawned the object, and on the owner, which
		/// predicted it. Everyone else rebuilds it from the seed and tick — the same inputs the
		/// server used, so the same object.
		/// </para>
		/// <para>
		/// The target is resolved back through the spawned map rather than re-raycast. Re-tracing
		/// here would reintroduce exactly the divergence this whole design removes: an observer
		/// holds its peers interpolated, so the same ray against stale colliders can select a
		/// different character than the server chose.
		/// </para>
		/// </remarks>
		private static void OnAbilityActivatedBroadcast(AbilityActivatedBroadcast msg, Channel channel)
		{
			FishNet.Managing.NetworkManager nm = FishNet.InstanceFinder.NetworkManager;
			if (nm == null || nm.ClientManager == null || nm.IsServerStarted)
			{
				return;
			}
			if (!nm.ClientManager.Objects.Spawned.TryGetValue(msg.CasterObjectID, out NetworkObject casterNob) ||
				casterNob == null || casterNob.IsOwner)
			{
				return;
			}

			AbilityController controller = casterNob.GetComponent<AbilityController>();
			if (controller == null || controller.Character == null)
			{
				return;
			}
			/* Both sources, not just KnownAbilities. An ability the caster learned after this
			 * client started observing it is never in KnownAbilities — the payload that filled
			 * that dictionary predates the learn — and dropping the cast here is what made such
			 * abilities permanently invisible. AbilityLearnedObserverBroadcast files those in the
			 * observer-only store this consults second. */
			if (!controller.TryGetAbilityForVisuals(msg.AbilityID, out Ability ability))
			{
				return;
			}

			Transform target = null;
			if (msg.TargetObjectID >= 0 &&
				nm.ClientManager.Objects.Spawned.TryGetValue(msg.TargetObjectID, out NetworkObject targetNob) &&
				targetNob != null)
			{
				target = targetNob.transform;
			}

			Vector3 aimDirection = AimDirectionCompression.Decode(msg.PackedAimDirection);

			/* Camera spawns re-derive their pose; every other mode was sent one.
			 *
			 * A Camera pose is a pure function of the aim origin, the aim direction and the
			 * template's range, all of which are on the wire or on the template, so passing null
			 * here lets ResolveSpawnPose reproduce the server's own arithmetic exactly. The other
			 * modes read the caster's motor, collider or spawner transform, which this peer holds
			 * interpolated several hundred milliseconds behind the server, so resolving them
			 * locally would put the object on a parallel, offset trajectory for its whole life. */
			bool derivesPose = msg.SpawnMode == (byte)AbilitySpawnTarget.Camera;
			AbilitySpawnPose? pose = derivesPose ? (AbilitySpawnPose?)null : new AbilitySpawnPose(msg.SpawnPosition, msg.SpawnRotation);

			/* TargetInfo's hit position no longer travels: the only thing that read it was the
			 * Target-mode branch of ResolveSpawnPose, and that mode now receives its resolved pose
			 * outright. The target transform itself still matters — RequiresTarget templates refuse
			 * to spawn without one, and ability behaviours track it. */
			TargetInfo targetInfo = new TargetInfo(target, msg.SpawnPosition);

			/* Whether this cast is already running here decides if the message is news.
			 *
			 * A duplicate reaches this handler two ways: a retransmit, or — if forwarding is ever
			 * enabled without the guard on the sender — the locally simulated spawn arriving first.
			 * The allocator collapses the duplicate and hands back the object that already exists,
			 * so spawning is safe; fast-forwarding is not, because that object has been simulating
			 * since it was created and advancing it again puts it ahead of the server's copy. */
			PredictionTick spawnTick = new PredictionTick(msg.SpawnTick);
			bool alreadyRunning = AbilityContainerAllocator.IsSpawnAlreadyRunning(ability, msg.Seed, spawnTick);

			AbilityObject spawned = AbilityObject.Spawn(ability, controller.Character, controller.AbilitySpawner, targetInfo,
				msg.AimOrigin, aimDirection, msg.Seed, spawnTick, pose);

			if (alreadyRunning)
			{
				return;
			}

			/* The message took one network delay to get here, during which the server's copy
			 * kept moving. Catch up, less the interpolation the observer renders its peers behind. */
			if (spawned != null && nm.TimeManager != null)
			{
				uint fastForward = ComputeObserverFastForwardTicks(nm.TimeManager.Tick, msg.ServerTick,
					LagCompensationTick.SpectatorInterpolationTicks);
				spawned.FastForward(fastForward);
			}
		}

		/// <summary>
		/// Spawns a channeled ability object during activation (e.g., beam effects, continuous damage).
		/// Delegates to <see cref="ResolveTargetAndSpawn"/> which handles replay skipping and seed advance.
		/// </summary>
		private void SpawnChanneledAbility(Ability validatedAbility, AbilityActivationReplicateData activationData, ReplicateState state)
		{
			ResolveTargetAndSpawn(validatedAbility, activationData, state, isChannelTick: true);

			// Channeled abilities consume resources every tick, on replay too — see FinishAbility:
			// the reconcile restored the pre-tick resource value, so the replay must re-drain it.
			validatedAbility.ConsumeResources(Character, BloodResourceConversionTemplate);
		}

		/// <summary>
		/// Completes an ability activation: spawns the final ability object, consumes resources,
		/// adds cooldown, and resets ability state.
		/// </summary>
		/// <remarks>
		/// Resource consumption and the cooldown are applied on replay as well. A reconcile
		/// restores the server's state <b>at the reconcile tick</b>, and for roughly one RTT after
		/// a cast the owner keeps receiving reconciles for ticks that PRECEDE the cast — each of
		/// which wipes the predicted cooldown and refunds the cost. The replay of the cast tick
		/// is the only thing that can put them back, and it is not a double application: the
		/// restore already removed them, <see cref="CooldownInstance"/> is keyed by ability and
		/// immutable so re-adding it is idempotent, and a reconcile at or after the cast tick never
		/// replays the cast at all. (Skipping them on replay made the cooldown overlay and the
		/// resource bar flicker back for one RTT per cast and let a second press predict a second,
		/// server-denied cast.)
		/// </remarks>
		private void FinishAbility(Ability validatedAbility, AbilityActivationReplicateData activationData, ReplicateState state)
		{
			// Spawn the final ability object (skipped during replay; seed still advanced).
			ResolveTargetAndSpawn(validatedAbility, activationData, state);

			//Log.Debug($"6 Consumed On Tick: {activationData.GetTick()} State: {state}");
			validatedAbility.ConsumeResources(Character, BloodResourceConversionTemplate);
			AddCooldown(validatedAbility, activationData.GetPredictionTick());

			// Server-side ECA triggers fire once per real tick; the server never replays.
			if (!state.ContainsReplayed() && base.IsServerStarted)
			{
				AbilityEventData aed = new AbilityEventData(Character, validatedAbility.ID);
				aed.Add(new TickEventData(Character, activationData.GetPredictionTick()));
				Character.Invoke(onAbilityCompleteTriggers, aed);
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
			// DETERMINISM: (int)Math.Ceiling avoids platform-specific float rounding differences
			// between x86 and ARM that Mathf.CeilToInt is susceptible to.
			remainingTicks = (uint)(int)Math.Ceiling(consumable.ActivationTime / (double)base.TimeManager.TickDelta);

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

			/* No target/range check here, deliberately. ITargetController.Current is not
			 * deterministic: on the owner it is rewritten every 50 ms from the mouse ray, on the
			 * server it is whatever the previous cast's raycast left behind, and during replay it
			 * is simply "now". Gating on it made owner and server disagree about activations and
			 * produced spurious denials. Range is enforced authoritatively by the raycast in
			 * ResolveTargetAndSpawn (ability.Range is the ray length); the cheap client-side
			 * pre-filter lives in CanActivateOptimistic. */
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
			// Reverse iteration so a handler unsubscribing itself does not skip the next entry.
			for (int i = canManipulateHandlers.Count - 1; i >= 0; i--)
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

				/* Target and range pre-filter. Client-only and best-effort: it reads the live
				 * (mouse-driven) target, which is exactly why it cannot live in CanActivate. It
				 * stops the cast bar and resource prediction from starting for a cast the server's
				 * raycast will find nothing for — a required target that is missing, or a target
				 * clearly beyond the ability's reach. */
				if (cachedTargetController != null)
				{
					Transform target = cachedTargetController.Current.Target;
					if (validatedAbility.Template.RequiresTarget && target == null)
					{
						return false;
					}

					float abilityRange = validatedAbility.Range;
					if (abilityRange > 0f && target != null)
					{
						float distSqr = (Character.Transform.position - target.position).sqrMagnitude;
						if (distSqr > abilityRange * abilityRange)
						{
							return false;
						}
					}
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
		/// <summary>
		/// Public parameterless Cancel required by <see cref="IAbilityController"/>.
		/// Delegates to the internal implementation with default parameters.
		/// </summary>
		public void Cancel() => Cancel(ReplicateState.Invalid, false);

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
			chargedHoldTicks = 0;
			// The next channel starts over and owes its observers a reliable opening message.
			channelSpawnsBroadcast = 0u;

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

			// Reset persistent animation state when any ability ends.
			// Safe to call unconditionally on non-replay ticks.
			if (!state.ContainsReplayed())
			{
				cachedAnimationController?.SetBlocking(false);
			}
		}

		/// <summary>
		/// Adds a cooldown for the given ability using the cooldown controller.
		/// </summary>
		/// <param name="ability">The ability to add a cooldown for.</param>
		/// <param name="currentTick">The deterministic replicate tick at the moment of activation.</param>
		internal void AddCooldown(Ability ability, PredictionTick currentTick)
		{
			if (ability.Cooldown > 0.0f &&
				cachedCooldownController != null)
			{
				float cooldownReduction = CalculateSpeedReduction(CooldownReductionTemplate);
				float cooldown = ability.Cooldown * cooldownReduction;

				// The cooldown controller caches the tick delta once OnStartNetwork has run (and a
				// test can seed it); fall back to the TimeManager only when it does not hold one.
				float tickDelta = cachedCooldownController is CooldownController cooldownController
					? cooldownController.TickDelta
					: (float)base.TimeManager.TickDelta;
				// PredictionTick implicitly converts to uint for CooldownInstance.
				cachedCooldownController.AddCooldown(ability.ID, new CooldownInstance(currentTick, cooldown, tickDelta));
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
			// Reverse iteration so a handler unsubscribing itself does not skip the next entry.
			for (int i = canManipulateHandlers.Count - 1; i >= 0; i--)
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
		/// Interrupts the current ability: through the input stream where this peer writes it,
		/// and directly where it does not.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The queued half only works for the peer that produces this character's input.</b>
		/// <c>localInputFlags</c> is read by <c>HandleCharacterInput</c>, which runs only from
		/// <c>PopulateInput</c>, which <see cref="CharacterPredictionController"/> invokes only
		/// where <c>HasInputAuthority</c> — the owning client for a player, the server for an AI
		/// character or a pet. That covers a player interrupting itself and every server-side AI
		/// call site, and it is the better path for them: the cancel then happens inside the
		/// deterministic replicate and is reconciled like any other predicted state.
		/// </para>
		/// <para>
		/// <b>It did nothing at all for the case the mechanic exists for.</b>
		/// <c>InterruptAction</c> is server-only and its target is normally a PLAYER — which is
		/// neither server-driven nor owned by the server, so the bit was set on a peer that never
		/// reads it, never cleared (only <c>HandleCharacterInput</c> clears it), and the cast simply
		/// ran to completion. Cast interruption worked against NPCs and pets and failed silently
		/// against players.
		/// </para>
		/// <para>
		/// So the server cancels directly when it is not the peer that writes this character's
		/// input. It is authoritative for the activation either way — <c>currentAbilityID</c>,
		/// <c>remainingTicks</c>, <c>chargedHoldTicks</c> and <c>replicatedFlags</c> all ride
		/// <see cref="CharacterReconcileData"/> — so the owner's next reconcile carries the cleared
		/// state and its prediction is corrected by the same machinery that corrects a denied
		/// activation. The owner's cast bar is cleared by <c>ReconcilePredictedHistory</c>, which
		/// already fires <c>OnCancel</c> when it predicted an ability the server does not have.
		/// </para>
		/// <para>
		/// No cooldown is added, matching <see cref="ProcessInterrupt"/>: an interrupted cast is
		/// lost, not put on cooldown. <c>OnCancel</c> is suppressed for the reason it is suppressed
		/// there too — <c>OnInterrupt</c> is the event UI subscribers pair with, and firing both
		/// makes a cast bar flicker.
		/// </para>
		/// </remarks>
		/// <param name="attacker">The character causing the interrupt (not used).</param>
		public void Interrupt(ICharacter attacker)
		{
			Interrupt(base.IsServerStarted, HasInputAuthority);
		}

		/// <summary>
		/// The body of <see cref="Interrupt(ICharacter)"/>, with the two peer facts as arguments.
		/// </summary>
		/// <remarks>
		/// Separated so the rule can be exercised without a NetworkManager: both facts come from
		/// <see cref="FishNet.Object.NetworkBehaviour"/> state that only exists on a spawned object,
		/// and the case that was broken is precisely the one an edit-mode test cannot otherwise
		/// reach — the server acting on a character it does not own.
		/// </remarks>
		/// <param name="isServerStarted">Whether this peer is the server.</param>
		/// <param name="hasInputAuthority">Whether this peer writes this character's replicate input.</param>
		internal void Interrupt(bool isServerStarted, bool hasInputAuthority)
		{
			switch (ResolveInterruptDisposition(isServerStarted, hasInputAuthority))
			{
				case InterruptDisposition.Applied:
					OnInterrupt?.Invoke();
					Cancel(ReplicateState.Invalid, suppressCancelEvent: true);
					return;

				case InterruptDisposition.Queued:
					localInputFlags.EnableBit(AbilityActivationFlags.Interrupt);
					return;

				default:
					return;
			}
		}

		/// <summary>What an <see cref="Interrupt(ICharacter)"/> does on one peer.</summary>
		internal enum InterruptDisposition : byte
		{
			/// <summary>This peer neither writes the input nor owns the decision. Nothing happens.</summary>
			Ignored = 0,

			/// <summary>Raised as a one-shot input flag, read out of the replicate on the next tick.</summary>
			Queued = 1,

			/// <summary>Cancelled here and now, because no input stream will carry it.</summary>
			Applied = 2,
		}

		/// <summary>
		/// The whole rule <see cref="Interrupt(ICharacter)"/> turns on, as a pure function.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The Interrupt bit is a ONE-SHOT flag, and only one peer drains it.</b>
		/// <c>HandleCharacterInput</c> copies <c>localInputFlags</c> into the replicate and clears
		/// the bit in the same breath — but it returns early for a peer with no input authority,
		/// BEFORE that clear. So raising the bit anywhere else does not queue anything: it latches,
		/// for the object's whole life, and the guards in <see cref="Activate"/> then read a flag
		/// that can never go down again. That is why the third state exists. It used to be raised
		/// unconditionally, ahead of the branch, on every peer.
		/// </para>
		/// <list type="bullet">
		/// <item><b>Server, no input authority</b> — a player interrupted by something the server
		/// resolved. <see cref="InterruptDisposition.Applied"/>: nobody would read a queued flag,
		/// and nobody would clear one either.</item>
		/// <item><b>Server, input authority</b> — an NPC or a pet, whose input the server writes.
		/// <see cref="InterruptDisposition.Queued"/>: the flag is read on the very next tick and
		/// the cancel then happens inside the deterministic replicate, which is strictly better.</item>
		/// <item><b>Owning client</b> — a player interrupting its own cast.
		/// <see cref="InterruptDisposition.Queued"/>, same reason.</item>
		/// <item><b>Observer</b> — no authority over anything and no input stream.
		/// <see cref="InterruptDisposition.Ignored"/>; it is told what happened.</item>
		/// </list>
		/// </remarks>
		/// <param name="isServerStarted">Whether this peer is the server.</param>
		/// <param name="hasInputAuthority">Whether this peer writes this character's replicate input.</param>
		internal static InterruptDisposition ResolveInterruptDisposition(bool isServerStarted, bool hasInputAuthority)
		{
			if (ServerCancelsDirectly(isServerStarted, hasInputAuthority))
			{
				return InterruptDisposition.Applied;
			}

			/* Queued only where HandleCharacterInput will actually drain it. An observer reaches
			 * neither branch: it has no input stream to carry the flag and no authority to cancel. */
			return hasInputAuthority ? InterruptDisposition.Queued : InterruptDisposition.Ignored;
		}

		/// <summary>
		/// Whether an interrupt has to be applied here and now rather than queued as input.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The whole rule <see cref="Interrupt(ICharacter)"/> turns on, as a pure function, so the
		/// truth table can be asserted directly instead of inferred. Both inputs matter and neither
		/// alone is sufficient.
		/// </para>
		/// <list type="bullet">
		/// <item><b>Server, no input authority</b> — a player being interrupted by something the
		/// server resolved. TRUE: nobody would ever read the queued flag, which is the defect this
		/// exists to close.</item>
		/// <item><b>Server, input authority</b> — an NPC or a pet, whose input the server writes.
		/// FALSE: the queued flag is read on the very next tick and the cancel then happens inside
		/// the deterministic replicate, which is strictly better.</item>
		/// <item><b>Owning client</b> — a player interrupting its own cast. FALSE, same reason.</item>
		/// <item><b>Observer</b> — no authority over anything. FALSE; it is told what happened.</item>
		/// </list>
		/// </remarks>
		/// <param name="isServerStarted">Whether this peer is the server.</param>
		/// <param name="hasInputAuthority">Whether this peer writes this character's replicate input.</param>
		/// <returns>True when the interrupt must be applied directly.</returns>
		internal static bool ServerCancelsDirectly(bool isServerStarted, bool hasInputAuthority)
		{
			return isServerStarted && !hasInputAuthority;
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

			/* Crowd control. ValidateActiveCast calls this every tick of an active activation, so
			 * a stun landing mid-cast now cancels the cast rather than letting it finish; Activate
			 * and ActivateConsumable call it before queueing, so a new activation is refused for
			 * the duration. Runs on both client and server: the client prediction refuses locally
			 * and the server refuses authoritatively, which keeps the two in agreement instead of
			 * producing a reconcile correction on every attempt. */
			if (CharacterIncapacitation.IsIncapacitated(Character))
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