using System.Collections;
using FishMMO.TestHarness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FishMMO.UnitTests.PlayMode
{
	/// <summary>
	/// Runs the zero-client combat simulation headlessly and asserts what a human watching the
	/// CombatSim scene would verify by eye: real NPC fighters cast the whole mock roster through
	/// the production pipeline, damage/heals/buffs flow, and — with a 500 ms synthetic latency
	/// claim on every caster — the REAL lag-compensation resolver produces rewind targets.
	/// </summary>
	/// <remarks>
	/// This drives the exact same <c>CombatSimBootstrap</c> the visual scene wraps, so a green
	/// run here and a green PASS banner in the scene are the same fact. The prediction/rollback
	/// half of the model is covered by <c>PlatformSimPlayModeTests</c>; a single process cannot
	/// be both peers of a FishNet session.
	/// </remarks>
	public class CombatSimPlayModeTests
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
		public IEnumerator ServerCombat_RunsTheMockRosterAndRewindsUnderTheLatencyCap()
		{
			host = new GameObject("CombatSimUnderTest");
			CombatSimBootstrap sim = host.AddComponent<CombatSimBootstrap>();
			sim.AutoSweep = false;
			sim.ClaimMs = 500; // the cap — the deepest claim the live game accepts
			sim.TeamSize = 2;

			/* The finally matters: a failed assert must still tear the harness down, or its
			 * server leaks into every later test in the session — FishNet destroys duplicate
			 * NetworkManagers, so a leaked one breaks the NEXT sim's boot too. */
			try
			{
				// Template load + server start; generous because the first addressable load of the
				// full template set dominates.
				float readyDeadline = Time.realtimeSinceStartup + 180f;
				while (!sim.Ready && Time.realtimeSinceStartup < readyDeadline)
				{
					yield return null;
				}
				Assert.IsTrue(sim.Ready, "the sim never finished loading templates / starting its server");
				Assert.Greater(sim.AliveFighters, 0, "no fighters spawned");

				// Let the director cycle the roster for a while at the latency cap.
				float combatDeadline = Time.realtimeSinceStartup + 30f;
				while (Time.realtimeSinceStartup < combatDeadline)
				{
					yield return null;
				}

				Assert.Greater(sim.CastsStarted, 0, "the director never cast anything");
				Assert.Greater(sim.DamageEvents, 0,
					"no damage flowed — the offensive mocks are not resolving hits server-side");
				Assert.Greater(sim.HealEvents, 0,
					"no heals flowed — the restorative mocks are not resolving server-side");
				Assert.Greater(sim.BuffAdds, 0,
					"no buffs applied — the buff/debuff mocks are not resolving server-side");
				Assert.Greater(sim.ClaimsConsulted, 0,
					"the synthetic latency claim was never consulted — the rewind path is not engaging");
				Assert.Greater(sim.RewindsResolved, 0,
					"no rewind target ever resolved at a 500ms claim — lag compensation is not " +
					"reconstructing past poses from the position history ring");

				// Drop the claim to zero and make sure the sim keeps running consistently.
				sim.ClaimMs = 0;
				long castsAtZeroStart = sim.CastsStarted;
				float zeroDeadline = Time.realtimeSinceStartup + 8f;
				while (Time.realtimeSinceStartup < zeroDeadline)
				{
					yield return null;
				}
				Assert.Greater(sim.CastsStarted, castsAtZeroStart,
					"combat stalled after dropping the latency claim to 0ms");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
			yield return null;
		}
	}
}
