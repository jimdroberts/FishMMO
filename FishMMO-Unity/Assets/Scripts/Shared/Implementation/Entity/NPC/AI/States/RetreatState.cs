using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Moves the NPC away from its target until a safe distance is reached, then disengages.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Used both as the flee state for a <see cref="NPCCombatStyle.Pathetic"/> or
	/// <see cref="NPCCombatStyle.Cautious"/> personality and as a caster's emergency-retreat
	/// hand-off. Enable <see cref="BaseAIState.KeepsCombatTarget"/> on assets of this type: the
	/// retreat direction is computed <em>from</em> the target, so clearing it on the way in would
	/// leave the NPC with no idea which way to run.
	/// </para>
	/// <para>
	/// Fleeing is the movement most likely to run out of room — the NPC is by definition being
	/// pushed toward whatever is behind it — so every step of it is bounded. A cornered NPC that
	/// cannot retreat gives up and re-engages rather than standing against a wall being hit while
	/// a "retreating" state reports success.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Retreat State", menuName = "FishMMO/Character/NPC/AI/Retreat State", order = 0)]
	public class RetreatState : BaseAIState
	{
		/// <summary>
		/// How far the NPC moves away from its target on each retreat leg.
		/// </summary>
		public float RetreatDistance = 10.0f;

		/// <summary>
		/// Distance from the target at which the NPC stops retreating.
		/// </summary>
		public float SafeDistance = 20.0f;

		/// <summary>
		/// Seconds the NPC will keep trying to retreat before accepting that it cannot.
		/// </summary>
		[Tooltip("Seconds spent retreating before giving up. 0 = retreat until safe.")]
		public float MaxRetreatSeconds = 8.0f;

		/// <summary>
		/// Picks the first retreat destination.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public override void Enter(AIController controller)
		{
			controller.SubStateTimer = MaxRetreatSeconds;

			if (controller.Target == null)
			{
				// Nothing to run from.
				controller.TransitionToIdleState();
				return;
			}

			controller.Resume();
			MoveAway(controller);
		}

		/// <summary>
		/// Called when the state is exited.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public override void Exit(AIController controller)
		{
		}

		/// <summary>
		/// Keeps backing away until safe, cornered, or out of patience.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		/// <param name="deltaTime">Seconds since the previous AI tick.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			if (controller.Target == null)
			{
				Disengage(controller);
				return;
			}

			// Safe already? Stop, whatever the path is doing.
			float sqrDistance = controller.GetSqrDistanceToTarget();
			if (sqrDistance > SafeDistance * SafeDistance)
			{
				Disengage(controller);
				return;
			}

			if (MaxRetreatSeconds > 0f)
			{
				controller.SubStateTimer -= deltaTime;
				if (controller.SubStateTimer <= 0f)
				{
					/* Out of patience. The NPC is cornered or the pursuer is faster than it is;
					 * either way, standing in a retreat state achieves nothing. Hand back to the
					 * attacking state so it fights rather than cowering in place. */
					ReturnToCombatOrIdle(controller);
					return;
				}
			}

			switch (controller.GetMovementProgress(deltaTime))
			{
				case AIMovementProgress.Arrived:
					// Reached this leg but still not safe — take another one.
					MoveAway(controller);
					return;

				case AIMovementProgress.Stuck:
					// Backed into geometry. Try to slide out; the timeout above is the backstop.
					controller.TryRecoverFromStuck(controller.Home);
					return;

				case AIMovementProgress.Idle:
					MoveAway(controller);
					return;

				default:
					return;
			}
		}

		/// <summary>
		/// Sets a destination directly away from the target, trying diagonals when straight back
		/// is blocked.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		private void MoveAway(AIController controller)
		{
			if (controller.Target == null)
			{
				return;
			}

			Vector3 position = controller.Character.Transform.position;
			Vector3 away = position - controller.Target.position;
			away.y = 0f;

			// Standing exactly on the target: any direction is away. Pick a stable one so the NPC
			// does not pick a different direction on every tick and vibrate in place.
			if (away.sqrMagnitude < 0.0001f)
			{
				away = -controller.Character.Transform.forward;
			}
			away.Normalize();

			if (controller.TryMoveTo(position + away * RetreatDistance, throttle: false) != AIMovementResult.Failed)
			{
				return;
			}

			// Straight back is off the NavMesh — try the two rear diagonals before conceding.
			Vector3 right = Vector3.Cross(Vector3.up, away);
			if (controller.TryMoveTo(position + (away + right).normalized * RetreatDistance, throttle: false) != AIMovementResult.Failed)
			{
				return;
			}
			controller.TryMoveTo(position + (away - right).normalized * RetreatDistance, throttle: false);
		}

		/// <summary>
		/// Stops fleeing and drops the target.
		/// </summary>
		/// <remarks>
		/// Clearing the target matters: leaving it set lets the out-of-combat sweep re-acquire
		/// whatever the NPC just fled from, so a fleeing NPC bounced straight back into the fight.
		/// </remarks>
		/// <param name="controller">The AI controller managing this NPC.</param>
		private static void Disengage(AIController controller)
		{
			controller.Target = null;
			controller.LookTarget = null;
			controller.ClearPath();
			controller.TransitionToIdleState();
		}
	}
}
