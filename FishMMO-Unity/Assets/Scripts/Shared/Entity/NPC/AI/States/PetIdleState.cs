using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New AI Pet Idle State", menuName = "FishMMO/Character/NPC/AI/Pet Idle State", order = 0)]
	public class PetIdleState : BaseAIState
	{
		public override void Enter(AIController controller)
		{
			controller.Agent.speed = Constants.Character.RunSpeed;
		}

		public override void Exit(AIController controller)
		{
		}

		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (controller.Target == null)
			{
				return;
			}
			
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
			else if ((pet.PetOwner.Transform.position - controller.Character.Transform.position).sqrMagnitude > distanceToOwner * distanceToOwner)
			{
				if (!controller.Agent.pathPending ||
					 controller.Agent.remainingDistance > controller.Agent.radius)
				{
					float sphereRadius = distanceToOwner * 0.95f;

					Vector3 nearestPosition = Vector3Extensions.GetNearestPositionOnSphere(controller.Character.Transform.position, pet.PetOwner.Transform.position, sphereRadius);

					//Log.Debug($"path | distanceToOwner: {distanceToOwner} | sphereRadius: {sphereRadius} | nearestPosition: {nearestPosition}");

					NavMeshHit hit;
					if (NavMesh.SamplePosition(nearestPosition, out hit, 5.0f, NavMesh.AllAreas))
					{
						//Log.Debug($"sample | hitPosition: {hit.position}");
						controller.Agent.SetDestination(hit.position);
					}
				}
			}
			else
			{
				//Log.Debug("idle");
			}
		}
	}
}