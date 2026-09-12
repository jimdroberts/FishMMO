using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What a retreating NPC should do next.
	/// </summary>
	public enum AIRetreatOutcome
	{
		/// <summary>Keep running. The pursuer is still coming and there is room to run.</summary>
		KeepRetreating = 0,

		/// <summary>Turn around and fight. The chase is over one way or another.</summary>
		TurnAndFight = 1,

		/// <summary>Stop running and drop the target — nothing is chasing any more.</summary>
		Disengage = 2,
	}

	/// <summary>
	/// Everything <see cref="AIRetreatDecision.Decide"/> needs to reach an outcome.
	/// </summary>
	/// <remarks>
	/// A plain struct of scalars, so a test can build one with named field initialisers and assert
	/// the outcome without a scene, a NavMesh or an NPC.
	/// </remarks>
	public struct AIRetreatContext
	{
		/// <summary>Squared distance from the NPC to its target.</summary>
		public float SqrDistanceToTarget;

		/// <summary>Distance at which the NPC considers itself clear.</summary>
		public float SafeDistance;

		/// <summary>
		/// How fast the target is closing on the NPC, in units per second. Negative when it is
		/// moving away.
		/// </summary>
		public float TargetClosingSpeed;

		/// <summary>True when the NPC cannot path away — backed into geometry.</summary>
		public bool PathBlocked;

		/// <summary>True when this retreat leg has run past its own time limit.</summary>
		public bool OutOfPatience;

		/// <summary>True when the cumulative retreat budget for this fight is spent.</summary>
		public bool CumulativeCapSpent;

		/// <summary>Seconds this retreat leg has been running.</summary>
		public float SecondsRetreating;

		/// <summary>Seconds to retreat before turning back is even considered.</summary>
		public float MinRetreatSeconds;

		/// <summary>How many times this NPC has fled during the current fight, including this one.</summary>
		public int ConsecutiveRetreats;

		/// <summary>Base chance per check of turning back, 0-1.</summary>
		public float ReengageChance;

		/// <summary>
		/// Added to the chance once per retreat this fight, counting the one in progress.
		/// </summary>
		/// <remarks>
		/// Counted from one rather than zero, so the first retreat of a fight rolls at
		/// <c>ReengageChance + ReengageChanceRamp</c> and a ramp with no base chance still does
		/// something on its own.
		/// </remarks>
		public float ReengageChanceRamp;

		/// <summary>This check's roll in 0-1, drawn from the NPC's seeded RNG by the caller.</summary>
		public float ReengageRoll;
	}

	/// <summary>
	/// Decides whether a fleeing NPC keeps running, turns around, or disengages.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this exists as a solver and not as distance tests inside the state.</b> The reported
	/// behaviour — an NPC that runs away forever the moment a player chases it — is not a bug in any
	/// one comparison. It is the absence of any state that survives a tick: the old state compared
	/// the target's distance against <c>SafeDistance</c> and nothing else, so the same inputs always
	/// produced the same answer, and a pursuer faster than the NPC meant the inputs never changed.
	/// A decision that can see how long this has been going on, and how many times it has already
	/// happened, is what makes a chase end.
	/// </para>
	/// <para>
	/// Pure and Unity-free, matching <c>AICombatDecision</c>: every input arrives in the context and
	/// the roll is supplied by the caller, so the whole ladder is assertable.
	/// </para>
	/// </remarks>
	public static class AIRetreatDecision
	{
		/// <summary>
		/// Closing speed above which a target counts as pursuing.
		/// </summary>
		/// <remarks>
		/// Not zero: two bodies shuffling around each other inside a NavMesh agent's avoidance
		/// radius produce a small positive closing speed in any direction, and treating that as a
		/// pursuit would stop every retreat from ever disengaging. Ground speed is several units a
		/// second, so half a unit cleanly separates "walking me down" from "standing nearby".
		/// </remarks>
		public const float PURSUIT_CLOSING_SPEED = 0.5f;

		/// <summary>
		/// Reaches the outcome for this tick.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The order of these tests is the design.</b> The two hard stops come first, because a
		/// chase that cannot end is worse than a chase that ends too early: an NPC that keeps fleeing
		/// is unkillable by a melee player, while one that turns too soon merely loses a fight it was
		/// going to lose anyway.
		/// </para>
		/// <para>
		/// <b>A pursuer turns <see cref="AIRetreatOutcome.Disengage"/> into
		/// <see cref="AIRetreatOutcome.KeepRetreating"/>.</b> That single rule is the reported issue.
		/// Reaching <c>SafeDistance</c> used to be sufficient on its own, so an NPC that outran
		/// nothing in particular stopped and went idle while the player was still running at it —
		/// and then the out-of-combat sweep re-acquired the player and the NPC fled again, forever.
		/// Being far away only means the NPC is done running if nobody is following.
		/// </para>
		/// </remarks>
		/// <param name="context">The inputs for this tick.</param>
		/// <returns>The outcome for this tick.</returns>
		public static AIRetreatOutcome Decide(in AIRetreatContext context)
		{
			// Out of time or out of budget: the fight resumes, wherever that leaves the NPC.
			if (context.OutOfPatience || context.CumulativeCapSpent)
			{
				return AIRetreatOutcome.TurnAndFight;
			}

			// Backed into geometry. Nothing further is achieved by staying in a retreat state.
			if (context.PathBlocked)
			{
				return AIRetreatOutcome.TurnAndFight;
			}

			/* A minimum leg, so a re-engagement roll cannot fire on the first tick of a retreat and
			 * make the NPC stutter on the spot instead of ever leaving. */
			if (context.SecondsRetreating < context.MinRetreatSeconds)
			{
				return AIRetreatOutcome.KeepRetreating;
			}

			bool safe = context.SafeDistance <= 0f ||
						context.SqrDistanceToTarget > context.SafeDistance * context.SafeDistance;

			if (safe)
			{
				/* Far, but still being chased. This is the case the old state got wrong: distance
				 * alone said "done", so the NPC stopped in the open with a player bearing down on
				 * it. Keep running — the budget above is what guarantees this cannot last forever. */
				return context.TargetClosingSpeed > PURSUIT_CLOSING_SPEED
					? AIRetreatOutcome.KeepRetreating
					: AIRetreatOutcome.Disengage;
			}

			// Still inside the danger band: decide whether to stop running and fight.
			float chance = Mathf.Clamp01(context.ReengageChance +
										  context.ReengageChanceRamp * Mathf.Max(0, context.ConsecutiveRetreats));

			return context.ReengageRoll < chance
				? AIRetreatOutcome.TurnAndFight
				: AIRetreatOutcome.KeepRetreating;
		}
	}
}
