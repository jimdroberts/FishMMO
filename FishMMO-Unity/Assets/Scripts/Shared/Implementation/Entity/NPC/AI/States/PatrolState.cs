using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Waypoint patrol. Walks the spawner-supplied waypoint ring in order.
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Patrol State", menuName = "FishMMO/Character/NPC/AI/Patrol State", order = 0)]
	public class PatrolState : BaseAIState
	{
		/// <summary>
		/// How close the NPC must get to a waypoint before moving on to the next one.
		/// </summary>
		[Tooltip("Distance from a waypoint that counts as reaching it.")]
		public float WaypointTolerance = 1.5f;

		/// <summary>
		/// Consecutive unreachable waypoints tolerated before the NPC gives up on patrolling.
		/// </summary>
		/// <remarks>
		/// Guards against a waypoint ring where every entry has drifted off the NavMesh: without a
		/// bound the NPC would advance through the whole ring every tick forever, never moving.
		/// </remarks>
		[Tooltip("Consecutive unreachable waypoints before the NPC abandons its patrol.")]
		public int MaxSkippedWaypoints = 4;

		/// <summary>
		/// Starts patrolling from the nearest waypoint.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Enter(AIController controller)
		{
			controller.Resume();
			controller.SubStateTimer = 0f;

			if (!controller.PickNearestWaypoint())
			{
				// No usable waypoints — patrolling is not possible for this NPC.
				controller.TransitionToIdleState();
			}
		}

		/// <summary>
		/// Called when exiting the Patrol state.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Exit(AIController controller)
		{
		}

		/// <summary>
		/// Advances to the next waypoint on arrival, and recovers when a waypoint cannot be reached.
		/// </summary>
		/// <remarks>
		/// The previous arrival test was <c>!pathPending &amp;&amp; remainingDistance &lt; 1</c>,
		/// which is also exactly what an agent with no path at all reports. A patrol whose first
		/// waypoint failed to sample onto the NavMesh therefore "arrived" at every waypoint in the
		/// ring, once per tick, without taking a single step.
		/// </remarks>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Seconds since the previous AI tick.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (controller.RandomizeState)
			{
				controller.TransitionToRandomMovementState();
				return;
			}

			switch (controller.GetMovementProgress(deltaTime, WaypointTolerance))
			{
				case AIMovementProgress.Arrived:
					controller.SubStateTimer = 0f;
					AdvanceWaypoint(controller);
					return;

				case AIMovementProgress.Stuck:
				case AIMovementProgress.Idle:
					// Unreachable or never started. Skip it, but do not skip forever.
					controller.SubStateTimer += 1f;
					if (MaxSkippedWaypoints > 0 && controller.SubStateTimer >= MaxSkippedWaypoints)
					{
						controller.ClearPath();
						controller.TransitionToIdleState();
						return;
					}
					AdvanceWaypoint(controller);
					return;

				default:
					return;
			}
		}

		/// <summary>
		/// Moves to the next waypoint, dropping to idle when there are none.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		private static void AdvanceWaypoint(AIController controller)
		{
			if (!controller.TransitionToNextWaypoint())
			{
				controller.TransitionToIdleState();
			}
		}
	}
}
