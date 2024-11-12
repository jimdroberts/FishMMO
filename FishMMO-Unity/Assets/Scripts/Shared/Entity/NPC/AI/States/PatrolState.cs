using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class PatrolState : BaseAIState
	{
		public override void Enter(AIController controller)
		{
			controller.PickNearestWaypoint();
		}

		public override void Exit(AIController controller)
		{
			// Cleanup if needed
		}

		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (controller.RandomizeState)
			{
				controller.TransitionToRandomMovementState();
				return;
			}
			
			// Check for nearby enemies
			if (controller.AttackingState != null &&
				SweepForEnemies(controller, out List<ICharacter> enemies))
			{
				controller.ChangeState(controller.AttackingState, enemies);
				return;
			}
			// Try to transition to the next waypoint
			else if (!controller.Agent.pathPending &&
				controller.Agent.remainingDistance < 1.0f)
			{
				controller.TransitionToNextWaypoint();
			}
		}
	}
}