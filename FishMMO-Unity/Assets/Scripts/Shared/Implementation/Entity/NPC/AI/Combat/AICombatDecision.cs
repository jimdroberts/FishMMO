namespace FishMMO.Shared
{
	/// <summary>
	/// Everything the shared combat decision needs, as plain numbers.
	/// </summary>
	/// <remarks>
	/// Deliberately Unity-free. Every field is filled from the attacking state's serialized
	/// tuning plus a handful of runtime measurements, which is what makes an archetype asset
	/// testable: a test can build the same context the game builds and assert that a
	/// "pathetic" wolf flees where a "raging" one does not.
	/// </remarks>
	public struct AICombatContext
	{
		/// <summary>Current distance from the NPC to its target, in world units.</summary>
		public float Distance;

		/// <summary>
		/// Distance the NPC wants to hold. 0 means "close to melee reach" — see <see cref="MeleeReach"/>.
		/// </summary>
		public float PreferredDistance;

		/// <summary>Distance below which the NPC starts backing away. 0 disables backing away.</summary>
		public float MinComfortDistance;

		/// <summary>
		/// Fraction (0-1) of <see cref="MinComfortDistance"/> at which backing away escalates to
		/// an interrupt-and-run emergency retreat.
		/// </summary>
		public float EmergencyRetreatThreshold;

		/// <summary>Range of the ability the picker chose. Ignored when <see cref="HasUsableAbility"/> is false.</summary>
		public float AbilityRange;

		/// <summary>True when the ability picker returned something castable this tick.</summary>
		public bool HasUsableAbility;

		/// <summary>
		/// True when the NPC attacked on the previous tick rather than moving.
		/// </summary>
		/// <remarks>
		/// Drives the hysteresis that keeps an NPC from flickering between attacking and closing
		/// when its target hovers at the edge of ability range. See
		/// <see cref="AICombatDecision.RANGE_HYSTERESIS"/>.
		/// </remarks>
		public bool WasAttacking;

		/// <summary>The NPC's current health as a fraction (0-1) of its maximum.</summary>
		public float HealthPercent;

		/// <summary>
		/// Health fraction at or below which the NPC abandons the fight. 0 means "never flee".
		/// Supplied by <see cref="AICombatPersonality.RetreatHealthThreshold"/>.
		/// </summary>
		public float FleeHealthThreshold;

		/// <summary>
		/// False when the NPC has no way to flee (no retreat state, or a style that refuses to).
		/// Gates <see cref="AICombatIntent.Flee"/> only; emergency retreat is still allowed.
		/// </summary>
		public bool CanFlee;

		/// <summary>
		/// Fallback engagement distance for melee archetypes, used when
		/// <see cref="PreferredDistance"/> is 0. Typically twice the agent radius, floored at 1.
		/// </summary>
		public float MeleeReach;
	}

	/// <summary>
	/// The decision produced from an <see cref="AICombatContext"/>.
	/// </summary>
	public struct AICombatPlan
	{
		/// <summary>What to do this tick.</summary>
		public AICombatIntent Intent;

		/// <summary>
		/// Distance the movement intents aim for. For <see cref="AICombatIntent.CloseDistance"/>
		/// this is where to stop; for the retreat intents it is how far to get away.
		/// Zero for <see cref="AICombatIntent.Attack"/> and <see cref="AICombatIntent.HoldPosition"/>.
		/// </summary>
		public float DesiredDistance;

		/// <summary>
		/// True when the NPC should squeeze off its chosen ability while executing a movement
		/// intent. Only ever set alongside <see cref="AICombatIntent.BackAway"/> — kiting
		/// archetypes shoot on the way out, but a full emergency retreat never does.
		/// </summary>
		public bool FireWhileMoving;
	}

	/// <summary>
	/// The single combat decision shared by every attacking state.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Melee, ranged, caster, healer, defender and rogue states previously each carried their own
	/// near-identical <c>TryAttack</c> override — five copies of "am I too close? can I cast? do I
	/// walk in?" that drifted apart from one another and could only be verified by playing the
	/// game. They now all funnel through <see cref="Plan"/> and differ purely in the numbers they
	/// feed it and in what they do with the resulting intent.
	/// </para>
	/// <para>
	/// Because this is a pure function over plain floats, an archetype asset's behaviour is
	/// directly assertable in an EditMode test — see <c>AICombatDecisionTests</c> and
	/// <c>AIArchetypeAssetTests</c>.
	/// </para>
	/// </remarks>
	public static class AICombatDecision
	{
		/// <summary>
		/// Multiplier applied to the engagement distance before the NPC bothers closing in.
		/// Without the slack the NPC oscillates between "one centimetre too far" and "stop".
		/// </summary>
		public const float ENGAGE_SLACK = 1.1f;

		/// <summary>
		/// Fraction of an ability's range the NPC actually walks to. Stopping exactly at maximum
		/// range means the first step the target takes puts the NPC out of range again.
		/// </summary>
		public const float RANGE_APPROACH_FACTOR = 0.9f;

		/// <summary>
		/// How far past its ability range a target may drift before an already-attacking NPC
		/// gives up and closes again.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Without this the decision at the range boundary is a bare <c>distance &lt;= range</c>,
		/// and a target strafing around that boundary flips the NPC between Attack and
		/// CloseDistance on alternating ticks. Each flip toggles <c>NavMeshAgent.isStopped</c>, and
		/// because the agent has acceleration and braking it never reaches speed in either
		/// direction — the NPC visibly shudders in place instead of either fighting or chasing.
		/// </para>
		/// <para>
		/// Ten percent is enough to absorb a strafing player without letting the NPC attack from
		/// meaningfully outside its stated range.
		/// </para>
		/// </remarks>
		public const float RANGE_HYSTERESIS = 1.1f;

		/// <summary>
		/// Decides what the NPC should do about its target this tick.
		/// </summary>
		/// <param name="context">The measured combat situation.</param>
		/// <returns>The intent plus the distance the movement intents should aim for.</returns>
		public static AICombatPlan Plan(in AICombatContext context)
		{
			AICombatPlan plan = default;

			float engageDistance = ResolveEngageDistance(context);

			// 1. Self-preservation outranks everything. A personality with no retreat threshold,
			//    or a Berserker/Rampaging style, arrives here with CanFlee false and fights on.
			if (context.CanFlee &&
				context.FleeHealthThreshold > 0f &&
				context.HealthPercent <= context.FleeHealthThreshold)
			{
				plan.Intent = AICombatIntent.Flee;
				plan.DesiredDistance = engageDistance;
				return plan;
			}

			// 2. Panic radius — the target is close enough to be a real threat to a squishy
			//    archetype. Callers interrupt the current cast for this one; they do not for
			//    the ordinary BackAway below.
			if (context.MinComfortDistance > 0f &&
				context.Distance < context.MinComfortDistance * context.EmergencyRetreatThreshold)
			{
				plan.Intent = AICombatIntent.EmergencyRetreat;
				plan.DesiredDistance = engageDistance;
				return plan;
			}

			// 3. Kiting band — uncomfortable but not desperate. Fire on the way out when the
			//    chosen ability still reaches.
			if (context.MinComfortDistance > 0f &&
				context.Distance < context.MinComfortDistance)
			{
				plan.Intent = AICombatIntent.BackAway;
				plan.DesiredDistance = engageDistance;
				plan.FireWhileMoving = context.HasUsableAbility && context.Distance <= context.AbilityRange;
				return plan;
			}

			// 4/5. Something to cast: fire if it reaches, otherwise walk it into range without
			//      overshooting past the archetype's preferred distance.
			if (context.HasUsableAbility)
			{
				/* Asymmetric threshold. An NPC that is not yet attacking must be inside its
				 * ability range to start; one that already is may drift slightly outside before
				 * being sent chasing again. The gap is what stops the stop/go shudder at the
				 * boundary. */
				float engageRange = context.WasAttacking
					? context.AbilityRange * RANGE_HYSTERESIS
					: context.AbilityRange;

				if (context.Distance <= engageRange)
				{
					plan.Intent = AICombatIntent.Attack;
					return plan;
				}

				plan.Intent = AICombatIntent.CloseDistance;
				plan.DesiredDistance = ResolveApproachDistance(context, engageDistance);
				return plan;
			}

			// 6. Everything is on cooldown or unaffordable. Hold the archetype's spacing so the
			//    NPC is already in position when something comes off cooldown.
			/* Same idea for spacing: an NPC already holding position tolerates more drift before
			 * setting off again than one that is still closing, so a target circling at the edge
			 * of the band does not start and stop it every tick. */
			float holdSlack = context.WasAttacking ? ENGAGE_SLACK * RANGE_HYSTERESIS : ENGAGE_SLACK;

			if (context.Distance > engageDistance * holdSlack)
			{
				plan.Intent = AICombatIntent.CloseDistance;
				plan.DesiredDistance = engageDistance;
				return plan;
			}

			plan.Intent = AICombatIntent.HoldPosition;
			return plan;
		}

		/// <summary>
		/// The distance this archetype wants to sit at: its preferred distance, or melee reach
		/// when it has none.
		/// </summary>
		/// <param name="context">The measured combat situation.</param>
		/// <returns>A strictly positive engagement distance.</returns>
		public static float ResolveEngageDistance(in AICombatContext context)
		{
			if (context.PreferredDistance > 0f)
			{
				return context.PreferredDistance;
			}
			return context.MeleeReach > 0f ? context.MeleeReach : 1f;
		}

		/// <summary>
		/// Where to stop when walking an out-of-range ability into range.
		/// </summary>
		/// <remarks>
		/// Approaches to a fraction of the ability's range so a moving target does not
		/// immediately step back out, but never closer than the archetype's engagement distance —
		/// a caster with a 30 m nuke and a 20 m preferred distance stops at 20, not at 27.
		/// A melee archetype (no preferred distance) is allowed to close all the way.
		/// </remarks>
		/// <param name="context">The measured combat situation.</param>
		/// <param name="engageDistance">The archetype's engagement distance.</param>
		/// <returns>The distance from the target at which to stop moving.</returns>
		public static float ResolveApproachDistance(in AICombatContext context, float engageDistance)
		{
			float approach = context.AbilityRange * RANGE_APPROACH_FACTOR;

			if (context.PreferredDistance > 0f && approach > engageDistance)
			{
				approach = engageDistance;
			}

			// A zero or negative approach would send the NPC into the target's collider and, for
			// MoveTowardTarget, produce a degenerate zero-radius sphere sample.
			if (approach <= 0f)
			{
				approach = engageDistance;
			}

			return approach;
		}
	}
}
