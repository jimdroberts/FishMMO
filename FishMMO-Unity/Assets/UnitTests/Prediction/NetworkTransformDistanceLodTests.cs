using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Band-selection, hysteresis and per-observer coverage for <see cref="NetworkTransformDistanceLod"/>.
	/// </summary>
	/// <remarks>
	/// The band decision is the whole component — everything else is a dictionary keyed by
	/// observer and the <c>IObserverSendFilter</c> plumbing. Three properties matter: that a
	/// distance maps to the band a designer would expect, that an observer sitting on a band edge
	/// <b>settles</b> rather than flipping every evaluation, and that the decision is made per
	/// observer — a near spectator keeps full rate while a far one is limited, and the owner is
	/// never counted at all.
	/// </remarks>
	[TestFixture]
	public class NetworkTransformDistanceLodTests
	{
		private GameObject go;
		private NetworkTransformDistanceLod lod;

		private static readonly NetworkTransformDistanceLod.Band[] Defaults =
		{
			new NetworkTransformDistanceLod.Band { MaximumDistance = 20f, Interval = 1 },
			new NetworkTransformDistanceLod.Band { MaximumDistance = 40f, Interval = 3 },
			new NetworkTransformDistanceLod.Band { MaximumDistance = 80f, Interval = 6 },
		};

		private const float Hysteresis = 0.15f;

		[SetUp]
		public void CreateComponent()
		{
			go = new GameObject("LodTest");
			// RequireComponent pulls in NetworkTransform, which pulls in NetworkObject.
			lod = go.AddComponent<NetworkTransformDistanceLod>();
		}

		[TearDown]
		public void DestroyComponent()
		{
			if (go != null)
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		private static int Resolve(float distance, int currentBand)
			=> NetworkTransformDistanceLod.ResolveBand(Defaults, Hysteresis, distance * distance, currentBand);

		private static NetworkConnection Connection(int clientId) => new NetworkConnection { ClientId = clientId };

		[Test]
		public void Distance_SelectsTheExpectedBand()
		{
			// Defaults: 20m -> every tick, 40m -> every 3rd, 80m -> every 6th.
			LogAssert.AreEqual(0, Resolve(0f, -1), "An observer standing on the object must get the fastest band.");
			LogAssert.AreEqual(0, Resolve(19f, -1), "Just inside the first edge must stay in the fastest band.");
			LogAssert.AreEqual(1, Resolve(21f, -1), "Past the first edge must step down one band.");
			LogAssert.AreEqual(1, Resolve(39f, -1), "Just inside the second edge must stay in the middle band.");
			LogAssert.AreEqual(2, Resolve(41f, -1), "Past the second edge must step down again.");
			LogAssert.AreEqual(2, Resolve(500f, -1), "Beyond the last band must clamp to the coarsest, not fall off the end.");
		}

		[Test]
		public void NoBands_MakesNoDecision()
		{
			LogAssert.AreEqual(-1, NetworkTransformDistanceLod.ResolveBand(null, Hysteresis, 1f, -1),
				"With no bands there is nothing to select.");
			LogAssert.AreEqual(1, NetworkTransformDistanceLod.IntervalForBand(Defaults, -1, 1),
				"No band must mean full rate, never a stall.");
		}

		[Test]
		public void BandEdge_Settles_RatherThanFlapping()
		{
			/* An observer hovering at exactly 20m alternating between bands would see its sample
			 * spacing flip between 33ms and 100ms every evaluation, which an interpolator renders
			 * as a hitch. The held band's edge is widened so it stays put. */
			float edge = Defaults[0].MaximumDistance;

			// Already in band 0: the edge is widened, so just past it we hold.
			LogAssert.AreEqual(0, Resolve(edge * 1.05f, 0),
				"An observer already in the fast band must hold it just past the edge rather than flapping.");

			// Far enough past the widened edge, it does move.
			LogAssert.AreEqual(1, Resolve(edge * 1.30f, 0),
				"Once clear of the hysteresis margin the band must actually change.");

			// Coming back the other way, the plain edge applies.
			LogAssert.AreEqual(0, Resolve(edge * 0.95f, 1),
				"Returning inside the plain edge must re-enter the fast band.");
		}

		[Test]
		public void Oscillation_AroundAnEdge_ProducesNoChanges()
		{
			// Walk an observer back and forth across the edge inside the margin and count how many
			// times its band would change. Without hysteresis this is once per sample.
			float edge = Defaults[0].MaximumDistance;

			int changes = 0;
			int band = 0;
			foreach (float d in new[] { 20.5f, 19.8f, 20.6f, 19.9f, 20.4f, 20.1f, 19.7f })
			{
				int next = Resolve(d, band);
				if (next != band)
				{
					changes++;
					band = next;
				}
			}

			TestContext.WriteLine($"MEASURE band changes while oscillating around a {edge}m edge: {changes}");
			LogAssert.AreEqual(0, changes,
				$"Oscillating inside the hysteresis margin produced {changes} band changes; this must stay at zero.");
		}

		[Test]
		public void IntervalScale_IsClampedToSaneValues()
		{
			lod.IntervalScale = 0;
			LogAssert.AreEqual(1, lod.IntervalScale, "A scale below 1 would mean sending more often than every tick.");

			lod.IntervalScale = 99;
			LogAssert.AreEqual(8, lod.IntervalScale, "An unbounded scale would let a zone silently stop updating.");

			lod.IntervalScale = 4;
			LogAssert.AreEqual(4, lod.IntervalScale, "A valid scale must be kept.");

			LogAssert.AreEqual(12, NetworkTransformDistanceLod.IntervalForBand(Defaults, 1, 4),
				"The scale multiplies the band interval.");
			LogAssert.AreEqual(255, NetworkTransformDistanceLod.IntervalForBand(
				new[] { new NetworkTransformDistanceLod.Band { MaximumDistance = 1f, Interval = 60 } }, 0, 8),
				"A scaled interval must clamp to the byte range rather than wrap.");
		}

		[Test]
		public void Observers_AreBandedIndependently()
		{
			/* The reason this component exists in its per-observer form. Under the old
			 * nearest-observer rule the near spectator would have pinned the far one at full rate;
			 * here each is answered on its own distance. */
			NetworkConnection near = Connection(1);
			NetworkConnection far = Connection(2);

			lod.BandObserver(near.ClientId, 5f * 5f);
			lod.BandObserver(far.ClientId, 60f * 60f);

			LogAssert.AreEqual(1, lod.GetInterval(near), "A spectator 5 m away must receive every tick.");
			LogAssert.AreEqual(6, lod.GetInterval(far), "A spectator 60 m away must be in the coarsest band.");
			LogAssert.AreEqual(1, lod.LimitedObserverCount, "Exactly one observer is limited.");
			LogAssert.AreEqual(1, lod.GetInterval(Connection(3)), "An observer never banded is at full rate.");
		}

		[Test]
		public void ShouldSend_NeverDeclinesReliable_OrTheOwner_OrAFullRateObserver()
		{
			NetworkConnection far = Connection(2);
			lod.BandObserver(far.ClientId, 60f * 60f);

			FishNet.Object.NetworkObject nob = go.GetComponent<FishNet.Object.NetworkObject>();

			LogAssert.IsTrue(lod.ShouldSend(nob, far, Channel.Reliable),
				"A reliable send (the settle after a stop) must reach every observer regardless of band.");
			LogAssert.IsTrue(lod.ShouldSend(nob, Connection(1), Channel.Unreliable),
				"An observer at full rate must always be sent to.");
			LogAssert.IsTrue(lod.ShouldSend(nob, null, Channel.Unreliable),
				"A null connection must not be declined.");

			// Every 6th tick, phase-spread by client id: exactly one send in any window of six.
			int sent = 0;
			for (uint tick = 0; tick < 6; tick++)
			{
				if (ObserverStreamingPolicy.ShouldSendThisTick(tick, lod.GetInterval(far), far.ClientId))
				{
					sent++;
				}
			}
			LogAssert.AreEqual(1, sent, "A coarsest-band observer must hear exactly once per six ticks, never zero.");
		}

		[Test]
		public void EveryNetworkTransformPrefab_CarriesTheLodComponent()
		{
			/* Asset-level guard. The component was attached by editing prefab YAML directly, so this
			 * asserts the result imported as a live component rather than a missing script — a
			 * malformed block would leave GetComponent returning null while the file still looks
			 * plausible. It also catches a new NetworkTransform prefab being added without one,
			 * which would quietly send at full rate forever. */
			int checkedPrefabs = 0;

			foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab == null)
				{
					continue;
				}

				FishNet.Component.Transforming.NetworkTransform nt =
					prefab.GetComponent<FishNet.Component.Transforming.NetworkTransform>();
				if (nt == null)
				{
					continue;
				}

				checkedPrefabs++;
				/* Compared to null rather than passed to IsNotNull: the assert stringifies whatever
				 * it is handed, and NetworkBehaviour.ToString() dereferences spawn-time state that
				 * a prefab asset does not have — so a PASSING assert throws while building its own
				 * message. */
				bool hasLod = prefab.GetComponent<NetworkTransformDistanceLod>() != null;
				LogAssert.IsTrue(hasLod,
					$"'{prefab.name}' has a NetworkTransform but no NetworkTransformDistanceLod, so it " +
					"synchronises at full rate no matter how far away every observer is.");
			}

			TestContext.WriteLine($"MEASURE prefabs with a NetworkTransform checked: {checkedPrefabs}");
			LogAssert.IsTrue(checkedPrefabs > 0,
				"No prefab with a NetworkTransform was found — this guard is not actually checking anything.");
		}

		[Test]
		public void Measure_WhatTheBandsSaveOnARealisticSpread()
		{
			/* What the component is worth, in the units that actually drive cost: transform messages
			 * per second per observer. Per-observer banding means the spread is over OBSERVERS of one
			 * object rather than over objects near the closest observer, so this now holds in a
			 * crowd as well as in a sparse zone. */
			(float distance, int count)[] spread =
			{
				(10f, 5),
				(30f, 15),
				(60f, 40),
			};

			const int tickRate = 30;
			int flat = 0;
			int lodded = 0;

			foreach (var (distance, count) in spread)
			{
				int band = Resolve(distance, -1);
				byte interval = NetworkTransformDistanceLod.IntervalForBand(Defaults, band, 1);
				flat += count * tickRate;
				lodded += count * tickRate / Mathf.Max(1, interval);
			}

			TestContext.WriteLine(
				$"MEASURE transform messages/sec to 60 observers: flat={flat}, with LOD={lodded} " +
				$"({flat / (double)lodded:F1}x fewer)");

			LogAssert.IsTrue(lodded < flat / 2,
				$"On a spread-out population the bands must at least halve transform messages; " +
				$"{flat} -> {lodded} is not worth the component's complexity.");
		}
	}
}
