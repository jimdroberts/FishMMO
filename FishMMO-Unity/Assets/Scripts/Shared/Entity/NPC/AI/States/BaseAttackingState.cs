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

		public override void UpdateState(AIController controller)
		{
			if (controller.Target == null)
			{
				// If the target is lost... Check for nearby enemies
				if (SweepForEnemies(controller, out List<ICharacter> enemies))
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
		/// <param name="targets"></param>
		public virtual void PickTarget(AIController controller, List<ICharacter> targets)
		{
			controller.Target = targets[0].Transform;
		}

		private void TryAttack(AIController controller)
		{
			float distanceToTarget = Vector3.Distance(controller.Transform.position, controller.Target.position);

			if (distanceToTarget <= controller.InteractionDistance)
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
			// If the target is out of range, move towards it or transition
			controller.Agent.SetDestination(controller.Target.position);
		}
	}
}