using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;

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

		/// <summary>
		/// A spawner whose <see cref="SpawnableSettings.NetworkObject"/> is null, so
		/// <see cref="ObjectSpawner.SpawnObject"/> reaches its own guard and no-ops. The timer
		/// bookkeeping under test still runs in full.
		/// </summary>
		private static ObjectSpawner BuildSpawner(GameObject go, int maxSpawnCount)
		{
			ObjectSpawner spawner = go.AddComponent<ObjectSpawner>();
			spawner.Spawnables = new List<SpawnableSettings> { new SpawnableSettings() };
			spawner.MaxSpawnCount = maxSpawnCount;
			spawner.SpawnableRespawnTimers.Clear();
			return spawner;
		}

		[Test]
		public void TryRespawn_DrainsEveryDueTimerInOnePass()
		{
			/* One spawn per call was invisible while this ran every frame — ten frames refilled a
			 * ten-monster camp. Behind a polling interval the same cap becomes one monster per
			 * interval, so a wiped camp takes the better part of a minute and no authored
			 * MinimumRespawnTime can raise the ceiling. */
			GameObject go = new GameObject("SpawnerDrain");
			try
			{
				ObjectSpawner spawner = BuildSpawner(go, maxSpawnCount: 10);
				DateTime overdue = DateTime.UtcNow.AddSeconds(-1.0);
				for (int i = 0; i < 5; ++i)
				{
					spawner.SpawnableRespawnTimers.Add(overdue);
				}

				spawner.TryRespawn();

				Assert.AreEqual(0, spawner.SpawnableRespawnTimers.Count,
					"Every due timer should be consumed in one pass, not just the first.");
			}
			finally { UnityEngine.Object.DestroyImmediate(go); }
		}

		[Test]
		public void TryRespawn_LeavesTimersThatAreNotYetDue()
		{
			// Draining must stay selective: a deadline in the future is not a deadline that passed.
			GameObject go = new GameObject("SpawnerDrainSelective");
			try
			{
				ObjectSpawner spawner = BuildSpawner(go, maxSpawnCount: 10);
				for (int i = 0; i < 3; ++i)
				{
					spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddSeconds(-1.0));
				}
				for (int i = 0; i < 2; ++i)
				{
					spawner.SpawnableRespawnTimers.Add(DateTime.UtcNow.AddMinutes(5.0));
				}

				spawner.TryRespawn();

				Assert.AreEqual(2, spawner.SpawnableRespawnTimers.Count,
					"Only the overdue timers should have been consumed.");
				foreach (DateTime remaining in spawner.SpawnableRespawnTimers)
				{
					Assert.Greater(remaining, DateTime.UtcNow, "A future timer was consumed.");
				}
			}
			finally { UnityEngine.Object.DestroyImmediate(go); }
		}

		// --- Respawn check interval -----------------------------------------------------------

		/// <summary>Drives the private scheduler and returns the delay it chose.</summary>
		private static float ScheduleAndMeasure(float minimum, float maximum)
		{
			GameObject go = new GameObject("SpawnerInterval");
			try
			{
				ObjectSpawner spawner = go.AddComponent<ObjectSpawner>();
				spawner.RespawnCheckIntervalMinimum = minimum;
				spawner.RespawnCheckIntervalMaximum = maximum;

				System.Type type = typeof(ObjectSpawner);
				type.GetMethod("ScheduleNextRespawnCheck", BindingFlags.Instance | BindingFlags.NonPublic)
					.Invoke(spawner, null);
				float next = (float)type.GetField("nextRespawnCheckTime", BindingFlags.Instance | BindingFlags.NonPublic)
					.GetValue(spawner);

				return next - Time.time;
			}
			finally { UnityEngine.Object.DestroyImmediate(go); }
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
			 * the past. That would put Update back to calling TryRespawn every frame — silently,
			 * with no error and no symptom other than the cost this interval exists to avoid. */
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
			ObjectSpawnerPool.Clear();

			Assert.AreEqual(0, ObjectSpawnerPool.Reserve(null, null, 10));
			Assert.AreEqual(0, ObjectSpawnerPool.TotalReserved);
		}

		[Test]
		public void PoolReservation_IgnoresNonPositiveCounts()
		{
			ObjectSpawnerPool.Clear();

			Assert.AreEqual(0, ObjectSpawnerPool.Reserve(null, null, 0));
			Assert.AreEqual(0, ObjectSpawnerPool.Reserve(null, null, -5));
		}

		[Test]
		public void PoolReservation_ClearResetsTheRunningTotal()
		{
			ObjectSpawnerPool.Clear();

			Assert.AreEqual(0, ObjectSpawnerPool.TotalReserved,
				"A stale total across scene loads would misreport the map's memory budget.");
		}
	}
}
