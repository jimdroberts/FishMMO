using System.Collections;
using FishMMO.TestHarness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FishMMO.UnitTests.PlayMode
{
	/// <summary>
	/// Runs the interaction simulation headlessly: real interactable NPCs and a real player
	/// character on a real server, with the exact server interact chain probed against cases
	/// whose answers are known — in range succeeds; out of range, inside the debounce, corpses,
	/// unregistered ids, and cannot-act characters are all refused.
	/// </summary>
	/// <remarks>
	/// Same component the InteractableSim scene wraps; a green run here and a green PASS banner
	/// there are the same fact. Written for the live report "can't interact with NPCs".
	/// </remarks>
	public class InteractableSimPlayModeTests
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
		public IEnumerator InteractChain_AnswersEveryKnownCaseCorrectly()
		{
			host = new GameObject("InteractableSimUnderTest");
			InteractableSimHarness sim = host.AddComponent<InteractableSimHarness>();
			sim.StepInterval = 0.15f;

			/* The finally matters: a failed assert must still tear the harness down, or its
			 * server (and its error-logging Update loop) leaks into every later test in the
			 * session — FishNet destroys duplicate NetworkManagers, so a leaked one breaks the
			 * NEXT sim's boot too. */
			try
			{
				float readyDeadline = Time.realtimeSinceStartup + 180f;
				while (!sim.Ready && Time.realtimeSinceStartup < readyDeadline)
				{
					yield return null;
				}
				Assert.IsTrue(sim.Ready, "the sim never finished loading templates / starting its server");

				// Enough wall time for several full passes over every case.
				float deadline = Time.realtimeSinceStartup + 10f;
				while (Time.realtimeSinceStartup < deadline)
				{
					yield return null;
				}

				Assert.GreaterOrEqual(sim.CasesCovered, sim.CaseCount,
					"not every scripted interaction case actually ran");
				Assert.AreEqual(0, sim.TotalWrong,
					"at least one interaction case answered wrongly — a step of the server interact " +
					"chain (CanAct / registry / resolve / range / corpse / rate limit) has regressed. " +
					"The harness logged which.");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
			yield return null;
		}
	}
}
