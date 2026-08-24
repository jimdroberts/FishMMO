using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The one attacking state. Handles target selection, spacing, ability activation and
	/// mid-combat re-targeting for every NPC archetype.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Archetypes are data, not subclasses.</b> Melee, archer, caster, defender and rogue
	/// behaviour all fall out of four serialized numbers — <see cref="PreferredDistance"/>,
	/// <see cref="MinComfortDistance"/>, <see cref="EmergencyRetreatThreshold"/> and the
	/// personality attached to the controller — fed into the shared
	/// <see cref="AICombatDecision.Plan"/>. A designer builds a new archetype by creating an
	/// asset, not by writing a class.
	/// </para>
	/// <para>
	/// The specialised subclasses that remain (<see cref="HealerAttackingState"/>,
	/// <see cref="DefenderAttackingState"/>, <see cref="RogueAttackingState"/>) exist only because
	/// they need something the numbers cannot express: healers must scan for injured allies,
	/// defenders must body-block for one, and rogues must open from behind.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Attacking State", menuName = "FishMMO/Character/NPC/AI/Attacking State", order = 0)]
	public class BaseAttackingState : BaseAIState
	{
		/// <summary>
		/// Preferred combat distance. The NPC will try to maintain roughly this distance from its target.
		/// A value of 0 means the NPC will close to melee range (agent radius).
		/// </summary>
		[Header("Spacing")]
		[Tooltip("Preferred combat distance. 0 = close to melee range.")]
		public float PreferredDistance = 0f;

		/// <summary>
		/// If the target is closer than this distance the NPC will try to back away.
		/// Only meaningful for ranged/caster archetypes. 0 disables retreat.
		/// </summary>
		[Tooltip("Distance below which the NPC backs away. 0 = never back away.")]
		public float MinComfortDistance = 0f;

		/// <summary>
		/// Fraction (0-1) of <see cref="MinComfortDistance"/> at which backing away escalates
		/// into an interrupt-and-run emergency retreat.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Fraction of MinComfortDistance that triggers an emergency retreat.")]
		public float EmergencyRetreatThreshold = 0.5f;

		/// <summary>
		/// Optional state to hand off to for an emergency retreat. When null the NPC backs away
		/// using the built-in logic without leaving this state.
		/// </summary>
		[Tooltip("Optional state for emergency backing off. Null = back away in place.")]
		public BaseAIState EmergencyRetreatState;

		/// <summary>
		/// Minimum seconds between ability activations. A small deterministic jitter is added so
		/// a pack of identical NPCs does not fire in lockstep.
		/// </summary>
		[Header("Pacing")]
		[Tooltip("Minimum seconds between ability activations. 0 = as fast as cooldowns allow.")]
		public float AttackCooldown = 1.5f;

		/// <summary>
		/// Maximum extra seconds randomly added to <see cref="AttackCooldown"/> per activation.
		/// </summary>
		[Tooltip("Maximum random jitter added to AttackCooldown.")]
		public float AttackCooldownJitter = 0.5f;

		/// <summary>
		/// How often (seconds) the NPC re-evaluates its target mid-combat.
		/// Set to 0 to disable mid-combat re-evaluation.
		/// </summary>
		[Header("Targeting")]
		[Tooltip("Seconds between mid-combat target re-evaluation. 0 = disabled.")]
		public float TargetReevaluationRate = 3.0f;

		/// <summary>
		/// A candidate must exceed the current target's aggression by at least this many points
		/// before the NPC will switch targets mid-combat. Prevents constant flip-flopping.
		/// </summary>
		[Tooltip("Aggression point lead required to switch targets mid-combat.")]
		public float AggressionSwitchThreshold = 50f;

		/// <summary>
		/// Optional positioning states entered occasionally for combat variety.
		/// These must have <see cref="BaseAIState.KeepsCombatTarget"/> enabled.
		/// </summary>
		[Header("Movement Variety")]
		[Tooltip("Optional positioning states (orbit / flank / strafe) entered mid-combat for variety.")]
		public List<BaseAIState> VarietyStates = new List<BaseAIState>();

		/// <summary>
		/// Chance (0-1) per attack cycle to step into one of the <see cref="VarietyStates"/>
		/// instead of attacking from where the NPC stands.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Chance per attack cycle to enter a variety state.")]
		public float MovementVarietyChance = 0f;

		/// <summary>
		/// Only roll for a variety state when the target is within this multiple of the
		/// engagement distance. Stops an NPC from strafing while still closing the gap.
		/// </summary>
		[Tooltip("Only roll for variety when within this multiple of the engagement distance.")]
		public float VarietyEngagementMultiplier = 1.5f;

		/// <summary>
		/// How far a pet may stray from its owner before it breaks off and returns to heel.
		/// Ignored for NPCs that are not pets. 0 disables.
		/// </summary>
		/// <remarks>
		/// Pet-awareness lives here rather than in a <see cref="PetAttackingState"/> subclass on
		/// purpose. A pet only differs from a wild NPC in two ways — its leash anchor follows its
		/// owner, and it returns to that owner instead of wandering — and putting those two rules
		/// in the shared base means a pet healer, defender or rogue gets them for free. A subclass
		/// could not, because it would have to inherit from the archetype instead.
		/// </remarks>
		[Header("Pet")]
		[Tooltip("Pets only: distance from the owner at which the pet breaks off and returns. 0 disables.")]
		public float OwnerLeashRange = 30.0f;

		/// <summary>
		/// When true, attackers spread around their shared target instead of all pathing to the
		/// same point.
		/// </summary>
		/// <remarks>
		/// The NavMeshAgent's own avoidance stops agents overlapping, but it has no say in where
		/// they are trying to go. Five NPCs told to reach one point will shove each other around
		/// it indefinitely no matter how avoidance is tuned, because the conflict is in the
		/// destinations, not the collisions. Slot assignment separates the destinations so
		/// avoidance is only asked to handle incidental crossings.
		/// </remarks>
		[Header("Spacing (multi-attacker)")]
		[Tooltip("Spread attackers into a ring around a shared target instead of converging on one point.")]
		public bool UseCombatSlots = true;

		/// <summary>
		/// Seconds the NPC will keep trying to reach an unreachable target before giving up on it.
		/// 0 disables the check.
		/// </summary>
		[Header("Chase")]
		[Tooltip("Seconds spent unable to reach a target before breaking off. 0 = chase forever.")]
		public float UnreachableTargetTimeout = 6.0f;

		/// <summary>
		/// Called when entering the attacking state. Sets agent speed to run.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Enter(AIController controller)
		{
			// Allow the agent to run
			controller.Agent.speed = Constants.Character.RunSpeed;
			controller.AttackCooldownTimer = 0f;
		}

		/// <summary>
		/// Called when exiting the attacking state. Resets agent speed and, unless the NPC is
		/// stepping into a combat sub-state, drops the target and interrupts any cast.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Exit(AIController controller)
		{
			// Return to walk speed
			controller.Agent.speed = Constants.Character.WalkSpeed;

			/* A transition into a combat sub-state (orbit, flank, kite) is not a disengage.
			 * Clearing the target here is what previously broke every variety roll: the sub-state
			 * was handed a null target and bailed straight to idle. */
			if (controller.PendingState != null && controller.PendingState.KeepsCombatTarget)
			{
				return;
			}

			// Leaving combat frees this attacker's place in the ring so the pack closes up.
			ReleaseCombatSlot(controller);

			controller.Target = null;
			controller.LookTarget = null;
			if (controller.Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.Interrupt(null); // Ensure any cast is stopped
			}
		}

		/// <summary>
		/// Gives up this NPC's slot in whatever ring it currently occupies.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		protected static void ReleaseCombatSlot(AIController controller)
		{
			if (controller.Character != null)
			{
				AICombatSlots.Release(controller.Character.ID);
			}
		}

		/// <summary>
		/// Called every AI tick to update the attacking state. Handles death, personality-driven
		/// flight, target loss, and attack logic.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Seconds elapsed since the previous AI tick.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (controller.AttackCooldownTimer > 0f)
			{
				controller.AttackCooldownTimer -= deltaTime;
			}

			// A pet's leash anchor travels with its owner; check it before anything else.
			if (!UpdatePetLeash(controller))
			{
				return;
			}

			// Check if the AI is dead
			if (!controller.Character.TryGet(out ICharacterDamageController damageController) ||
				!damageController.IsAlive)
			{
				// If AI is dead, stop attacking
				controller.TransitionToIdleState(); // Or a specific 'Dead' state
				return;
			}

			// Check if the target is lost or inactive
			if (controller.Target == null ||
				!controller.Target.gameObject.activeSelf)
			{
				// Re-use the controller's buffer to avoid per-frame GC allocations.
				controller.CombatTargetBuffer.Clear();
				if (controller.AttackingState != null &&
					SweepForEnemies(controller, controller.CombatTargetBuffer))
				{
					controller.ChangeState(controller.AttackingState, controller.CombatTargetBuffer);
					return;
				}

				OnCombatEnded(controller);
				return;
			}

			// Verify the target is still alive
			ICharacter targetCharacter = controller.TargetCharacter;
			if (targetCharacter == null ||
				!targetCharacter.TryGet(out ICharacterDamageController targetDamage) ||
				!targetDamage.IsAlive)
			{
				controller.Target = null;
				controller.LookTarget = null;
				OnCombatEnded(controller);
				return;
			}

			// Try to attack the current target
			TryAttack(controller, targetCharacter);

			// Periodically re-evaluate target using the personality's targeting mode.
			ReevaluateTarget(controller, deltaTime);
		}

		/// <summary>
		/// Keeps a pet's leash anchored to its owner and breaks off the fight if the pet has
		/// chased its target too far from them.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <returns>False if the pet disengaged and the caller should stop.</returns>
		protected bool UpdatePetLeash(AIController controller)
		{
			Pet pet = controller.Character as Pet;
			if (pet == null || pet.PetOwner == null || pet.PetOwner.Transform == null)
			{
				return true;
			}

			/* AIController.Home already resolves to the owner's live position for a pet, so the
			 * inherited leash check tracks the owner without this state having to write anything.
			 * What is left here is the pet-specific range at which it gives up the chase. */
			Vector3 ownerPosition = pet.PetOwner.Transform.position;

			if (OwnerLeashRange <= 0f)
			{
				return true;
			}

			float sqrDistance = (controller.Character.Transform.position - ownerPosition).sqrMagnitude;
			if (sqrDistance <= OwnerLeashRange * OwnerLeashRange)
			{
				return true;
			}

			OnCombatEnded(controller);
			return false;
		}

		/// <summary>
		/// Called when the NPC has run out of targets: a wild NPC drifts into a movement state,
		/// a pet returns to its owner's heel.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		protected virtual void OnCombatEnded(AIController controller)
		{
			ReleaseCombatSlot(controller);

			if (controller.Character is Pet)
			{
				/* A pet's idle state is its follow state. Sending it to a random movement state
				 * would have it wander off from the owner it belongs to. */
				controller.Target = null;
				controller.LookTarget = null;
				controller.TransitionToIdleState();
				return;
			}

			controller.TransitionToRandomMovementState();
		}

		/// <summary>
		/// A passive pet stops fighting the moment its ordered target is gone; it does not go
		/// looking for the next one.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <returns>True if the NPC is a pet that refuses to pick a new target.</returns>
		protected static bool RefusesNewTarget(AIController controller)
		{
			return controller.Character is Pet pet && pet.Stance == PetStance.Passive;
		}

		/// <summary>
		/// Picks a valid target from the provided list using the group role, the personality's
		/// <see cref="AITargetingMode"/>, and the aggression table, in that order.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="targets">List of potential targets.</param>
		public virtual void PickTarget(AIController controller, List<ICharacter> targets)
		{
			if (RefusesNewTarget(controller))
			{
				OnCombatEnded(controller);
				return;
			}

			ICharacter target = null;

			// --- Role-based group targeting ---
			if (controller.Group != null && controller.GroupRole != NPCGroupRole.None)
			{
				target = PickRoleBasedTarget(controller, targets);
			}

			// --- Personality-driven targeting ---
			if (target == null)
			{
				target = PickByTargetingMode(controller, targets);
			}

			// Fallback: pick the first alive candidate.
			if (target == null)
			{
				target = FirstAlive(targets);
			}

			if (target != null)
			{
				controller.Target = target.Transform;
				controller.LookTarget = target.Transform;
			}
			else
			{
				// No valid target found, transition out of attacking state
				OnCombatEnded(controller);
			}
		}

		/// <summary>
		/// Selects a target according to the controller personality's <see cref="AITargetingMode"/>.
		/// Falls back to the aggression table when no personality is assigned.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="targets">Available candidates.</param>
		/// <returns>The chosen target, or null when the mode produced nothing.</returns>
		protected static ICharacter PickByTargetingMode(AIController controller, List<ICharacter> targets)
		{
			AITargetingMode mode = controller.Personality != null
				? controller.Personality.TargetingMode
				: AITargetingMode.Threat;

			switch (mode)
			{
				case AITargetingMode.Random:
					return AITargetSelection.PickRandom(targets, controller.NpcRNG);

				case AITargetingMode.Weakest:
					return AITargetSelection.PickWeakest(targets);

				case AITargetingMode.Nearest:
					return AITargetSelection.PickNearest(targets, controller.Character.Transform.position);

				default:
					if (controller.Aggression != null && controller.Aggression.HasAggression)
					{
						return controller.Aggression.PickTarget(targets, controller.NpcRNG);
					}
					return null;
			}
		}

		/// <summary>
		/// Returns the first living, active candidate in the list.
		/// </summary>
		/// <param name="targets">Candidates to scan.</param>
		/// <returns>The first valid candidate, or null.</returns>
		protected static ICharacter FirstAlive(List<ICharacter> targets)
		{
			if (targets == null) return null;

			for (int i = 0; i < targets.Count; i++)
			{
				ICharacter candidate = targets[i];
				if (AITargetSelection.IsValidTarget(candidate))
				{
					return candidate;
				}
			}
			return null;
		}

		/// <summary>
		/// Selects a target based on the controller's <see cref="NPCGroupRole"/>.
		/// Returns null if the role doesn't produce a valid target (caller falls through
		/// to default logic).
		/// </summary>
		/// <param name="controller">The AI controller with group and role info.</param>
		/// <param name="targets">Available targets from the sweep.</param>
		/// <returns>A role-appropriate target, or null.</returns>
		protected virtual ICharacter PickRoleBasedTarget(AIController controller, List<ICharacter> targets)
		{
			switch (controller.GroupRole)
			{
				case NPCGroupRole.Tank:
					// Tank: always target the highest-threat entry.
					if (controller.Aggression == null || !controller.Aggression.HasAggression)
						return null;
					return controller.Aggression.PickTarget(targets, controller.NpcRNG);

				case NPCGroupRole.DPS:
				case NPCGroupRole.Support:
					// DPS/Support: follow the group's shared target when available.
					return PickGroupSharedTarget(controller);

				// Healer role is handled by HealerAttackingState — fall through to default.
				default:
					return null;
			}
		}

		/// <summary>
		/// Returns the group's shared target if it is still alive and active.
		/// Used by DPS and Support roles to focus fire with the group.
		/// </summary>
		private static ICharacter PickGroupSharedTarget(AIController controller)
		{
			if (controller.Group == null || controller.Group.GroupTarget == null)
				return null;

			ICharacter groupTarget = controller.Group.GroupTarget.GetComponent<ICharacter>();
			return AITargetSelection.IsValidTarget(groupTarget) ? groupTarget : null;
		}

		/// <summary>
		/// Periodically reconsiders the current target.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Threat-based NPCs switch when a tracked aggressor exceeds the current target by
		/// <see cref="AggressionSwitchThreshold"/>; a rampaging NPC instead re-rolls onto a random
		/// nearby enemy at its personality's retarget chance, which is what makes it impossible to
		/// hold with threat.
		/// </para>
		/// <para>
		/// Candidates come from a fresh detection sweep. The previous implementation scanned
		/// <c>controller.SweepHits</c> directly, but that buffer is only filled by
		/// <see cref="AIController"/>'s out-of-combat sweep, which is skipped for the whole
		/// duration of a fight — so it held stale colliders from before combat started (and
		/// entries past the last hit count from sweeps before that), and could retarget onto
		/// something no longer present.
		/// </para>
		/// </remarks>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Seconds elapsed since the previous AI tick.</param>
		protected virtual void ReevaluateTarget(AIController controller, float deltaTime)
		{
			if (TargetReevaluationRate <= 0f)
				return;

			controller.TargetReevaluationTimer -= deltaTime;
			if (controller.TargetReevaluationTimer > 0f)
				return;

			controller.TargetReevaluationTimer = TargetReevaluationRate;

			if (controller.Target == null)
				return;

			ICharacter currentTarget = controller.TargetCharacter;
			if (currentTarget == null)
				return;

			// --- Rampage: unfocused re-targeting that ignores threat entirely. ---
			float retargetChance = controller.Personality != null
				? controller.Personality.EffectiveRetargetChance
				: 0f;

			if (retargetChance > 0f)
			{
				DeterministicRNG rng = controller.NpcRNG;
				if ((rng ?? DeterministicRNG.Shared).NextFloat() < retargetChance)
				{
					controller.CombatTargetBuffer.Clear();
					if (SweepForEnemies(controller, controller.CombatTargetBuffer))
					{
						ICharacter roll = AITargetSelection.PickRandom(controller.CombatTargetBuffer, rng);
						if (roll != null && roll != currentTarget)
						{
							controller.Target = roll.Transform;
							controller.LookTarget = roll.Transform;
						}
					}
				}
				return;
			}

			// --- Threat: only switch on a decisive aggression lead. ---
			if (controller.Aggression == null || !controller.Aggression.HasAggression)
				return;

			controller.CombatTargetBuffer.Clear();
			if (!SweepForEnemies(controller, controller.CombatTargetBuffer))
				return;

			long currentID = currentTarget.ID;
			for (int i = 0; i < controller.CombatTargetBuffer.Count; i++)
			{
				ICharacter candidate = controller.CombatTargetBuffer[i];
				if (candidate == null || candidate.ID == currentID)
					continue;

				if (controller.Aggression.ShouldSwitchTarget(currentID, candidate.ID, AggressionSwitchThreshold))
				{
					controller.Target = candidate.Transform;
					controller.LookTarget = candidate.Transform;
					return;
				}
			}
		}

		/// <summary>
		/// Builds the decision context for this tick from the archetype's tuning and the current
		/// combat measurements.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="distance">Current distance to the target.</param>
		/// <param name="chosenAbility">The ability the picker returned, or null.</param>
		/// <returns>A context ready for <see cref="AICombatDecision.Plan"/>.</returns>
		protected AICombatContext BuildContext(AIController controller, float distance, Ability chosenAbility)
		{
			AICombatContext context = default;
			context.Distance = distance;
			context.PreferredDistance = PreferredDistance;
			context.MinComfortDistance = MinComfortDistance;
			context.EmergencyRetreatThreshold = EmergencyRetreatThreshold;
			context.HasUsableAbility = chosenAbility != null;
			context.AbilityRange = chosenAbility != null ? chosenAbility.Range : 0f;
			context.MeleeReach = GetMeleeReach(controller);
			context.HealthPercent = controller.GetHealthPercent();
			context.WasAttacking = controller.WasAttackingLastTick;

			AICombatPersonality personality = controller.Personality;
			context.FleeHealthThreshold = personality != null ? personality.EffectiveRetreatHealthThreshold : 0f;
			context.CanFlee = personality != null && controller.RetreatState != null;

			return context;
		}

		/// <summary>
		/// The distance a melee archetype closes to, derived from the agent's footprint but never
		/// smaller than one metre so a small agent can still reach its target.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <returns>The melee engagement distance in world units.</returns>
		protected static float GetMeleeReach(AIController controller)
		{
			return (controller.Agent.radius * 2.0f).Max(1.0f);
		}

		/// <summary>
		/// Attempts to attack the current target: picks an ability, asks
		/// <see cref="AICombatDecision"/> what to do about it, then executes that intent.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="targetCharacter">The target character being attacked.</param>
		protected virtual void TryAttack(AIController controller, ICharacter targetCharacter)
		{
			if (!controller.Character.TryGet(out IAbilityController abilityController))
			{
				controller.TransitionToIdleState();
				return;
			}

			if (HandleActivationInProgress(controller, abilityController))
				return;

			float distance = Mathf.Sqrt(controller.GetSqrDistanceToTarget());

			// Occasionally step into a positioning sub-state instead of attacking flat-footed.
			if (TryMovementVariety(controller, distance))
				return;

			Ability chosenAbility = controller.AttackCooldownTimer > 0f ? null : PickAbility(controller);

			AICombatContext context = BuildContext(controller, distance, chosenAbility);
			AICombatPlan plan = AICombatDecision.Plan(context);

			ExecutePlan(controller, abilityController, targetCharacter, plan, context, chosenAbility);
		}

		/// <summary>
		/// Chooses the ability to use against the current enemy target.
		/// Overridden by archetypes that must reorder their spellbook — a defender leads with a
		/// taunt, a healer breaks off to heal.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <returns>The chosen ability, or null when nothing is usable.</returns>
		protected virtual Ability PickAbility(AIController controller)
		{
			return controller.PickBestAbility(
				PreferredDistance > 0f ? PreferredDistance : float.MaxValue,
				IsEnemyAbility);
		}

		/// <summary>
		/// True when an ability is something to aim at an enemy.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The classifier reads what an ability does out of its ECA actions, so this needs no list
		/// of ability IDs on the state: an ability whose graph only heals or only buffs is filtered
		/// out of the attack rotation wherever it came from, and one added tomorrow is filtered out
		/// too.
		/// </para>
		/// <para>
		/// Deliberately permissive at the edges. An ability that both damages and buffs stays in —
		/// the damage is the point. A self-cast shield stays in, because the NPC aims it at itself
		/// rather than at the enemy and casting it mid-fight is exactly right. Only an ability that
		/// is purely supportive <em>and</em> aimed at someone else is excluded, which is the case
		/// that produced the actual absurdity: an NPC healing the player it was trying to kill.
		/// </para>
		/// <para>
		/// An ability with no recognisable actions classifies as
		/// <see cref="AIAbilityIntent.None"/> and is allowed through, so content that predates
		/// classification keeps working rather than silently disarming the NPC that knows it.
		/// </para>
		/// </remarks>
		/// <param name="ability">The ability to test.</param>
		/// <returns>True if the ability may be used against an enemy.</returns>
		public static bool IsEnemyAbility(Ability ability)
		{
			if (ability == null || ability.Template == null)
			{
				return false;
			}

			AIAbilityIntent intent = AIAbilityClassifier.Classify(ability);

			// Anything with an offensive component belongs in the attack rotation.
			if (intent.IsOffensive())
			{
				return true;
			}

			// Purely supportive: only if the NPC is casting it on itself.
			if (intent.IsSupportive())
			{
				return ability.Template.AbilitySpawnTarget == AbilitySpawnTarget.Self;
			}

			return true;
		}

		/// <summary>
		/// Carries out a decided <see cref="AICombatPlan"/>.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="abilityController">The NPC's ability controller.</param>
		/// <param name="targetCharacter">The target character.</param>
		/// <param name="plan">The decision to execute.</param>
		/// <param name="context">The context the decision was made from.</param>
		/// <param name="chosenAbility">The ability the picker returned, or null.</param>
		protected virtual void ExecutePlan(
			AIController controller,
			IAbilityController abilityController,
			ICharacter targetCharacter,
			AICombatPlan plan,
			in AICombatContext context,
			Ability chosenAbility)
		{
			// Remembered for the next tick's range hysteresis.
			controller.WasAttackingLastTick = plan.Intent == AICombatIntent.Attack ||
											  plan.Intent == AICombatIntent.HoldPosition;

			switch (plan.Intent)
			{
				case AICombatIntent.Flee:
					abilityController.Interrupt(null);
					controller.ChangeState(controller.RetreatState);
					return;

				case AICombatIntent.EmergencyRetreat:
					abilityController.Interrupt(null);
					if (EmergencyRetreatState != null)
					{
						controller.ChangeState(EmergencyRetreatState);
						return;
					}
					RetreatFromTarget(controller, plan.DesiredDistance);
					return;

				case AICombatIntent.BackAway:
					if (plan.FireWhileMoving && chosenAbility != null)
					{
						ActivateAbility(controller, abilityController, chosenAbility);
					}
					RetreatFromTarget(controller, plan.DesiredDistance);
					return;

				case AICombatIntent.Attack:
					PerformAttack(controller, abilityController, chosenAbility, targetCharacter, context.Distance);
					return;

				case AICombatIntent.CloseDistance:
					MoveTowardTarget(controller, plan.DesiredDistance);
					return;

				default:
					controller.Agent.isStopped = true;
					return;
			}
		}

		/// <summary>
		/// Rolls for a movement-variety transition into one of the <see cref="VarietyStates"/>.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="distance">Current distance to the target.</param>
		/// <returns>True if a transition happened and the caller should stop.</returns>
		protected bool TryMovementVariety(AIController controller, float distance)
		{
			if (MovementVarietyChance <= 0f || VarietyStates == null || VarietyStates.Count < 1)
				return false;

			float engageDistance = PreferredDistance > 0f ? PreferredDistance : GetMeleeReach(controller);
			if (distance > engageDistance * VarietyEngagementMultiplier)
				return false;

			DeterministicRNG rng = controller.NpcRNG;
			if ((rng ?? DeterministicRNG.Shared).NextFloat() >= MovementVarietyChance)
				return false;

			BaseAIState variety = VarietyStates.GetRandom();
			if (variety == null)
				return false;

			controller.ChangeState(variety);
			return true;
		}

		/// <summary>
		/// Performs the attack by activating the chosen ability via the ability controller.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="abilityController">The ability controller to activate on.</param>
		/// <param name="ability">The chosen ability to activate.</param>
		/// <param name="targetCharacter">The target character being attacked.</param>
		/// <param name="distance">Current distance to the target.</param>
		public virtual void PerformAttack(AIController controller, IAbilityController abilityController, Ability ability, ICharacter targetCharacter, float distance)
		{
			// Stop moving while attacking for accuracy.
			controller.Agent.isStopped = true;

			ActivateAbility(controller, abilityController, ability);
		}

		/// <summary>
		/// Activates an ability and arms the per-NPC attack pacing timer.
		/// </summary>
		/// <remarks>
		/// <see cref="AttackCooldown"/> was previously a documented but entirely unread field —
		/// NPCs fired on every tick that had anything off cooldown. The timer lives on the
		/// controller rather than on this shared ScriptableObject, which every NPC using this
		/// archetype has a reference to.
		/// </remarks>
		/// <param name="controller">The AI controller.</param>
		/// <param name="abilityController">The ability controller to activate on.</param>
		/// <param name="ability">The ability to activate.</param>
		protected void ActivateAbility(AIController controller, IAbilityController abilityController, Ability ability)
		{
			if (ability == null)
				return;

			// Pass isHeld=true for channeled/charged abilities so the activation pipeline
			// keeps the held state active. Regular abilities get isHeld=false.
			bool held = abilityController.RequiresHeld(ability.ID);
			abilityController.Activate(ability.ID, held);

			if (AttackCooldown > 0f)
			{
				float jitter = 0f;
				if (AttackCooldownJitter > 0f)
				{
					DeterministicRNG rng = controller.NpcRNG;
					jitter = (rng ?? DeterministicRNG.Shared).Range(0f, AttackCooldownJitter);
				}
				controller.AttackCooldownTimer = AttackCooldown + jitter;
			}
		}

		/// <summary>
		/// Returns true if the ability controller is currently activating or has a queued ability.
		/// Stops the agent and auto-releases charged abilities when their charge time completes.
		/// Call at the start of <see cref="TryAttack"/> to skip attack logic while casting.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="abilityController">The ability controller to check.</param>
		/// <returns>True if an activation is in progress and the caller should return early.</returns>
		protected static bool HandleActivationInProgress(AIController controller, IAbilityController abilityController)
		{
			if (!abilityController.IsActivating && !abilityController.AbilityQueued)
				return false;

			controller.Agent.isStopped = true;

			if (abilityController.IsActivating && abilityController.RemainingActivationTime <= 0f)
			{
				abilityController.Release();
			}
			return true;
		}

		/// <summary>
		/// Moves the NPC toward its target, stopping at the specified range.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="stopRange">Distance from the target at which to stop.</param>
		protected void MoveTowardTarget(AIController controller, float stopRange)
		{
			if (controller.Target == null)
			{
				OnCombatEnded(controller);
				return;
			}

			controller.Resume();

			float sphereRadius = Mathf.Max(stopRange * AICombatDecision.RANGE_APPROACH_FACTOR, 0.1f);
			Vector3 approach = ResolveApproachPosition(controller, sphereRadius);

			controller.TryMoveTo(approach);

			HandleChaseObstruction(controller, stopRange);
		}

		/// <summary>
		/// Picks the world position this attacker should walk to, given how many others are
		/// already engaging the same target.
		/// </summary>
		/// <remarks>
		/// With slots disabled, or when this attacker is the only one on its target, the NPC
		/// simply projects its own position onto a circle at range — the shortest approach, and
		/// the one that looks most natural for a single attacker. Once a second attacker joins,
		/// both take assigned angular slots so they arrive on opposite sides rather than fighting
		/// over the same metre of ground.
		/// </remarks>
		/// <param name="controller">The AI controller.</param>
		/// <param name="stopDistance">Distance from the target to stand at.</param>
		/// <returns>The world position to path to.</returns>
		protected Vector3 ResolveApproachPosition(AIController controller, float stopDistance)
		{
			Vector3 selfPosition = controller.Character.Transform.position;
			Vector3 targetPosition = controller.Target.position;

			if (UseCombatSlots)
			{
				ICharacter target = controller.TargetCharacter;
				if (target != null && controller.Character != null)
				{
					AICombatSlots.Claim(
						target.ID,
						controller.Character.ID,
						stopDistance,
						controller.Agent.radius,
						out int slot,
						out int ring,
						out int ringCapacity);

					// A lone attacker gains nothing from a fixed slot and looks better taking the
					// direct line, so only use the ring once there is someone to share it with.
					if (AICombatSlots.GetAttackerCount(target.ID) > 1)
					{
						return AICombatSlots.GetSlotPosition(
							targetPosition, slot, ring, ringCapacity, stopDistance, controller.Agent.radius);
					}
				}
			}

			// Aim for a point on a sphere around the target so the NPC stops at range rather than
			// walking into the target's collider.
			return Vector3Extensions.GetNearestPositionOnSphere(selfPosition, targetPosition, stopDistance);
		}

		/// <summary>
		/// Detects an NPC that cannot reach its target and breaks off rather than jogging on the
		/// spot forever.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A target that stands somewhere the NPC cannot path to — on a rock, across a gap, behind
		/// a door — produces a partial path. The NPC walks to the closest reachable point and
		/// stops, still "in combat", still holding threat, and never attacks or disengages. That is
		/// the melee equivalent of the stuck pet, and it is how players kite mobs into a permanent
		/// no-op.
		/// </para>
		/// <para>
		/// Recovery first tries to walk around; failing that, the NPC gives up on the target so a
		/// leash or a fresh sweep can take over.
		/// </para>
		/// </remarks>
		/// <param name="controller">The AI controller.</param>
		/// <param name="stopRange">Distance from the target the NPC is trying to reach.</param>
		protected void HandleChaseObstruction(AIController controller, float stopRange)
		{
			if (UnreachableTargetTimeout <= 0f)
			{
				return;
			}

			AIMovementProgress progress = controller.GetMovementProgress(
				controller.LastAiDeltaTime,
				Mathf.Max(stopRange, AIController.ARRIVAL_TOLERANCE));

			bool blocked = progress == AIMovementProgress.Stuck || controller.LastPathWasPartial;

			if (!blocked)
			{
				controller.UnreachableTargetTimer = 0f;
				return;
			}

			controller.UnreachableTargetTimer += controller.LastAiDeltaTime;

			if (controller.UnreachableTargetTimer < UnreachableTargetTimeout)
			{
				// Still within the grace period — try to walk around the obstruction.
				controller.TryRecoverFromStuck(controller.Home);
				return;
			}

			// Give up on this target. Dropping threat as well prevents an immediate re-acquire of
			// the same unreachable enemy on the very next sweep.
			controller.UnreachableTargetTimer = 0f;
			controller.Aggression?.RemoveEntry(ResolveTargetID(controller));
			controller.Target = null;
			controller.LookTarget = null;
			OnCombatEnded(controller);
		}

		/// <summary>
		/// Returns the character ID of the current target, or 0.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <returns>The target's ID, or 0 when it cannot be resolved.</returns>
		private static long ResolveTargetID(AIController controller)
		{
			if (controller.Target == null)
			{
				return 0;
			}
			ICharacter target = controller.TargetCharacter;
			return target != null ? target.ID : 0;
		}

		/// <summary>
		/// Moves the NPC away from its target to the specified safe distance.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="safeDistance">The distance to retreat to.</param>
		protected void RetreatFromTarget(AIController controller, float safeDistance)
		{
			if (controller.Target == null) return;

			controller.Resume();

			float distance = Mathf.Max(safeDistance, 1.0f);
			Vector3 position = controller.Character.Transform.position;

			Vector3 away = position - controller.Target.position;
			away.y = 0f;

			// Directly on top of the target: any direction is "away". Pick a deterministic one so
			// the NPC does not jitter between arbitrary directions on successive ticks.
			if (away.sqrMagnitude < 0.0001f)
			{
				away = -controller.Character.Transform.forward;
			}

			away.Normalize();

			if (controller.TryMoveTo(position + away * distance) != AIMovementResult.Failed)
			{
				return;
			}

			/* Backed into a corner: straight back is off the NavMesh. Try sidestepping before
			 * giving up, otherwise a kiting archer pinned against a wall stops retreating and
			 * simply stands there being hit. */
			Vector3 right = Vector3.Cross(Vector3.up, away);
			if (controller.TryMoveTo(position + (away + right).normalized * distance) != AIMovementResult.Failed)
			{
				return;
			}
			controller.TryMoveTo(position + (away - right).normalized * distance);
		}
	}
}
