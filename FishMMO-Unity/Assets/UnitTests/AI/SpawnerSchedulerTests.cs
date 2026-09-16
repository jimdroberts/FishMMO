using System;
using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs for the active list that replaced the per-spawner respawn <c>Update</c>.
	/// </summary>
	/// <remarks>
	/// Two classes of defect are pinned here. The first is the point of the list: a spawner with
	/// nothing to respawn must not be in it at all, or the cost goes back to scaling with how much
	/// world exists rather than with how much of it is in motion. The second is what maintaining
	/// membership can get wrong that a blanket poll cannot — a spawner that leaves the list and is
	/// never asked again. A poll forgives that by definition; a membership list does not, and the
	/// failure is silent.
	/// </remarks>
	[TestFixture]
	public class SpawnerSchedulerTests
	{
		private SpawnerScheduler scheduler;

		[SetUp]
		public void NewScheduler()
		{
			scheduler = new SpawnerScheduler();
		}

		private SpawnerRuntime NewSpawner(int maxSpawnCount = 10)
		{
			return SpawnerTestKit.NewRuntime(scheduler, maxSpawnCount);
		}

		// --- Not being in the list at all is the whole point ------------------------------------

		[Test]
		public void SpawnerAtItsCap_IsNotInTheActiveList()
		{
			/* The original question: ten monsters, none killed. A full spawner must leave the list
			 * entirely rather than be walked to discover it has nothing to do. */
			SpawnerRuntime spawner = NewSpawner(maxSpawnCount: 2);
			SpawnerTestKit.FillWith(spawner, 2);
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(-1));

			scheduler.Refresh(spawner);

			Assert.AreEqual(0, scheduler.ActiveCount,
				"A spawner at its cap has no work and must not be walked.");
		}

		[Test]
		public void SpawnerWithNoPendingTimers_IsNotInTheActiveList()
		{
			SpawnerRuntime spawner = NewSpawner();

			scheduler.Refresh(spawner);

			Assert.AreEqual(0, scheduler.ActiveCount);
		}

		[Test]
		public void SpawnerWithAPendingTimer_JoinsTheActiveList()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(30.0));

			scheduler.Refresh(spawner);

			Assert.AreEqual(1, scheduler.ActiveCount);
		}

		[Test]
		public void RefreshingTwice_AddsTheSpawnerOnce()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(30.0));

			scheduler.Refresh(spawner);
			scheduler.Refresh(spawner);
			scheduler.Refresh(spawner);

			Assert.AreEqual(1, scheduler.ActiveCount,
				"Membership is a set; refreshing is not the same as enqueuing.");
		}

		[Test]
		public void ASpawnerThatFinishesItsWork_LeavesTheActiveList()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(-1.0));
			scheduler.Refresh(spawner);

			scheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(0, scheduler.ActiveCount,
				"Nothing left to respawn, so nothing left to walk.");
		}

		[Test]
		public void TwoSchedulers_DoNotShareSpawners()
		{
			/* The scheduler used to be static, which put every spawner in the process in one list.
			 * A simulation running beside a server must not walk the server's spawners. */
			SpawnerScheduler other = new SpawnerScheduler();
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(30.0));

			scheduler.Refresh(spawner);

			Assert.AreEqual(1, scheduler.ActiveCount);
			Assert.AreEqual(0, other.ActiveCount);
		}

		// --- Deadlines --------------------------------------------------------------------------

		[Test]
		public void TickBeforeTheDeadline_LeavesTheTimerAlone()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(30.0));
			scheduler.Refresh(spawner);

			scheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(1, spawner.PendingRespawnCount,
				"Nothing was due, so nothing should have been consumed.");
		}

		[Test]
		public void TickAfterTheDeadline_ConsumesEveryDueTimerInOnePass()
		{
			/* The refill-rate cap. The walk used to spawn one object and return, so a group wiped
			 * together refilled at one per check — a rate set by the polling interval rather than by
			 * the respawn times anybody authored. */
			SpawnerRuntime spawner = NewSpawner();
			DateTime past = DateTime.UtcNow.AddSeconds(-1.0);
			for (int i = 0; i < 5; ++i)
			{
				spawner.AddRespawnTimer(past);
			}
			scheduler.Refresh(spawner);

			scheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(0, spawner.PendingRespawnCount,
				"Every elapsed deadline should be honoured in the pass that finds it.");
		}

		[Test]
		public void TickAfterTheDeadline_LeavesTimersThatAreStillInTheFuture()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(-1.0));
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(600.0));
			scheduler.Refresh(spawner);

			scheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(1, spawner.PendingRespawnCount,
				"A deadline ten minutes out is not due now.");
		}

		[Test]
		public void ASpawnerWithWorkRemaining_StaysInTheActiveList()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(-1.0));
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(600.0));
			scheduler.Refresh(spawner);

			scheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(1, scheduler.ActiveCount,
				"The remaining deadline still needs looking at.");
		}

		// --- The failure a membership list can have and a blanket poll cannot -------------------

		[Test]
		public void ARefusedRespawn_StaysInTheActiveList()
		{
			/* The defect this design is most exposed to. A condition that says no consumes no
			 * timer, so a list that only kept spawners which made progress would drop this one for
			 * good and the camp would never come back once the boss died. */
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(-1.0));
			spawner.Definition.TrueConditions.Add(new CountingRespawnCondition { Allow = false });
			scheduler.Refresh(spawner);

			scheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(1, spawner.PendingRespawnCount,
				"A refused respawn must not consume its deadline.");
			Assert.AreEqual(1, scheduler.ActiveCount,
				"A refused spawner must stay in the list, or it is never asked again.");
		}

		[Test]
		public void ARefusedRespawn_ProceedsOnceTheConditionClears()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(-1.0));
			CountingRespawnCondition condition = new CountingRespawnCondition { Allow = false };
			spawner.Definition.TrueConditions.Add(condition);
			scheduler.Refresh(spawner);

			scheduler.Tick(DateTime.UtcNow, 0.0f);
			Assert.AreEqual(1, spawner.PendingRespawnCount, "Still blocked.");

			condition.Allow = true;
			scheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(0, spawner.PendingRespawnCount,
				"The boss is dead; the camp should come back.");
		}

		[Test]
		public void AnOrListWithNoAllowingCondition_RefusesTheRespawn()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(-1.0));
			spawner.Definition.OrConditions.Add(new CountingRespawnCondition { Allow = false });
			spawner.Definition.OrConditions.Add(new CountingRespawnCondition { Allow = false });
			scheduler.Refresh(spawner);

			scheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(1, spawner.PendingRespawnCount);

			spawner.Definition.OrConditions.Add(new CountingRespawnCondition { Allow = true });
			scheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(0, spawner.PendingRespawnCount,
				"Any one allowing condition is enough.");
		}

		// --- Conditions are a spawner-wide answer, not a per-timer one ---------------------------

		[Test]
		public void Conditions_AreEvaluatedOncePerPass_NotOncePerDueTimer()
		{
			/* A condition is handed only the spawner, so its answer cannot differ between two
			 * timers in the same pass. Asking per timer cost the most in precisely the case that
			 * creates many due timers at once. */
			SpawnerRuntime spawner = NewSpawner();
			DateTime past = DateTime.UtcNow.AddSeconds(-1.0);
			for (int i = 0; i < 6; ++i)
			{
				spawner.AddRespawnTimer(past);
			}
			CountingRespawnCondition condition = new CountingRespawnCondition { Allow = true };
			spawner.Definition.TrueConditions.Add(condition);
			scheduler.Refresh(spawner);

			scheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(1, condition.Calls,
				"Six due timers is still one question.");
		}

		// --- Membership bookkeeping --------------------------------------------------------------

		[Test]
		public void Unregister_TakesTheSpawnerOutOfTheWalk()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(-1.0));
			scheduler.Refresh(spawner);

			scheduler.Unregister(spawner);
			scheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(1, spawner.PendingRespawnCount,
				"An unregistered spawner - an unloaded scene, say - must not be walked.");
		}

		[Test]
		public void Stop_TakesTheSpawnerOutOfTheWalkAndForgetsItsWork()
		{
			SpawnerRuntime spawner = NewSpawner();
			SpawnerTestKit.FillWith(spawner, 2);
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(600.0));
			scheduler.Refresh(spawner);

			spawner.Stop();

			Assert.AreEqual(0, scheduler.ActiveCount);
			Assert.AreEqual(0, spawner.SpawnedCount);
			Assert.AreEqual(0, spawner.PendingRespawnCount);
		}

		[Test]
		public void RemovingFromTheMiddle_DoesNotStrandTheSpawnerThatTookItsPlace()
		{
			/* Membership is maintained by swapping the last entry into the vacated slot, which is
			 * what keeps removal O(1). Get the moved spawner's stored index wrong and it is either
			 * walked twice or dropped silently, and dropped is the one nobody notices. */
			SpawnerRuntime first = NewSpawner();
			SpawnerRuntime second = NewSpawner();
			SpawnerRuntime third = NewSpawner();
			foreach (SpawnerRuntime s in new[] { first, second, third })
			{
				s.AddRespawnTimer(DateTime.UtcNow.AddSeconds(600.0));
				scheduler.Refresh(s);
			}

			// Take the middle one out; the third is swapped into its slot.
			scheduler.Unregister(second);

			Assert.AreEqual(2, scheduler.ActiveCount);

			// If third's index was not repaired, removing it now corrupts the list.
			scheduler.Unregister(third);

			Assert.AreEqual(1, scheduler.ActiveCount);
			scheduler.Unregister(first);
			Assert.AreEqual(0, scheduler.ActiveCount,
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
				SpawnerRuntime s = NewSpawner();
				s.AddRespawnTimer(DateTime.UtcNow.AddSeconds(-1.0));
				CountingRespawnCondition condition = new CountingRespawnCondition { Allow = true };
				s.Definition.TrueConditions.Add(condition);
				conditions.Add(condition);
				scheduler.Refresh(s);
			}

			// One full sweep. The list is walked a slice at a time, so this is not one tick.
			DateTime now = DateTime.UtcNow;
			for (int frame = 0; frame < SpawnerScheduler.FramesPerSweep; ++frame)
			{
				scheduler.Tick(now, 0.0f);
			}

			for (int i = 0; i < conditions.Count; ++i)
			{
				Assert.AreEqual(1, conditions[i].Calls,
					$"Spawner {i} was skipped by the sweep.");
			}
			Assert.AreEqual(0, scheduler.ActiveCount,
				"All five finished their work and should have left the list.");
		}

		[Test]
		public void TheSweepSpreadsTheListAcrossFrames()
		{
			/* The reason this is a sweep and not a walk. A spawner waiting on a deadline minutes
			 * away must not be visited every frame just because it is waiting. */
			List<CountingRespawnCondition> conditions = new List<CountingRespawnCondition>();
			for (int i = 0; i < SpawnerScheduler.FramesPerSweep * 2; ++i)
			{
				SpawnerRuntime s = NewSpawner();
				// Due, but refused: every visit asks the condition and consumes nothing.
				s.AddRespawnTimer(DateTime.UtcNow.AddSeconds(-1.0));
				CountingRespawnCondition condition = new CountingRespawnCondition { Allow = false };
				s.Definition.TrueConditions.Add(condition);
				conditions.Add(condition);
				scheduler.Refresh(s);
			}

			scheduler.Tick(DateTime.UtcNow, 0.0f);

			int visited = 0;
			for (int i = 0; i < conditions.Count; ++i)
			{
				visited += conditions[i].Calls;
			}
			Assert.AreEqual(2, visited,
				"A list twice the sweep length should be covered two entries per frame.");
		}

		[Test]
		public void ASpawnerRemovedDuringItsOwnPass_DoesNotDropTheOneSwappedIntoItsSlot()
		{
			/* The sweep removes the entry it just finished with, and removal swaps the last entry
			 * into that slot. A pass is not inert though - spawning fires callbacks that can
			 * despawn, and a scene can be unloaded from one - so the spawner at the cursor may
			 * already have left by the time the sweep decides what to do with the slot. Acting on
			 * the slot rather than on the spawner then removes whoever was swapped in. */
			SpawnerRuntime leaves = NewSpawner();
			SpawnerRuntime bystander = NewSpawner();

			leaves.AddRespawnTimer(DateTime.UtcNow.AddSeconds(-1.0));
			bystander.AddRespawnTimer(DateTime.UtcNow.AddSeconds(600.0));

			scheduler.Refresh(leaves);
			scheduler.Refresh(bystander);

			// Unregisters itself mid-pass, then allows the respawn so it also finishes its work.
			leaves.Definition.TrueConditions.Add(new SelfUnregisteringRespawnCondition());

			scheduler.Tick(DateTime.UtcNow, 0.0f);

			Assert.AreEqual(1, scheduler.ActiveCount,
				"The spawner swapped into the vacated slot was dropped with it.");
			Assert.AreNotEqual(SpawnerScheduler.NotActive, bystander.SchedulerIndex,
				"A spawner still waiting on a deadline must not be evicted by someone else leaving.");
		}

		[Test]
		public void Clear_ResetsMembershipSoSpawnersCanRejoin()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(DateTime.UtcNow.AddSeconds(600.0));
			scheduler.Refresh(spawner);

			scheduler.Clear();
			Assert.AreEqual(0, scheduler.ActiveCount);

			scheduler.Refresh(spawner);

			Assert.AreEqual(1, scheduler.ActiveCount,
				"A cleared spawner still believing it is a member could never rejoin.");
		}

		// --- Ownership ---------------------------------------------------------------------------

		[Test]
		public void Despawn_SchedulesARespawnAndReleasesTheObject()
		{
			SpawnerRuntime spawner = NewSpawner(maxSpawnCount: 2);
			FakeSpawnable spawned = new FakeSpawnable(7);
			spawner.Track(spawned, null);

			Assert.AreSame(spawner, spawned.Spawner, "Tracking must make the spawner the object's owner.");

			spawned.Despawn();

			Assert.AreEqual(0, spawner.SpawnedCount);
			Assert.AreEqual(1, spawner.PendingRespawnCount, "The death is what schedules the respawn.");
			Assert.IsNull(spawned.Spawner, "A despawned object must not keep a route back to its spawner.");
			Assert.AreEqual(1, scheduler.ActiveCount);
		}

		[Test]
		public void DespawningTwice_SchedulesOneRespawn()
		{
			/* A corpse decaying while the death handler also fires reaches Despawn twice. Each extra
			 * pass used to queue another timer, and the spawner slowly over-spawned. */
			SpawnerRuntime spawner = NewSpawner(maxSpawnCount: 2);
			FakeSpawnable spawned = new FakeSpawnable(7);
			spawner.Track(spawned, null);

			spawner.Despawn(spawned);
			spawner.Despawn(spawned);

			Assert.AreEqual(1, spawner.PendingRespawnCount);
		}
	}
}
