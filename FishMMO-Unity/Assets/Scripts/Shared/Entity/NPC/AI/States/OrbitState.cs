using UnityEngine;
using UnityEngine.AI;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI state for orbiting around a target. Handles movement, rotation, and state transitions for NPCs.
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Orbit State", menuName = "FishMMO/Character/NPC/AI/Orbit State", order = 0)]
	public class OrbitState : BaseAIState
	{
		/// <summary>
		/// Radius of the orbit around the target.
		/// </summary>
		public float OrbitRadius = 10.0f;
		/// <summary>
		/// Speed at which the AI orbits around the target.
		/// </summary>
		public float OrbitSpeed = 2.0f;
		/// <summary>
		/// Speed at which the AI rotates to face the target.
		/// </summary>
		public float RotationSpeed = 5.0f;

		/// <summary>
		/// Current angle in the orbit (radians).
		/// </summary>
		private float currentAngle;

		/// <summary>
		/// Called when entering the Orbit state. Initializes orbit angle and checks for target.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Enter(AIController controller)
		{
			if (controller.Target == null)
			{
				Log.Warning("OrbitState", "No target set for OrbitState.");
				controller.TransitionToIdleState(); // Or another default state
				return;
			}

			// Initialize the current angle
			currentAngle = 0.0f;
		}

		/// <summary>
		/// Called when exiting the Orbit state. Can be used to stop movement or reset parameters.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Exit(AIController controller)
		{
			// Optional: Stop movement or reset parameters if needed
		}

		/// <summary>
		/// Called every frame to update the Orbit state. Handles orbit movement, rotation, and state transitions.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Frame time.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			// Transition to random movement if requested
			if (controller.RandomizeState)
			{
				controller.TransitionToRandomMovementState();
				return;
			}

			// Transition to idle if target is lost
			if (controller.Target == null)
			{
				controller.TransitionToIdleState(); // Transition if target is lost
				return;
			}

			// Calculate the new position around the target using polar coordinates
			currentAngle += OrbitSpeed * Time.deltaTime;
			float x = Mathf.Cos(currentAngle) * OrbitRadius;
			float z = Mathf.Sin(currentAngle) * OrbitRadius;
			Vector3 offset = new Vector3(x, 0, z);
			Vector3 targetPosition = controller.Target.position + offset;

			// Move the AI to the calculated position using NavMesh sampling
			NavMeshHit hit;
			if (NavMesh.SamplePosition(targetPosition, out hit, OrbitRadius, NavMesh.AllAreas))
			{
				controller.Agent.SetDestination(hit.position);
			}

			// Rotate the AI to face the target smoothly
			Vector3 directionToTarget = (controller.Target.position - controller.Character.Transform.position).normalized;
			Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
			controller.Character.Transform.rotation = Quaternion.Slerp(controller.Character.Transform.rotation, targetRotation, RotationSpeed * Time.deltaTime);
		}
	}
}