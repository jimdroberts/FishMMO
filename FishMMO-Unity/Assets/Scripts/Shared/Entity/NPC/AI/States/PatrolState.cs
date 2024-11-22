using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New AI Patrol State", menuName = "Character/NPC/AI/Patrol State", order = 0)]
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

			// Try to transition to the next waypoint
			if (!controller.Agent.pathPending &&
				controller.Agent.remainingDistance < 1.0f)
			{
				controller.TransitionToNextWaypoint();
			}
		}
	}
}