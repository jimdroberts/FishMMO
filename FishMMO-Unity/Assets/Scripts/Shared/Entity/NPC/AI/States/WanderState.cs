using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New AI Wander State", menuName = "Character/NPC/AI/Wander State", order = 0)]
	public class WanderState : BaseAIState
	{
		public bool AlwaysPickNewDestination;
		public float WanderRadius;
		[Tooltip("If max update rate is greater than the update rate it will return a random range between the two.")]
		public float MaxUpdateRate;

        public override float GetUpdateRate()
        {
			float updateRate = base.GetUpdateRate();
			if (MaxUpdateRate > updateRate)
			{
				updateRate = Random.Range(updateRate, MaxUpdateRate);
			}
            return updateRate;
        }

        public override void Enter(AIController controller)
		{
			// Set wander parameters
			controller.SetRandomHomeDestination(WanderRadius);
		}

		public override void Exit(AIController controller)
		{
			// Cleanup if needed
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

			if (controller.RandomizeState)
			{
				controller.TransitionToRandomMovementState();
				return;
			}
			
			// Otherwise check if we should pick a new wander destination
			if (controller.Agent.pathStatus == NavMeshPathStatus.PathInvalid)
			{
				// Find a new destination if the current path is invalid
				controller.SetRandomHomeDestination(WanderRadius);
			}
			else if (AlwaysPickNewDestination ||
					(!controller.Agent.pathPending && controller.Agent.remainingDistance < 1.0f))
			{
				controller.SetRandomHomeDestination(WanderRadius);
			}
		}
	}
}