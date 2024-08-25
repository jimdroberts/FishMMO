using System.Collections.Generic;
using UnityEngine.AI;

namespace FishMMO.Shared
{
	public class WanderState : BaseAIState
	{
		public float WanderRadius;
		public float RandomDestinationRate;

		public override void Enter(AIController controller)
		{
			// Set wander parameters
			controller.SetRandomHomeDestination(WanderRadius);
		}

		public override void Exit(AIController controller)
		{
			// Cleanup if needed
		}

		public override void UpdateState(AIController controller)
		{
			// Check for nearby enemies
			if (SweepForEnemies(controller, out List<ICharacter> enemies))
			{
				controller.ChangeState(controller.AttackingState, enemies);
				return;
			}

			// Otherwise check if we should pick a new wander destination
			if (controller.Agent.pathStatus == NavMeshPathStatus.PathInvalid)
			{
				// Find a new destination if the current path is invalid
				controller.SetRandomHomeDestination(WanderRadius);
			}
			else if (!controller.Agent.pathPending &&
				controller.Agent.remainingDistance < 1.0f)
			{
				controller.SetRandomHomeDestination(WanderRadius);
			}
		}
	}
}