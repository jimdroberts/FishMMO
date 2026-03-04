namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for AI waypoint management — patrolling and picking waypoints.
	/// Separated from IAIController to satisfy ISP: consumers that only need waypoint logic
	/// can depend on this smaller contract.
	/// </summary>
	public interface IAIWaypoints : ICharacterBehaviour
	{
		/// <summary>
		/// Transitions to the next waypoint in the waypoint array.
		/// </summary>
		void TransitionToNextWaypoint();

		/// <summary>
		/// Picks the nearest waypoint to the current position and sets it as the destination.
		/// </summary>
		void PickNearestWaypoint();
	}
}