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
		/// Places the agent at a world position, updating its NavMesh location rather than only
		/// its transform, and clears whatever it was doing.
		/// </summary>
		/// <remarks>
		/// Exposed here so callers outside the AI assembly are not driven to
		/// <see cref="NavMeshAgent.Warp"/> directly. The raw call skips NavMesh sampling, leaves
		/// the existing path in place so the agent immediately walks back, and throws when the
		/// agent is null or inactive — all three of which the pet summon path hit.
		/// </remarks>
		/// <param name="position">The world position to place the agent at.</param>
		/// <returns>True if the agent was placed on the NavMesh.</returns>
		bool WarpTo(Vector3 position);

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