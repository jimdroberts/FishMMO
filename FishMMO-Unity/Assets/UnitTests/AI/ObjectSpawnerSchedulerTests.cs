using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs for the active list that replaced the per-spawner respawn <c>Update</c>.
	/// </summary>
	/// <remarks>
	/// Two classes of defect are pinned here. The first is the point of the change: a spawner with
	/// nothing to respawn must not be in the list at all, or the cost goes back to scaling with how
	/// much world exists rather than with how much of it is in motion. The second is what
	/// maintaining membership can get wrong that a blanket poll cannot — a spawner that leaves the
	/// list and is never asked again. A poll forgives that by definition; a membership list does
	/// not, and the failure is silent.
	/// </remarks>
	[TestFixture]
	public class ObjectSpawnerSchedulerTests
	{
		private readonly List<GameObject> created = new List<GameObject>();

		[SetUp]
		public void ClearSchedule()
		{
			ObjectSpawnerScheduler.Clear();
		}

		[TearDown]
		public void DestroyCreated()
		{
			ObjectSpawnerScheduler.Clear();

			foreach (GameObject go in created)
			{
				if (go != null)
				{
					UnityEngine.Object.DestroyImmediate(go);
				}
			}
			created.Clear();
		}

		/// <summary>
		/// Builds a spawner that can run a full respawn pass without touching the network.
		/// </summary>
		/// <remarks>
		/// The settings entry carries a null <c>NetworkObject</c>, which <c>SpawnObject</c> treats
		/// as nothing to spawn and returns on. Everything up to that point — the guards, the
		/// condition evaluation, the timer bookkeeping — is the code under test and runs for real.
		/// The check interval is zeroed so a test tick is never swallowed by the spawner's own gate;
		/// that gate is Jim's and is covered by SpawnerSettingsTests.
		/// </remarks>
		private ObjectSpawner NewSpawner(int maxSpawnCount = 10)
		{
			GameObject go = new GameObject("Spawner");
			created.Add(go);

			ObjectSpawner spawner = go.AddComponent<ObjectSpawner>();
			spawner.MaxSpawnCount = maxSpawnCount;
			spawner.Spawnables = new List<SpawnableSettings> { new ItemSpawnableSettings() };
			spawner.SpawnableRespawnTimers = new List<DateTime>();
			spawner.Spawned = new Dictionary<long, ISpawnable>();
			spawner.RespawnCheckIntervalMinimum = 0.0f;
			spawner.RespawnCheckIntervalMaximum = 0.0f;
			return spawner;
		}

		// --- Not being in the list at all is the whole point ------------------------------------

		[Test]
		public void SpawnerAtItsCap_IsNotInTheActiveList()
		{
			/* The original question: ten monsters, none killed. A full spawner must leave the list
			 * entirely rather than be walked to discover it has nothing to do. */
			ObjectSpawner spawner = NewSpawner(maxSpawnCount: 2);
			spawner.Spawned[1] = null;
			spawner.Spawned[2] = null;
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1));

			ObjectSpawnerScheduler.Refresh(spawner);

			Assert.AreEqual(0, ObjectSpawnerScheduler.ActiveCount,
				"A spawner at its cap has no work and must not be walked.");
		}

		[Test]
		public void SpawnerWithNoPendingTimers_IsNotInTheActiveList()
		{
			ObjectSpawner spawner = NewSpawner();

			ObjectSpawnerScheduler.Refresh(spawner);

			Assert.AreEqual(0, ObjectSpawnerScheduler.ActiveCount);
		}

		[Test]
		public void SpawnerWithAPendingTimer_JoinsTheActiveList()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(30.0));

			ObjectSpawnerScheduler.Refresh(spawner);

			Assert.AreEqual(1, ObjectSpawnerScheduler.ActiveCount);
		}

		[Test]
		public void RefreshingTwice_AddsTheSpawnerOnce()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(30.0));

			ObjectSpawnerScheduler.Refresh(spawner);
			ObjectSpawnerScheduler.Refresh(spawner);
			ObjectSpawnerScheduler.Refresh(spawner);

			Assert.AreEqual(1, ObjectSpawnerScheduler.ActiveCount,
				"Membership is a set; refreshing is not the same as enqueuing.");
		}

		[Test]
		public void ASpawnerThatFinishesItsWork_LeavesTheActiveList()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			ObjectSpawnerScheduler.Refresh(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(0, ObjectSpawnerScheduler.ActiveCount,
				"Nothing left to respawn, so nothing left to walk.");
		}

		// --- Deadlines --------------------------------------------------------------------------

		[Test]
		public void TickBeforeTheDeadline_LeavesTheTimerAlone()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(30.0));
			ObjectSpawnerScheduler.Refresh(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, spawner.SpawnableRespawnTimers.Count,
				"Nothing was due, so nothing should have been consumed.");
		}

		[Test]
		public void TickAfterTheDeadline_ConsumesEveryDueTimerInOnePass()
		{
			/* The refill-rate cap. The walk used to spawn one object and return, so a group wiped
			 * together refilled at one per check — a rate set by the polling interval rather than by
			 * the respawn times anybody authored. At frame rate that was invisible; behind a
			 * multi-second interval it is the difference between a camp returning in seconds and in
			 * the better part of a minute, and no MinimumRespawnTime can ask for faster. */
			ObjectSpawner spawner = NewSpawner();
			DateTime past = DateTime.UtcNow.AddSeconds(-1.0);
			for (int i = 0; i < 5; ++i)
			{
				spawner.SpawnableRespawnTimers.Add(past);
			}
			ObjectSpawnerScheduler.Refresh(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(0, spawner.SpawnableRespawnTimers.Count,
				"Every elapsed deadline should be honoured in the pass that finds it.");
		}

		[Test]
		public void TickAfterTheDeadline_LeavesTimersThatAreStillInTheFuture()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(600.0));
			ObjectSpawnerScheduler.Refresh(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, spawner.SpawnableRespawnTimers.Count,
				"A deadline ten minutes out is not due now.");
		}

		[Test]
		public void ASpawnerWithWorkRemaining_StaysInTheActiveList()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(600.0));
			ObjectSpawnerScheduler.Refresh(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, ObjectSpawnerScheduler.ActiveCount,
				"The remaining deadline still needs looking at.");
		}

		// --- The failure a membership list can have and a blanket poll cannot -------------------

		[Test]
		public void ARefusedRespawn_StaysInTheActiveList()
		{
			/* The defect this design is most exposed to. A condition that says no consumes no
			 * timer, so a list that only kept spawners which made progress would drop this one for
			 * good and the camp would never come back once the boss died. Silent, and no test that
			 * kills a single monster would ever see it. */
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			spawner.TrueConditions.Add(NewCondition(spawner, allow: false));
			ObjectSpawnerScheduler.Refresh(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, spawner.SpawnableRespawnTimers.Count,
				"A refused respawn must not consume its deadline.");
			Assert.AreEqual(1, ObjectSpawnerScheduler.ActiveCount,
				"A refused spawner must stay in the list, or it is never asked again.");
		}

		[Test]
		public void ARefusedRespawn_ProceedsOnceTheConditionClears()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			CountingRespawnCondition condition = NewCondition(spawner, allow: false);
			spawner.TrueConditions.Add(condition);
			ObjectSpawnerScheduler.Refresh(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);
			Assert.AreEqual(1, spawner.SpawnableRespawnTimers.Count, "Still blocked.");

			condition.Allow = true;
			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(0, spawner.SpawnableRespawnTimers.Count,
				"The boss is dead; the camp should come back.");
		}

		// --- Conditions are a spawner-wide answer, not a per-timer one ---------------------------

		[Test]
		public void Conditions_AreEvaluatedOncePerPass_NotOncePerDueTimer()
		{
			/* OnCheckCondition is handed only the spawner, so its answer cannot differ between two
			 * timers in the same pass. Asking per timer walked every condition's NPC list again for
			 * each one, and cost the most in precisely the case that creates many due timers at
			 * once. */
			ObjectSpawner spawner = NewSpawner();
			DateTime past = DateTime.UtcNow.AddSeconds(-1.0);
			for (int i = 0; i < 6; ++i)
			{
				spawner.SpawnableRespawnTimers.Add(past);
			}
			CountingRespawnCondition condition = NewCondition(spawner, allow: true);
			spawner.TrueConditions.Add(condition);
			ObjectSpawnerScheduler.Refresh(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, condition.Calls,
				"Six due timers is still one question.");
		}

		// --- Membership bookkeeping --------------------------------------------------------------

		[Test]
		public void Unregister_TakesTheSpawnerOutOfTheWalk()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			ObjectSpawnerScheduler.Refresh(spawner);

			ObjectSpawnerScheduler.Unregister(spawner);
			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, spawner.SpawnableRespawnTimers.Count,
				"An unregistered spawner - an unloaded scene, say - must not be walked.");
		}

		[Test]
		public void RemovingFromTheMiddle_DoesNotStrandTheSpawnerThatTookItsPlace()
		{
			/* Membership is maintained by swapping the last entry into the vacated slot, which is
			 * what keeps removal O(1). Get the moved spawner's stored index wrong and it is either
			 * walked twice or dropped silently, and dropped is the one nobody notices. */
			ObjectSpawner first = NewSpawner();
			ObjectSpawner second = NewSpawner();
			ObjectSpawner third = NewSpawner();
			foreach (ObjectSpawner s in new[] { first, second, third })
			{
				s.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(600.0));
				ObjectSpawnerScheduler.Refresh(s);
			}

			// Take the middle one out; the third is swapped into its slot.
			ObjectSpawnerScheduler.Unregister(second);

			Assert.AreEqual(2, ObjectSpawnerScheduler.ActiveCount);

			// If third's index was not repaired, removing it now corrupts the list.
			ObjectSpawnerScheduler.Unregister(third);

			Assert.AreEqual(1, ObjectSpawnerScheduler.ActiveCount);
			ObjectSpawnerScheduler.Unregister(first);
			Assert.AreEqual(0, ObjectSpawnerScheduler.ActiveCount,
				"Every spawner should have been removable exactly once.");
		}

		[Test]
		public void ASpawnerLeavingMidSweep_DoesNotSkipTheOthers()
		{
			/* The sweep removes entries as it goes, and removal swaps the last entry into the
			 * vacated slot. A cursor that advances past that slot anyway silently skips whichever
			 * spawner was moved into it — which reads as one camp in a zone that simply never comes
			 * back. */
			List<CountingRespawnCondition> conditions = new List<CountingRespawnCondition>();
			for (int i = 0; i < 5; ++i)
			{
				ObjectSpawner s = NewSpawner();
				s.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
				CountingRespawnCondition condition = NewCondition(s, allow: true);
				s.TrueConditions.Add(condition);
				conditions.Add(condition);
				ObjectSpawnerScheduler.Refresh(s);
			}

			// One full sweep. The list is walked a slice at a time, so this is not one tick.
			DateTime now = DateTime.UtcNow;
			for (int frame = 0; frame < ObjectSpawnerScheduler.FramesPerSweep; ++frame)
			{
				ObjectSpawnerScheduler.Tick(now, 0.0f);
			}

			for (int i = 0; i < conditions.Count; ++i)
			{
				Assert.AreEqual(1, conditions[i].Calls,
					$"Spawner {i} was skipped by the sweep.");
			}
			Assert.AreEqual(0, ObjectSpawnerScheduler.ActiveCount,
				"All five finished their work and should have left the list.");
		}

		[Test]
		public void TheSweepSpreadsTheListAcrossFrames()
		{
			/* The reason this is a sweep and not a walk. A spawner waiting on a deadline minutes
			 * away must not be visited every frame just because it is waiting: that is the cost the
			 * active list exists to avoid, moved rather than removed. */
			for (int i = 0; i < ObjectSpawnerScheduler.FramesPerSweep * 2; ++i)
			{
				ObjectSpawner s = NewSpawner();
				s.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(600.0));
				ObjectSpawnerScheduler.Refresh(s);
			}

			int active = ObjectSpawnerScheduler.ActiveCount;
			Assert.AreEqual(ObjectSpawnerScheduler.FramesPerSweep * 2, active);

			/* Nothing is due, so no membership changes and the arithmetic is stable: one frame
			 * should cover about a FramesPerSweep-th of the list, not all of it. */
			int expectedSlice = (active + ObjectSpawnerScheduler.FramesPerSweep - 1)
				/ ObjectSpawnerScheduler.FramesPerSweep;

			Assert.AreEqual(2, expectedSlice,
				"A list twice the sweep length should be covered two entries at a time.");
			Assert.Less(expectedSlice, active,
				"A sweep that visits everything every frame is just the poll again.");
		}

		[Test]
		public void ASpawnerRemovedDuringItsOwnPass_DoesNotDropTheOneSwappedIntoItsSlot()
		{
			/* The sweep removes the entry it just finished with, and removal swaps the last entry
			 * into that slot. A pass is not inert though - spawning fires callbacks that can
			 * despawn, and a spawner can be destroyed from one - so the spawner at the cursor may
			 * already have left by the time the sweep decides what to do with the slot. Acting on
			 * the slot rather than on the spawner then removes whoever was swapped in, which is the
			 * silent failure: a camp that is never asked again. */
			ObjectSpawner leaves = NewSpawner();
			ObjectSpawner bystander = NewSpawner();

			leaves.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			bystander.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(600.0));

			ObjectSpawnerScheduler.Refresh(leaves);
			ObjectSpawnerScheduler.Refresh(bystander);

			// Unregisters itself mid-pass, then allows the respawn so it also finishes its work.
			SelfUnregisteringRespawnCondition condition =
				leaves.gameObject.AddComponent<SelfUnregisteringRespawnCondition>();
			leaves.TrueConditions.Add(condition);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(1, ObjectSpawnerScheduler.ActiveCount,
				"The spawner swapped into the vacated slot was dropped with it.");
			Assert.AreNotEqual(ObjectSpawnerScheduler.NotActive, bystander.SchedulerIndex,
				"A spawner still waiting on a deadline must not be evicted by someone else leaving.");
		}

		[Test]
		public void Clear_ResetsMembershipSoSpawnersCanRejoin()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(600.0));
			ObjectSpawnerScheduler.Refresh(spawner);

			ObjectSpawnerScheduler.Clear();
			Assert.AreEqual(0, ObjectSpawnerScheduler.ActiveCount);

			ObjectSpawnerScheduler.Refresh(spawner);

			Assert.AreEqual(1, ObjectSpawnerScheduler.ActiveCount,
				"A cleared spawner still believing it is a member could never rejoin.");
		}

		// --- Helpers -----------------------------------------------------------------------------

		private CountingRespawnCondition NewCondition(ObjectSpawner spawner, bool allow)
		{
			CountingRespawnCondition condition = spawner.gameObject.AddComponent<CountingRespawnCondition>();
			condition.Allow = allow;
			return condition;
		}
	}

	/// <summary>
	/// A respawn condition that drops its own spawner from the schedule while it is being asked,
	/// standing in for anything a spawn or despawn callback could do mid-pass.
	/// </summary>
	public class SelfUnregisteringRespawnCondition : BaseRespawnCondition
	{
		/// <inheritdoc />
		public override bool OnCheckCondition(ObjectSpawner spawner)
		{
			ObjectSpawnerScheduler.Unregister(spawner);
			return true;
		}
	}

	/// <summary>
	/// A respawn condition that records how often it was asked.
	/// </summary>
	public class CountingRespawnCondition : BaseRespawnCondition
	{
		/// <summary>What this condition answers.</summary>
		public bool Allow = true;

		/// <summary>How many times it has been asked.</summary>
		public int Calls;

		/// <inheritdoc />
		public override bool OnCheckCondition(ObjectSpawner spawner)
		{
			++Calls;
			return Allow;
		}
	}
}
