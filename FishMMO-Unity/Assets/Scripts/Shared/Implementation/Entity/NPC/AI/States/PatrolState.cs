using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI state for patrolling behavior. Handles waypoint selection and transitions for NPCs.
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Patrol State", menuName = "FishMMO/Character/NPC/AI/Patrol State", order = 0)]
	public class PatrolState : BaseAIState
	{
		/// <summary>
		/// Called when entering the Patrol state. Picks the nearest waypoint to start patrolling.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Enter(AIController controller)
		{
			controller.PickNearestWaypoint();
		}

		/// <summary>
		/// Called when exiting the Patrol state. Can be used for cleanup.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Exit(AIController controller)
		{
			// Cleanup if needed
		}

		/// <summary>
		/// Called every frame to update the Patrol state. Handles randomization and waypoint transitions.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Frame time.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (controller.RandomizeState)
			{
				controller.TransitionToRandomMovementState();
				return;
			}

			// Try to transition to the next waypoint if close enough
			if (!controller.Agent.pathPending &&
				controller.Agent.remainingDistance < 1.0f)
			{
				controller.TransitionToNextWaypoint();
			}
		}
	}
}