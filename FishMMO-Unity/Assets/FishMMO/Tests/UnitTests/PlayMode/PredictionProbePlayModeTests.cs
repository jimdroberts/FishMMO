using System.Collections;
using FishMMO.TestHarness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FishMMO.UnitTests.PlayMode
{
	/// <summary>
	/// End-to-end proofs, on the combat sim's real server, of three prediction behaviours whose arithmetic the EditMode
	/// tests cover but whose wiring they cannot reach: a cast across ticks the server ran without input, a hit the owner
	/// claims, and an observed copy of a projectile running on the playback clock.
	/// </summary>
	/// <remarks>
	/// One sim boot serves all three (see <see cref="CombatSimBootstrap.RunPredictionProbe"/>): booting costs most of
	/// the test's time, and the probe takes the fighters off their own clocks, so nothing it measures is disturbed by
	/// the combat the sim otherwise runs. Cleanup is in <see cref="TearDownHost"/>, not a finally, for the reason
	/// <c>CombatSimPlayModeTests</c> gives: a failed UnityTest abandons its iterator.
	/// </remarks>
	public class PredictionProbePlayModeTests
	{
		private GameObject host;

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

		/// <summary>A UDP port nothing is bound to right now, from the operating system.</summary>
		private static ushort FreeUdpPort()
		{
			using (System.Net.Sockets.UdpClient probe = new System.Net.Sockets.UdpClient(0))
			{
				return (ushort)((System.Net.IPEndPoint)probe.Client.LocalEndPoint).Port;
			}
		}

		[UnityTest]
		public IEnumerator Server_HoldsCastsOnBlankTicks_HonoursHitClaims_AndRunsObservedCopiesOnThePlaybackClock()
		{
			host = new GameObject("PredictionProbeUnderTest");
			CombatSimBootstrap sim = host.AddComponent<CombatSimBootstrap>();
			sim.AutoSweep = false;
			sim.ClaimMs = 0;
			sim.TeamSize = 1;
			// Whatever is free: a machine that runs the game's own servers or a proxy for them holds the sim's default.
			sim.Port = FreeUdpPort();
			// The probe starts its own casts; the director's would only be in the way before it takes over.
			sim.DirectorPaused = true;

			float readyDeadline = Time.realtimeSinceStartup + 180f;
			while (!sim.Ready && Time.realtimeSinceStartup < readyDeadline)
			{
				yield return null;
			}
			Assert.IsTrue(sim.Ready, "the sim never finished loading templates / starting its server");
			Assert.Greater(sim.AliveFighters, 1, "the probe needs a fighter on each team");

			// A moment for the fighters to finish spawning and their controllers to take their first ticks.
			float settle = Time.realtimeSinceStartup + 1f;
			while (Time.realtimeSinceStartup < settle)
			{
				yield return null;
			}

			PredictionProbeResult result = new PredictionProbeResult();
			yield return sim.RunPredictionProbe(result);
			Assert.IsNull(result.Error, "the probe could not run: " + result.Error);

			// ── A cast waits out a tick with no input, and finishes on the same input ──
			Assert.Greater(result.InputsToFinishSteady, 0, "the cast under test must take time, or blank ticks cannot fall inside it");
			Assert.IsTrue(result.SpawnedOnFinish, "a finished cast must launch its projectile");
			Assert.Greater(result.BlankTicksRun, 0, "no blank tick fell inside the cast: the run proved nothing");
			Assert.IsTrue(result.CastSurvivedEveryBlankTick, "a tick without input ended the cast");
			Assert.IsFalse(result.SpawnedOnABlankTick, "a tick without input launched the projectile (with no aim: due north)");
			Assert.AreEqual(result.InputsToFinishSteady, result.InputsToFinishWithBlanks,
				"a cast must finish on the same INPUT however many ticks the server ran without one: the owner predicts it " +
				"in inputs, and a server that counted blank ticks too finished early and left the owner without its projectile");
			Assert.IsTrue(result.CancelledAfterStall, "input that never comes must not hold a cast for ever");
			Assert.IsFalse(result.SpawnedWhenCancelled, "a cast cancelled for missing input must launch nothing");

			// ── A hit the owner claims ──
			Assert.IsTrue(result.ClaimGrantedWithinTolerance,
				"a victim the projectile's sweep passed within the tolerance of must be granted to the owner who saw it hit");
			Assert.IsTrue(result.ClaimNotGrantedTwice, "one projectile, one hit: the same claim again must be refused");
			Assert.IsTrue(result.ClaimRefusedOutsideTolerance, "a victim two metres clear of the sweep was never drawn in its path");

			// ── An observed copy on the playback clock ──
			Assert.IsTrue(result.ObservedBegan, "a copy this peer does not resolve hits for must join the playback clock");
			Assert.AreEqual(0, result.ObservedTicksBeforeClock,
				"before the clock reaches the tick the server spawned it on, an observed copy must not have moved");
			Assert.AreEqual(3, result.ObservedTicksAtThreeAndAHalf,
				"three and a half ticks past its spawn tick on the clock, an observed copy is three whole ticks old");
			Assert.IsTrue(result.ObservedEndWaitedForClock, "an end the server reports must wait for the playback clock to reach it");
			Assert.IsTrue(result.ObservedEndedOnClock, "and must be applied when the clock does");
		}
	}
}
