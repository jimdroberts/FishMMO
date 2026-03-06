using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Base AI state for attacking behavior. Handles entering, exiting, updating, target picking,
	/// range checking, and ability activation for NPCs. Subclasses can override distance preferences,
	/// ability selection, and movement behavior for melee, ranged, or caster archetypes.
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Attacking State", menuName = "FishMMO/Character/NPC/AI/Attacking State", order = 0)]
	public class BaseAttackingState : BaseAIState
	{
		/// <summary>
		/// Preferred combat distance. The NPC will try to maintain roughly this distance from its target.
		/// A value of 0 means the NPC will close to melee range (agent radius).
		/// </summary>
		[Tooltip("Preferred combat distance. 0 = close to melee range.")]
		public float PreferredDistance = 0f;

		/// <summary>
		/// If the target is closer than this distance the NPC will try to back away.
		/// Only meaningful for ranged/caster archetypes. 0 disables retreat.
		/// </summary>
		[Tooltip("Distance below which the NPC retreats. 0 = never retreat.")]
		public float MinComfortDistance = 0f;

		/// <summary>
		/// Minimum time (seconds) between ability activations. Prevents the NPC from
		/// spamming abilities every tick. A small random jitter is added automatically.
		/// </summary>
		[Tooltip("Minimum seconds between ability activations.")]
		public float AttackCooldown = 1.5f;

		/// <summary>
		/// How often (seconds) the NPC re-evaluates its target mid-combat using the aggression table.
		/// Lower values make the NPC more responsive to threat changes but slightly more expensive.
		/// Set to 0 to disable mid-combat re-evaluation.
		/// </summary>
		[Tooltip("Seconds between mid-combat target re-evaluation. 0 = disabled.")]
		public float TargetReevaluationRate = 3.0f;

		/// <summary>
		/// A candidate must exceed the current target's aggression by at least this many points
		/// before the NPC will switch targets mid-combat. Prevents constant flip-flopping.
		/// </summary>
		[Tooltip("Aggression point lead required to switch targets mid-combat.")]
		public float AggressionSwitchThreshold = 50f;

		/// <summary>
		/// Called when entering the attacking state. Sets agent speed to run.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Enter(AIController controller)
		{
			// Allow the agent to run
			controller.Agent.speed = Constants.Character.RunSpeed;
		}

		/// <summary>
		/// Called when exiting the attacking state. Resets agent speed, clears targets, and interrupts abilities.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Exit(AIController controller)
		{
			// Return to walk speed
			controller.Agent.speed = Constants.Character.WalkSpeed;
			controller.Target = null;
			controller.LookTarget = null;
			if (controller.Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.Interrupt(null); // Ensure any cast is stopped
			}
		}

		/// <summary>
		/// Called every frame to update the attacking state. Handles death, target loss, and attack logic.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Frame time.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
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
				// If the target is lost... Check again for nearby enemies
				List<ICharacter> enemies = new List<ICharacter>();
				if (controller.AttackingState != null &&
					SweepForEnemies(controller, enemies))
				{
					controller.ChangeState(controller.AttackingState, enemies);
					return;
				}
				// Otherwise handle post attack logic
				else
				{
					controller.TransitionToRandomMovementState();
				}
				return;
			}

			// Verify the target is still alive
			ICharacter targetCharacter = controller.Target.GetComponent<ICharacter>();
			if (targetCharacter == null ||
				!targetCharacter.TryGet(out ICharacterDamageController targetDamage) ||
				!targetDamage.IsAlive)
			{
				controller.Target = null;
				controller.LookTarget = null;
				controller.TransitionToRandomMovementState();
				return;
			}

			// Try to attack the current target
			TryAttack(controller, targetCharacter);

			// Periodically re-evaluate target using aggression table.
			ReevaluateTarget(controller, deltaTime);
		}

		/// <summary>
		/// Picks a valid target from the provided list using the aggression table for intelligent
		/// threat-based selection. Falls back to first-alive selection when no aggression data exists.
		/// Sets controller's target and look target.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="targets">List of potential targets.</param>
		public virtual void PickTarget(AIController controller, List<ICharacter> targets)
		{
			ICharacter target = null;

			// Use aggression-based picking if the table has data.
			if (controller.Aggression != null && controller.Aggression.HasAggression)
			{
				target = controller.Aggression.PickTarget(targets, controller.NpcRNG);
			}

			// Fallback: pick the first alive candidate.
			if (target == null)
			{
				for (int i = 0; i < targets.Count; i++)
				{
					ICharacter potentialTarget = targets[i];
					if (potentialTarget != null && potentialTarget.GameObject.activeSelf &&
						potentialTarget.TryGet(out ICharacterDamageController targetDamageController) &&
						targetDamageController.IsAlive)
					{
						target = potentialTarget;
						break;
					}
				}
			}

			if (target != null)
			{
				controller.Target = target.Transform;
				controller.LookTarget = target.Transform;
			}
			else
			{
				// No valid target found, transition out of attacking state
				controller.TransitionToRandomMovementState();
			}
		}

		/// <summary>
		/// Periodically checks the aggression table to see if a higher-threat target should
		/// replace the current one. Uses <see cref="TargetReevaluationRate"/> for timing and
		/// <see cref="AggressionSwitchThreshold"/> to prevent constant flip-flopping.
		/// The reevaluation timer is stored on the controller (not on this ScriptableObject)
		/// to avoid the shared-instance mutable state problem.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Time since last update.</param>
		protected virtual void ReevaluateTarget(AIController controller, float deltaTime)
		{
			if (TargetReevaluationRate <= 0f || controller.Aggression == null || !controller.Aggression.HasAggression)
				return;

			// Use the controller's per-NPC reevaluation timer.
			controller.TargetReevaluationTimer -= deltaTime;
			if (controller.TargetReevaluationTimer > 0f)
				return;

			controller.TargetReevaluationTimer = TargetReevaluationRate;

			if (controller.Target == null)
				return;

			ICharacter currentTarget = controller.Target.GetComponent<ICharacter>();
			if (currentTarget == null)
				return;

			// Check each tracked aggressor to see if anyone exceeds the threshold.
			long currentID = currentTarget.ID;
			foreach (var kvp in controller.Aggression.Table)
			{
				if (kvp.Key == currentID)
					continue;

				if (controller.Aggression.ShouldSwitchTarget(currentID, kvp.Key, AggressionSwitchThreshold))
				{
					// Find the character in the scene. We need a Transform reference.
					// Sweep hits from the last enemy sweep may contain them.
					for (int i = 0; i < controller.SweepHits.Length; i++)
					{
						Collider col = controller.SweepHits[i];
						if (col == null) continue;

						ICharacter candidate = col.GetComponent<ICharacter>();
						if (candidate != null && candidate.ID == kvp.Key &&
							candidate.TryGet(out ICharacterDamageController dmg) && dmg.IsAlive)
						{
							controller.Target = candidate.Transform;
							controller.LookTarget = candidate.Transform;
							return;
						}
					}
				}
			}
		}

		/// <summary>
		/// Attempts to attack the current target. Uses ability range rather than just agent radius.
		/// Picks the best available ability and either activates it or closes distance to get in range.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="targetCharacter">The target character being attacked.</param>
		protected virtual void TryAttack(AIController controller, ICharacter targetCharacter)
		{
			if (!controller.Character.TryGet(out IAbilityController abilityController))
			{
				// No ability controller — nothing we can do.
				controller.TransitionToIdleState();
				return;
			}

			float sqrDistance = controller.GetSqrDistanceToTarget();
			float distance = Mathf.Sqrt(sqrDistance);

			// If the ability controller is currently activating, stop movement and wait.
			if (abilityController.IsActivating || abilityController.AbilityQueued)
			{
				// Stop the agent while casting/channeling to avoid cancellation.
				controller.Agent.isStopped = true;

				// Auto-release charged abilities once the charge time is complete.
				if (abilityController.IsActivating && abilityController.RemainingActivationTime <= 0f)
				{
					abilityController.Release();
				}
				return;
			}

			// Pick an ability to use.
			Ability chosenAbility = controller.PickBestAbility(PreferredDistance > 0f ? PreferredDistance : float.MaxValue);
			if (chosenAbility == null)
			{
				// All abilities on cooldown or no resources — stay in range and wait.
				ManagePositioning(controller, distance);
				return;
			}

			float abilityRange = chosenAbility.Range;

			if (distance <= abilityRange)
			{
				// In range — perform the attack.
				PerformAttack(controller, abilityController, chosenAbility, targetCharacter, distance);
			}
			else
			{
				// Out of range — close distance.
				MoveTowardTarget(controller, abilityRange);
			}
		}

		/// <summary>
		/// Performs the attack by activating the chosen ability via the ability controller.
		/// Override in subclasses for specialized behavior (e.g., stopping before cast, facing target).
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

			// Pass isHeld=true for channeled/charged abilities so the activation pipeline
			// keeps the held state active. Regular abilities get isHeld=false.
			bool held = abilityController.RequiresHeld(ability.ID);
			abilityController.Activate(ability.ID, held);
		}

		/// <summary>
		/// Manages NPC positioning during combat when not actively attacking.
		/// Override in subclasses for different movement styles (kiting, orbiting, etc.).
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="distance">Current distance to the target.</param>
		protected virtual void ManagePositioning(AIController controller, float distance)
		{
			if (controller.Target == null) return;

			// Never reposition while casting or channeling.
			if (controller.Character.TryGet(out IAbilityController ac) &&
				(ac.IsActivating || ac.AbilityQueued))
			{
				controller.Agent.isStopped = true;
				return;
			}

			// If too close and we have a minimum comfort distance, back away.
			if (MinComfortDistance > 0f && distance < MinComfortDistance)
			{
				RetreatFromTarget(controller, MinComfortDistance);
				return;
			}

			// If too far from preferred distance, close in.
			float effectiveRange = PreferredDistance > 0f ? PreferredDistance : (controller.Agent.radius * 2.0f).Min(1.0f);
			if (distance > effectiveRange * 1.1f)
			{
				MoveTowardTarget(controller, effectiveRange);
			}
			else
			{
				// At comfortable distance — stop and face target.
				controller.Agent.isStopped = true;
			}
		}

		/// <summary>
		/// Moves the NPC toward its target, stopping at the specified range.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="stopRange">Distance from the target at which to stop.</param>
		protected void MoveTowardTarget(AIController controller, float stopRange)
		{
			if (controller.Target == null ||
				controller.Agent.pathStatus == NavMeshPathStatus.PathInvalid)
			{
				controller.TransitionToIdleState();
				return;
			}

			controller.Agent.isStopped = false;

			if (!controller.Agent.pathPending)
			{
				float sphereRadius = stopRange * 0.9f;

				// Find nearest position on a sphere around the target to stop at range.
				Vector3 nearestPosition = Vector3Extensions.GetNearestPositionOnSphere(
					controller.Character.Transform.position,
					controller.Target.position,
					sphereRadius);

				NavMeshHit hit;
				if (NavMesh.SamplePosition(nearestPosition, out hit, 5.0f, NavMesh.AllAreas))
				{
					controller.Agent.SetDestination(hit.position);
				}
			}
		}

		/// <summary>
		/// Moves the NPC away from its target to the specified safe distance.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="safeDistance">The distance to retreat to.</param>
		protected void RetreatFromTarget(AIController controller, float safeDistance)
		{
			if (controller.Target == null) return;

			controller.Agent.isStopped = false;

			Vector3 retreatDirection = (controller.Character.Transform.position - controller.Target.position).normalized;
			Vector3 retreatPosition = controller.Character.Transform.position + retreatDirection * safeDistance;

			NavMeshHit hit;
			if (NavMesh.SamplePosition(retreatPosition, out hit, safeDistance, NavMesh.AllAreas))
			{
				controller.Agent.SetDestination(hit.position);
			}
		}
	}
}