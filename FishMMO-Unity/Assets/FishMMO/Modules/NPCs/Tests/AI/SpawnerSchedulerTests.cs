using System;
using System.Collections.Generic;
using System.Linq;
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
			spawner.AddRespawnTimer(scheduler.Now - 1);

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
			spawner.AddRespawnTimer(scheduler.Now + 30.0);

			scheduler.Refresh(spawner);

			Assert.AreEqual(1, scheduler.ActiveCount);
		}

		[Test]
		public void RefreshingTwice_AddsTheSpawnerOnce()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(scheduler.Now + 30.0);

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
			spawner.AddRespawnTimer(scheduler.Now - 1.0);
			scheduler.Refresh(spawner);

			scheduler.Tick(scheduler.Now);

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
			spawner.AddRespawnTimer(scheduler.Now + 30.0);

			scheduler.Refresh(spawner);

			Assert.AreEqual(1, scheduler.ActiveCount);
			Assert.AreEqual(0, other.ActiveCount);
		}

		// --- Deadlines --------------------------------------------------------------------------

		[Test]
		public void TickBeforeTheDeadline_LeavesTheTimerAlone()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(scheduler.Now + 30.0);
			scheduler.Refresh(spawner);

			scheduler.Tick(scheduler.Now);

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
			double past = scheduler.Now - 1.0;
			for (int i = 0; i < 5; ++i)
			{
				spawner.AddRespawnTimer(past);
			}
			scheduler.Refresh(spawner);

			scheduler.Tick(scheduler.Now);

			Assert.AreEqual(0, spawner.PendingRespawnCount,
				"Every elapsed deadline should be honoured in the pass that finds it.");
		}

		[Test]
		public void TickAfterTheDeadline_LeavesTimersThatAreStillInTheFuture()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(scheduler.Now - 1.0);
			spawner.AddRespawnTimer(scheduler.Now + 600.0);
			scheduler.Refresh(spawner);

			scheduler.Tick(scheduler.Now);

			Assert.AreEqual(1, spawner.PendingRespawnCount,
				"A deadline ten minutes out is not due now.");
		}

		[Test]
		public void ASpawnerWithWorkRemaining_StaysInTheActiveList()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(scheduler.Now - 1.0);
			spawner.AddRespawnTimer(scheduler.Now + 600.0);
			scheduler.Refresh(spawner);

			scheduler.Tick(scheduler.Now);

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
			spawner.AddRespawnTimer(scheduler.Now - 1.0);
			spawner.Definition.TrueConditions.Add(new CountingRespawnCondition { Allow = false });
			scheduler.Refresh(spawner);

			scheduler.Tick(scheduler.Now);

			Assert.AreEqual(1, spawner.PendingRespawnCount,
				"A refused respawn must not consume its deadline.");
			Assert.AreEqual(1, scheduler.ActiveCount,
				"A refused spawner must stay in the list, or it is never asked again.");
		}

		[Test]
		public void ARefusedRespawn_ProceedsOnceTheConditionClears()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(scheduler.Now - 1.0);
			CountingRespawnCondition condition = new CountingRespawnCondition { Allow = false };
			spawner.Definition.TrueConditions.Add(condition);
			scheduler.Refresh(spawner);

			scheduler.Tick(scheduler.Now);
			Assert.AreEqual(1, spawner.PendingRespawnCount, "Still blocked.");

			condition.Allow = true;
			scheduler.Tick(scheduler.Now);

			Assert.AreEqual(0, spawner.PendingRespawnCount,
				"The boss is dead; the camp should come back.");
		}

		[Test]
		public void AnOrListWithNoAllowingCondition_RefusesTheRespawn()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(scheduler.Now - 1.0);
			spawner.Definition.OrConditions.Add(new CountingRespawnCondition { Allow = false });
			spawner.Definition.OrConditions.Add(new CountingRespawnCondition { Allow = false });
			scheduler.Refresh(spawner);

			scheduler.Tick(scheduler.Now);

			Assert.AreEqual(1, spawner.PendingRespawnCount);

			spawner.Definition.OrConditions.Add(new CountingRespawnCondition { Allow = true });
			scheduler.Tick(scheduler.Now);

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
			double past = scheduler.Now - 1.0;
			for (int i = 0; i < 6; ++i)
			{
				spawner.AddRespawnTimer(past);
			}
			CountingRespawnCondition condition = new CountingRespawnCondition { Allow = true };
			spawner.Definition.TrueConditions.Add(condition);
			scheduler.Refresh(spawner);

			scheduler.Tick(scheduler.Now);

			Assert.AreEqual(1, condition.Calls,
				"Six due timers is still one question.");
		}

		// --- Membership bookkeeping --------------------------------------------------------------

		[Test]
		public void Unregister_TakesTheSpawnerOutOfTheWalk()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(scheduler.Now - 1.0);
			scheduler.Refresh(spawner);

			scheduler.Unregister(spawner);
			scheduler.Tick(scheduler.Now);

			Assert.AreEqual(1, spawner.PendingRespawnCount,
				"An unregistered spawner - an unloaded scene, say - must not be walked.");
		}

		[Test]
		public void Stop_TakesTheSpawnerOutOfTheWalkAndForgetsItsWork()
		{
			SpawnerRuntime spawner = NewSpawner();
			SpawnerTestKit.FillWith(spawner, 2);
			spawner.AddRespawnTimer(scheduler.Now + 600.0);
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
				s.AddRespawnTimer(scheduler.Now + 600.0);
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
				s.AddRespawnTimer(scheduler.Now - 1.0);
				CountingRespawnCondition condition = new CountingRespawnCondition { Allow = true };
				s.Definition.TrueConditions.Add(condition);
				conditions.Add(condition);
				scheduler.Refresh(s);
			}

			// One full sweep. The list is walked a slice at a time, so this is not one tick.
			double now = scheduler.Now;
			for (int frame = 0; frame < SpawnerScheduler.FramesPerSweep; ++frame)
			{
				scheduler.Tick(now);
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
				s.AddRespawnTimer(scheduler.Now - 1.0);
				CountingRespawnCondition condition = new CountingRespawnCondition { Allow = false };
				s.Definition.TrueConditions.Add(condition);
				conditions.Add(condition);
				scheduler.Refresh(s);
			}

			scheduler.Tick(scheduler.Now);

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

			leaves.AddRespawnTimer(scheduler.Now - 1.0);
			bystander.AddRespawnTimer(scheduler.Now + 600.0);

			scheduler.Refresh(leaves);
			scheduler.Refresh(bystander);

			// Unregisters itself mid-pass, then allows the respawn so it also finishes its work.
			leaves.Definition.TrueConditions.Add(new SelfUnregisteringRespawnCondition());

			scheduler.Tick(scheduler.Now);

			Assert.AreEqual(1, scheduler.ActiveCount,
				"The spawner swapped into the vacated slot was dropped with it.");
			Assert.AreNotEqual(SpawnerScheduler.NotActive, bystander.SchedulerIndex,
				"A spawner still waiting on a deadline must not be evicted by someone else leaving.");
		}

		[Test]
		public void Clear_ResetsMembershipSoSpawnersCanRejoin()
		{
			SpawnerRuntime spawner = NewSpawner();
			spawner.AddRespawnTimer(scheduler.Now + 600.0);
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

		// --- A respawn that does not happen is still owed ---------------------------------------

		[Test]
		public void ConsumesTimer_SpendsTheSlotOnlyWhenNothingIsStillOwed()
		{
			/* The truth table. The timer used to be removed before every attempt whatever came of
			 * it, so each failure left the spawner one short for the life of the scene. */
			Assert.IsTrue(SpawnerRuntime.ConsumesTimer(SpawnOutcome.Spawned));
			Assert.IsTrue(SpawnerRuntime.ConsumesTimer(SpawnOutcome.SpawnedUntracked),
				"Something entered the world; giving the slot back would spawn another every check.");
			Assert.IsTrue(SpawnerRuntime.ConsumesTimer(SpawnOutcome.AtCapacity));
			Assert.IsTrue(SpawnerRuntime.ConsumesTimer(SpawnOutcome.NothingEligible),
				"Every unique entry is alive; a death queues a fresh timer.");
			Assert.IsFalse(SpawnerRuntime.ConsumesTimer(SpawnOutcome.Misconfigured),
				"A random spawner that picked a bad entry picks again next time.");
			Assert.IsFalse(SpawnerRuntime.ConsumesTimer(SpawnOutcome.SceneNotLoaded));
			Assert.IsFalse(SpawnerRuntime.ConsumesTimer(SpawnOutcome.Failed));

			foreach (SpawnOutcome outcome in (SpawnOutcome[])Enum.GetValues(typeof(SpawnOutcome)))
			{
				// Forces a decision for any outcome added later.
				SpawnerRuntime.ConsumesTimer(outcome);
			}
		}

		[Test]
		public void ASpawnThatProducesNothing_KeepsItsTimerAndItsPlaceInTheList()
		{
			SpawnerRuntime spawner = SpawnerTestKit.NewRuntime(scheduler, spawn: _ => null);
			spawner.AddRespawnTimer(scheduler.Now - 1.0);
			scheduler.Refresh(spawner);

			scheduler.Tick(scheduler.Now);

			Assert.AreEqual(0, spawner.SpawnedCount);
			Assert.AreEqual(1, spawner.PendingRespawnCount, "The object is still owed.");
			Assert.AreEqual(1, scheduler.ActiveCount, "It must be asked again.");
		}

		[Test]
		public void ASpawnThatThrows_KeepsItsTimer_AndTheSweepCarriesOn()
		{
			/* The exception used to leave the sweep: every spawner after the broken one was skipped,
			 * at every check of the broken one, and its timer was already gone. */
			bool ignored = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			try
			{
				SpawnerRuntime broken = SpawnerTestKit.NewRuntime(scheduler, spawn: _ => throw new InvalidOperationException("OnSpawned failed"));
				SpawnerRuntime healthy = NewSpawner();
				broken.AddRespawnTimer(scheduler.Now - 1.0);
				healthy.AddRespawnTimer(scheduler.Now - 1.0);
				scheduler.Refresh(broken);
				scheduler.Refresh(healthy);

				for (int frame = 0; frame < SpawnerScheduler.FramesPerSweep; ++frame)
				{
					Assert.DoesNotThrow(() => scheduler.Tick(scheduler.Now), "A spawner's exception must not leave the sweep.");
				}

				Assert.AreEqual(1, healthy.SpawnedCount, "The spawner after the broken one was skipped.");
				Assert.AreEqual(0, broken.SpawnedCount);
				Assert.AreEqual(1, broken.PendingRespawnCount, "The broken spawner's respawn is still owed.");
				Assert.AreEqual(1, scheduler.ActiveCount, "Only the broken spawner still has work.");
			}
			finally
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = ignored;
			}
		}

		[Test]
		public void AnInitialSpawnThatThrows_LeavesItsSlotsOwedAsRespawns()
		{
			int calls = 0;
			SpawnerRuntime spawner = SpawnerTestKit.NewRuntime(scheduler, maxSpawnCount: 5, tune: d => d.InitialSpawnCount = 3,
				spawn: _ => ++calls == 2 ? throw new InvalidOperationException("second spawn failed") : new FakeSpawnable(500 + calls));

			Assert.Throws<InvalidOperationException>(() => spawner.Start());

			Assert.IsTrue(spawner.Running);
			Assert.AreEqual(1, spawner.SpawnedCount);
			Assert.AreEqual(4, spawner.PendingRespawnCount, "Every slot the start did not fill must be owed.");
			Assert.AreEqual(1, scheduler.ActiveCount, "A spawner whose start failed must still be scheduled.");
		}

		[Test]
		public void AnEmptyEntry_DoesNotCostTheSpawnerASlotAtStart()
		{
			/* The start picks an entry for each unfilled slot's respawn delay, and used to skip the
			 * slot when the pick was empty: a list with a blank in it stood below its maximum for
			 * the life of the scene. */
			SpawnerRuntime spawner = SpawnerTestKit.NewRuntime(scheduler, maxSpawnCount: 4, tune: d =>
			{
				d.SpawnType = ObjectSpawnType.Linear;
				d.Spawnables = new List<SpawnableSettings> { null, new ItemSpawnableSettings() };
			});

			spawner.Start();

			Assert.AreEqual(4, spawner.PendingRespawnCount);
		}

		// --- The spawn budget --------------------------------------------------------------------

		[Test]
		public void TheSpawnBudget_SpreadsARefillOverFrames_AndTheCutSpawnerFinishesFirst()
		{
			scheduler.SpawnsPerFrame = 3;
			SpawnerRuntime camp = NewSpawner();
			SpawnerRuntime other = NewSpawner();
			for (int i = 0; i < 7; ++i)
			{
				camp.AddRespawnTimer(scheduler.Now - 1.0);
			}
			other.AddRespawnTimer(scheduler.Now - 1.0);
			scheduler.Refresh(camp);
			scheduler.Refresh(other);

			scheduler.Tick(scheduler.Now);
			Assert.AreEqual(3, camp.SpawnedCount, "One frame's budget.");

			scheduler.Tick(scheduler.Now);
			Assert.AreEqual(6, camp.SpawnedCount, "Cut short, it must carry on next frame, not an interval later.");
			Assert.AreEqual(0, other.SpawnedCount);

			scheduler.Tick(scheduler.Now);
			Assert.AreEqual(7, camp.SpawnedCount);
			Assert.AreEqual(0, camp.PendingRespawnCount);

			scheduler.Tick(scheduler.Now);
			Assert.AreEqual(1, other.SpawnedCount, "The next spawner's turn comes once the first is done.");
			Assert.AreEqual(0, scheduler.ActiveCount);
		}

		[Test]
		public void TheSpawnBudget_CannotStarveASpawner()
		{
			scheduler.SpawnsPerFrame = 1;
			List<SpawnerRuntime> spawners = new List<SpawnerRuntime>();
			for (int s = 0; s < 4; ++s)
			{
				SpawnerRuntime spawner = NewSpawner();
				for (int i = 0; i < 5; ++i)
				{
					spawner.AddRespawnTimer(scheduler.Now - 1.0);
				}
				scheduler.Refresh(spawner);
				spawners.Add(spawner);
			}

			// Twenty spawns at one a frame; each frame's first spawner always gets the budget.
			for (int frame = 0; frame < 20; ++frame)
			{
				scheduler.Tick(scheduler.Now);
			}

			foreach (SpawnerRuntime spawner in spawners)
			{
				Assert.AreEqual(5, spawner.SpawnedCount, "A spawner was starved by the budget.");
			}
			Assert.AreEqual(0, scheduler.ActiveCount);
		}

		// --- The clock ----------------------------------------------------------------------------

		[Test]
		public void Deadlines_AreMeasuredOnTheSchedulersClock()
		{
			/* Respawn deadlines were DateTime.UtcNow values, so a step in the host's wall clock
			 * fired every respawn in the world at once. They are durations on the scheduler's own
			 * clock now, which a test can hold still. */
			double now = 1000.0;
			SpawnerScheduler held = new SpawnerScheduler(() => now);
			SpawnerRuntime spawner = SpawnerTestKit.NewRuntime(held, tune: d =>
			{
				d.Spawnables = new List<SpawnableSettings> { new ItemSpawnableSettings { MinimumRespawnTime = 60f, MaximumRespawnTime = 60f } };
			});
			spawner.Start();
			Assert.AreEqual(10, spawner.PendingRespawnCount);

			now = 1059.0;
			held.Tick(now);
			Assert.AreEqual(0, spawner.SpawnedCount, "Not yet a minute on the scheduler's clock.");

			now = 1060.0;
			for (int frame = 0; frame < 2; ++frame)
			{
				held.Tick(now);
			}
			Assert.AreEqual(10, spawner.SpawnedCount, "A minute has passed on the scheduler's clock.");
		}

		// --- Scene starts ---------------------------------------------------------------------------

		[Test]
		public void SceneStarts_AreSpreadByTheBudget_AndTheSceneIsScheduledOnlyOnceAllHaveStarted()
		{
			SpawnerHost host = new SpawnerHost(null, null) { StartWorkPerFrame = 2 };
			SceneSpawnTable table = UnityEngine.ScriptableObject.CreateInstance<SceneSpawnTable>();
			UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
			try
			{
				for (int i = 0; i < 5; ++i)
				{
					table.Spawners.Add(new SpawnerDefinition
					{
						Name = "S" + i,
						// Owed but not due, so nothing tries to spawn without a network.
						Spawnables = new List<SpawnableSettings> { new ItemSpawnableSettings { MinimumRespawnTime = 600f, MaximumRespawnTime = 600f } },
					});
				}

				host.QueueScene(scene, table);
				Assert.IsTrue(host.TryGetScene(scene.handle, out IReadOnlyList<SpawnerRuntime> spawners));

				host.Tick(host.Scheduler.Now);
				Assert.AreEqual(2, spawners.Count(s => s.Running), "Two starts fit one frame's budget.");
				Assert.AreEqual(0, host.Scheduler.ActiveCount, "No spawner is scheduled before its whole scene has started.");

				host.Tick(host.Scheduler.Now);
				Assert.AreEqual(4, spawners.Count(s => s.Running));
				Assert.AreEqual(0, host.Scheduler.ActiveCount);

				host.Tick(host.Scheduler.Now);
				Assert.AreEqual(5, spawners.Count(s => s.Running));
				Assert.AreEqual(5, host.Scheduler.ActiveCount, "The whole scene joins the schedule together.");
				Assert.AreEqual(0, host.StartingSceneCount);

				host.StopScene(scene.handle);
				Assert.AreEqual(0, host.Scheduler.ActiveCount);
			}
			finally
			{
				UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
				UnityEngine.Object.DestroyImmediate(table);
			}
		}

		[Test]
		public void AnUnloadedScene_LeavesTheStartQueue()
		{
			SpawnerHost host = new SpawnerHost(null, null) { StartWorkPerFrame = 1 };
			SceneSpawnTable table = UnityEngine.ScriptableObject.CreateInstance<SceneSpawnTable>();
			UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
			try
			{
				for (int i = 0; i < 3; ++i)
				{
					table.Spawners.Add(new SpawnerDefinition { Name = "S" + i, Spawnables = new List<SpawnableSettings> { new ItemSpawnableSettings { MinimumRespawnTime = 600f, MaximumRespawnTime = 600f } } });
				}

				host.QueueScene(scene, table);
				host.Tick(host.Scheduler.Now);
				Assert.AreEqual(1, host.StartingSceneCount);

				host.StopScene(scene.handle);

				Assert.AreEqual(0, host.StartingSceneCount, "A scene that unloaded mid-start must not keep starting.");
				Assert.AreEqual(0, host.SceneCount);
				host.Tick(host.Scheduler.Now);
				Assert.AreEqual(0, host.Scheduler.ActiveCount);
			}
			finally
			{
				UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
				UnityEngine.Object.DestroyImmediate(table);
			}
		}
	}
}
