using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins how NPC brain work is spread across network ticks, and the per-tick body work a brain
	/// skips when there is nothing to do (hot-path audit, 2026-09-25: M1, M3, M4, L9).
	/// </summary>
	[TestFixture]
	public class AIScheduleSpreadTests
	{
		/// <summary>
		/// Which slot of a <c>ticksPerAiUpdate × lodInterval</c> network-tick cycle an NPC's full
		/// pipeline lands on, computed with the controller's own formulas: the brain-tick phase
		/// from <c>staggerID % ticksPerAiUpdate</c>, and the LOD gate
		/// <c>(aiTickIndex + lodPhase) % lodInterval == 0</c>.
		/// </summary>
		private static int PipelineSlot(int staggerID, int ticksPerAiUpdate, int lodInterval, int lodPhase)
		{
			/* aiTickCounter starts at staggerID % T and fires when it reaches T, so the first brain
			 * tick is T - phase network ticks in; later ones every T. */
			int brainOffset = (ticksPerAiUpdate - staggerID % ticksPerAiUpdate) % ticksPerAiUpdate;
			for (int aiTickIndex = 1; aiTickIndex <= lodInterval; ++aiTickIndex)
			{
				if (((long)aiTickIndex + lodPhase) % lodInterval == 0)
				{
					return ((aiTickIndex - 1) * ticksPerAiUpdate + brainOffset) % (ticksPerAiUpdate * lodInterval);
				}
			}
			return -1;
		}

		/// <summary>
		/// A population fills every network-tick slot of the combined cycle once the LOD phase is
		/// the part of the stagger the brain-tick phase did not use.
		/// </summary>
		/// <remarks>
		/// The Dense Population LOD asset runs Active NPCs every 2nd brain tick, and the brain ticks
		/// every 4th network tick. Reusing the whole stagger for both phases correlated them — the
		/// same low bits decided both — and the population filled only 4 of the 8 slots, so each
		/// used slot carried twice the work. The control half of this test shows that defect.
		/// </remarks>
		[Test]
		public void LodPhase_IsIndependentOfTheBrainPhase_SoEverySlotIsUsed()
		{
			const int T = 4;
			const int I = 2;

			HashSet<int> fixedSlots = new HashSet<int>();
			HashSet<int> oldSlots = new HashSet<int>();
			for (int n = 0; n < 1000; ++n)
			{
				int staggerID = (int)(AIController.MixIdentity(-1000 - 12 * n) & 0x7FFFFFFFU);
				fixedSlots.Add(PipelineSlot(staggerID, T, I, AIController.ResolveLodPhase(staggerID, T)));
				oldSlots.Add(PipelineSlot(staggerID, T, I, staggerID));
			}

			Assert.AreEqual(T * I, fixedSlots.Count, "every slot of the cycle must carry some NPCs");
			Assert.AreEqual(T, oldSlots.Count, "control: the old shared stagger used only half the slots");
		}

		/// <summary>
		/// Instance IDs a constant stride apart must still spread over every brain phase.
		/// </summary>
		/// <remarks>
		/// Unity assigns instance IDs sequentially, so one prefab's instances spawned together tend
		/// to sit a constant stride apart. With a stride that is a multiple of four, the raw
		/// <c>|id| % 4</c> the stagger used to take put every NPC on the same brain tick.
		/// </remarks>
		[Test]
		public void StridedInstanceIds_SpreadOverEveryBrainPhase()
		{
			int[] buckets = new int[4];
			int[] rawBuckets = new int[4];
			for (int n = 0; n < 4000; ++n)
			{
				int instanceId = -1000 - 12 * n;
				int staggerID = (int)(AIController.MixIdentity(instanceId) & 0x7FFFFFFFU);
				buckets[staggerID % 4]++;
				rawBuckets[Mathf.Abs(instanceId) % 4]++;
			}

			for (int b = 0; b < 4; ++b)
			{
				Assert.That(buckets[b], Is.InRange(800, 1200), $"brain phase {b} must get about a quarter of the NPCs");
			}
			Assert.AreEqual(4000, rawBuckets[0], "control: the raw ID put every NPC on one phase");
		}

		[Test]
		public void MixIdentity_NeverGivesTwoInstancesOneKey()
		{
			HashSet<uint> seen = new HashSet<uint>();
			for (int n = 0; n < 10000; ++n)
			{
				Assert.IsTrue(seen.Add(AIController.MixIdentity(-1000 - 2 * n)),
					"identity keys break separation ties and stagger NPCs, so they must be distinct");
			}
		}

		[Test]
		public void PhaseFraction_IsInTheUnitInterval_AndDiffersBySalt()
		{
			uint key = AIController.MixIdentity(-4242);
			float sweep = AIController.PhaseFraction(key, 1);
			float leash = AIController.PhaseFraction(key, 3);

			Assert.That(sweep, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
			Assert.That(leash, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
			Assert.AreNotEqual(sweep, leash, "each timer draws its own phase from the one identity");

			// Spread: a camp's sweep phases cover the whole period, not one point of it.
			int[] deciles = new int[10];
			for (int n = 0; n < 2000; ++n)
			{
				deciles[Mathf.Min(9, (int)(AIController.PhaseFraction(AIController.MixIdentity(-1000 - 12 * n), 1) * 10f))]++;
			}
			for (int d = 0; d < 10; ++d)
			{
				Assert.That(deciles[d], Is.InRange(120, 280), $"decile {d} of the sweep period");
			}
		}

		/// <summary>
		/// A Dormant NPC with nowhere to go skips the per-network-tick body work; every other tier,
		/// and a Dormant NPC still walking, does not.
		/// </summary>
		[Test]
		public void BodyStep_IsSkippedOnlyForAnIdleDormantNpc()
		{
			Assert.IsTrue(AIController.ShouldStepBody(AILodTier.Active, false));
			Assert.IsTrue(AIController.ShouldStepBody(AILodTier.Nearby, false));
			Assert.IsTrue(AIController.ShouldStepBody(AILodTier.Far, false));
			Assert.IsTrue(AIController.ShouldStepBody(AILodTier.Dormant, true), "a dormant NPC with a path keeps walking to it");
			Assert.IsFalse(AIController.ShouldStepBody(AILodTier.Dormant, false));
		}

		/// <summary>
		/// A standing NPC whose agent already sits on its transform writes neither.
		/// </summary>
		[Test]
		public void AgentWrite_OnlyWhenMovingOrDisplaced()
		{
			Vector3 at = new Vector3(10f, 2f, -4f);

			Assert.IsFalse(AIController.NeedsAgentWrite(Vector3.zero, at, at), "standing still: no NavMesh projection, no transform write");
			Assert.IsTrue(AIController.NeedsAgentWrite(new Vector3(0.05f, 0f, 0f), at, at), "moving");
			Assert.IsTrue(AIController.NeedsAgentWrite(Vector3.zero, at, at + new Vector3(0f, 0f, 0.3f)),
				"a platform or a scripted placement moved the transform: the agent must be re-seated");
		}

		[Test]
		public void ABrainIsQuarantined_AfterTheNamedNumberOfFailuresInARow()
		{
			Assert.IsFalse(AIBrainHost.ShouldQuarantine(0));
			Assert.IsFalse(AIBrainHost.ShouldQuarantine(AIBrainHost.MaxConsecutiveTickFaults - 1));
			Assert.IsTrue(AIBrainHost.ShouldQuarantine(AIBrainHost.MaxConsecutiveTickFaults));
			Assert.That(AIBrainHost.MaxConsecutiveTickFaults, Is.GreaterThan(1),
				"one transient failure (a target despawned mid-tick) must never stop a brain");
		}
	}
}
