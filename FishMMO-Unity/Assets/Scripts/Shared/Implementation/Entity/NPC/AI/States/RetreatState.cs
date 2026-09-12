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

		[Header("Turning Back")]
		/// <summary>
		/// Seconds to retreat before turning back to fight is considered at all.
		/// </summary>
		/// <remarks>
		/// Without a floor the re-engagement roll can fire on the first tick of a retreat, so an NPC
		/// whose roll succeeds immediately never actually leaves — it turns on the spot, which reads
		/// as a stutter rather than as a decision.
		/// </remarks>
		[Tooltip("Seconds to retreat before turning back is considered. 0 allows turning back immediately.")]
		public float MinRetreatSeconds = 1.5f;

		/// <summary>
		/// Base chance, per check, that the NPC turns around and fights instead of continuing to run.
		/// </summary>
		[Tooltip("Base chance per check of turning back to fight. 0 = never turns back on chance alone.")]
		[Range(0f, 1f)]
		public float ReengageChance = 0.15f;

		/// <summary>
		/// Added to <see cref="ReengageChance"/> for each retreat taken this fight, counting the one in
		/// progress.
		/// </summary>
		/// <remarks>
		/// This ramp is what guarantees a long chase ends in a fight. A flat chance can lose the same
		/// roll a hundred times; a chance that climbs with every retreat the NPC has already made
		/// against the same opponent converges on certainty, so an NPC being chased across the zone
		/// will always eventually stop and turn.
		/// </remarks>
		[Tooltip("Added to the re-engagement chance per retreat taken this fight, the one in progress included. This is what guarantees a long chase ends in a fight.")]
		[Range(0f, 1f)]
		public float ReengageChanceRamp = 0.2f;

		/// <summary>
		/// Seconds between re-engagement checks.
		/// </summary>
		[Tooltip("Seconds between re-engagement checks while retreating.")]
		public float ReengageCheckInterval = 1.0f;

		/// <summary>
		/// Seconds during which fleeing is refused after the NPC turns back to fight.
		/// </summary>
		/// <remarks>
		/// Without this the attacking state re-plans on the very next tick, finds the same health
		/// threshold still violated, and hands straight back to this state — the NPC turns to fight
		/// and flees again before either is visible.
		/// </remarks>
		[Tooltip("Seconds during which fleeing is refused after turning back to fight.")]
		public float ReengageCooldownSeconds = 6.0f;

		[Header("Cumulative Limit")]
		/// <summary>
		/// Total seconds this NPC may spend retreating within a single fight. 0 disables the cap.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Separate from <see cref="MaxRetreatSeconds"/>, which bounds one leg. This bounds the sum
		/// across every leg of one fight, and is the backstop for a pursuer that keeps re-triggering
		/// the flee threshold: the NPC gets its budget, and then it fights.
		/// </para>
		/// <para>
		/// <b>0 preserves the old behaviour exactly.</b> Combined with
		/// <see cref="ReengageChance"/> and <see cref="ReengageChanceRamp"/> at 0, an archetype that
		/// wants to keep fleeing forever — a pet ordered to run, say — can, without a code change.
		/// </para>
		/// </remarks>
		[Tooltip("Total seconds of retreating allowed per fight. 0 = unlimited, which is the old behaviour.")]
		public float MaxCumulativeRetreatSeconds = 12.0f;

		/// <summary>
		/// Seconds the NPC must stand and fight once the cumulative cap is spent.
		/// </summary>
		[Tooltip("Seconds of hold once the cumulative cap is spent.")]
		public float RetreatRecoverySeconds = 8.0f;

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

			/* Drop the look target so the NPC turns and runs, rather than backpedalling at its
			 * pursuer.
			 *
			 * FaceLookTarget runs on every network tick and yaws the body at LookTarget, and
			 * StepAgent only applies its own heading resolution while LookTarget is null — so a look
			 * target left set here suppressed the one mechanism that would have turned the body
			 * around, for the whole of the retreat. The target survived the transition because this
			 * state has KeepsCombatTarget enabled, which is required for a different reason (the
			 * retreat direction is computed from the target). The two are not in conflict: keep the
			 * target, drop the look target. */
			controller.LookTarget = null;

			controller.Retreat.NoteRetreatStart(ReengageCheckInterval);

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

			bool outOfPatience = false;
			if (MaxRetreatSeconds > 0f)
			{
				controller.SubStateTimer -= deltaTime;
				outOfPatience = controller.SubStateTimer <= 0f;
			}

			AIMovementProgress progress = controller.GetMovementProgress(deltaTime);

			/* Cornered is measured from this tick, not remembered: a stuck step and a destination
			 * that could not be pathed to at all are the two ways "there is no room behind me"
			 * shows up, and both are visible right here. A flag kept across ticks on this asset
			 * would be shared by every NPC using it. */
			bool pathBlocked = progress == AIMovementProgress.Stuck;

			AIRetreatContext context = new AIRetreatContext
			{
				SqrDistanceToTarget = controller.GetSqrDistanceToTarget(),
				SafeDistance = SafeDistance,
				TargetClosingSpeed = controller.TargetClosingSpeed,
				PathBlocked = pathBlocked,
				OutOfPatience = outOfPatience,
				CumulativeCapSpent = controller.Retreat.RecoveryTimer > 0f,
				SecondsRetreating = MaxRetreatSeconds > 0f
					? MaxRetreatSeconds - controller.SubStateTimer
					: float.PositiveInfinity,
				MinRetreatSeconds = MinRetreatSeconds,
				ConsecutiveRetreats = controller.Retreat.ConsecutiveRetreats,
				ReengageChance = ReengageChance,
				ReengageChanceRamp = ReengageChanceRamp,
				ReengageRoll = 0f,
			};

			/* The roll is drawn only when it can matter, and only once per check interval. The NPC's
			 * RNG is shared with cooldown jitter, target selection and movement-variety rolls, so
			 * drawing from it every tick would perturb all of them — and a roll redrawn every tick
			 * would fire almost immediately on any chance above zero rather than testing the chance
			 * once per decision. */
			if (!context.OutOfPatience &&
				!context.CumulativeCapSpent &&
				!context.PathBlocked &&
				context.SecondsRetreating >= MinRetreatSeconds)
			{
				if (controller.Retreat.DecisionTimer <= 0f)
				{
					context.ReengageRoll = (controller.NpcRNG ?? DeterministicRNG.Shared).NextFloat();
					controller.Retreat.DecisionTimer = ReengageCheckInterval;
				}
				else
				{
					// Not yet due a check: keep running, and do not let the roll decide anything.
					context.ReengageRoll = float.PositiveInfinity;
				}
			}
			else
			{
				context.ReengageRoll = float.PositiveInfinity;
			}

			switch (AIRetreatDecision.Decide(context))
			{
				case AIRetreatOutcome.TurnAndFight:
					TurnAndFight(controller);
					return;

				case AIRetreatOutcome.Disengage:
					Disengage(controller);
					return;
			}

			// Keep retreating.
			switch (progress)
			{
				case AIMovementProgress.Arrived:
					// Reached this leg but still not safe — take another one.
					MoveAway(controller);
					return;

				case AIMovementProgress.Stuck:
					// Backed into geometry. Try to slide out; the decision above is the backstop.
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
		/// Stops running and goes back on the offensive.
		/// </summary>
		/// <remarks>
		/// The cooldown is what makes the decision stick. Handing control back without it re-enters
		/// this state on the next tick, because the health threshold that sent the NPC running is
		/// still violated — the NPC would flicker between running and turning without ever being
		/// visibly committed to either.
		/// </remarks>
		/// <param name="controller">The AI controller managing this NPC.</param>
		private void TurnAndFight(AIController controller)
		{
			controller.Retreat.NoteReengage(ReengageCooldownSeconds);
			ReturnToCombatOrIdle(controller);
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
