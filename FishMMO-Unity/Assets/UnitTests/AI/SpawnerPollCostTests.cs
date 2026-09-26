using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Measures what a <see cref="SpawnerRuntime"/> respawn poll actually costs, so the choice of
	/// scheduler is settled with numbers rather than intuition.
	/// </summary>
	/// <remarks>
	/// The steady state for a live spawner is "some objects alive, their respawn deadlines still in
	/// the future" — the poll walks the timer list, finds nothing due, and returns. That is the case
	/// that used to run on every spawner on every frame, so it is the one worth pricing.
	/// </remarks>
	[TestFixture]
	public class SpawnerPollCostTests
	{
		private const int ITERATIONS = 200000;

		private static SpawnerRuntime Build(int pendingTimers)
		{
			SpawnerRuntime spawner = SpawnerTestKit.NewRuntime(new SpawnerScheduler(), maxSpawnCount: 20);
			for (int i = 0; i < pendingTimers; ++i)
			{
				// Not yet due: the steady state, where the poll scans and finds nothing to do.
				spawner.AddRespawnTimer(spawner.Scheduler.Now + 300.0);
			}
			return spawner;
		}

		/// <summary>Nanoseconds per <see cref="SpawnerRuntime.TryRespawn()"/> call.</summary>
		private static double MeasurePoll(int pendingTimers)
		{
			SpawnerRuntime spawner = Build(pendingTimers);

			// Warm up the JIT before timing.
			for (int i = 0; i < 1000; ++i)
			{
				spawner.TryRespawn();
			}

			Stopwatch watch = Stopwatch.StartNew();
			for (int i = 0; i < ITERATIONS; ++i)
			{
				spawner.TryRespawn();
			}
			watch.Stop();

			return watch.Elapsed.TotalMilliseconds * 1000000.0 / ITERATIONS;
		}

		[Test]
		public void Measure_RespawnPollCost()
		{
			double empty = MeasurePoll(0);
			double typical = MeasurePoll(10);
			double large = MeasurePoll(20);

			TestContext.WriteLine($"MEASURE spawner.pollEmptyNs = {empty:F0}");
			TestContext.WriteLine($"MEASURE spawner.poll10TimersNs = {typical:F0}");
			TestContext.WriteLine($"MEASURE spawner.poll20TimersNs = {large:F0}");

			/* Invocation counts, which is the part the interval gate changes. The poll itself is
			 * unchanged; it simply stops running on every frame. */
			const double frameRate = 45.0;
			const double meanInterval = 4.5; // midpoint of the authored 3-6s range
			double before = frameRate;
			double after = 1.0 / meanInterval;

			TestContext.WriteLine($"MEASURE spawner.pollsPerSecondBefore = {before:F2}");
			TestContext.WriteLine($"MEASURE spawner.pollsPerSecondAfter = {after:F3}");
			TestContext.WriteLine($"MEASURE spawner.invocationReduction = {before / after:F0}");

			foreach (int spawners in new[] { 100, 1000, 5000 })
			{
				double msBefore = spawners * before * typical / 1000000.0;
				double msAfter = spawners * after * typical / 1000000.0;
				TestContext.WriteLine(
					$"MEASURE spawner.msPerSecondAt{spawners} = before {msBefore:F2} / after {msAfter:F3}");
			}

			Assert.Greater(typical, 0.0, "The poll should take measurable time.");
			Assert.Greater(large, empty, "A longer timer list should cost more to scan.");
		}
	}
}
