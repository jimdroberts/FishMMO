using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs for the combat ring that keeps several attackers from converging on one point.
	/// </summary>
	/// <remarks>
	/// The NavMeshAgent's local avoidance stops agents overlapping but has no say in where they are
	/// trying to go, so N attackers sent to one point shove each other around it forever. These
	/// cover the slot arithmetic that separates their destinations instead.
	/// </remarks>
	[TestFixture]
	public class AICombatSlotTests
	{
		private const long TARGET = 9001L;
		private const long OTHER_TARGET = 9002L;
		private const long ATTACKER_A = 8001L;
		private const long ATTACKER_B = 8002L;
		private const long ATTACKER_C = 8003L;

		private const float COMBAT_RADIUS = 2f;
		private const float AGENT_RADIUS = 0.5f;

		/// <summary>
		/// Static registry — clear it between tests so ordering cannot leak.
		/// </summary>
		[SetUp]
		public void SetUp()
		{
			AICombatSlots.Clear();
		}

		/// <summary>
		/// Leaves the registry clean for anything that runs afterwards.
		/// </summary>
		[TearDown]
		public void TearDown()
		{
			AICombatSlots.Clear();
		}

		/// <summary>
		/// Claims a slot and returns its world position.
		/// </summary>
		/// <param name="attacker">The attacking character's ID.</param>
		/// <param name="target">The target's ID.</param>
		/// <returns>The world position the attacker should stand at.</returns>
		private static Vector3 ClaimPosition(long attacker, long target)
		{
			AICombatSlots.Claim(target, attacker, COMBAT_RADIUS, AGENT_RADIUS,
				out int slot, out int ring, out int capacity);
			return AICombatSlots.GetSlotPosition(Vector3.zero, slot, ring, capacity, COMBAT_RADIUS, AGENT_RADIUS);
		}

		// --- Claiming -------------------------------------------------------------------------

		[Test]
		public void Claim_FirstAttackerTakesSlotZero()
		{
			AICombatSlots.Claim(TARGET, ATTACKER_A, COMBAT_RADIUS, AGENT_RADIUS,
				out int slot, out int ring, out _);

			Assert.AreEqual(0, slot);
			Assert.AreEqual(0, ring);
			Assert.AreEqual(1, AICombatSlots.GetAttackerCount(TARGET));
		}

		[Test]
		public void Claim_IsIdempotentForTheSameAttacker()
		{
			AICombatSlots.Claim(TARGET, ATTACKER_A, COMBAT_RADIUS, AGENT_RADIUS, out int first, out _, out _);
			AICombatSlots.Claim(TARGET, ATTACKER_A, COMBAT_RADIUS, AGENT_RADIUS, out int second, out _, out _);

			Assert.AreEqual(first, second,
				"Re-claiming must keep the same slot; a slot that moves every tick makes the NPC " +
				"chase its own destination around the ring.");
			Assert.AreEqual(1, AICombatSlots.GetAttackerCount(TARGET));
		}

		[Test]
		public void Claim_GivesDistinctSlotsToDistinctAttackers()
		{
			AICombatSlots.Claim(TARGET, ATTACKER_A, COMBAT_RADIUS, AGENT_RADIUS, out int a, out _, out _);
			AICombatSlots.Claim(TARGET, ATTACKER_B, COMBAT_RADIUS, AGENT_RADIUS, out int b, out _, out _);

			Assert.AreNotEqual(a, b);
			Assert.AreEqual(2, AICombatSlots.GetAttackerCount(TARGET));
		}

		[Test]
		public void Claim_SwitchingTargetsReleasesTheOldSlot()
		{
			AICombatSlots.Claim(TARGET, ATTACKER_A, COMBAT_RADIUS, AGENT_RADIUS, out _, out _, out _);
			AICombatSlots.Claim(OTHER_TARGET, ATTACKER_A, COMBAT_RADIUS, AGENT_RADIUS, out _, out _, out _);

			Assert.AreEqual(0, AICombatSlots.GetAttackerCount(TARGET),
				"A phantom occupant left on the old target inflates its ring and pushes real " +
				"attackers out to an unnecessary outer rank.");
			Assert.AreEqual(1, AICombatSlots.GetAttackerCount(OTHER_TARGET));
		}

		[Test]
		public void Claim_IgnoresZeroIdentifiers()
		{
			// An unspawned character has ID 0; it must not occupy a slot.
			AICombatSlots.Claim(0, ATTACKER_A, COMBAT_RADIUS, AGENT_RADIUS, out _, out _, out _);
			AICombatSlots.Claim(TARGET, 0, COMBAT_RADIUS, AGENT_RADIUS, out _, out _, out _);

			Assert.AreEqual(0, AICombatSlots.GetAttackerCount(TARGET));
		}

		// --- Releasing ------------------------------------------------------------------------

		[Test]
		public void Release_FreesTheSlotAndClosesTheRing()
		{
			AICombatSlots.Claim(TARGET, ATTACKER_A, COMBAT_RADIUS, AGENT_RADIUS, out _, out _, out _);
			AICombatSlots.Claim(TARGET, ATTACKER_B, COMBAT_RADIUS, AGENT_RADIUS, out _, out _, out _);
			AICombatSlots.Claim(TARGET, ATTACKER_C, COMBAT_RADIUS, AGENT_RADIUS, out int cBefore, out _, out _);

			AICombatSlots.Release(ATTACKER_A);

			AICombatSlots.Claim(TARGET, ATTACKER_C, COMBAT_RADIUS, AGENT_RADIUS, out int cAfter, out _, out _);

			Assert.AreEqual(2, AICombatSlots.GetAttackerCount(TARGET));
			Assert.Less(cAfter, cBefore,
				"When a front-rank attacker dies the ones behind should close up, not leave a hole.");
		}

		[Test]
		public void Release_OfAnUnknownAttackerIsHarmless()
		{
			Assert.DoesNotThrow(() => AICombatSlots.Release(ATTACKER_A));
			Assert.DoesNotThrow(() => AICombatSlots.Release(0));
		}

		[Test]
		public void ReleaseTarget_ClearsEveryAttacker()
		{
			AICombatSlots.Claim(TARGET, ATTACKER_A, COMBAT_RADIUS, AGENT_RADIUS, out _, out _, out _);
			AICombatSlots.Claim(TARGET, ATTACKER_B, COMBAT_RADIUS, AGENT_RADIUS, out _, out _, out _);

			AICombatSlots.ReleaseTarget(TARGET);

			Assert.AreEqual(0, AICombatSlots.GetAttackerCount(TARGET));

			// The reverse lookup must be cleared too, or a later Release would resurrect the ring.
			AICombatSlots.Release(ATTACKER_A);
			Assert.AreEqual(0, AICombatSlots.GetAttackerCount(TARGET));
		}

		// --- Geometry -------------------------------------------------------------------------

		[Test]
		public void RingCapacity_GrowsWithTheRing()
		{
			int tight = AICombatSlots.GetRingCapacity(1f, AGENT_RADIUS);
			int wide = AICombatSlots.GetRingCapacity(6f, AGENT_RADIUS);

			Assert.Greater(wide, tight,
				"A larger combat radius has more circumference and must fit more attackers.");
		}

		[Test]
		public void RingCapacity_ShrinksAsAgentsGetLarger()
		{
			int small = AICombatSlots.GetRingCapacity(COMBAT_RADIUS, 0.25f);
			int large = AICombatSlots.GetRingCapacity(COMBAT_RADIUS, 1.5f);

			Assert.Greater(small, large,
				"Bigger agents take more of the ring, so fewer fit.");
		}

		[Test]
		public void RingCapacity_IsAlwaysAtLeastOne()
		{
			// Degenerate inputs must not produce a zero and a division by zero downstream.
			Assert.GreaterOrEqual(AICombatSlots.GetRingCapacity(0f, 0f), 1);
			Assert.GreaterOrEqual(AICombatSlots.GetRingCapacity(-5f, -5f), 1);
		}

		[Test]
		public void SlotPositions_AreSeparatedByAtLeastAnAgentDiameter()
		{
			/* The entire point of the ring: neighbouring destinations must be far enough apart
			 * that two agents can occupy them simultaneously. If they are not, avoidance is back
			 * to resolving a conflict the destinations created. */
			int capacity = AICombatSlots.GetRingCapacity(COMBAT_RADIUS, AGENT_RADIUS);
			Assume.That(capacity, Is.GreaterThan(1));

			Vector3 first = AICombatSlots.GetSlotPosition(Vector3.zero, 0, 0, capacity, COMBAT_RADIUS, AGENT_RADIUS);
			Vector3 second = AICombatSlots.GetSlotPosition(Vector3.zero, 1, 0, capacity, COMBAT_RADIUS, AGENT_RADIUS);

			Assert.Greater(Vector3.Distance(first, second), AGENT_RADIUS * 2f,
				"Adjacent slots are closer together than the agents standing in them.");
		}

		[Test]
		public void SlotPositions_SitAtTheCombatRadiusOnTheInnerRing()
		{
			int capacity = AICombatSlots.GetRingCapacity(COMBAT_RADIUS, AGENT_RADIUS);
			Vector3 position = AICombatSlots.GetSlotPosition(Vector3.zero, 0, 0, capacity, COMBAT_RADIUS, AGENT_RADIUS);

			Assert.AreEqual(COMBAT_RADIUS, position.magnitude, 0.001f,
				"Inner-ring attackers must stand exactly at their weapon range.");
		}

		[Test]
		public void SlotPositions_PlaceOuterRingsFurtherOut()
		{
			int capacity = AICombatSlots.GetRingCapacity(COMBAT_RADIUS, AGENT_RADIUS);

			Vector3 inner = AICombatSlots.GetSlotPosition(Vector3.zero, 0, 0, capacity, COMBAT_RADIUS, AGENT_RADIUS);
			Vector3 outer = AICombatSlots.GetSlotPosition(Vector3.zero, 0, 1, capacity, COMBAT_RADIUS, AGENT_RADIUS);

			Assert.Greater(outer.magnitude, inner.magnitude,
				"Overflow attackers form a second rank rather than being squeezed into the first.");
		}

		[Test]
		public void SlotPositions_StaggerOuterRingsOffTheInnerOnes()
		{
			/* An outer attacker standing directly behind an inner one has no line to the target and
			 * is permanently blocked by its own ally. */
			int capacity = AICombatSlots.GetRingCapacity(COMBAT_RADIUS, AGENT_RADIUS);
			Assume.That(capacity, Is.GreaterThan(1));

			Vector3 inner = AICombatSlots.GetSlotPosition(Vector3.zero, 0, 0, capacity, COMBAT_RADIUS, AGENT_RADIUS);
			Vector3 outer = AICombatSlots.GetSlotPosition(Vector3.zero, 0, 1, capacity, COMBAT_RADIUS, AGENT_RADIUS);

			float innerAngle = Mathf.Atan2(inner.z, inner.x);
			float outerAngle = Mathf.Atan2(outer.z, outer.x);

			Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(innerAngle * Mathf.Rad2Deg, outerAngle * Mathf.Rad2Deg)), 1f,
				"Ring 1 shares ring 0's bearing, so the second rank stands in the first rank's shadow.");
		}

		[Test]
		public void Overflow_MovesAttackersToAnOuterRing()
		{
			int capacity = AICombatSlots.GetRingCapacity(COMBAT_RADIUS, AGENT_RADIUS);

			// Fill the inner ring exactly, then add one more.
			for (int i = 0; i < capacity; ++i)
			{
				AICombatSlots.Claim(TARGET, 7000L + i, COMBAT_RADIUS, AGENT_RADIUS, out _, out int ring, out _);
				Assert.AreEqual(0, ring, "Attacker should still fit on the inner ring.");
			}

			AICombatSlots.Claim(TARGET, 7000L + capacity, COMBAT_RADIUS, AGENT_RADIUS,
				out _, out int overflowRing, out _);

			Assert.AreEqual(1, overflowRing,
				"The attacker past capacity forms a second rank instead of overlapping the first.");
		}

		[Test]
		public void TwoAttackers_GetGenuinelyDifferentDestinations()
		{
			Vector3 a = ClaimPosition(ATTACKER_A, TARGET);
			Vector3 b = ClaimPosition(ATTACKER_B, TARGET);

			Assert.Greater(Vector3.Distance(a, b), AGENT_RADIUS * 2f,
				"Two attackers on one target must not be sent to the same metre of ground.");
		}
	}
}
