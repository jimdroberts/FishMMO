namespace FishMMO.Shared
{
	/// <summary>
	/// Outcome of asking an NPC to move somewhere.
	/// </summary>
	/// <remarks>
	/// The distinction that matters is <see cref="Partial"/>. Unity does not fail a path to an
	/// unreachable destination — it silently returns the closest reachable point instead. An NPC
	/// that only checks "did SetDestination return true" walks to the near side of a cliff, sees a
	/// tiny <c>remainingDistance</c>, concludes it arrived, and never tries again. Every "my pet is
	/// stuck on the terrain" report is that path, so callers need to be able to tell the two apart.
	/// </remarks>
	public enum AIMovementResult
	{
		/// <summary>The destination could not be placed on the NavMesh at all.</summary>
		Failed = 0,

		/// <summary>A complete path to the requested destination was set.</summary>
		Complete,

		/// <summary>
		/// A path was set, but it only reaches the closest point to the request. The NPC will
		/// stop short and must not treat stopping as arriving.
		/// </summary>
		Partial,

		/// <summary>The request was skipped because the repath throttle has not elapsed.</summary>
		Throttled,
	}

	/// <summary>
	/// How an NPC is faring against its current destination.
	/// </summary>
	public enum AIMovementProgress
	{
		/// <summary>No destination is set, or the agent cannot move.</summary>
		Idle = 0,

		/// <summary>A path is being computed.</summary>
		Computing,

		/// <summary>Moving normally.</summary>
		Moving,

		/// <summary>The destination has been reached.</summary>
		Arrived,

		/// <summary>
		/// The agent has stopped making progress while it still has somewhere to be — wedged on
		/// geometry, crowded out by other agents, or holding a partial path it has run out of.
		/// </summary>
		Stuck,
	}
}
