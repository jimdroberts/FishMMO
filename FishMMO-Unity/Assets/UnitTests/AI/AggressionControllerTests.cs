using NUnit.Framework;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs for the NPC threat table. Plain C#, no scene required.
	/// </summary>
	[TestFixture]
	public class AggressionControllerTests
	{
		private const long ATTACKER = 1001L;
		private const long BYSTANDER = 1002L;

		private AggressionController controller;

		/// <summary>
		/// Builds a fresh table with decay off so the tests measure recorded threat rather than
		/// wall-clock timing.
		/// </summary>
		[SetUp]
		public void SetUp()
		{
			controller = new AggressionController
			{
				DamageWeight = 1.0f,
				HitBonusPoints = 5.0f,
				DecayRate = 0f,
				TargetVarietyChance = 0f,
			};
		}

		[Test]
		public void NewTable_HasNoAggression()
		{
			Assert.IsFalse(controller.HasAggression);
			Assert.AreEqual(0, controller.Count);
		}

		[Test]
		public void RecordDamage_TracksTheAttacker()
		{
			controller.RecordDamage(ATTACKER, 20);

			Assert.IsTrue(controller.HasAggression);
			Assert.AreEqual(25f, controller.GetPoints(ATTACKER), 0.001f,
				"20 damage at weight 1 plus the flat 5-point hit bonus.");
		}

		[Test]
		public void GetPoints_ForAnUntrackedCharacter_DoesNotCreateAnEntry()
		{
			controller.GetPoints(BYSTANDER);

			Assert.IsFalse(controller.HasAggression,
				"Merely asking about a character must not put it in the table.");
			Assert.AreEqual(0, controller.Count);
		}

		[Test]
		public void RemoveEntry_ForAnUntrackedCharacter_DoesNotCreateAnEntry()
		{
			/* This is the regression that matters. The kill handler is a *global* event: it fires
			 * for every death anywhere in the scene. It used to clear threat by calling
			 * AddPoints(victim, -99999), which routes through GetOrCreate — so every unrelated
			 * kill inserted a zero-point entry into every NPC's table. HasAggression then read
			 * true for an NPC nothing had ever touched, and, worse, the empty-to-non-empty edge
			 * that drives event-driven combat entry had already been consumed, so the NPC's first
			 * real hit no longer pulled it into combat. */
			bool removed = controller.RemoveEntry(BYSTANDER);

			Assert.IsFalse(removed);
			Assert.IsFalse(controller.HasAggression,
				"Forgetting an untracked character must not track it.");
			Assert.AreEqual(0, controller.Count);
		}

		[Test]
		public void RemoveEntry_ForATrackedCharacter_ForgetsIt()
		{
			controller.RecordDamage(ATTACKER, 20);
			controller.RecordDamage(BYSTANDER, 5);

			Assert.IsTrue(controller.RemoveEntry(ATTACKER));

			Assert.AreEqual(0f, controller.GetPoints(ATTACKER), 0.001f);
			Assert.AreEqual(1, controller.Count, "The other aggressor must be untouched.");
			Assert.IsTrue(controller.HasAggression);
		}

		[Test]
		public void RemoveEntry_LastTrackedCharacter_EmptiesTheTable()
		{
			controller.RecordDamage(ATTACKER, 20);
			controller.RemoveEntry(ATTACKER);

			Assert.IsFalse(controller.HasAggression,
				"An emptied table must report empty, so the next first-hit re-enters combat.");
		}

		[Test]
		public void ShouldSwitchTarget_RequiresADecisiveLead()
		{
			controller.RecordDamage(ATTACKER, 100);    // 105 points
			controller.RecordDamage(BYSTANDER, 100);   // 105 points

			Assert.IsFalse(controller.ShouldSwitchTarget(ATTACKER, BYSTANDER, 50f),
				"An equal threat must not steal the target.");

			controller.RecordDamage(BYSTANDER, 100);   // 210 points

			Assert.IsTrue(controller.ShouldSwitchTarget(ATTACKER, BYSTANDER, 50f),
				"A lead beyond the threshold must steal the target.");
		}

		[Test]
		public void AddPoints_NeverGoesNegative()
		{
			controller.RecordDamage(ATTACKER, 10);
			controller.AddPoints(ATTACKER, -99999f);

			Assert.AreEqual(0f, controller.GetPoints(ATTACKER), 0.001f);
		}

		[Test]
		public void Clear_EmptiesTheTable()
		{
			controller.RecordDamage(ATTACKER, 20);
			controller.RecordDamage(BYSTANDER, 20);

			controller.Clear();

			Assert.IsFalse(controller.HasAggression);
			Assert.AreEqual(0, controller.Count);
		}

		[Test]
		public void Tick_DecaysThreatAndFloorsAtZero()
		{
			controller.DecayRate = 10f;
			controller.StaleEntryTimeout = 30f;

			controller.RecordDamage(ATTACKER, 20);   // 25 points
			controller.Tick(1f);                     // -10 → 15

			Assert.AreEqual(15f, controller.GetPoints(ATTACKER), 0.001f);

			controller.Tick(10f);                    // -100, floored

			Assert.AreEqual(0f, controller.GetPoints(ATTACKER), 0.001f,
				"Threat must floor at zero rather than going negative.");
			Assert.AreEqual(1, controller.Count,
				"A drained entry stays tracked until its stale timeout elapses.");
		}

		[Test]
		public void Tick_PrunesEntriesThatHaveBeenDrainedPastTheStaleTimeout()
		{
			controller.DecayRate = 100f;
			controller.StaleEntryTimeout = 30f;

			controller.RecordDamage(ATTACKER, 20);

			/* Pruning is gated on wall-clock time since the last threat event, which does not
			 * advance inside a single EditMode test frame. Back-dating the entry is what lets the
			 * real prune path run rather than asserting against a contrived timeout. */
			AggressionEntry entry = controller.GetEntry(ATTACKER);
			Assert.IsNotNull(entry);
			entry.LastEventTime = UnityEngine.Time.time - 120f;

			controller.Tick(1f);

			Assert.AreEqual(0, controller.Count,
				"A drained entry past its stale timeout should be pruned.");
			Assert.IsFalse(controller.HasAggression);
		}
	}
}
