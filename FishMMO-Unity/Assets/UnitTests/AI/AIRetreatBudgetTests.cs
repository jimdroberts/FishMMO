using NUnit.Framework;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the memory that lets a chase end: the cumulative retreat cap, the hold once it is spent,
	/// and the hold imposed by choosing to turn and fight (issue #262).
	/// </summary>
	/// <remarks>
	/// This mirrors <c>AIKiteBudget</c>, which solves the identical "the NPC never stands and fights"
	/// problem for backing away. The two differ only in their refund rate — running for your life is a
	/// decision an NPC should have to earn again, so this budget refills more slowly.
	/// </remarks>
	[TestFixture]
	public class AIRetreatBudgetTests
	{
		/// <summary>Budget and recovery used unless a test overrides them.</summary>
		private const float Budget = 12f;
		private const float Recovery = 8f;

		// --- The cumulative cap -----------------------------------------------------------------

		[Test]
		public void Drained_ThenHeld_ThenRefilledToFull()
		{
			AIRetreatBudget budget = default;
			budget.Reset(2f);

			budget.Tick(true, 1f, 2f, Recovery);
			Assert.That(budget.Exhausted, Is.False, "still has a second in hand");
			Assert.That(budget.Remaining, Is.EqualTo(1f).Within(1e-5f));

			budget.Tick(true, 1f, 2f, Recovery);
			Assert.That(budget.Exhausted, Is.True, "the cap is spent");

			// The hold runs whether or not the NPC is still trying to run.
			for (int i = 0; i < 7; i++)
			{
				budget.Tick(true, 1f, 2f, Recovery);
				Assert.That(budget.Exhausted, Is.True, $"the hold must last {Recovery} s, broke after {i + 1}");
			}
			budget.Tick(true, 1f, 2f, Recovery);

			Assert.That(budget.Exhausted, Is.False);
			Assert.That(budget.Remaining, Is.EqualTo(2f).Within(1e-5f), "a spent budget refills to full");
		}

		[Test]
		public void RefundsSlowlyWhileStanding()
		{
			AIRetreatBudget budget = default;
			budget.Reset(2f);
			budget.Tick(true, 1.5f, 2f, Recovery);
			Assert.That(budget.Remaining, Is.EqualTo(0.5f).Within(1e-5f));

			budget.Tick(false, 1f, 2f, Recovery);
			Assert.That(budget.Remaining, Is.EqualTo(0.5f + AIRetreatBudget.REFUND_RATE).Within(1e-5f));

			budget.Tick(false, 100f, 2f, Recovery);
			Assert.That(budget.Remaining, Is.EqualTo(2f), "never above the budget");
		}

		[Test]
		public void RefundsMoreSlowlyThanKiting()
		{
			// Running away should be harder to earn back than a few seconds of kiting, which is what
			// makes a long fight with several retreats progressively harder to flee from.
			Assert.That(AIRetreatBudget.REFUND_RATE, Is.LessThan(AIKiteBudget.REFUND_RATE));
		}

		[Test]
		public void AZeroBudget_DisablesTheCap()
		{
			AIRetreatBudget budget = default;
			budget.Tick(true, 1000f, 0f, Recovery);

			Assert.That(budget.Exhausted, Is.False,
				"0 means unlimited retreating, which is the behaviour every archetype had before this existed");
		}

		[Test]
		public void AZeroRecovery_DoesNotLeaveTheBudgetPermanentlySpent()
		{
			AIRetreatBudget budget = default;
			budget.Reset(1f);
			budget.Tick(true, 2f, 1f, 0f);

			Assert.That(budget.Exhausted, Is.False,
				"a zero-second hold would otherwise leave nothing refusing the next retreat and no way back");
			Assert.That(budget.Remaining, Is.EqualTo(1f).Within(1e-5f));
		}

		[Test]
		public void AnUnprimedTick_PrimesItself()
		{
			// A pooled NPC whose attacking state was entered before Retreat existed still gets a budget.
			AIRetreatBudget budget = default;
			budget.Tick(true, 1f, Budget, Recovery);

			Assert.That(budget.Remaining, Is.EqualTo(Budget - 1f).Within(1e-5f));
		}

		// --- The turn-and-fight hold ------------------------------------------------------------

		[Test]
		public void ChoosingToFight_RefusesRunningForTheHold()
		{
			AIRetreatBudget budget = default;
			budget.Tick(false, 0f, Budget, Recovery);
			Assert.That(budget.Exhausted, Is.False);

			budget.NoteReengage(6f);
			Assert.That(budget.Exhausted, Is.True,
				"without the hold the attacking state re-plans next tick and hands straight back to the retreat");

			for (int i = 0; i < 5; i++)
			{
				budget.Tick(false, 1f, Budget, Recovery);
				Assert.That(budget.Exhausted, Is.True, $"the hold must last 6 s, broke after {i + 1}");
			}

			budget.Tick(false, 1f, Budget, Recovery);
			Assert.That(budget.Exhausted, Is.False);
		}

		[Test]
		public void TheHoldExpiresOnItsOwnClock_EvenWhileRunning()
		{
			AIRetreatBudget budget = default;
			budget.Reset(Budget);
			budget.NoteReengage(2f);

			budget.Tick(true, 1f, Budget, Recovery);
			Assert.That(budget.Exhausted, Is.True);
			Assert.That(budget.Remaining, Is.EqualTo(Budget - 1f).Within(1e-5f),
				"the clock runs either way — a hold that paused while retreating would never expire");

			budget.Tick(true, 1f, Budget, Recovery);
			Assert.That(budget.Exhausted, Is.False);
		}

		[Test]
		public void TheTwoHolds_AreIndependent()
		{
			// A hold imposed by choosing to fight, on top of a cap that is also spent: the flag is
			// derived from both clocks, so neither can clear the other by expiring first.
			AIRetreatBudget budget = default;
			budget.Reset(1f);
			budget.Tick(true, 2f, 1f, Recovery);
			Assert.That(budget.Exhausted, Is.True);

			budget.NoteReengage(30f);
			budget.Tick(false, Recovery + 1f, 1f, Recovery);

			Assert.That(budget.Exhausted, Is.True,
				"the cap's hold expired, but the turn-and-fight hold is still running");
		}

		// --- The streak -------------------------------------------------------------------------

		[Test]
		public void NoteRetreatStart_CountsAndArmsTheCheckTimer()
		{
			AIRetreatBudget budget = default;
			budget.NoteRetreatStart(1f);

			Assert.That(budget.ConsecutiveRetreats, Is.EqualTo(1));
			Assert.That(budget.DecisionTimer, Is.EqualTo(1f).Within(1e-5f));

			budget.Tick(false, 0.5f, Budget, Recovery);
			Assert.That(budget.DecisionTimer, Is.EqualTo(0.5f).Within(1e-5f));

			budget.NoteRetreatStart(1f);
			Assert.That(budget.ConsecutiveRetreats, Is.EqualTo(2));
		}

		[Test]
		public void Reset_KeepsTheStreak_ButClearEndsTheFight()
		{
			/* The distinction the ramp depends on. Reset runs whenever the budget refills mid-fight, so
			 * clearing the streak there would mean it never accumulated — every retreat drops the target
			 * on the way out, and Reset follows. Only a genuine end to combat clears it. */
			AIRetreatBudget budget = default;
			budget.NoteRetreatStart(1f);
			budget.NoteRetreatStart(1f);
			Assert.That(budget.ConsecutiveRetreats, Is.EqualTo(2));

			budget.Reset(Budget);
			Assert.That(budget.ConsecutiveRetreats, Is.EqualTo(2),
				"a mid-fight refill must not erase how many times the NPC has already run");

			budget.Clear();
			Assert.That(budget.ConsecutiveRetreats, Is.EqualTo(0));
			Assert.That(budget.Exhausted, Is.False);
			Assert.That(budget.DecisionTimer, Is.EqualTo(0f));
		}

		[Test]
		public void Reset_ClearsTheCapsHoldButKeepsTheChoiceHold()
		{
			/* Reset is the cap's own recovery path, so it must clear the cap's hold. The turn-and-fight
			 * hold belongs to a decision the NPC just made and has its own clock. */
			AIRetreatBudget budget = default;
			budget.Reset(1f);
			budget.Tick(true, 2f, 1f, Recovery);
			budget.NoteReengage(5f);

			budget.Reset(1f);

			Assert.That(budget.RecoveryTimer, Is.EqualTo(0f).Within(1e-5f));
			Assert.That(budget.ReengageHold, Is.EqualTo(5f).Within(1e-5f));
			Assert.That(budget.Exhausted, Is.True);
		}

		[Test]
		public void ANegativeDeltaTime_DoesNotRefund()
		{
			// AI ticks are computed from the network tick and should never be negative, but a refund
			// from a negative delta would silently hand back budget.
			AIRetreatBudget budget = default;
			budget.Reset(2f);
			budget.Tick(true, 1f, 2f, Recovery);
			budget.Tick(false, -5f, 2f, Recovery);

			Assert.That(budget.Remaining, Is.EqualTo(1f).Within(1e-5f));
		}
	}
}
