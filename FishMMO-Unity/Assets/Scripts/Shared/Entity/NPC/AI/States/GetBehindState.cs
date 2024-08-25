using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared
{
	public class GetBehindState : BaseAIState
	{
		public float BehindDistance = 5.0f; // Distance behind the target to move
		public float RotationSpeed = 5.0f; // Speed at which the AI rotates to face the target

		public override void Enter(AIController controller)
		{
			if (controller.Target == null)
			{
				Debug.LogWarning("No target set for GetBehindState.");
				controller.TransitionToDefaultState(); // Or another default state
				return;
			}

			// Calculate the position behind the target
			Vector3 behindPosition = CalculateBehindPosition(controller.Transform.position, controller.Target.position, controller.Target.forward);

			// Set the destination
			NavMeshHit hit;
			if (NavMesh.SamplePosition(behindPosition, out hit, BehindDistance, NavMesh.AllAreas))
			{
				controller.Agent.SetDestination(hit.position);
			}
		}

		public override void Exit(AIController controller)
		{
			// Optional: Stop movement or reset parameters if needed
		}

		public override void UpdateState(AIController controller)
		{
			if (controller.Target == null)
			{
				controller.TransitionToDefaultState(); // Transition if target is lost
				return;
			}

			// Rotate to face the target
			Vector3 directionToTarget = (controller.Target.position - controller.Transform.position).normalized;
			Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
			controller.Transform.rotation = Quaternion.Slerp(controller.Transform.rotation, targetRotation, RotationSpeed * Time.deltaTime);

			// Check if we reached the destination
			if (!controller.Agent.pathPending && controller.Agent.remainingDistance < 1.0f)
			{
				// Optionally, transition to another state or behavior
				controller.TransitionToDefaultState(); // Example transition
			}
		}

		private Vector3 CalculateBehindPosition(Vector3 aiPosition, Vector3 targetPosition, Vector3 targetForward)
		{
			// Calculate the direction to move behind the target
			Vector3 directionToTarget = (aiPosition - targetPosition).normalized;
			Vector3 behindDirection = -targetForward; // Move in the direction opposite to where the target is facing
			Vector3 behindPosition = targetPosition + behindDirection * BehindDistance;

			return behindPosition;
		}
	}
}