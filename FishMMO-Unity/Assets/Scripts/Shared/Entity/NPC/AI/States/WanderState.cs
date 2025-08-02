using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI State for wandering behavior. NPCs will move randomly within a specified radius, occasionally transitioning to idle or picking new destinations.
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Wander State", menuName = "FishMMO/Character/NPC/AI/Wander State", order = 0)]
	public class WanderState : BaseAIState
	{
		/// <summary>
		/// If true, the NPC will always pick a new destination when possible, rather than waiting to reach the current one.
		/// </summary>
		public bool AlwaysPickNewDestination;

		/// <summary>
		/// The radius within which the NPC will wander from its home position.
		/// </summary>
		public float WanderRadius;

		/// <summary>
		/// If greater than the base update rate, the update rate will be randomized between the base and this value.
		/// </summary>
		[Tooltip("If max update rate is greater than the update rate it will return a random range between the two.")]
		public float MaxUpdateRate;

		/// <summary>
		/// Returns the update rate for this state, possibly randomized if MaxUpdateRate is set higher than the base rate.
		/// </summary>
		/// <returns>Update rate in seconds.</returns>
		public override float GetUpdateRate()
		{
			float updateRate = base.GetUpdateRate();
			if (MaxUpdateRate > updateRate)
			{
				// Randomize update rate between base and max value for more natural wandering.
				updateRate = Random.Range(updateRate, MaxUpdateRate);
			}
			return updateRate;
		}

		/// <summary>
		/// Called when the state is entered. Can be used to initialize wander parameters.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public override void Enter(AIController controller)
		{
			// Set wander parameters if needed (currently no specific logic).
		}

		/// <summary>
		/// Called when the state is exited. Can be used for cleanup.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public override void Exit(AIController controller)
		{
			// Cleanup if needed (currently no specific logic).
		}

		/// <summary>
		/// Called every frame while in this state. Handles wandering logic, destination picking, and transitions.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		/// <param name="deltaTime">Time since last update.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			// If the controller requests randomization, transition to random movement state.
			if (controller.RandomizeState)
			{
				controller.TransitionToRandomMovementState();
				return;
			}

			// If the agent's path is invalid, pick a new random destination within the wander radius.
			if (controller.Agent.pathStatus == NavMeshPathStatus.PathInvalid)
			{
				controller.SetRandomHomeDestination(WanderRadius);
			}
			// Otherwise, pick a new destination if AlwaysPickNewDestination is true,
			// or if the agent has reached its current destination.
			else if (AlwaysPickNewDestination ||
					 (!controller.Agent.pathPending && controller.Agent.remainingDistance < 1.0f))
			{
				// Randomly decide whether to transition to idle or continue wandering.
				float randomChance = Random.Range(0f, 1f); // Random value between 0 and 1
				float transitionThreshold = 0.5f; // Probability threshold (e.g., 50%)

				if (randomChance <= transitionThreshold)
				{
					// Transition to idle state with a certain probability.
					controller.TransitionToIdleState();
				}
				else
				{
					// Otherwise, set a new random home destination to continue wandering.
					controller.SetRandomHomeDestination(WanderRadius);
				}
			}
		}
	}
}