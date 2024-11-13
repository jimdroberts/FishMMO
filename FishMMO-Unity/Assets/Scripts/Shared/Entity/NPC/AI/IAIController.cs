using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface IAIController : ICharacterBehaviour
	{
		NavMeshAgent Agent { get; }
		BaseAIState CurrentState { get; }
		Vector3 Home { get; }

		void Initialize(Vector3 home, Vector3[] waypoints = null);
		void ChangeState(BaseAIState newState, List<ICharacter> targets = null);
		void TransitionToIdleState();
		void TransitionToRandomMovementState();
		void SetRandomHomeDestination(float radius);
		void SetRandomDestination(float radius);
		void TransitionToNextWaypoint();
		void PickNearestWaypoint();
		void Stop();
		void Resume();
	}
}