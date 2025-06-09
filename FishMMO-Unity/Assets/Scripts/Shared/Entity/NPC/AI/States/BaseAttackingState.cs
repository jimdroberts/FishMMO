using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New AI Attacking State", menuName = "FishMMO/Character/NPC/AI/Attacking State", order = 0)]
	public class BaseAttackingState : BaseAIState
	{
		public override void Enter(AIController controller)
		{
			// Allow the agent to run
			controller.Agent.speed = Constants.Character.RunSpeed;
		}

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

		public override void UpdateState(AIController controller, float deltaTime)
		{
			// Cache frequently used components
			if (!controller.Character.TryGet(out ICharacterDamageController damageController) ||
				!damageController.IsAlive)
			{
				// If AI is dead, stop attacking
				controller.TransitionToIdleState(); // Or a specific 'Dead' state
				return;
			}

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
				TryAttack(controller);
			}
		}

		/// <summary>
		/// Implement your target picking logic here.
		/// </summary>
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

		private void TryAttack(AIController controller)
		{
			ICharacter character = controller.Target.GetComponent<ICharacter>();
			if (character == null)
			{
				controller.TransitionToIdleState();
				return;
			}

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
		/// Implement your attack logic here.
		/// </summary>
		public virtual void PerformAttack(AIController controller, ICharacter targetCharacter, float distanceToTarget, float agentRadius)
		{
			/*if (!HasLineOfSight(controller, targetCharacter))
			{
				Debug.Log("Line of sight lost!");
				return;
			}*/
			// if (distanceToTarget is small)
			// controller.TransitionToCombatState();
			Debug.Log("Attacking target!");
		}

		/// <summary>
		/// Implement out of attack range logic here.
		/// </summary>
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