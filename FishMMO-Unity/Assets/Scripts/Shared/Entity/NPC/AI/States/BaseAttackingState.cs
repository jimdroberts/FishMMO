using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class BaseAttackingState : BaseAIState
	{
		public override void Enter(AIController controller)
		{
			if (controller.Target != null)
			{
				TryAttack(controller);
			}
		}

		public override void Exit(AIController controller)
		{
			controller.Target = null;
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
			controller.Target = targets[0].Transform;
		}

		private void TryAttack(AIController controller)
		{
			if (controller.Target == null)
			{
				return;
			}
			ICharacter character = controller.Target.GetComponent<ICharacter>();
			if (character == null)
			{
				return;
			}

			float distanceToTarget = Vector3.Distance(controller.Character.Transform.position, controller.Target.position);

			if (distanceToTarget <= controller.Agent.radius &&
				HasLineOfSight(controller, character))
			{
				// If we are in range. Perform attack
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
			Debug.Log("Attacking target!");
		}

		/// <summary>
		/// Implement out of attack range logic here.
		/// </summary>
		public virtual void OutOfAttackRange(AIController controller, float distanceToTarget)
		{
			// Allow the agent to run
			controller.Agent.speed = Constants.Character.RunSpeed;
			
			// If the target is out of range, move towards it or transition
			controller.Agent.SetDestination(controller.Target.position);
		}
	}
}