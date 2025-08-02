using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for AI controllers managing NPC navigation, state transitions, and movement logic.
	/// </summary>
	public interface IAIController : ICharacterBehaviour
	{
		/// <summary>
		/// The NavMeshAgent component used for navigation.
		/// </summary>
		NavMeshAgent Agent { get; }

		/// <summary>
		/// The current AI state.
		/// </summary>
		BaseAIState CurrentState { get; }

		/// <summary>
		/// The home position for this AI (used for leash and wandering).
		/// </summary>
		Vector3 Home { get; set; }

		/// <summary>
		/// The current target for the AI (e.g., enemy, destination).
		/// </summary>
		Transform Target { get; set; }

		/// <summary>
		/// Initializes the controller with a home position and optional waypoints.
		/// </summary>
		/// <param name="home">The home position for the AI.</param>
		/// <param name="waypoints">Optional waypoints for patrol.</param>
		void Initialize(Vector3 home, Vector3[] waypoints = null);

		/// <summary>
		/// Changes the AI state, optionally providing targets for attacking states.
		/// </summary>
		/// <param name="newState">The new state to transition to.</param>
		/// <param name="targets">Optional list of targets for attacking states.</param>
		void ChangeState(BaseAIState newState, List<ICharacter> targets = null);

		/// <summary>
		/// Transitions to the idle state.
		/// </summary>
		void TransitionToIdleState();

		/// <summary>
		/// Transitions to a random movement state from the available movement states.
		/// </summary>
		void TransitionToRandomMovementState();

		/// <summary>
		/// Sets a random destination within a radius around the home position.
		/// </summary>
		/// <param name="radius">Radius to randomize destination.</param>
		void SetRandomHomeDestination(float radius);

		/// <summary>
		/// Sets a random destination within a radius around the current position.
		/// </summary>
		/// <param name="radius">Radius to randomize destination.</param>
		void SetRandomDestination(float radius);

		/// <summary>
		/// Transitions to the next waypoint in the waypoint array.
		/// </summary>
		void TransitionToNextWaypoint();

		/// <summary>
		/// Picks the nearest waypoint to the current position and sets it as the destination.
		/// </summary>
		void PickNearestWaypoint();

		/// <summary>
		/// Stops the agent's movement.
		/// </summary>
		void Stop();

		/// <summary>
		/// Resumes the agent's movement.
		/// </summary>
		void Resume();
	}
}