using NUnit.Framework;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the rules that stop a fleeing NPC running forever (issue #262).
	/// </summary>
	/// <remarks>
	/// The reported behaviour was an NPC that stayed in a retreat state for as long as a player chased
	/// it. The rules below are what make a chase end: distance alone no longer counts as "done" while
	/// something is still following, the cornered case gives up, and the re-engagement chance climbs
	/// with every retreat so a long chase converges on a fight.
	/// </remarks>
	[TestFixture]
	public class AIRetreatDecisionTests
	{
		/// <summary>
		/// The context the shipped <c>Combat Flee State</c> asset produces, standing still at a
		/// distance well inside its 30 m safe radius, with a roll that could go either way.
		/// </summary>
		/// <returns>A context that keeps retreating unless a test says otherwise.</returns>
		private static AIRetreatContext Context()
		{
			return new AIRetreatContext
			{
				SqrDistanceToTarget = 10f * 10f,
				SafeDistance = 30f,
				TargetClosingSpeed = 0f,
				PathBlocked = false,
				OutOfPatience = false,
				CumulativeCapSpent = false,
				SecondsRetreating = 5f,
				MinRetreatSeconds = 1.5f,
				ConsecutiveRetreats = 1,
				ReengageChance = 0.15f,
				ReengageChanceRamp = 0.2f,
				ReengageRoll = 1f,
			};
		}

		/// <summary>A context that has already outrun everything and is clear of the danger band.</summary>
		/// <returns>A context at twice the safe distance.</returns>
		private static AIRetreatContext Safe()
		{
			AIRetreatContext context = Context();
			context.SqrDistanceToTarget = 100f * 100f;
			return context;
		}

		// --- The reported issue -----------------------------------------------------------------

		[Test]
		public void ClearOfTheTarget_ButStillBeingChased_KeepsRetreating()
		{
			AIRetreatContext context = Safe();
			context.TargetClosingSpeed = 4f;

			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.KeepRetreating),
				"being far away only means the NPC is done running if nobody is following it");
		}

		[Test]
		public void ClearOfTheTarget_AndNotBeingChased_Disengages()
		{
			AIRetreatContext context = Safe();
			context.TargetClosingSpeed = 0f;

			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.Disengage));
		}

		[Test]
		public void AStrollingTarget_IsNotAPursuit()
		{
			/* NavMesh avoidance between two bodies shuffling near each other produces a small closing
			 * speed in any direction. Treating that as a pursuit would mean no retreat ever ended. */
			AIRetreatContext context = Safe();
			context.TargetClosingSpeed = AIRetreatDecision.PURSUIT_CLOSING_SPEED;

			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.Disengage));
		}

		[Test]
		public void ATargetMovingAway_IsNotAPursuit()
		{
			AIRetreatContext context = Safe();
			context.TargetClosingSpeed = -3f;

			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.Disengage));
		}

		[Test]
		public void ASafeDistanceOfZero_NeverGatesOnDistance()
		{
			// 0 is "no safe distance configured", so the distance test is skipped entirely rather
			// than the NPC declaring itself safe the moment it is 0 m away.
			AIRetreatContext context = Context();
			context.SafeDistance = 0f;
			context.TargetClosingSpeed = 0f;

			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.Disengage));
		}

		// --- The hard stops ---------------------------------------------------------------------

		[Test]
		public void OutOfPatience_TurnsAndFights_EvenWhileClearAndChased()
		{
			AIRetreatContext context = Safe();
			context.TargetClosingSpeed = 8f;
			context.OutOfPatience = true;

			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.TurnAndFight),
				"a chase that cannot end is worse than one that ends too early");
		}

		[Test]
		public void CumulativeCapSpent_TurnsAndFights()
		{
			AIRetreatContext context = Safe();
			context.TargetClosingSpeed = 8f;
			context.CumulativeCapSpent = true;

			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.TurnAndFight));
		}

		[Test]
		public void Cornered_TurnsAndFights()
		{
			AIRetreatContext context = Context();
			context.PathBlocked = true;

			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.TurnAndFight),
				"nothing is achieved by staying in a retreat state against a wall");
		}

		// --- The minimum leg --------------------------------------------------------------------

		[Test]
		public void BeforeTheMinimumLeg_DoesNotTurnBack_EvenOnACertainRoll()
		{
			AIRetreatContext context = Context();
			context.SecondsRetreating = 0.5f;
			context.MinRetreatSeconds = 1.5f;
			context.ReengageRoll = 0f;

			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.KeepRetreating),
				"a roll that fires on the first tick makes the NPC stutter on the spot instead of leaving");
		}

		[Test]
		public void PastTheMinimumLeg_TheRollDecides()
		{
			AIRetreatContext context = Context();
			context.SecondsRetreating = 2f;

			context.ReengageRoll = 0f;
			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.TurnAndFight));

			context.ReengageRoll = 1f;
			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.KeepRetreating));
		}

		// --- The ramp ---------------------------------------------------------------------------

		[Test]
		public void TheRollIsTestedAgainstTheChance_NotTheOtherWayRound()
		{
			AIRetreatContext context = Context();
			context.ReengageChance = 0.4f;
			context.ReengageChanceRamp = 0f;
			context.ConsecutiveRetreats = 0;

			context.ReengageRoll = 0.39f;
			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.TurnAndFight));

			context.ReengageRoll = 0.41f;
			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.KeepRetreating));
		}

		[Test]
		public void TheRamp_MakesALongChaseEndInAFight()
		{
			/* The guarantee the issue asks for. A flat chance can lose the same roll forever; a chance
			 * that climbs with each retreat already taken converges on certainty, so an NPC chased
			 * across the zone always eventually stops and turns. */
			AIRetreatContext context = Context();
			context.ReengageRoll = 0.9f;

			int turnedAt = 0;
			for (int retreats = 1; retreats <= 10; retreats++)
			{
				context.ConsecutiveRetreats = retreats;
				if (AIRetreatDecision.Decide(context) == AIRetreatOutcome.TurnAndFight)
				{
					turnedAt = retreats;
					break;
				}
			}

			Assert.That(turnedAt, Is.GreaterThan(0).And.LessThanOrEqualTo(10),
				"a roll of 0.9 must eventually be beaten by the ramp");
		}

		[Test]
		public void AnUnbeatableRoll_StillEndsAtTheHardStops()
		{
			// The ramp is not the only backstop: an NPC whose rolls never land still turns when the
			// cumulative cap for the fight is spent.
			AIRetreatContext context = Context();
			context.ReengageRoll = 0.999999f;
			context.ConsecutiveRetreats = 100;
			context.CumulativeCapSpent = true;

			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.TurnAndFight));
		}

		[Test]
		public void AZeroChanceAndNoRamp_NeverTurnsBack()
		{
			// The escape hatch: this is exactly the old behaviour, so an archetype that should keep
			// fleeing — a pet ordered to run — can be dialled back on its own asset.
			AIRetreatContext context = Context();
			context.ReengageChance = 0f;
			context.ReengageChanceRamp = 0f;
			context.ConsecutiveRetreats = 50;
			context.ReengageRoll = 0f;

			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.KeepRetreating));
		}

		[Test]
		public void AChanceAboveOne_IsClamped()
		{
			AIRetreatContext context = Context();
			context.ReengageChance = 1f;
			context.ReengageChanceRamp = 1f;
			context.ConsecutiveRetreats = 10;
			context.ReengageRoll = 0.99999f;

			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.TurnAndFight));
		}

		// --- The shipped numbers ----------------------------------------------------------------

		[Test]
		public void TheShippedFleeState_ConvergesWithinAFewRetreats()
		{
			/* The values authored on Combat Flee State — 0.15 base, 0.2 ramp. Checked here so a later
			 * retune of that asset has to be deliberate: with a roll of 0.9 the NPC must have turned by
			 * its fourth retreat. */
			AIRetreatContext context = Context();
			context.ReengageRoll = 0.9f;
			context.ConsecutiveRetreats = 1;
			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.KeepRetreating));

			context.ConsecutiveRetreats = 2;
			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.KeepRetreating));

			context.ConsecutiveRetreats = 3;
			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.KeepRetreating));

			context.ConsecutiveRetreats = 4;
			Assert.That(AIRetreatDecision.Decide(context), Is.EqualTo(AIRetreatOutcome.TurnAndFight));
		}
	}
}
