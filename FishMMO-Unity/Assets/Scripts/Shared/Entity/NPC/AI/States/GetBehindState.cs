using UnityEngine;
using UnityEngine.AI;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI state for moving behind a target. Handles movement, rotation, and state transitions for NPCs.
	/// </summary>
	[CreateAssetMenu(fileName = "New AI GetBehind State", menuName = "FishMMO/Character/NPC/AI/GetBehind State", order = 0)]
	public class GetBehindState : BaseAIState
	{
		/// <summary>
		/// Distance behind the target to move.
		/// </summary>
		public float BehindDistance = 5.0f;
		/// <summary>
		/// Speed at which the AI rotates to face the target.
		/// </summary>
		public float RotationSpeed = 5.0f;

		/// <summary>
		/// Called when entering the GetBehind state. Calculates and sets destination behind the target.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Enter(AIController controller)
		{
			if (controller.Target == null)
			{
				Log.Warning("GetBehindState", "No target set for GetBehindState.");
				controller.TransitionToIdleState(); // Or another default state
				return;
			}

			// Calculate the position behind the target
			Vector3 behindPosition = CalculateBehindPosition(controller.Character.Transform.position, controller.Target.position, controller.Target.forward);

			// Set the destination using NavMesh sampling
			NavMeshHit hit;
			if (NavMesh.SamplePosition(behindPosition, out hit, BehindDistance, NavMesh.AllAreas))
			{
				controller.Agent.SetDestination(hit.position);
			}
		}

		/// <summary>
		/// Called when exiting the GetBehind state. Can be used to stop movement or reset parameters.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Exit(AIController controller)
		{
			// Optional: Stop movement or reset parameters if needed
		}

		/// <summary>
		/// Called every frame to update the GetBehind state. Handles rotation and destination checks.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Frame time.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (controller.Target == null)
			{
				controller.TransitionToIdleState(); // Transition if target is lost
				return;
			}

			// Rotate to face the target smoothly
			Vector3 directionToTarget = (controller.Target.position - controller.Character.Transform.position).normalized;
			Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
			controller.Character.Transform.rotation = Quaternion.Slerp(controller.Character.Transform.rotation, targetRotation, RotationSpeed * Time.deltaTime);

			// Check if we reached the destination
			if (!controller.Agent.pathPending && controller.Agent.remainingDistance < 1.0f)
			{
				// Optionally, transition to another state or behavior
				controller.TransitionToIdleState(); // Example transition
			}
		}

		/// <summary>
		/// Calculates the position behind the target based on its forward direction and desired distance.
		/// </summary>
		/// <param name="aiPosition">Current AI position.</param>
		/// <param name="targetPosition">Target's position.</param>
		/// <param name="targetForward">Target's forward direction.</param>
		/// <returns>Position behind the target.</returns>
		private Vector3 CalculateBehindPosition(Vector3 aiPosition, Vector3 targetPosition, Vector3 targetForward)
		{
			// Move in the direction opposite to where the target is facing
			Vector3 behindDirection = -targetForward;
			Vector3 behindPosition = targetPosition + behindDirection * BehindDistance;

			return behindPosition;
		}
	}
}