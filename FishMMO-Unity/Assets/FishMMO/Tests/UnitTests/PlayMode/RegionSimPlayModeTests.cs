using System.Collections;
using FishMMO.TestHarness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FishMMO.UnitTests.PlayMode
{
	/// <summary>
	/// Runs the region simulation headlessly: real Region components with real NetworkTrigger
	/// colliders on a real server, a walker crossing them on a fixed route, and three properties
	/// asserted — enter/exit pairing, nested-region ownership handoff, and the region-owned
	/// attribute ledger releasing exactly what it applied.
	/// </summary>
	/// <remarks>
	/// Same component the RegionSim scene wraps; a green run here and a green PASS banner there
	/// are the same fact.
	/// </remarks>
	public class RegionSimPlayModeTests
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
		public IEnumerator Regions_PairEnterExit_HandOffNesting_AndReleaseTheLedger()
		{
			host = new GameObject("RegionSimUnderTest");
			RegionSimHarness sim = host.AddComponent<RegionSimHarness>();
			sim.WalkSpeed = 10f;

			/* The finally matters: a failed assert must still tear the harness down, or its
			 * server leaks into every later test in the session — FishNet destroys duplicate
			 * NetworkManagers, so a leaked one breaks the NEXT sim's boot too. */
			try
			{
				float readyDeadline = Time.realtimeSinceStartup + 180f;
				while (!sim.Ready && Time.realtimeSinceStartup < readyDeadline)
				{
					yield return null;
				}
				Assert.IsTrue(sim.Ready, "the sim never finished loading templates / starting its server");

				// The route is ~90m per lap at 10 m/s; run until at least two full laps completed.
				float deadline = Time.realtimeSinceStartup + 45f;
				while (sim.Laps < 2 && Time.realtimeSinceStartup < deadline)
				{
					yield return null;
				}

				Assert.GreaterOrEqual(sim.Laps, 2, "the walker never completed two laps of its route");
				Assert.Greater(sim.TotalEnters, 0, "the walker never entered any region at all");
				Assert.IsTrue(sim.PairingIsSound,
					"a region's exits fell behind its enters — a character was left stranded inside");
				Assert.AreEqual(0, sim.NestingViolations,
					"parent and child region both owned the walker at once — nested ownership handoff is broken");
				Assert.AreEqual(0, sim.LedgerViolations,
					"the region attribute ledger leaked or accumulated — a ModifierSource.Region " +
					"contribution was not released (or was restated cumulatively). The harness logged which.");
				Assert.Greater(sim.MaxObservedAttribute, sim.BaseAttributeValue,
					"the boosting region never actually applied its attribute contribution");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
			yield return null;
		}
	}
}
