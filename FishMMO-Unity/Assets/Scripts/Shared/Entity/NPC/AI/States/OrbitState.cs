using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared
{
	public class OrbitState : BaseAIState
	{
		public float OrbitRadius = 10.0f; // Radius of the orbit around the target
		public float OrbitSpeed = 2.0f; // Speed at which the AI orbits around the target
		public float RotationSpeed = 5.0f; // Speed at which the AI rotates to face the target

		private float currentAngle;

		public override void Enter(AIController controller)
		{
			if (controller.Target == null)
			{
				Debug.LogWarning("No target set for OrbitState.");
				controller.TransitionToDefaultState(); // Or another default state
				return;
			}

			// Initialize the current angle
			currentAngle = 0.0f;
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

			// Calculate the new position around the target
			currentAngle += OrbitSpeed * Time.deltaTime;
			float x = Mathf.Cos(currentAngle) * OrbitRadius;
			float z = Mathf.Sin(currentAngle) * OrbitRadius;
			Vector3 offset = new Vector3(x, 0, z);
			Vector3 targetPosition = controller.Target.position + offset;

			// Move the AI to the calculated position
			NavMeshHit hit;
			if (NavMesh.SamplePosition(targetPosition, out hit, OrbitRadius, NavMesh.AllAreas))
			{
				controller.Agent.SetDestination(hit.position);
			}

			// Rotate the AI to face the target
			Vector3 directionToTarget = (controller.Target.position - controller.Transform.position).normalized;
			Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
			controller.Transform.rotation = Quaternion.Slerp(controller.Transform.rotation, targetRotation, RotationSpeed * Time.deltaTime);
		}
	}
}