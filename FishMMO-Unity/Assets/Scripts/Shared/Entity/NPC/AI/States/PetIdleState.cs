using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI state for pet idle behavior. Handles following owner, path correction, and idle logic for pets.
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Pet Idle State", menuName = "FishMMO/Character/NPC/AI/Pet Idle State", order = 0)]
	public class PetIdleState : BaseAIState
	{
		/// <summary>
		/// Called when entering the Pet Idle state. Sets agent speed to run.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Enter(AIController controller)
		{
			controller.Agent.speed = Constants.Character.RunSpeed;
		}

		/// <summary>
		/// Called when exiting the Pet Idle state. No cleanup needed by default.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Exit(AIController controller)
		{
		}

		/// <summary>
		/// Called every frame to update the Pet Idle state. Handles owner following and path correction.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Frame time.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			// If no target, do nothing
			if (controller.Target == null)
			{
				return;
			}

			// Get the Pet component
			Pet pet = controller.gameObject.GetComponent<Pet>();
			if (pet == null)
			{
				return;
			}

			// Try to get closer to owner
			if (pet.PetOwner == null)
			{
				return;
			}

			float distanceToOwner = controller.Agent.radius * 1.5f;

			// Check for valid path or return to owner
			if (controller.Agent.pathStatus == NavMeshPathStatus.PathInvalid)
			{
				// Warp to owner if the path is invalid
				controller.Agent.Warp(pet.PetOwner.Transform.position);
			}
			// If too far from owner, move closer
			else if ((pet.PetOwner.Transform.position - controller.Character.Transform.position).sqrMagnitude > distanceToOwner * distanceToOwner)
			{
				if (!controller.Agent.pathPending ||
					 controller.Agent.remainingDistance > controller.Agent.radius)
				{
					float sphereRadius = distanceToOwner * 0.95f;

					// Find nearest position on sphere around owner
					Vector3 nearestPosition = Vector3Extensions.GetNearestPositionOnSphere(controller.Character.Transform.position, pet.PetOwner.Transform.position, sphereRadius);

					NavMeshHit hit;
					if (NavMesh.SamplePosition(nearestPosition, out hit, 5.0f, NavMesh.AllAreas))
					{
						controller.Agent.SetDestination(hit.position);
					}
				}
			}
			else
			{
				// Pet is close enough to owner, remain idle
			}
		}
	}
}