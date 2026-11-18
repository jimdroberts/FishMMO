using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FishMMO.UnitTests.PlayMode
{
	/// <summary>
	/// Prices Unity's per-MonoBehaviour <c>Update</c> dispatch, which is what an interval gate
	/// cannot remove and a central scheduler can.
	/// </summary>
	/// <remarks>
	/// Once ObjectSpawner stopped polling every frame, the poll itself became about 1% of its
	/// remaining cost — effectively all of what is left is the cost of Unity calling N Update
	/// methods at all. That number decides whether a central scheduler is worth its complexity,
	/// so it is measured here rather than assumed.
	/// </remarks>
	public class UpdateDispatchCostTests
	{
		private const int PROBES = 5000;
		private const int FRAMES = 200;

		/// <summary>Mirrors the spawner's gate: one compare, then return.</summary>
		private class GatedUpdateProbe : MonoBehaviour
		{
			internal float NextTime = float.MaxValue;

			private void Update()
			{
				if (Time.time < NextTime)
				{
					return;
				}
			}
		}

		private static IEnumerator TimeFrames(int frames, Stopwatch watch)
		{
			// Settle before timing: the first frames after a spawn burst are not representative.
			for (int i = 0; i < 30; ++i)
			{
				yield return null;
			}

			watch.Restart();
			for (int i = 0; i < frames; ++i)
			{
				yield return null;
			}
			watch.Stop();
		}

		[UnityTest]
		public IEnumerator Measure_UpdateDispatchCost()
		{
			/* Uncap the frame rate. Paced frames sit at a fixed 16.67 ms whatever the scripts do,
			 * which hides the very work being measured inside the frame budget. */
			int vSync = QualitySettings.vSyncCount;
			int target = Application.targetFrameRate;
			QualitySettings.vSyncCount = 0;
			Application.targetFrameRate = -1;

			Stopwatch baseline = new Stopwatch();
			yield return TimeFrames(FRAMES, baseline);

			GameObject root = new GameObject("DispatchProbes");
			for (int i = 0; i < PROBES; ++i)
			{
				GameObject go = new GameObject("Probe");
				go.transform.SetParent(root.transform);
				go.AddComponent<GatedUpdateProbe>();
			}

			Stopwatch loaded = new Stopwatch();
			yield return TimeFrames(FRAMES, loaded);

			Object.Destroy(root);

			QualitySettings.vSyncCount = vSync;
			Application.targetFrameRate = target;

			double baseMs = baseline.Elapsed.TotalMilliseconds / FRAMES;
			double loadMs = loaded.Elapsed.TotalMilliseconds / FRAMES;
			double perProbeNs = (loadMs - baseMs) * 1000000.0 / PROBES;

			TestContext.WriteLine($"MEASURE dispatch.baselineFrameMs = {baseMs:F4}");
			TestContext.WriteLine($"MEASURE dispatch.loadedFrameMs = {loadMs:F4}");
			TestContext.WriteLine($"MEASURE dispatch.probes = {PROBES}");
			TestContext.WriteLine($"MEASURE dispatch.perProbePerFrameNs = {perProbeNs:F1}");

			foreach (int spawners in new[] { 100, 1000, 5000 })
			{
				double msPerSecond = spawners * 45.0 * perProbeNs / 1000000.0;
				TestContext.WriteLine($"MEASURE dispatch.msPerSecondAt{spawners} = {msPerSecond:F2}");
			}

			Assert.Greater(loadMs, baseMs, "5000 Update callbacks should cost measurably more than none.");
		}
	}
}
