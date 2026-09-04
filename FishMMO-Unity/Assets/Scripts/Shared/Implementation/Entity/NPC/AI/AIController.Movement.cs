using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared
{
	/// <summary>
	/// Navigation half of <see cref="AIController"/>: destination requests that actually verify
	/// they landed, an arrival test that is not fooled by a missing path, and stuck detection with
	/// escalating recovery.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every AI state previously drove the NavMeshAgent with the same three-line pattern:
	/// <c>NavMesh.SamplePosition</c>, <c>SetDestination</c>, and then
	/// <c>!pathPending &amp;&amp; remainingDistance &lt; 1</c> to decide it had arrived. All three
	/// steps have failure modes that the pattern silently swallows:
	/// </para>
	/// <list type="bullet">
	///   <item>
	///     <c>SamplePosition</c> returns false when the sampled point has no NavMesh near it, and
	///     the caller then simply did not move — with no retry and no fallback.
	///   </item>
	///   <item>
	///     An unreachable destination does not fail. Unity returns a <em>partial</em> path to the
	///     closest reachable point, so the agent walks to the near side of the obstacle and stops.
	///   </item>
	///   <item>
	///     <c>remainingDistance</c> is 0 when there is no path at all, and <c>pathPending</c> is
	///     false at the same moment — so "no path" and "arrived" are indistinguishable. A patrol
	///     whose first waypoint failed to sample cycled through every waypoint once per tick
	///     without walking anywhere.
	///   </item>
	/// </list>
	/// <para>
	/// These helpers collapse that pattern into calls that report what actually happened.
	/// </para>
	/// </remarks>
	public partial class AIController
	{
		/// <summary>
		/// Distance from the destination at which an NPC counts as arrived, in world units.
		/// </summary>
		public const float ARRIVAL_TOLERANCE = 1.0f;

		/// <summary>
		/// How far from the requested point the NavMesh may be sampled on the first attempt.
		/// </summary>
		private const float SAMPLE_RADIUS = 2.0f;

		/// <summary>
		/// Multiplier applied to the sample radius on each retry.
		/// </summary>
		private const float SAMPLE_RADIUS_GROWTH = 4.0f;

		/// <summary>
		/// How many times to widen the sample radius before giving up on a destination.
		/// </summary>
		private const int SAMPLE_ATTEMPTS = 3;

		/// <summary>
		/// Speed below which an agent that is supposed to be moving counts as not moving.
		/// </summary>
		private const float STUCK_SPEED_THRESHOLD = 0.05f;

		/// <summary>
		/// Seconds of no progress before the NPC is considered stuck.
		/// </summary>
		[Header("Navigation Recovery")]
		[Tooltip("Seconds of no movement, while trying to move, before the NPC is considered stuck.")]
		public float StuckTimeout = 2.5f;

		/// <summary>
		/// Seconds of continued no progress after the first recovery attempt before the NPC is
		/// teleported to its destination. 0 disables teleport recovery.
		/// </summary>
		[Tooltip("Seconds stuck before the NPC is warped free. 0 = never warp.")]
		public float StuckWarpTimeout = 8.0f;

		/// <summary>
		/// Accumulated seconds during which the agent wanted to move but did not.
		/// </summary>
		private float stuckTimer;

		/// <summary>
		/// Number of recovery attempts made against the current stuck episode.
		/// </summary>
		private int stuckRecoveryAttempts;

		/// <summary>
		/// The destination most recently requested through <see cref="TryMoveTo"/>.
		/// </summary>
		public Vector3 RequestedDestination { get; private set; }

		/// <summary>
		/// True when the last accepted destination request produced only a partial path.
		/// </summary>
		/// <remarks>
		/// Consumed by <see cref="GetMovementProgress"/>: an agent that has run out of a partial
		/// path has stopped somewhere it was not asked to go, which is a stuck condition rather
		/// than an arrival.
		/// </remarks>
		public bool LastPathWasPartial { get; private set; }

		/// <summary>
		/// Resets the navigation recovery state. Called from <see cref="AIController.ResetState"/>.
		/// </summary>
		private void ResetMovementState()
		{
			stuckTimer = 0f;
			stuckRecoveryAttempts = 0;
			RequestedDestination = Vector3.zero;
			LastPathWasPartial = false;
		}

		/// <summary>
		/// Places a world position onto the NavMesh, widening the search until it lands or the
		/// attempts run out.
		/// </summary>
		/// <remarks>
		/// A single fixed-radius sample is the difference between "the NPC walks to a slightly
		/// different spot" and "the NPC does not move at all", and the old call sites all took the
		/// second outcome silently.
		/// </remarks>
		/// <param name="position">The desired world position.</param>
		/// <param name="result">The nearest position actually on the NavMesh.</param>
		/// <param name="initialRadius">Radius for the first attempt.</param>
		/// <returns>True if a NavMesh position was found.</returns>
		public static bool TrySampleNavMesh(Vector3 position, out Vector3 result, float initialRadius = SAMPLE_RADIUS)
		{
			float radius = initialRadius > 0f ? initialRadius : SAMPLE_RADIUS;

			for (int attempt = 0; attempt < SAMPLE_ATTEMPTS; ++attempt)
			{
				if (NavMesh.SamplePosition(position, out NavMeshHit hit, radius, NavMesh.AllAreas))
				{
					result = hit.position;
					return true;
				}
				radius *= SAMPLE_RADIUS_GROWTH;
			}

			result = position;
			return false;
		}

		/// <summary>
		/// Asks the agent to move to a world position, reporting whether the path actually reaches it.
		/// </summary>
		/// <param name="destination">Desired world position.</param>
		/// <param name="throttle">
		/// True to respect <see cref="RepathInterval"/>. Use for ongoing movement toward something
		/// that moves; pass false for one-shot destinations such as a waypoint or a spawn point,
		/// where a silently dropped request means the NPC never sets off at all.
		/// </param>
		/// <param name="sampleRadius">Initial NavMesh sample radius.</param>
		/// <returns>What happened.</returns>
		public AIMovementResult TryMoveTo(Vector3 destination, bool throttle = true, float sampleRadius = SAMPLE_RADIUS)
		{
			if (!AgentIsUsable())
			{
				return AIMovementResult.Failed;
			}

			if (throttle && repathCooldown > 0f)
			{
				return AIMovementResult.Throttled;
			}

			if (!TrySampleNavMesh(destination, out Vector3 sampled, sampleRadius))
			{
				return AIMovementResult.Failed;
			}

			if (!Agent.SetDestination(sampled))
			{
				return AIMovementResult.Failed;
			}

			repathCooldown = RepathInterval;
			RequestedDestination = sampled;
			Agent.isStopped = false;

			/* pathStatus is only meaningful once the path is computed. SetDestination usually
			 * resolves synchronously for short paths and defers for long ones, so a Pending result
			 * here is not an error — GetMovementProgress re-checks it every tick. */
			if (!Agent.pathPending && Agent.pathStatus == NavMeshPathStatus.PathPartial)
			{
				LastPathWasPartial = true;
				return AIMovementResult.Partial;
			}

			LastPathWasPartial = false;
			return AIMovementResult.Complete;
		}

		/// <summary>
		/// True when the agent has arrived at its destination.
		/// </summary>
		/// <remarks>
		/// Requires an actual path to exist. Without that check, an agent whose destination never
		/// took reports <c>remainingDistance == 0</c> and <c>pathPending == false</c> — which the
		/// naive test reads as "arrived" on the very first tick.
		/// </remarks>
		/// <param name="tolerance">Distance from the destination that counts as arrived.</param>
		/// <returns>True if arrived.</returns>
		public bool HasArrived(float tolerance = ARRIVAL_TOLERANCE)
		{
			if (!AgentIsUsable() || Agent.pathPending)
			{
				return false;
			}

			// No path means the NPC never set off; that is not arrival.
			if (!Agent.hasPath && Agent.remainingDistance <= 0f)
			{
				return false;
			}

			// A partial path ends short of where the NPC was asked to go.
			if (Agent.pathStatus != NavMeshPathStatus.PathComplete)
			{
				return false;
			}

			return Agent.remainingDistance <= Mathf.Max(tolerance, Agent.stoppingDistance);
		}

		/// <summary>
		/// Classifies how the agent is doing against its destination, and accumulates the stuck timer.
		/// </summary>
		/// <remarks>
		/// Call once per AI tick from a state that is trying to move. States that are deliberately
		/// standing still must not call it, or a stationary NPC would be reported stuck.
		/// </remarks>
		/// <param name="deltaTime">Seconds since the previous AI tick.</param>
		/// <param name="tolerance">Arrival tolerance.</param>
		/// <returns>The current progress classification.</returns>
		public AIMovementProgress GetMovementProgress(float deltaTime, float tolerance = ARRIVAL_TOLERANCE)
		{
			if (!AgentIsUsable() || Agent.isStopped)
			{
				stuckTimer = 0f;
				return AIMovementProgress.Idle;
			}

			if (Agent.pathPending)
			{
				return AIMovementProgress.Computing;
			}

			if (HasArrived(tolerance))
			{
				stuckTimer = 0f;
				stuckRecoveryAttempts = 0;
				return AIMovementProgress.Arrived;
			}

			if (!Agent.hasPath)
			{
				stuckTimer = 0f;
				return AIMovementProgress.Idle;
			}

			/* Wanting to move but not moving. desiredVelocity is what the agent would like to do;
			 * velocity is what it is managing. A large gap between them for several seconds means
			 * something physical is in the way — terrain, a prop, or a knot of other agents. */
			bool wantsToMove = Agent.desiredVelocity.sqrMagnitude > (STUCK_SPEED_THRESHOLD * STUCK_SPEED_THRESHOLD);
			bool isMoving = Agent.velocity.sqrMagnitude > (STUCK_SPEED_THRESHOLD * STUCK_SPEED_THRESHOLD);

			// A partial path that has been walked out is also stuck: the agent stopped somewhere
			// it was not sent, and standing there produces neither movement nor arrival.
			bool strandedOnPartialPath = Agent.pathStatus == NavMeshPathStatus.PathPartial &&
										 Agent.remainingDistance <= Mathf.Max(tolerance, Agent.stoppingDistance);

			if ((wantsToMove && !isMoving) || strandedOnPartialPath)
			{
				stuckTimer += deltaTime;
				if (StuckTimeout > 0f && stuckTimer >= StuckTimeout)
				{
					return AIMovementProgress.Stuck;
				}
			}
			else
			{
				stuckTimer = 0f;
				stuckRecoveryAttempts = 0;
			}

			return AIMovementProgress.Moving;
		}

		/// <summary>
		/// Attempts to free a stuck NPC, escalating with each call within the same episode.
		/// </summary>
		/// <remarks>
		/// <para>
		/// First a nudge: re-sample the destination from a wider radius and repath, which clears
		/// the common case of two agents wedged against each other or a destination that sampled
		/// onto the wrong side of a wall. Then, once <see cref="StuckWarpTimeout"/> has elapsed,
		/// a warp — the only remedy for genuinely unreachable geometry.
		/// </para>
		/// <para>
		/// Warping is a last resort rather than the first move on purpose: it is visible to
		/// players, so an NPC should be seen to try to walk before it blinks.
		/// </para>
		/// </remarks>
		/// <param name="fallback">
		/// Where to go if the original destination cannot be recovered — a pet's owner, an NPC's
		/// home. Pass the NPC's own position to mean "just get unstuck where you are".
		/// </param>
		/// <returns>True if a recovery action was taken.</returns>
		public bool TryRecoverFromStuck(Vector3 fallback)
		{
			if (!AgentIsUsable())
			{
				return false;
			}

			stuckRecoveryAttempts++;

			// Escalate to a warp only after the NPC has visibly failed to walk out of it.
			bool shouldWarp = StuckWarpTimeout > 0f && stuckTimer >= StuckWarpTimeout;

			if (shouldWarp)
			{
				Vector3 warpTarget = RequestedDestination != Vector3.zero ? RequestedDestination : fallback;
				if (TrySampleNavMesh(warpTarget, out Vector3 sampled, SAMPLE_RADIUS * SAMPLE_RADIUS_GROWTH) &&
					Agent.Warp(sampled))
				{
					SyncTransformToAgent();
					stuckTimer = 0f;
					stuckRecoveryAttempts = 0;
					LastPathWasPartial = false;
					return true;
				}
			}

			// Nudge: clear the current path and repath from a wider sample.
			repathCooldown = 0f;
			Agent.ResetPath();

			Vector3 retryTarget = stuckRecoveryAttempts > 1 ? fallback : RequestedDestination;
			if (retryTarget == Vector3.zero)
			{
				retryTarget = fallback;
			}

			AIMovementResult result = TryMoveTo(retryTarget, throttle: false, sampleRadius: SAMPLE_RADIUS * SAMPLE_RADIUS_GROWTH);

			// Give the nudge time to take effect before the timer trips again.
			stuckTimer = 0f;

			return result != AIMovementResult.Failed;
		}

		/// <summary>
		/// Places the agent at a world position, using <see cref="NavMeshAgent.Warp"/> so the
		/// agent's internal NavMesh position is updated rather than only its transform.
		/// </summary>
		/// <remarks>
		/// Required for object pooling. A recycled NPC is reactivated at a new position, and
		/// assigning <c>transform.position</c> alone leaves the agent believing it is still where
		/// the previous occupant died — it then either refuses to path or walks back toward the
		/// old location.
		/// </remarks>
		/// <param name="position">The world position to place the agent at.</param>
		/// <returns>True if the agent was placed on the NavMesh.</returns>
		public bool WarpTo(Vector3 position)
		{
			if (Agent == null || !Agent.isActiveAndEnabled)
			{
				Character.Transform.position = position;
				return false;
			}

			if (TrySampleNavMesh(position, out Vector3 sampled) && Agent.Warp(sampled))
			{
				SyncTransformToAgent();
				ResetMovementState();
				return true;
			}

			// Off-mesh spawn point: place the transform and let the agent recover on its own.
			Character.Transform.position = position;
			ResetMovementState();
			return false;
		}

		/// <summary>
		/// Copies the agent's simulated position onto the transform after a warp.
		/// </summary>
		/// <remarks>
		/// The transform is no longer driven by the agent (<c>updatePosition</c> is off, see
		/// <see cref="StepAgent"/>), so a warp that re-seats the simulation must be mirrored here
		/// or the NetworkTransform keeps sending the old spot until the next tick's step.
		/// </remarks>
		private void SyncTransformToAgent()
		{
			if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
			{
				Character.Transform.position = Agent.nextPosition;
			}
		}

		/// <summary>
		/// Clears the agent's path without moving it.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ClearPath()
		{
			if (!AgentIsUsable())
			{
				return;
			}
			Agent.ResetPath();
			RequestedDestination = Vector3.zero;
			LastPathWasPartial = false;
			stuckTimer = 0f;
		}
	}
}
