using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI State for retreating from a target. Causes the NPC to move away from its target until a safe distance is reached.
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Retreat State", menuName = "FishMMO/Character/NPC/AI/Retreat State", order = 0)]
	public class RetreatState : BaseAIState
	{
		/// <summary>
		/// The distance the NPC will attempt to move away from its target when retreating.
		/// </summary>
		public float RetreatDistance = 10.0f;

		/// <summary>
		/// The minimum distance from the target that is considered safe. Once reached, the NPC will stop retreating.
		/// </summary>
		public float SafeDistance = 20.0f;

		/// <summary>
		/// Called when the state is entered. Sets the retreat destination away from the target.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public override void Enter(AIController controller)
		{
			if (controller.Target == null)
			{
				// If there is no target, transition to idle or another default state.
				controller.TransitionToIdleState();
				return;
			}

			// Calculate the direction away from the target.
			Vector3 retreatDirection = (controller.Character.Transform.position - controller.Target.position).normalized;
			// Calculate the retreat position by moving RetreatDistance units away from the target.
			Vector3 retreatPosition = controller.Character.Transform.position + retreatDirection * RetreatDistance;

			// Use NavMesh to find a valid position near the calculated retreat position.
			NavMeshHit hit;
			if (NavMesh.SamplePosition(retreatPosition, out hit, RetreatDistance, NavMesh.AllAreas))
			{
				// Set the agent's destination to the valid retreat position.
				controller.Agent.SetDestination(hit.position);
			}
		}

		/// <summary>
		/// Called when the state is exited. No special logic needed for retreat state.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public override void Exit(AIController controller)
		{
			// No exit logic required for retreat state.
		}

		/// <summary>
		/// Called every frame while in this state. Handles retreat logic and transitions when safe distance is reached.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		/// <param name="deltaTime">Time since last update.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (controller.Target == null)
			{
				// If the target is lost, transition to idle state.
				controller.TransitionToIdleState();
				return;
			}

			// Check if the agent has reached its current retreat destination.
			if (!controller.Agent.pathPending && controller.Agent.remainingDistance < 1.0f)
			{
				// Measure the distance to the target.
				float distanceToTarget = Vector3.Distance(controller.Character.Transform.position, controller.Target.position);
				if (distanceToTarget > SafeDistance)
				{
					// If safe distance is maintained, transition to idle or another appropriate state.
					controller.TransitionToIdleState();
				}
				else
				{
					// If not yet at safe distance, calculate a new retreat position and continue retreating.
					Vector3 retreatDirection = (controller.Character.Transform.position - controller.Target.position).normalized;
					Vector3 retreatPosition = controller.Character.Transform.position + retreatDirection * RetreatDistance;

					NavMeshHit hit;
					if (NavMesh.SamplePosition(retreatPosition, out hit, RetreatDistance, NavMesh.AllAreas))
					{
						controller.Agent.SetDestination(hit.position);
					}
				}
			}
		}
	}
}