using System;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Band-selection and hysteresis coverage for <see cref="NetworkTransformDistanceLod"/>.
	/// </summary>
	/// <remarks>
	/// The band decision is the whole component — everything else is plumbing around
	/// <c>NetworkTransform.SetInterval</c>. Two properties matter and neither is obvious from
	/// reading it: that a distance maps to the band a designer would expect, and that an object
	/// sitting on a band edge <b>settles</b> rather than emitting a buffered observers RPC on every
	/// evaluation. The second is the one that turns this from a saving into a cost if it regresses.
	/// </remarks>
	[TestFixture]
	public class NetworkTransformDistanceLodTests
	{
		private GameObject go;
		private NetworkTransformDistanceLod lod;
		private MethodInfo resolveBand;
		private FieldInfo currentBand;
		private FieldInfo bandsField;

		[SetUp]
		public void CreateComponent()
		{
			go = new GameObject("LodTest");
			// RequireComponent pulls in NetworkTransform, which pulls in NetworkObject.
			lod = go.AddComponent<NetworkTransformDistanceLod>();

			Type t = typeof(NetworkTransformDistanceLod);
			resolveBand = t.GetMethod("ResolveBand", BindingFlags.Instance | BindingFlags.NonPublic);
			currentBand = t.GetField("currentBand", BindingFlags.Instance | BindingFlags.NonPublic);
			bandsField = t.GetField("bands", BindingFlags.Instance | BindingFlags.NonPublic);

			LogAssert.IsNotNull(resolveBand, "ResolveBand must exist; the band decision is what this fixture covers.");
			LogAssert.IsNotNull(currentBand, "currentBand must exist; hysteresis is expressed through it.");
			LogAssert.IsNotNull(bandsField, "bands must exist.");
		}

		[TearDown]
		public void DestroyComponent()
		{
			if (go != null)
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		private int Resolve(float distance)
		{
			float sqr = distance == float.MaxValue ? float.MaxValue : distance * distance;
			return (int)resolveBand.Invoke(lod, new object[] { sqr });
		}

		private void SetCurrentBand(int band) => currentBand.SetValue(lod, band);

		private NetworkTransformDistanceLod.Band[] Bands =>
			(NetworkTransformDistanceLod.Band[])bandsField.GetValue(lod);

		[Test]
		public void Distance_SelectsTheExpectedBand()
		{
			// Defaults: 20m -> every tick, 40m -> every 3rd, 80m -> every 6th.
			SetCurrentBand(-1);

			LogAssert.AreEqual(0, Resolve(0f), "An observer standing on the object must get the fastest band.");
			LogAssert.AreEqual(0, Resolve(19f), "Just inside the first edge must stay in the fastest band.");
			LogAssert.AreEqual(1, Resolve(21f), "Past the first edge must step down one band.");
			LogAssert.AreEqual(1, Resolve(39f), "Just inside the second edge must stay in the middle band.");
			LogAssert.AreEqual(2, Resolve(41f), "Past the second edge must step down again.");
			LogAssert.AreEqual(2, Resolve(500f), "Beyond the last band must clamp to the coarsest, not fall off the end.");
		}

		[Test]
		public void Unobserved_MakesNoDecision()
		{
			/* Nothing is watching, so whatever interval is set costs nothing to leave alone.
			 * Returning a band here would spend a buffered observers RPC on a change no one can
			 * see — and would do it for every unobserved object in the scene. */
			SetCurrentBand(-1);
			LogAssert.AreEqual(-1, Resolve(float.MaxValue),
				"An unobserved object must not select a band, so no interval RPC is emitted.");
		}

		[Test]
		public void BandEdge_Settles_RatherThanFlapping()
		{
			/* The failure this guards is subtle and expensive: an object hovering at exactly 20m
			 * alternating between bands emits SetInterval on every evaluation. Each of those is a
			 * buffered observers RPC, so the component would spend more bandwidth than the reduced
			 * send rate saves — a net loss that looks like a win in any table of interval values. */
			float edge = Bands[0].MaximumDistance;

			// Already in band 0: the edge is widened, so just past it we hold.
			SetCurrentBand(0);
			LogAssert.AreEqual(0, Resolve(edge * 1.05f),
				"An object already in the fast band must hold it just past the edge rather than flapping.");

			// Far enough past the widened edge, it does move.
			LogAssert.AreEqual(1, Resolve(edge * 1.30f),
				"Once clear of the hysteresis margin the band must actually change.");

			// Coming back the other way, the plain edge applies.
			SetCurrentBand(1);
			LogAssert.AreEqual(0, Resolve(edge * 0.95f),
				"Returning inside the plain edge must re-enter the fast band.");
		}

		[Test]
		public void Oscillation_AroundAnEdge_ProducesAtMostOneChange()
		{
			// Walk a character back and forth across the edge inside the margin and count how many
			// times the band would change. Without hysteresis this is once per sample.
			float edge = Bands[0].MaximumDistance;
			SetCurrentBand(0);

			int changes = 0;
			int band = 0;
			foreach (float d in new[] { 20.5f, 19.8f, 20.6f, 19.9f, 20.4f, 20.1f, 19.7f })
			{
				int next = Resolve(d);
				if (next != band)
				{
					changes++;
					band = next;
					SetCurrentBand(band);
				}
			}

			TestContext.WriteLine($"MEASURE band changes while oscillating around a {edge}m edge: {changes}");
			LogAssert.AreEqual(0, changes,
				$"Oscillating inside the hysteresis margin produced {changes} interval changes; each one is a " +
				"buffered observers RPC, so this must stay at zero.");
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
			 * per second. Uses a spread where most entities are far — the sparse-zone case this
			 * helps. A packed capital is deliberately NOT modelled here because the nearest-observer
			 * rule cannot help there; that case needs IntervalScale, which is a zone decision. */
			(float distance, int count)[] spread =
			{
				(10f, 5),
				(30f, 15),
				(60f, 40),
			};

			const int tickRate = 30;
			int flat = 0;
			int lodded = 0;

			SetCurrentBand(-1);
			foreach (var (distance, count) in spread)
			{
				int band = Resolve(distance);
				byte interval = Bands[Mathf.Max(0, band)].Interval;
				flat += count * tickRate;
				lodded += count * tickRate / Mathf.Max(1, interval);
			}

			TestContext.WriteLine(
				$"MEASURE transform messages/sec over 60 entities: flat={flat}, with LOD={lodded} " +
				$"({flat / (double)lodded:F1}x fewer)");

			LogAssert.IsTrue(lodded < flat / 2,
				$"On a spread-out population the bands must at least halve transform messages; " +
				$"{flat} -> {lodded} is not worth the component's complexity.");
		}
	}
}
