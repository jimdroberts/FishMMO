using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs for the personality styles that give the shipped archetypes their character:
	/// a "pathetic" enemy must run, a "determined" or "raging" one must not, and a raging one
	/// must be impossible to hold with threat.
	/// </summary>
	[TestFixture]
	public class AICombatPersonalityTests
	{
		private AICombatPersonality personality;

		/// <summary>
		/// Creates a throwaway in-memory personality for each test.
		/// </summary>
		[SetUp]
		public void SetUp()
		{
			personality = ScriptableObject.CreateInstance<AICombatPersonality>();
		}

		/// <summary>
		/// Destroys the throwaway asset so EditMode runs do not leak ScriptableObjects.
		/// </summary>
		[TearDown]
		public void TearDown()
		{
			if (personality != null)
			{
				Object.DestroyImmediate(personality);
				personality = null;
			}
		}

		// --- Pathetic ----------------------------------------------------------------------

		[Test]
		public void Pathetic_WithConfiguredThreshold_RetreatsAtIt()
		{
			personality.Style = NPCCombatStyle.Pathetic;
			personality.RetreatHealthThreshold = 0.45f;

			Assert.IsTrue(personality.ShouldRetreat(0.45f), "Must retreat at the threshold.");
			Assert.IsTrue(personality.ShouldRetreat(0.10f), "Must still be retreating below it.");
			Assert.IsFalse(personality.ShouldRetreat(0.90f), "Must not retreat while healthy.");
		}

		[Test]
		public void Pathetic_WithUnsetThreshold_StillRetreats()
		{
			/* The point of the style guard. A designer who picks "Pathetic" and leaves the
			 * numeric field at its zero default has stated an intent; without the fallback the
			 * asset silently produces a fearless enemy that never runs. */
			personality.Style = NPCCombatStyle.Pathetic;
			personality.RetreatHealthThreshold = 0f;

			Assert.AreEqual(AICombatPersonality.PATHETIC_DEFAULT_RETREAT_THRESHOLD,
				personality.EffectiveRetreatHealthThreshold, 0.0001f);
			Assert.IsTrue(personality.ShouldRetreat(0.2f),
				"A Pathetic personality must never be silently fearless.");
		}

		// --- Fearless styles ---------------------------------------------------------------

		[Test]
		public void Determined_NeverRetreats()
		{
			personality.Style = NPCCombatStyle.Determined;
			personality.RetreatHealthThreshold = 0.9f;   // deliberately set, and must be ignored

			Assert.IsTrue(personality.IsFearless);
			Assert.AreEqual(0f, personality.EffectiveRetreatHealthThreshold);
			Assert.IsFalse(personality.ShouldRetreat(0.01f),
				"A determined enemy fights to the last point of health.");
		}

		[Test]
		public void Berserker_NeverRetreats()
		{
			personality.Style = NPCCombatStyle.Berserker;
			personality.RetreatHealthThreshold = 0.9f;

			Assert.IsFalse(personality.ShouldRetreat(0.01f));
		}

		[Test]
		public void Rampaging_NeverRetreats()
		{
			personality.Style = NPCCombatStyle.Rampaging;
			personality.RetreatHealthThreshold = 0.9f;

			Assert.IsFalse(personality.ShouldRetreat(0.01f));
		}

		// --- Targeting ---------------------------------------------------------------------

		[Test]
		public void Rampaging_ForcesRandomTargetingRegardlessOfTheSerializedValue()
		{
			personality.Style = NPCCombatStyle.Rampaging;
			personality.Targeting = AITargetingMode.Threat;

			Assert.AreEqual(AITargetingMode.Random, personality.TargetingMode,
				"A rampaging enemy must be impossible to hold with threat.");
		}

		[Test]
		public void Rampaging_RetargetsMidCombat()
		{
			personality.Style = NPCCombatStyle.Rampaging;
			personality.RampageRetargetChance = 0.6f;

			Assert.AreEqual(0.6f, personality.EffectiveRetargetChance, 0.0001f);
		}

		[Test]
		public void NonRampaging_NeverRetargetsAtRandom()
		{
			personality.Style = NPCCombatStyle.Aggressive;
			personality.RampageRetargetChance = 0.9f;

			Assert.AreEqual(0f, personality.EffectiveRetargetChance,
				"Only a rampaging style abandons its target at random.");
		}

		[Test]
		public void NonRampaging_HonoursItsSerializedTargetingMode()
		{
			personality.Style = NPCCombatStyle.Cautious;
			personality.Targeting = AITargetingMode.Weakest;

			Assert.AreEqual(AITargetingMode.Weakest, personality.TargetingMode);
		}

		// --- Scoring -----------------------------------------------------------------------

		[Test]
		public void GetBonusScore_FearlessStyle_NeverAwardsTheLowHealthSupportBonus()
		{
			/* The low-health support bonus is keyed off the retreat threshold. A fearless style
			 * has no threshold, so a berserker must not start favouring defensive abilities the
			 * moment it is hurt. */
			personality.Style = NPCCombatStyle.Berserker;
			personality.RetreatHealthThreshold = 0.5f;
			personality.LowHealthSupportBonus = 500f;
			personality.HealthyAggressionBonus = 0f;

			Assert.AreEqual(0f, personality.GetBonusScore(null, 0.05f),
				"A fearless style must not switch to defensive abilities when hurt.");
		}
	}
}
