using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs for the scheduled respawn tick that replaced the per-spawner <c>Update</c> poll.
	/// </summary>
	/// <remarks>
	/// Two classes of defect are pinned here. The first is the point of the change: a spawner with
	/// nothing pending must not be queued at all, or the cost goes back to scaling with how much
	/// world exists. The second is what a schedule can get wrong that a poll cannot — a spawner
	/// that drops out of the queue and is never looked at again. A poll forgives that by definition;
	/// a schedule does not, and the failure is silent.
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
			return spawner;
		}

		// --- Not being queued at all is the whole point ----------------------------------------

		[Test]
		public void SpawnerAtItsCap_IsNotQueued()
		{
			/* The original question: ten monsters, none killed. A full spawner must leave the
			 * schedule entirely rather than be woken to discover it has nothing to do. */
			ObjectSpawner spawner = NewSpawner(maxSpawnCount: 2);
			spawner.Spawned[1] = null;
			spawner.Spawned[2] = null;
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1));

			ObjectSpawnerScheduler.Reschedule(spawner);

			Assert.AreEqual(0, ObjectSpawnerScheduler.QueuedCount,
				"A spawner at its cap has no work and must not hold a wake.");
		}

		[Test]
		public void SpawnerWithNoPendingTimers_IsNotQueued()
		{
			ObjectSpawner spawner = NewSpawner();

			ObjectSpawnerScheduler.Reschedule(spawner);

			Assert.AreEqual(0, ObjectSpawnerScheduler.QueuedCount);
		}

		[Test]
		public void SpawnerWithAPendingTimer_IsQueued()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(30.0));

			ObjectSpawnerScheduler.Reschedule(spawner);

			Assert.AreEqual(1, ObjectSpawnerScheduler.QueuedCount);
		}

		// --- Deadlines ------------------------------------------------------------------------

		[Test]
		public void TickBeforeTheDeadline_LeavesTheTimerAlone()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(30.0));
			ObjectSpawnerScheduler.Reschedule(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, spawner.SpawnableRespawnTimers.Count,
				"Nothing was due, so nothing should have been consumed.");
		}

		[Test]
		public void TickAfterTheDeadline_ConsumesEveryDueTimerInOnePass()
		{
			/* The refill-rate cap. The walk used to spawn one object and return, so a group wiped
			 * together refilled at one per tick — a rate set by how often the tick ran rather than
			 * by the respawn times anybody authored. Behind a multi-second gate that is the
			 * difference between a camp returning in seconds and in the best part of a minute. */
			ObjectSpawner spawner = NewSpawner();
			DateTime past = DateTime.UtcNow.AddSeconds(-1.0);
			for (int i = 0; i < 5; ++i)
			{
				spawner.SpawnableRespawnTimers.Add(past);
			}
			ObjectSpawnerScheduler.Reschedule(spawner);

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
			ObjectSpawnerScheduler.Reschedule(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, spawner.SpawnableRespawnTimers.Count,
				"A deadline ten minutes out is not due now.");
		}

		[Test]
		public void ASpawnerWithWorkRemaining_StaysQueuedAfterItsPass()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(600.0));
			ObjectSpawnerScheduler.Reschedule(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, ObjectSpawnerScheduler.QueuedCount,
				"The remaining deadline still needs a wake.");
		}

		// --- The failure a schedule can have and a poll cannot ---------------------------------

		[Test]
		public void ARefusedRespawn_StaysInTheSchedule()
		{
			/* The defect this design is most exposed to. A condition that says no consumes no
			 * timer, so a scheduler that only re-queues on progress drops the spawner for good and
			 * the camp never comes back once the boss dies. Silent, and no test that kills a single
			 * monster would ever see it. */
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			spawner.TrueConditions.Add(NewCondition(spawner, allow: false));
			ObjectSpawnerScheduler.Reschedule(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, spawner.SpawnableRespawnTimers.Count,
				"A refused respawn must not consume its deadline.");
			Assert.AreEqual(1, ObjectSpawnerScheduler.QueuedCount,
				"A refused spawner must remain queued, or it is never asked again.");
		}

		[Test]
		public void ARefusedRespawn_DoesNotRetryOnTheVeryNextTick()
		{
			/* The other half: the deadline has already passed, so re-queueing on the deadline would
			 * wake this spawner every single frame for as long as the condition holds - the poll
			 * that was just removed, wearing a hat. */
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			CountingRespawnCondition condition = NewCondition(spawner, allow: false);
			spawner.TrueConditions.Add(condition);
			ObjectSpawnerScheduler.Reschedule(spawner);

			DateTime now = DateTime.UtcNow;
			ObjectSpawnerScheduler.Tick(now);
			int afterFirst = condition.Calls;

			ObjectSpawnerScheduler.Tick(now);

			Assert.AreEqual(afterFirst, condition.Calls,
				"The retry is on its own clock; an immediate second tick must not re-test.");
		}

		[Test]
		public void ARefusedRespawn_RetriesOnceItsDelayHasPassed()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			CountingRespawnCondition condition = NewCondition(spawner, allow: false);
			spawner.BlockedRetryIntervalMinimum = 1.0f;
			spawner.BlockedRetryIntervalMaximum = 2.0f;
			spawner.TrueConditions.Add(condition);
			ObjectSpawnerScheduler.Reschedule(spawner);

			DateTime now = DateTime.UtcNow;
			ObjectSpawnerScheduler.Tick(now);
			int afterFirst = condition.Calls;

			// Past the longest retry delay this spawner can pick.
			ObjectSpawnerScheduler.Tick(now.AddSeconds(3.0));

			Assert.Greater(condition.Calls, afterFirst,
				"Once the retry delay elapses the condition must be asked again.");
		}

		[Test]
		public void ARefusedRespawn_ProceedsOnceTheConditionClears()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			CountingRespawnCondition condition = NewCondition(spawner, allow: false);
			spawner.BlockedRetryIntervalMinimum = 1.0f;
			spawner.BlockedRetryIntervalMaximum = 2.0f;
			spawner.TrueConditions.Add(condition);
			ObjectSpawnerScheduler.Reschedule(spawner);

			DateTime now = DateTime.UtcNow;
			ObjectSpawnerScheduler.Tick(now);

			condition.Allow = true;
			ObjectSpawnerScheduler.Tick(now.AddSeconds(3.0));

			Assert.AreEqual(0, spawner.SpawnableRespawnTimers.Count,
				"The boss is dead; the camp should come back.");
		}

		// --- Conditions are a spawner-wide answer, not a per-timer one -------------------------

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
			ObjectSpawnerScheduler.Reschedule(spawner);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, condition.Calls,
				"Six due timers is still one question.");
		}

		[Test]
		public void AZeroRetryInterval_DoesNotHangTheTick()
		{
			/* A retry delay of zero schedules the re-test at the instant of the refusal. The clock
			 * does not advance inside a tick, so popping and re-queueing that spawner would never
			 * terminate. Zero is reachable straight from the inspector - both fields are [Min(0)] -
			 * so this has to be survivable rather than merely discouraged. */
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			spawner.BlockedRetryIntervalMinimum = 0.0f;
			spawner.BlockedRetryIntervalMaximum = 0.0f;
			spawner.TrueConditions.Add(NewCondition(spawner, allow: false));
			ObjectSpawnerScheduler.Reschedule(spawner);

			// Fails by hanging rather than by asserting, so the timeout is the real assertion.
			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.Pass("The tick terminated.");
		}

		// --- Schedule bookkeeping ---------------------------------------------------------------

		[Test]
		public void Unregister_PreventsTheQueuedWakeFromFiring()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			ObjectSpawnerScheduler.Reschedule(spawner);

			ObjectSpawnerScheduler.Unregister(spawner);
			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, spawner.SpawnableRespawnTimers.Count,
				"An unregistered spawner - an unloaded scene, say - must not be woken.");
		}

		[Test]
		public void ReschedulingTwice_RunsTheSpawnerOnce()
		{
			/* Rescheduling pushes rather than moving, so a spawner that reschedules often leaves
			 * superseded entries behind it. Each must be recognised and dropped, or one death
			 * produces several respawns. */
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));

			ObjectSpawnerScheduler.Reschedule(spawner);
			ObjectSpawnerScheduler.Reschedule(spawner);
			ObjectSpawnerScheduler.Reschedule(spawner);
			Assert.AreEqual(3, ObjectSpawnerScheduler.QueuedCount,
				"Superseded entries are expected to still be in the heap.");

			CountingRespawnCondition condition = NewCondition(spawner, allow: true);
			spawner.TrueConditions.Add(condition);

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(1, condition.Calls,
				"Only the current entry should run; the superseded ones are stale.");
		}

		[Test]
		public void StaleEntries_AreDrainedRatherThanAccumulating()
		{
			ObjectSpawner spawner = NewSpawner();
			spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));

			for (int i = 0; i < 20; ++i)
			{
				ObjectSpawnerScheduler.Reschedule(spawner);
			}

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			Assert.AreEqual(0, ObjectSpawnerScheduler.QueuedCount,
				"Every stale entry should have been dropped as it surfaced.");
		}

		[Test]
		public void SpawnersAreWokenInDeadlineOrder()
		{
			ObjectSpawner late = NewSpawner();
			late.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
			CountingRespawnCondition lateCondition = NewCondition(late, allow: false);
			late.TrueConditions.Add(lateCondition);

			ObjectSpawner early = NewSpawner();
			early.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-30.0));
			CountingRespawnCondition earlyCondition = NewCondition(early, allow: false);
			early.TrueConditions.Add(earlyCondition);

			ObjectSpawnerScheduler.Reschedule(late);
			ObjectSpawnerScheduler.Reschedule(early);

			List<string> order = new List<string>();
			earlyCondition.OnChecked = () => order.Add("early");
			lateCondition.OnChecked = () => order.Add("late");

			ObjectSpawnerScheduler.Tick(DateTime.UtcNow);

			CollectionAssert.AreEqual(new[] { "early", "late" }, order,
				"The heap orders wakes by deadline, oldest first.");
		}

		// --- Helpers ---------------------------------------------------------------------------

		private CountingRespawnCondition NewCondition(ObjectSpawner spawner, bool allow)
		{
			CountingRespawnCondition condition = spawner.gameObject.AddComponent<CountingRespawnCondition>();
			condition.Allow = allow;
			return condition;
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

		/// <summary>Raised on each check, for tests that care about ordering.</summary>
		public Action OnChecked;

		/// <inheritdoc />
		public override bool OnCheckCondition(ObjectSpawner spawner)
		{
			++Calls;
			OnChecked?.Invoke();
			return Allow;
		}
	}
}
