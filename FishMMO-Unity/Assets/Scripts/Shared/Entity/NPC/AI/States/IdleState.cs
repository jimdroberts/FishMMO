using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class IdleState : BaseAIState
	{
		public override void Enter(AIController controller)
		{
		}

		public override void Exit(AIController controller)
		{
			controller.LookTarget = null;
		}

		public override void UpdateState(AIController controller, float deltaTime)
		{
			// Check for nearby enemies
			if (controller.AttackingState != null &&
				SweepForEnemies(controller, out List<ICharacter> enemies))
			{
				controller.ChangeState(controller.AttackingState, enemies);
				return;
			}

			if (controller.LookTarget == null ||
				Vector3.Distance(controller.transform.position, controller.LookTarget.position) < DetectionRadius * 0.5f)
			{
				controller.TransitionToRandomMovementState();
			}
		}
	}
}