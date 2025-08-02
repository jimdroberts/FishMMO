using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI state for attacking behavior. Handles entering, exiting, updating, and attack logic for NPCs.
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Attacking State", menuName = "FishMMO/Character/NPC/AI/Attacking State", order = 0)]
	public class BaseAttackingState : BaseAIState
	{
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
				if (controller.AttackingState != null &&
					SweepForEnemies(controller, out List<ICharacter> enemies))
				{
					controller.ChangeState(controller.AttackingState, enemies);
					return;
				}
				// Otherwise handle post attack logic
				else
				{
					controller.TransitionToRandomMovementState();
				}
			}
			else
			{
				// Try to attack the current target
				TryAttack(controller);
			}
		}

		/// <summary>
		/// Picks a valid target from the provided list. Sets controller's target and look target.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="targets">List of potential targets.</param>
		public virtual void PickTarget(AIController controller, List<ICharacter> targets)
		{
			ICharacter target = null;
			foreach (var potentialTarget in targets)
			{
				if (potentialTarget != null && potentialTarget.GameObject.activeSelf &&
					potentialTarget.TryGet(out ICharacterDamageController targetDamageController) &&
					targetDamageController.IsAlive)
				{
					target = potentialTarget;
					break;
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
		/// Attempts to attack the current target. Handles range checking and attack logic.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		private void TryAttack(AIController controller)
		{
			ICharacter character = controller.Target.GetComponent<ICharacter>();
			if (character == null)
			{
				controller.TransitionToIdleState();
				return;
			}

			// Calculate attack radius and distance to target
			float agentAttackRadius = (controller.Agent.radius * 2.0f).Min(1.0f);
			float distanceToTarget = (controller.Target.position - controller.Character.Transform.position).sqrMagnitude;

			if (distanceToTarget > agentAttackRadius * agentAttackRadius)
			{
				// If we are out of range handle follow up
				OutOfAttackRange(controller, distanceToTarget, agentAttackRadius);
			}
			else
			{
				// Attack if we are in range and we have line of sight
				PerformAttack(controller, character, distanceToTarget, agentAttackRadius);
			}
		}

		/// <summary>
		/// Performs the attack on the target character. Override to implement custom attack logic.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="targetCharacter">The target character to attack.</param>
		/// <param name="distanceToTarget">Distance to the target.</param>
		/// <param name="agentRadius">Attack radius of the agent.</param>
		public virtual void PerformAttack(AIController controller, ICharacter targetCharacter, float distanceToTarget, float agentRadius)
		{
			/*if (!HasLineOfSight(controller, targetCharacter))
			{
				Log.Debug("Line of sight lost!");
				return;
			}*/
			// if (distanceToTarget is small)
			// controller.TransitionToCombatState();
			Log.Debug("BaseAttackingState", "Attacking target!");
		}

		/// <summary>
		/// Handles logic when the target is out of attack range. Moves agent closer if possible.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="distanceToTarget">Distance to the target.</param>
		/// <param name="agentRadius">Attack radius of the agent.</param>
		public virtual void OutOfAttackRange(AIController controller, float distanceToTarget, float agentRadius)
		{
			if (controller.Target == null ||
				controller.Agent.pathStatus == NavMeshPathStatus.PathInvalid)
			{
				controller.TransitionToIdleState();
				return;
			}

			if (!controller.Agent.pathPending)
			{
				float sphereRadius = agentRadius * 0.95f;

				// Find nearest position on sphere around target to move agent closer
				Vector3 nearestPosition = Vector3Extensions.GetNearestPositionOnSphere(controller.Character.Transform.position, controller.Target.position, sphereRadius);

				NavMeshHit hit;
				if (NavMesh.SamplePosition(nearestPosition, out hit, 5.0f, NavMesh.AllAreas))
				{
					controller.Agent.SetDestination(hit.position);
				}
			}
		}
	}
}