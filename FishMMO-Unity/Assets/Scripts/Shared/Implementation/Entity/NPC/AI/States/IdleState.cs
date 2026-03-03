using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI state for idle behavior. Handles update rate, entering, exiting, and transition logic for NPCs.
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Idle State", menuName = "FishMMO/Character/NPC/AI/Idle State", order = 0)]
	public class IdleState : BaseAIState
	{
		/// <summary>
		/// If MaxUpdateRate is greater than the base update rate, a random value between the two is used.
		/// </summary>
		[Tooltip("If max update rate is greater than the update rate it will return a random range between the two.")]
		public float MaxUpdateRate;

		/// <summary>
		/// Returns the update rate for the idle state, possibly randomized between base and max.
		/// </summary>
		/// <returns>Update rate in seconds.</returns>
		public override float GetUpdateRate()
		{
			float updateRate = base.GetUpdateRate();
			if (MaxUpdateRate > updateRate)
			{
				updateRate = Random.Range(updateRate, MaxUpdateRate);
			}
			return updateRate;
		}

		/// <summary>
		/// Called when entering the idle state. Stops the AI's movement.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Enter(AIController controller)
		{
			controller.Stop();
		}

		/// <summary>
		/// Called when exiting the idle state. Clears look target and resumes movement.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Exit(AIController controller)
		{
			controller.LookTarget = null;
			controller.Resume();
		}

		/// <summary>
		/// Called every frame to update the idle state. Transitions to random movement if look target is lost or too far.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Frame time.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (controller.LookTarget == null ||
				Vector3.Distance(controller.transform.position, controller.LookTarget.position) > DetectionRadius * 0.5f)
			{
				controller.TransitionToRandomMovementState();
			}
		}
	}
}