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
		// --- Blocked-respawn retry interval ----------------------------------------------------

		/* Ported from the per-spawner respawn-check interval these assertions were written for.
		 * The scheduler removed that poll, but the same range-repair semantics still govern the
		 * one interval a spawner still owns: how soon it re-tests a respawn a condition refused. */

		/// <summary>Drives the private retry-delay resolver and returns the delay it chose.</summary>
		private static float ResolveRetryDelay(float minimum, float maximum)
		{
			GameObject go = new GameObject("SpawnerInterval");
			try
			{
				ObjectSpawner spawner = go.AddComponent<ObjectSpawner>();
				spawner.BlockedRetryIntervalMinimum = minimum;
				spawner.BlockedRetryIntervalMaximum = maximum;

				System.Type type = typeof(ObjectSpawner);
				return (float)type.GetMethod("ResolveBlockedRetryDelay", BindingFlags.Instance | BindingFlags.NonPublic)
					.Invoke(spawner, null);
			}
			finally { Object.DestroyImmediate(go); }
		}

		[Test]
		public void BlockedRetry_SchedulesInsideTheConfiguredRange()
		{
			/* Sampled rather than checked once: the delay is random per pass, and a bound that is
			 * only occasionally violated is exactly the kind that survives a single assertion. */
			for (int i = 0; i < 50; ++i)
			{
				float delay = ResolveRetryDelay(3.0f, 6.0f);

				Assert.GreaterOrEqual(delay, 3.0f, "Scheduled sooner than the configured minimum.");
				Assert.LessOrEqual(delay, 6.0f, "Scheduled later than the configured maximum.");
			}
		}

		[Test]
		public void BlockedRetry_RepairsABadIntervalInsteadOfRetryingForever()
		{
			/* An inverted or negative range typed into the inspector must not resolve to zero or
			 * less. Under the old poll that silently restored per-frame checking; under the
			 * scheduler it is worse than silent, because a zero delay schedules the re-test at the
			 * instant of the refusal and the clock does not advance inside a tick. */
			Assert.AreEqual(6.0f, ResolveRetryDelay(6.0f, 3.0f), 0.001f,
				"An inverted range should clamp to the minimum, not invert or throw.");
			Assert.Greater(ResolveRetryDelay(-5.0f, -1.0f), 0.0f,
				"A negative range must not resolve to an instantaneous retry.");
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
