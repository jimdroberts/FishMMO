using System.Collections;
using FishMMO.TestHarness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FishMMO.UnitTests.PlayMode
{
	/// <summary>
	/// Runs the twin-world platform simulation headlessly and asserts what a human watching the
	/// scene would verify by eye: nobody falls through a moving deck, rollback is an identity,
	/// and the platform phases stay aligned — at zero latency, at a mid latency, and at the cap.
	/// </summary>
	/// <remarks>
	/// This drives the exact same <c>PlatformSimHarness</c> the visual scene uses (the scene is a
	/// bootstrap around this component), so a green run here and a green banner in the scene are
	/// the same fact. Each latency step runs ~40 simulated seconds at 10x.
	/// </remarks>
	public class PlatformSimPlayModeTests
	{
		private GameObject host;

		/* Runs even when the runner ABANDONS a failed test's iterator (an unhandled error log
		 * fails a UnityTest at the next yield without unwinding it, so try/finally alone does
		 * not fire). A leaked harness keeps logging into every later test and its NetworkManager
		 * makes FishNet destroy the next sim's manager as a duplicate. */
		[UnityTearDown]
		public IEnumerator TearDownHost()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
				host = null;
			}
			yield return null;
		}

		[UnityTest]
		public IEnumerator PlatformRiding_HoldsAcrossTheLatencyRange()
		{
			host = new GameObject("PlatformSimUnderTest");
			PlatformSimHarness harness = host.AddComponent<PlatformSimHarness>();
			harness.TimeScale = 10f;
			harness.AlwaysReconcile = true;

			// Let Start() build the worlds.
			yield return null;

			try
			{
			foreach (int rtt in new[] { 0, 250, 500 })
			{
				harness.RttMs = rtt;
				harness.ResetSimulation();

				float deadline = Time.realtimeSinceStartup + 4f;
				float nextStatus = Time.realtimeSinceStartup + 1f;
				while (Time.realtimeSinceStartup < deadline)
				{
					if (Time.realtimeSinceStartup >= nextStatus)
					{
						nextStatus += 1f;
						Debug.Log($"[PlatformSimTest] RTT {rtt}ms: {harness.DebugStatus}");
					}
					yield return null;
				}

				Assert.Greater((int)harness.ClientTick, 600,
					$"RTT {rtt}ms: the simulation must actually have run (got {harness.ClientTick} ticks).");

				/* The guard against a vacuous pass: an earlier version of this scenario green-lit
				 * a rider that never managed to board the ferry at all (it chased the departing
				 * deck, fell, and walked back forever). The ride is the point of the scene, so at
				 * least one full island-to-island crossing must complete per leg. */
				Assert.GreaterOrEqual(harness.CompletedCrossings, 1,
					$"RTT {rtt}ms: the rider never completed a ferry crossing — it is not actually " +
					"riding the moving platform this scene exists to exercise.");

				/* The contract, held exactly as the live game holds it: nobody falls, on either
				 * side. Rollback identity is a hard error only at zero delay, where the world
				 * cannot have moved under the replay and any divergence is real nondeterminism;
				 * at latency the same divergence is counted as an edge replay and asserted just
				 * below. */
				Assert.AreEqual(0, harness.TotalFallThroughs,
					$"RTT {rtt}ms: a rider fell and stayed fallen (or the SERVER fell at all) — the exact " +
					"regression this scene guards.");
				if (rtt == 0)
				{
					Assert.AreEqual(0L, harness.IdentityFailures,
						"RTT 0ms: rollback+replay of an agreeing snapshot must land exactly on the live state; " +
						"a drift here means the simulation reads something outside its reconciled state.");
				}

				/* Issue #228. A replay must reproduce the world of the tick it is replaying, and
				 * the platforms are part of that world: they roll back with the rider, so a replay
				 * of an agreeing snapshot is an identity AT EVERY LATENCY, not just at zero. It
				 * was not before — with the decks left standing in the present, every near-edge
				 * replay probed geometry up to a round trip downstream and landed somewhere else
				 * (measured on this scene: 27 at 250ms, 47 at 500ms over ~900 ticks), which in the
				 * live game is a rider sinking through the deck it is standing on. A nonzero count
				 * here means the geometry rewind has been lost. */
				Assert.AreEqual(0L, harness.EdgeReplayDivergences,
					$"RTT {rtt}ms: replays diverged from the live state they replaced — the platforms are " +
					"no longer rolling back with the rider (issue #228).");
				Assert.Less(harness.MaxPlatformPhaseError, 0.05f,
					$"RTT {rtt}ms: client and server platforms diverged in phase at matched ticks — the payload " +
					"catch-up contract is broken.");
			}
			}
			finally
			{
				// A failed assert must still tear the harness down, or its Update loop (and any
				// error logging) leaks into every later test in the session.
				Object.DestroyImmediate(host);
			}
			yield return null;
		}
	}
}
