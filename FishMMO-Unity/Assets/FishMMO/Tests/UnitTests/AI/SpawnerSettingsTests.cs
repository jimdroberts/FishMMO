using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs for the spawner settings that decide what a recycled object becomes when it comes
	/// back out of the pool.
	/// </summary>
	/// <remarks>
	/// The rolling logic is pure arithmetic over serialized fields, so it is directly testable —
	/// and it is exactly the code that a misconfigured inspector value turns into an exception at
	/// spawn time on a live server.
	/// </remarks>
	[TestFixture]
	public class SpawnerSettingsTests
	{
		// --- Respawn drain --------------------------------------------------------------------

		[Test]
		public void TryRespawn_DrainsEveryDueTimerInOnePass()
		{
			/* One spawn per call was invisible while this ran every frame — ten frames refilled a
			 * ten-monster camp. Behind a polling interval the same cap becomes one monster per
			 * interval, so a wiped camp takes the better part of a minute and no authored
			 * MinimumRespawnTime can raise the ceiling. */
			SpawnerRuntime spawner = SpawnerTestKit.NewRuntime(new SpawnerScheduler(), maxSpawnCount: 10);
			double overdue = spawner.Scheduler.Now - 1.0;
			for (int i = 0; i < 5; ++i)
			{
				spawner.AddRespawnTimer(overdue);
			}

			spawner.TryRespawn();

			Assert.AreEqual(0, spawner.PendingRespawnCount,
				"Every due timer should be consumed in one pass, not just the first.");
		}

		[Test]
		public void TryRespawn_LeavesTimersThatAreNotYetDue()
		{
			// Draining must stay selective: a deadline in the future is not a deadline that passed.
			SpawnerRuntime spawner = SpawnerTestKit.NewRuntime(new SpawnerScheduler(), maxSpawnCount: 10);
			for (int i = 0; i < 3; ++i)
			{
				spawner.AddRespawnTimer(spawner.Scheduler.Now - 1.0);
			}
			for (int i = 0; i < 2; ++i)
			{
				spawner.AddRespawnTimer(spawner.Scheduler.Now + 300.0);
			}

			spawner.TryRespawn();

			Assert.AreEqual(2, spawner.PendingRespawnCount,
				"Only the overdue timers should have been consumed.");
			List<double> remaining = (List<double>)typeof(SpawnerRuntime)
				.GetField("respawnTimers", BindingFlags.Instance | BindingFlags.NonPublic)
				.GetValue(spawner);
			foreach (double deadline in remaining)
			{
				Assert.Greater(deadline, spawner.Scheduler.Now, "A future timer was consumed.");
			}
		}

		// --- Respawn check interval -----------------------------------------------------------

		/// <summary>Drives the private scheduler and returns the delay it chose.</summary>
		private static float ScheduleAndMeasure(float minimum, float maximum)
		{
			SpawnerRuntime spawner = SpawnerTestKit.NewRuntime(new SpawnerScheduler(), tune: d =>
			{
				d.RespawnCheckIntervalMinimum = minimum;
				d.RespawnCheckIntervalMaximum = maximum;
			});

			System.Type type = typeof(SpawnerRuntime);
			double now = spawner.Scheduler.Now;
			type.GetMethod("ScheduleNextRespawnCheck", BindingFlags.Instance | BindingFlags.NonPublic)
				.Invoke(spawner, new object[] { now });
			double next = (double)type.GetField("nextRespawnCheckTime", BindingFlags.Instance | BindingFlags.NonPublic)
				.GetValue(spawner);

			return (float)(next - now);
		}

		[Test]
		public void RespawnCheck_SchedulesInsideTheConfiguredRange()
		{
			/* Sampled rather than checked once: the delay is random per pass, and a bound that is
			 * only occasionally violated is exactly the kind that survives a single assertion. */
			for (int i = 0; i < 50; ++i)
			{
				float delay = ScheduleAndMeasure(3.0f, 6.0f);

				Assert.GreaterOrEqual(delay, 3.0f, "Scheduled sooner than the configured minimum.");
				Assert.LessOrEqual(delay, 6.0f, "Scheduled later than the configured maximum.");
			}
		}

		[Test]
		public void RespawnCheck_RepairsABadIntervalInsteadOfPollingEveryFrame()
		{
			/* An inverted or negative range typed into the inspector must not resolve to a time in
			 * the past. That would put the spawner back to running TryRespawn on every sweep —
			 * silently, with no error and no symptom other than the cost this interval exists to
			 * avoid. */
			Assert.AreEqual(6.0f, ScheduleAndMeasure(6.0f, 3.0f), 0.001f,
				"An inverted range should clamp to the minimum, not invert or throw.");
			Assert.GreaterOrEqual(ScheduleAndMeasure(-5.0f, -1.0f), 0.0f,
				"A negative range must not schedule the next check in the past.");
		}

		// --- Item roll table ------------------------------------------------------------------

		[Test]
		public void ItemSettings_OnValidateRepairsAnInvertedStackRange()
		{
			/* Range's upper bound is exclusive and it throws when high < low, so an inverted range
			 * typed into the inspector is a spawn-time exception rather than a bad item. */
			ItemSpawnableSettings settings = new ItemSpawnableSettings
			{
				MinimumAmount = 10,
				MaximumAmount = 2,
			};

			settings.OnValidate();

			Assert.GreaterOrEqual(settings.MaximumAmount, settings.MinimumAmount);
		}

		[Test]
		public void ItemSettings_OnValidateRejectsAZeroStack()
		{
			ItemSpawnableSettings settings = new ItemSpawnableSettings
			{
				MinimumAmount = 0,
				MaximumAmount = 0,
			};

			settings.OnValidate();

			Assert.GreaterOrEqual(settings.MinimumAmount, 1,
				"A world item stack of zero is an item nobody can pick up.");
		}

		[Test]
		public void ItemSettings_OnValidateRepairsRollTableEntries()
		{
			ItemSpawnableSettings settings = new ItemSpawnableSettings();
			settings.RollTable.Add(new ItemSpawnableSettings.ItemRoll
			{
				MinimumAmount = 9,
				MaximumAmount = 1,
				Weight = -5f,
			});

			settings.OnValidate();

			ItemSpawnableSettings.ItemRoll entry = settings.RollTable[0];
			Assert.GreaterOrEqual(entry.MaximumAmount, entry.MinimumAmount);
			Assert.GreaterOrEqual(entry.Weight, 0f,
				"A negative weight corrupts the cumulative total and skews every other entry.");
		}

		[Test]
		public void ItemSettings_OnValidateToleratesNullRollEntries()
		{
			// An inspector list sized before its entries are filled in is normal, not an error.
			ItemSpawnableSettings settings = new ItemSpawnableSettings();
			settings.RollTable.Add(null);

			Assert.DoesNotThrow(() => settings.OnValidate());
		}

		// --- NPC settings ---------------------------------------------------------------------

		[Test]
		public void NPCSettings_OnValidateRepairsAnInvertedScaleRange()
		{
			NPCSpawnableSettings settings = new NPCSpawnableSettings
			{
				MinimumScale = 2f,
				MaximumScale = 0.5f,
			};

			settings.OnValidate();

			Assert.GreaterOrEqual(settings.MaximumScale, settings.MinimumScale);
		}

		[Test]
		public void NPCSettings_OnValidateRejectsANegativeScale()
		{
			NPCSpawnableSettings settings = new NPCSpawnableSettings
			{
				MinimumScale = -3f,
				MaximumScale = 1f,
			};

			settings.OnValidate();

			Assert.GreaterOrEqual(settings.MinimumScale, 0f);
		}

		[Test]
		public void NPCSettings_DefaultScaleLeavesThePrefabAlone()
		{
			/* 1..1 must be a no-op. Any other reading would silently rescale every NPC in the
			 * project the moment these settings were introduced. */
			NPCSpawnableSettings settings = new NPCSpawnableSettings();

			Assert.AreEqual(1f, settings.MinimumScale);
			Assert.AreEqual(1f, settings.MaximumScale);
		}

		[Test]
		public void NPCSettings_AbilitiesAreAdditiveByDefault()
		{
			/* Additive is the safe default: a spawner that grants one signature ability should not
			 * have to re-list everything the species already knows, and silently dropping the
			 * prefab's abilities would leave the NPC unable to fight. */
			NPCSpawnableSettings settings = new NPCSpawnableSettings();

			Assert.IsFalse(settings.ReplacePrefabAbilities);
			Assert.IsNotNull(settings.AdditionalAbilities);
		}

		// --- Pool reservation -----------------------------------------------------------------

		[Test]
		public void PoolReservation_IsANoOpWithoutANetworkManager()
		{
			// Called during scene start-up, where a manager is not guaranteed to exist yet.
			SpawnerPool.Clear();

			Assert.AreEqual(0, SpawnerPool.Reserve(null, null, 10));
			Assert.AreEqual(0, SpawnerPool.TotalReserved);
		}

		[Test]
		public void PoolReservation_IgnoresNonPositiveCounts()
		{
			SpawnerPool.Clear();

			Assert.AreEqual(0, SpawnerPool.Reserve(null, null, 0));
			Assert.AreEqual(0, SpawnerPool.Reserve(null, null, -5));
		}

		[Test]
		public void PoolReservation_ClearResetsTheRunningTotal()
		{
			SpawnerPool.Clear();

			Assert.AreEqual(0, SpawnerPool.TotalReserved,
				"A stale total across scene loads would misreport the map's memory budget.");
		}
	}
}
