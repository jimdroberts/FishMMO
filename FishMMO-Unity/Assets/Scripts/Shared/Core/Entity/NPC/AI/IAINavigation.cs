using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for AI navigation — controlling agent movement, stopping, and resuming.
	/// Separated from IAIController to satisfy ISP: consumers that only need navigation
	/// can depend on this smaller contract.
	/// </summary>
	public interface IAINavigation : ICharacterBehaviour
	{
		/// <summary>
		/// The NavMeshAgent component used for navigation.
		/// </summary>
		NavMeshAgent Agent { get; }

		/// <summary>
		/// Sets a random destination within a radius around the home position.
		/// </summary>
		/// <param name="radius">Radius to randomize destination.</param>
		/// <returns>True if a reachable destination was found and set.</returns>
		bool SetRandomHomeDestination(float radius);

		/// <summary>
		/// Sets a random destination within a radius around the current position.
		/// </summary>
		/// <param name="radius">Radius to randomize destination.</param>
		/// <returns>True if a reachable destination was found and set.</returns>
		bool SetRandomDestination(float radius);

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