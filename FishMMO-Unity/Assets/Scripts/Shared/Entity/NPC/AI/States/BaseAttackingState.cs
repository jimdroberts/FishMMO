using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared
{
	public abstract class BaseAttackingState : BaseAIState
	{
		public override void Enter(AIController controller)
		{
			// Allow the agent to run
			controller.Agent.speed = Constants.Character.RunSpeed;

			if (controller.Target != null)
			{
				TryAttack(controller);
			}
		}

		public override void Exit(AIController controller)
		{
			// Return to walk speed
			controller.Agent.speed = Constants.Character.WalkSpeed;

			controller.Target = null;
			controller.SetRandomHomeDestination();
		}

		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (controller.Target == null)
			{
				// If the target is lost... Check for nearby enemies
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
			ICharacter target = targets[0];

			controller.Target = target.Transform;
			controller.LookTarget = target.Transform;
		}

		private void TryAttack(AIController controller)
		{
			if (controller.Target == null)
			{
				controller.TransitionToIdleState();
				return;
			}
			ICharacter character = controller.Target.GetComponent<ICharacter>();
			if (character == null)
			{
				controller.TransitionToIdleState();
				return;
			}

			float distanceToTarget = (controller.Target.position - controller.Character.Transform.position).sqrMagnitude;

			if (distanceToTarget <= controller.Agent.radius * controller.Agent.radius &&
				HasLineOfSight(controller, character))
			{
				// Attack if we are in range and we have line of sight
				PerformAttack(distanceToTarget);
			}
			else
			{
				// If we are out of range handle follow up
				OutOfAttackRange(controller, distanceToTarget);
			}
		}

		/// <summary>
		/// Implement your attack logic here.
		/// </summary>
		public virtual void PerformAttack(float distanceToTarget)
		{
			// if (distanceToTarget is small)
			// controller.TransitionToCombatState();
			Debug.Log("Attacking target!");
		}

		/// <summary>
		/// Implement out of attack range logic here.
		/// </summary>
		public virtual void OutOfAttackRange(AIController controller, float distanceToTarget)
		{
			if (controller.Target == null)
			{
				controller.TransitionToIdleState();
				return;
			}

			if (!controller.Agent.pathPending &&
				 controller.Agent.remainingDistance > controller.Agent.radius)
			{
				Vector3 nearestPosition = Vector3Extensions.GetNearestPositionOnSphere(controller.Character.Transform.position, controller.Target.position, controller.Agent.radius);

				NavMeshHit hit;
				if (NavMesh.SamplePosition(nearestPosition, out hit, 5.0f, NavMesh.AllAreas))
				{
					controller.Agent.SetDestination(hit.position);
				}
			}
		}
	}
}