using NUnit.Framework;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs for <see cref="AICombatDecision"/>, the single decision every NPC archetype runs on.
	/// </summary>
	/// <remarks>
	/// The decision was deliberately extracted into a pure function over plain floats so that an
	/// archetype's behaviour is assertable without a NavMeshAgent, a NetworkManager or a scene.
	/// These tests exercise the production type directly — nothing is reimplemented here.
	/// </remarks>
	[TestFixture]
	public class AICombatDecisionTests
	{
		/// <summary>
		/// Builds a melee-ish context: no preferred distance, no comfort gap, full health.
		/// </summary>
		/// <param name="distance">Distance to the target.</param>
		/// <returns>A context ready to be mutated by a test.</returns>
		private static AICombatContext Melee(float distance)
		{
			AICombatContext context = default;
			context.Distance = distance;
			context.PreferredDistance = 0f;
			context.MinComfortDistance = 0f;
			context.EmergencyRetreatThreshold = 0.5f;
			context.MeleeReach = 1f;
			context.HealthPercent = 1f;
			context.FleeHealthThreshold = 0f;
			context.CanFlee = false;
			return context;
		}

		/// <summary>
		/// Builds an archer-ish context: holds 15 m, uncomfortable inside 5 m.
		/// </summary>
		/// <param name="distance">Distance to the target.</param>
		/// <returns>A context ready to be mutated by a test.</returns>
		private static AICombatContext Ranged(float distance)
		{
			AICombatContext context = Melee(distance);
			context.PreferredDistance = 15f;
			context.MinComfortDistance = 5f;
			context.EmergencyRetreatThreshold = 0.5f;
			return context;
		}

		// --- Fleeing -----------------------------------------------------------------------

		[Test]
		public void Plan_HealthAtFleeThreshold_Flees()
		{
			AICombatContext context = Melee(2f);
			context.CanFlee = true;
			context.FleeHealthThreshold = 0.45f;
			context.HealthPercent = 0.45f;

			Assert.AreEqual(AICombatIntent.Flee, AICombatDecision.Plan(context).Intent,
				"A pathetic archetype must break off at its threshold, not merely below it.");
		}

		[Test]
		public void Plan_HealthAboveFleeThreshold_DoesNotFlee()
		{
			AICombatContext context = Melee(2f);
			context.CanFlee = true;
			context.FleeHealthThreshold = 0.45f;
			context.HealthPercent = 0.46f;

			Assert.AreNotEqual(AICombatIntent.Flee, AICombatDecision.Plan(context).Intent,
				"A healthy NPC must not flee.");
		}

		[Test]
		public void Plan_ZeroFleeThreshold_NeverFlees()
		{
			// This is the shape a Determined / Berserker / Rampaging personality produces:
			// EffectiveRetreatHealthThreshold is 0, so the flee branch is unreachable.
			AICombatContext context = Melee(2f);
			context.CanFlee = true;
			context.FleeHealthThreshold = 0f;
			context.HealthPercent = 0.01f;

			Assert.AreNotEqual(AICombatIntent.Flee, AICombatDecision.Plan(context).Intent,
				"A fearless archetype must fight on at 1% health.");
		}

		[Test]
		public void Plan_CannotFlee_DoesNotFleeEvenBelowThreshold()
		{
			// CanFlee is false when no RetreatState is wired. The NPC must keep fighting rather
			// than issue an intent nobody can carry out.
			AICombatContext context = Melee(2f);
			context.CanFlee = false;
			context.FleeHealthThreshold = 0.9f;
			context.HealthPercent = 0.1f;

			Assert.AreNotEqual(AICombatIntent.Flee, AICombatDecision.Plan(context).Intent,
				"Without a retreat state there is nothing to flee into.");
		}

		// --- Spacing -----------------------------------------------------------------------

		[Test]
		public void Plan_InsidePanicRadius_EmergencyRetreats()
		{
			// Comfort 5, threshold 0.5 → panic inside 2.5 m.
			AICombatContext context = Ranged(2.0f);

			AICombatPlan plan = AICombatDecision.Plan(context);

			Assert.AreEqual(AICombatIntent.EmergencyRetreat, plan.Intent);
			Assert.AreEqual(15f, plan.DesiredDistance, 0.001f,
				"An emergency retreat should aim for the archetype's working distance.");
		}

		[Test]
		public void Plan_InKitingBandWithAbilityInRange_BacksAwayAndFires()
		{
			AICombatContext context = Ranged(4.0f);   // between panic (2.5) and comfort (5)
			context.HasUsableAbility = true;
			context.AbilityRange = 20f;

			AICombatPlan plan = AICombatDecision.Plan(context);

			Assert.AreEqual(AICombatIntent.BackAway, plan.Intent);
			Assert.IsTrue(plan.FireWhileMoving, "A kiting archer shoots on the way out.");
		}

		[Test]
		public void Plan_InKitingBandWithAbilityOutOfRange_BacksAwayWithoutFiring()
		{
			AICombatContext context = Ranged(4.0f);
			context.HasUsableAbility = true;
			context.AbilityRange = 2f;   // shorter than the current distance

			AICombatPlan plan = AICombatDecision.Plan(context);

			Assert.AreEqual(AICombatIntent.BackAway, plan.Intent);
			Assert.IsFalse(plan.FireWhileMoving, "Do not fire an ability that cannot reach.");
		}

		[Test]
		public void Plan_EmergencyRetreat_NeverFiresWhileMoving()
		{
			AICombatContext context = Ranged(1.0f);
			context.HasUsableAbility = true;
			context.AbilityRange = 30f;

			AICombatPlan plan = AICombatDecision.Plan(context);

			Assert.AreEqual(AICombatIntent.EmergencyRetreat, plan.Intent);
			Assert.IsFalse(plan.FireWhileMoving,
				"An emergency retreat interrupts the cast; it must not also request one.");
		}

		[Test]
		public void Plan_NoComfortDistance_NeverBacksAway()
		{
			// A melee archetype standing on top of its target must not decide to retreat.
			AICombatContext context = Melee(0.1f);
			context.HasUsableAbility = true;
			context.AbilityRange = 2f;

			AICombatPlan plan = AICombatDecision.Plan(context);

			Assert.AreEqual(AICombatIntent.Attack, plan.Intent);
		}

		// --- Attacking and approach --------------------------------------------------------

		[Test]
		public void Plan_AbilityInRange_Attacks()
		{
			AICombatContext context = Ranged(12f);
			context.HasUsableAbility = true;
			context.AbilityRange = 20f;

			Assert.AreEqual(AICombatIntent.Attack, AICombatDecision.Plan(context).Intent);
		}

		[Test]
		public void Plan_AbilityOutOfRange_ClosesButNotPastPreferredDistance()
		{
			// A caster with a long nuke must stop at its preferred distance rather than walk to
			// 90% of the spell's range and end up in melee.
			AICombatContext context = Ranged(40f);
			context.HasUsableAbility = true;
			context.AbilityRange = 30f;

			AICombatPlan plan = AICombatDecision.Plan(context);

			Assert.AreEqual(AICombatIntent.CloseDistance, plan.Intent);
			Assert.AreEqual(15f, plan.DesiredDistance, 0.001f,
				"Approach must clamp to the preferred distance, not 0.9 * ability range.");
		}

		[Test]
		public void Plan_MeleeAbilityOutOfRange_ClosesAllTheWay()
		{
			// A melee archetype has no preferred distance, so it is free to close to 90% of reach.
			AICombatContext context = Melee(6f);
			context.HasUsableAbility = true;
			context.AbilityRange = 2f;

			AICombatPlan plan = AICombatDecision.Plan(context);

			Assert.AreEqual(AICombatIntent.CloseDistance, plan.Intent);
			Assert.AreEqual(1.8f, plan.DesiredDistance, 0.001f);
		}

		[Test]
		public void Plan_ApproachDistanceIsNeverZero()
		{
			// A zero approach would send the NPC into the target's collider and produce a
			// degenerate zero-radius NavMesh sample.
			AICombatContext context = Melee(6f);
			context.HasUsableAbility = true;
			context.AbilityRange = 0f;

			AICombatPlan plan = AICombatDecision.Plan(context);

			Assert.Greater(plan.DesiredDistance, 0f);
		}

		// --- Nothing to cast ---------------------------------------------------------------

		[Test]
		public void Plan_NoAbilityAndAtSpacing_HoldsPosition()
		{
			AICombatContext context = Ranged(15f);
			context.HasUsableAbility = false;

			Assert.AreEqual(AICombatIntent.HoldPosition, AICombatDecision.Plan(context).Intent,
				"With everything on cooldown the NPC should hold its spacing, not shuffle.");
		}

		[Test]
		public void Plan_NoAbilityAndTooFar_ClosesToSpacing()
		{
			AICombatContext context = Ranged(40f);
			context.HasUsableAbility = false;

			AICombatPlan plan = AICombatDecision.Plan(context);

			Assert.AreEqual(AICombatIntent.CloseDistance, plan.Intent);
			Assert.AreEqual(15f, plan.DesiredDistance, 0.001f);
		}

		[Test]
		public void Plan_NoAbilityJustOutsideSpacing_HoldsRatherThanOscillating()
		{
			// Inside the engagement slack the NPC must not keep issuing move orders.
			AICombatContext context = Ranged(15f * AICombatDecision.ENGAGE_SLACK - 0.01f);
			context.HasUsableAbility = false;

			Assert.AreEqual(AICombatIntent.HoldPosition, AICombatDecision.Plan(context).Intent);
		}

		// --- Engagement distance -----------------------------------------------------------

		[Test]
		public void ResolveEngageDistance_MeleeArchetypeUsesMeleeReach()
		{
			AICombatContext context = Melee(1f);
			context.MeleeReach = 2.5f;

			Assert.AreEqual(2.5f, AICombatDecision.ResolveEngageDistance(context), 0.001f);
		}

		[Test]
		public void ResolveEngageDistance_NeverReturnsZero()
		{
			AICombatContext context = Melee(1f);
			context.MeleeReach = 0f;

			Assert.Greater(AICombatDecision.ResolveEngageDistance(context), 0f);
		}
	}
}
