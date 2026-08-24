using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Wandering behaviour. The NPC drifts to random points around its home, pausing at idle now
	/// and then.
	/// </summary>
	/// <remarks>
	/// For a pet, <see cref="AIController.Home"/> is its owner, so the same state produces
	/// "mill about near the player" without any pet-specific code.
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Wander State", menuName = "FishMMO/Character/NPC/AI/Wander State", order = 0)]
	public class WanderState : BaseAIState
	{
		/// <summary>
		/// If true, the NPC picks a new destination on every update instead of walking to the
		/// current one.
		/// </summary>
		public bool AlwaysPickNewDestination;

		/// <summary>
		/// The radius within which the NPC will wander from its home position.
		/// </summary>
		public float WanderRadius = 15f;

		/// <summary>
		/// If greater than the base update rate, the update rate is randomized between the two.
		/// </summary>
		[Tooltip("If max update rate is greater than the update rate it will return a random range between the two.")]
		public float MaxUpdateRate;

		/// <summary>
		/// Chance (0-1) of standing idle instead of picking another destination on arrival.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Chance to pause at idle on arrival instead of wandering on.")]
		public float IdleChance = 0.5f;

		/// <summary>
		/// Returns the update rate for this state, randomized when <see cref="MaxUpdateRate"/>
		/// exceeds the base rate.
		/// </summary>
		/// <param name="controller">The AI controller running this state.</param>
		/// <returns>Update rate in seconds.</returns>
		public override float GetUpdateRate(AIController controller)
		{
			// Randomize update rate between base and max value for more natural wandering,
			// using the NPC's own seeded RNG so behaviour stays reproducible.
			return RandomizeRate(controller, base.GetUpdateRate(), MaxUpdateRate);
		}

		/// <summary>
		/// Sets off toward a first destination.
		/// </summary>
		/// <remarks>
		/// Entering used to do nothing at all, leaving the NPC pathless until UpdateState happened
		/// to notice — and the check it used to notice with could not distinguish "no path" from
		/// "arrived".
		/// </remarks>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public override void Enter(AIController controller)
		{
			controller.Resume();
			PickDestination(controller);
		}

		/// <summary>
		/// Called when the state is exited.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public override void Exit(AIController controller)
		{
		}

		/// <summary>
		/// Walks to the current destination, then decides whether to idle or wander on.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		/// <param name="deltaTime">Seconds since the previous AI tick.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			// If the controller requests randomization, transition to random movement state.
			if (controller.RandomizeState)
			{
				controller.TransitionToRandomMovementState();
				return;
			}

			if (AlwaysPickNewDestination)
			{
				PickDestination(controller);
				return;
			}

			switch (controller.GetMovementProgress(deltaTime))
			{
				case AIMovementProgress.Arrived:
					OnArrived(controller);
					return;

				case AIMovementProgress.Stuck:
					// Wandering has no destination worth fighting for — pick a different one.
					controller.ClearPath();
					PickDestination(controller);
					return;

				case AIMovementProgress.Idle:
					// No path: either the first sample failed or something reset it. Try again.
					PickDestination(controller);
					return;

				default:
					return;
			}
		}

		/// <summary>
		/// Decides between pausing and wandering on.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		private void OnArrived(AIController controller)
		{
			DeterministicRNG rng = controller.NpcRNG;
			float roll = (rng ?? DeterministicRNG.Shared).NextFloat();

			if (roll <= IdleChance)
			{
				controller.TransitionToIdleState();
				return;
			}

			PickDestination(controller);
		}

		/// <summary>
		/// Picks a random reachable point around home and sets off toward it.
		/// </summary>
		/// <remarks>
		/// Unthrottled: this is a one-shot destination, and a silently dropped request leaves the
		/// NPC standing still until the next update rather than merely delaying a repath.
		/// </remarks>
		/// <param name="controller">The AI controller managing this NPC.</param>
		private void PickDestination(AIController controller)
		{
			Vector3 home = controller.Home;
			Vector3 destination = WanderRadius > 0f
				? Vector3Extensions.RandomPositionWithinRadius(home, WanderRadius)
				: home;

			if (controller.TryMoveTo(destination, throttle: false) == AIMovementResult.Failed)
			{
				// Nowhere to wander to from here. Fall back to home itself, then to standing still.
				if (controller.TryMoveTo(home, throttle: false) == AIMovementResult.Failed)
				{
					controller.TransitionToIdleState();
				}
			}
		}
	}
}
