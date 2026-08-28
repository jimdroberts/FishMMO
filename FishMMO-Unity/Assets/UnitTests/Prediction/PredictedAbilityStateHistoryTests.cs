using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Covers the per-tick predicted-state ring that <c>AbilityController.OnReconcile</c> compares
	/// server reconciles against, and the observer fast-forward arithmetic.
	/// </summary>
	[TestFixture]
	public class PredictedAbilityStateHistoryTests
	{
		[Test]
		public void Record_ThenTryGet_ReturnsSameTickState()
		{
			AuthTestTrace.LogTestStart(nameof(Record_ThenTryGet_ReturnsSameTickState),
				"A recorded tick is returned with the seed and ability id recorded for it.")
				.GetAwaiter().GetResult();

			var history = new PredictedAbilityStateHistory(16);
			history.Record(100u, 1234, 77L);

			bool found = history.TryGet(100u, out int seed, out long abilityID);

			LogAssert.IsTrue(found, "Tick 100 was recorded and must be found.");
			LogAssert.AreEqual(1234, seed, "Seed must round-trip.");
			LogAssert.AreEqual(77L, abilityID, "Ability id must round-trip.");
		}

		[Test]
		public void TryGet_UnrecordedTick_ReturnsFalse()
		{
			AuthTestTrace.LogTestStart(nameof(TryGet_UnrecordedTick_ReturnsFalse),
				"A tick that was never recorded is not found, even when its slot is empty.")
				.GetAwaiter().GetResult();

			var history = new PredictedAbilityStateHistory(16);

			LogAssert.IsFalse(history.TryGet(5u, out _, out _), "Nothing recorded; nothing found.");
		}

		[Test]
		public void TryGet_SlotOverwrittenByLaterTick_DoesNotReturnStaleState()
		{
			AuthTestTrace.LogTestStart(nameof(TryGet_SlotOverwrittenByLaterTick_DoesNotReturnStaleState),
				"A tick that aliases to the same slot as an older tick must not return the older tick's state.")
				.GetAwaiter().GetResult();

			var history = new PredictedAbilityStateHistory(16);
			history.Record(3u, 1, 1L);
			history.Record(3u + 16u, 2, 2L); // same slot

			bool oldFound = history.TryGet(3u, out _, out _);
			bool newFound = history.TryGet(19u, out int seed, out long abilityID);

			LogAssert.IsFalse(oldFound, "The older tick's slot was reused; it must report missing rather than the newer state.");
			LogAssert.IsTrue(newFound, "The newer tick must be found.");
			LogAssert.AreEqual(2, seed, "The newer tick's seed must be returned.");
			LogAssert.AreEqual(2L, abilityID, "The newer tick's ability id must be returned.");
		}

		[Test]
		public void Record_SameTickTwice_LatestWins()
		{
			AuthTestTrace.LogTestStart(nameof(Record_SameTickTwice_LatestWins),
				"A replayed tick re-records; the later simulation is what the next reconcile compares with.")
				.GetAwaiter().GetResult();

			var history = new PredictedAbilityStateHistory(16);
			history.Record(42u, 10, 5L);
			history.Record(42u, 11, 0L);

			history.TryGet(42u, out int seed, out long abilityID);

			LogAssert.AreEqual(11, seed, "The replayed seed must replace the first.");
			LogAssert.AreEqual(0L, abilityID, "The replayed ability id must replace the first.");
		}

		[Test]
		public void Clear_ForgetsEverything()
		{
			AuthTestTrace.LogTestStart(nameof(Clear_ForgetsEverything),
				"After Clear no tick is found.")
				.GetAwaiter().GetResult();

			var history = new PredictedAbilityStateHistory(16);
			history.Record(1u, 1, 1L);
			history.Clear();

			LogAssert.IsFalse(history.TryGet(1u, out _, out _), "Cleared history must not return recorded ticks.");
		}

		[Test]
		public void Capacity_RoundsUpToPowerOfTwo()
		{
			AuthTestTrace.LogTestStart(nameof(Capacity_RoundsUpToPowerOfTwo),
				"Capacity is rounded up so tick & mask indexing is valid.")
				.GetAwaiter().GetResult();

			LogAssert.AreEqual(128, new PredictedAbilityStateHistory(100).Capacity, "100 rounds up to 128.");
			LogAssert.AreEqual(2, new PredictedAbilityStateHistory(1).Capacity, "Minimum capacity is 2.");
			LogAssert.AreEqual(64, new PredictedAbilityStateHistory(64).Capacity, "A power of two is kept.");
		}

		[Test]
		public void TickWrap_RecordsAndFindsAcrossUIntBoundary()
		{
			AuthTestTrace.LogTestStart(nameof(TickWrap_RecordsAndFindsAcrossUIntBoundary),
				"Ticks near uint.MaxValue index cleanly and are found.")
				.GetAwaiter().GetResult();

			var history = new PredictedAbilityStateHistory(16);
			history.Record(uint.MaxValue, 9, 9L);
			history.Record(0u, 10, 10L);

			LogAssert.IsTrue(history.TryGet(uint.MaxValue, out int seedMax, out _), "uint.MaxValue must be found.");
			LogAssert.IsTrue(history.TryGet(0u, out int seedZero, out _), "0 must be found.");
			LogAssert.AreEqual(9, seedMax, "uint.MaxValue keeps its own state.");
			LogAssert.AreEqual(10, seedZero, "0 keeps its own state.");
		}

		// ── Observer fast-forward ──

		private static uint FastForward(uint estimatedServerTick, uint serverSpawnTick, uint interpolationTicks)
		{
			MethodInfo method = typeof(AbilityController).GetMethod("ComputeObserverFastForwardTicks",
				BindingFlags.Static | BindingFlags.NonPublic);
			LogAssert.IsNotNull(method, "AbilityController.ComputeObserverFastForwardTicks must exist.");
			return (uint)method.Invoke(null, new object[] { estimatedServerTick, serverSpawnTick, interpolationTicks });
		}

		[Test]
		public void FastForward_SubtractsInterpolationFromTransitDelay()
		{
			AuthTestTrace.LogTestStart(nameof(FastForward_SubtractsInterpolationFromTransitDelay),
				"An observer 10 ticks behind the spawn that renders peers 2 ticks late fast-forwards 8 ticks.")
				.GetAwaiter().GetResult();

			LogAssert.AreEqual(8u, FastForward(1010u, 1000u, 2u), "10 - 2 = 8.");
		}

		[Test]
		public void FastForward_ClampsToZero_WhenEstimateLagsSpawn()
		{
			AuthTestTrace.LogTestStart(nameof(FastForward_ClampsToZero_WhenEstimateLagsSpawn),
				"A server-tick estimate at or behind the spawn, or within the interpolation window, fast-forwards nothing.")
				.GetAwaiter().GetResult();

			LogAssert.AreEqual(0u, FastForward(999u, 1000u, 2u), "Estimate behind spawn clamps to 0.");
			LogAssert.AreEqual(0u, FastForward(1001u, 1000u, 2u), "Inside the interpolation window clamps to 0.");
			LogAssert.AreEqual(0u, FastForward(1002u, 1000u, 2u), "Exactly the interpolation window is 0.");
		}

		[Test]
		public void FastForward_HandlesTickWrap()
		{
			AuthTestTrace.LogTestStart(nameof(FastForward_HandlesTickWrap),
				"A spawn just before uint wrap and an estimate just after it still measure the small positive gap.")
				.GetAwaiter().GetResult();

			LogAssert.AreEqual(5u, FastForward(2u, uint.MaxValue - 2u, 0u), "Wrap-safe difference is 5.");
			LogAssert.AreEqual(3u, FastForward(2u, uint.MaxValue - 2u, 2u), "5 - 2 = 3 across the wrap.");
		}
	}
}
