using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Combat sub-state that circles the NPC around to the back of its current target.
	/// </summary>
	/// <remarks>
	/// Enable <see cref="BaseAIState.KeepsCombatTarget"/> on assets of this type. Without it the
	/// attacking state clears the combat target on the way out and this state finds nothing to
	/// move behind, which turned every flanking roll into a disengage.
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI GetBehind State", menuName = "FishMMO/Character/NPC/AI/GetBehind State", order = 0)]
	public class GetBehindState : BaseAIState
	{
		/// <summary>
		/// Distance behind the target to move.
		/// </summary>
		public float BehindDistance = 5.0f;
		/// <summary>
		/// Speed at which the AI rotates to face the target.
		/// </summary>
		public float RotationSpeed = 5.0f;

		/// <summary>
		/// Seconds to spend getting behind the target before giving up and attacking from here.
		/// </summary>
		[Tooltip("Seconds spent manoeuvring before rejoining the attack. 0 = no limit.")]
		public float MaxManoeuvreSeconds = 3.0f;

		/// <summary>
		/// Called when entering the GetBehind state. Calculates and sets destination behind the target.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Enter(AIController controller)
		{
			if (controller.Target == null)
			{
				Log.Warning("GetBehindState", $"{controller.gameObject.name} entered GetBehindState with no target. " +
					"Enable 'Keeps Combat Target' on this state asset if it is used as a combat sub-state.");
				controller.TransitionToIdleState(); // Or another default state
				return;
			}

			controller.Resume();

			// Calculate the position behind the target.
			Vector3 behindPosition = CalculateBehindPosition(controller.Target.position, controller.Target.forward);

			/* Unthrottled: this is the whole point of entering the state, and a request dropped by
			 * the repath throttle would leave the NPC standing in a flanking state it never acts
			 * on until the timeout below rescues it. */
			if (controller.TryMoveTo(behindPosition, throttle: false) == AIMovementResult.Failed)
			{
				// No room behind the target — go straight back to fighting.
				ReturnToCombatOrIdle(controller);
				return;
			}

			controller.SubStateTimer = MaxManoeuvreSeconds;
		}

		/// <summary>
		/// Called when exiting the GetBehind state. Can be used to stop movement or reset parameters.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Exit(AIController controller)
		{
			// Optional: Stop movement or reset parameters if needed
		}

		/// <summary>
		/// Called every frame to update the GetBehind state. Handles rotation and destination checks.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Frame time.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (controller.Target == null)
			{
				controller.TransitionToIdleState(); // Transition if target is lost
				return;
			}

			// Rotate to face the target smoothly
			Vector3 directionToTarget = (controller.Target.position - controller.Character.Transform.position).normalized;
			Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
			controller.Character.Transform.rotation = Quaternion.Slerp(controller.Character.Transform.rotation, targetRotation, RotationSpeed * deltaTime);

			/* Bounded. Without a timeout an NPC whose flank position became unreachable mid-move
			 * — the target walked into a corner — sits in this state indefinitely, out of combat
			 * in every way that matters but still holding threat. */
			if (MaxManoeuvreSeconds > 0f)
			{
				controller.SubStateTimer -= deltaTime;
				if (controller.SubStateTimer <= 0f)
				{
					ReturnToCombatOrIdle(controller);
					return;
				}
			}

			switch (controller.GetMovementProgress(deltaTime))
			{
				case AIMovementProgress.Arrived:
				case AIMovementProgress.Stuck:
				case AIMovementProgress.Idle:
					// Manoeuvre complete, or not going to complete. Either way, rejoin the fight.
					ReturnToCombatOrIdle(controller);
					return;

				default:
					return;
			}
		}

		/// <summary>
		/// Calculates the position behind the target based on its forward direction and desired distance.
		/// </summary>
		/// <param name="targetPosition">Target's position.</param>
		/// <param name="targetForward">Target's forward direction.</param>
		/// <returns>Position behind the target.</returns>
		private Vector3 CalculateBehindPosition(Vector3 targetPosition, Vector3 targetForward)
		{
			return targetPosition - targetForward * BehindDistance;
		}
	}
}