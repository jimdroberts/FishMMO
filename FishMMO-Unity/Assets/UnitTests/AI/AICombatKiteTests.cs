using NUnit.Framework;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the two rules that make a kiting archetype fightable in melee (issue #220): spacing is
	/// fitted to what the NPC's abilities can reach, and backing away is budgeted.
	/// </summary>
	[TestFixture]
	public class AICombatKiteTests
	{
		private static AICombatContext Caster(float distance)
		{
			return new AICombatContext
			{
				Distance = distance,
				PreferredDistance = 22f,
				MinComfortDistance = 10f,
				EmergencyRetreatThreshold = 0.4f,
				HasUsableAbility = true,
				AbilityRange = 25f,
				MeleeReach = 1f,
				HealthPercent = 1f,
			};
		}

		// --- Spacing fitted to reach ---------------------------------------------------------

		[Test]
		public void CasterSpacingOnAMeleeKit_ClosesInsteadOfHoldingRange()
		{
			// The orc mage: Caster archetype (22 / 10) with one 1.25 m ability.
			AICombatDecision.ResolveSpacing(22f, 10f, 1.25f, out float preferred, out float comfort);

			Assert.That(preferred, Is.EqualTo(1.25f));
			Assert.That(comfort, Is.EqualTo(0f), "a comfort distance you cannot attack from is not kiting");
		}

		[Test]
		public void CasterSpacingOnARangedKit_IsKeptAsAuthored()
		{
			AICombatDecision.ResolveSpacing(22f, 10f, 30f, out float preferred, out float comfort);

			Assert.That(preferred, Is.EqualTo(22f));
			Assert.That(comfort, Is.EqualTo(10f));
		}

		[Test]
		public void ComfortJustUnderTheLongestReach_Survives_ButPreferredIsCapped()
		{
			AICombatDecision.ResolveSpacing(22f, 10f, 12f, out float preferred, out float comfort);

			Assert.That(preferred, Is.EqualTo(12f));
			Assert.That(comfort, Is.EqualTo(10f));
		}

		[Test]
		public void NoAbilitiesKnown_LeavesSpacingAlone()
		{
			AICombatDecision.ResolveSpacing(22f, 10f, 0f, out float preferred, out float comfort);

			Assert.That(preferred, Is.EqualTo(22f));
			Assert.That(comfort, Is.EqualTo(10f));
		}

		[Test]
		public void MeleeSpacing_IsUnaffected()
		{
			AICombatDecision.ResolveSpacing(0f, 0f, 1.75f, out float preferred, out float comfort);

			Assert.That(preferred, Is.EqualTo(0f));
			Assert.That(comfort, Is.EqualTo(0f));
		}

		// --- Kite budget in the planner --------------------------------------------------------

		[Test]
		public void InsideComfort_WithBudget_BacksAway()
		{
			AICombatContext context = Caster(6f);
			Assert.That(AICombatDecision.Plan(context).Intent, Is.EqualTo(AICombatIntent.BackAway));
		}

		[Test]
		public void InsideComfort_BudgetSpent_StandsAndAttacks()
		{
			AICombatContext context = Caster(6f);
			context.KiteExhausted = true;

			Assert.That(AICombatDecision.Plan(context).Intent, Is.EqualTo(AICombatIntent.Attack));
		}

		[Test]
		public void InsidePanicRadius_BudgetSpent_DoesNotEmergencyRetreatEither()
		{
			AICombatContext context = Caster(2f);
			context.KiteExhausted = true;

			Assert.That(AICombatDecision.Plan(context).Intent, Is.EqualTo(AICombatIntent.Attack));
		}

		[Test]
		public void BudgetSpent_StillFleesAtTheHealthThreshold()
		{
			AICombatContext context = Caster(2f);
			context.KiteExhausted = true;
			context.CanFlee = true;
			context.FleeHealthThreshold = 0.3f;
			context.HealthPercent = 0.2f;

			Assert.That(AICombatDecision.Plan(context).Intent, Is.EqualTo(AICombatIntent.Flee));
		}

		// --- The budget itself ---------------------------------------------------------------

		[Test]
		public void Budget_DrainsWhileKiting_ThenHolds_ThenRefills()
		{
			AIKiteBudget budget = default;
			budget.Reset(2.5f);

			// 2.5 s of kiting at 1 s per combat update.
			budget.Tick(true, 1f, 2.5f, 5f);
			budget.Tick(true, 1f, 2.5f, 5f);
			Assert.That(budget.Exhausted, Is.False, "still has half a second");
			budget.Tick(true, 1f, 2.5f, 5f);
			Assert.That(budget.Exhausted, Is.True);

			// Kiting or not, the hold lasts the recovery time.
			for (int i = 0; i < 4; i++)
			{
				budget.Tick(true, 1f, 2.5f, 5f);
				Assert.That(budget.Exhausted, Is.True, $"hold must last 5 s, broke after {i + 1}");
			}
			budget.Tick(true, 1f, 2.5f, 5f);
			Assert.That(budget.Exhausted, Is.False);
			Assert.That(budget.Remaining, Is.EqualTo(2.5f));
		}

		[Test]
		public void Budget_RefundsSlowlyWhileStanding()
		{
			AIKiteBudget budget = default;
			budget.Reset(2.5f);
			budget.Tick(true, 2f, 2.5f, 5f);
			Assert.That(budget.Remaining, Is.EqualTo(0.5f).Within(1e-5f));

			budget.Tick(false, 1f, 2.5f, 5f);
			Assert.That(budget.Remaining, Is.EqualTo(0.5f + AIKiteBudget.REFUND_RATE).Within(1e-5f));

			budget.Tick(false, 100f, 2.5f, 5f);
			Assert.That(budget.Remaining, Is.EqualTo(2.5f), "never above the budget");
		}

		[Test]
		public void Budget_ZeroDisablesIt()
		{
			AIKiteBudget budget = default;
			budget.Tick(true, 100f, 0f, 5f);
			Assert.That(budget.Exhausted, Is.False);
		}

		[Test]
		public void Budget_UnprimedTickPrimesItself()
		{
			// A pooled NPC whose attacking state was entered before Kite existed still gets a budget.
			AIKiteBudget budget = default;
			budget.Tick(true, 1f, 2.5f, 5f);
			Assert.That(budget.Remaining, Is.EqualTo(1.5f).Within(1e-5f));
		}
	}
}
