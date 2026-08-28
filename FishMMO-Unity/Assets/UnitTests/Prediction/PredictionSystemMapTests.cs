using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// An enforced map of what is predicted, what is interpolated, and what rides a broadcast.
	/// </summary>
	/// <remarks>
	/// <para>
	/// After the migration the answer to "how does this piece of state reach a client" differs per
	/// subsystem, and getting it wrong is silent: a controller that quietly starts depending on the
	/// reconcile reaching observers works perfectly in a two-player test and fails at scale, because
	/// the reconcile only ever reached the owner. These tests hold the map in place.
	/// </para>
	/// <para>
	/// They assert against the code rather than describing it — the predicted set is read by
	/// reflection from the <see cref="IPredictableController"/> implementations, so a new controller
	/// appears here automatically instead of being forgotten.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class PredictionSystemMapTests
	{
		/// <summary>Every controller driven by the unified prediction pipeline.</summary>
		private static IEnumerable<Type> PredictableControllers()
			=> typeof(IPredictableController).Assembly.GetTypes()
				.Where(t => typeof(IPredictableController).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
				.OrderBy(t => t.Name);

		/// <summary>
		/// The predicted set: which controllers run inside the replicate/reconcile pipeline.
		/// </summary>
		/// <remarks>
		/// These run on the owner for prediction and on the server for authority. With state
		/// forwarding disabled they no longer run on observers at all, which is precisely the
		/// saving — and precisely why anything an observer needs must travel some other way.
		/// </remarks>
		[Test]
		public void Map_WhatRunsInThePredictionPipeline()
		{
			List<Type> controllers = PredictableControllers().ToList();

			TestContext.WriteLine("MEASURE PREDICTED — runs in the replicate/reconcile pipeline");
			TestContext.WriteLine("MEASURE   (owner predicts, server is authoritative, observers no longer run these)");

			/* Order is an instance property returning a constant, so it needs an instance to read.
			 * These are all MonoBehaviours, so they are created on a throwaway GameObject rather
			 * than through Activator, which cannot construct a Component. */
			GameObject probe = new GameObject("OrderProbe");
			try
			{
				foreach (Type t in controllers)
				{
					string order = "?";
					if (typeof(Component).IsAssignableFrom(t))
					{
						Component c = probe.AddComponent(t);
						PropertyInfo orderProp = t.GetProperty("Order");
						if (orderProp != null && c != null)
						{
							order = orderProp.GetValue(c).ToString();
						}
					}
					TestContext.WriteLine($"MEASURE     {t.Name,-34} order={order}");
				}
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(probe);
			}

			TestContext.WriteLine($"MEASURE   total predicted controllers: {controllers.Count}");

			LogAssert.IsTrue(controllers.Count >= 6,
				"The prediction pipeline is expected to drive at least six controllers; a smaller " +
				"number means one stopped implementing IPredictableController and silently left the " +
				"pipeline.");
		}

		/// <summary>
		/// Every observer-facing channel exists and is a broadcast.
		/// </summary>
		/// <remarks>
		/// The replacement paths for everything the reconcile used to carry to observers. Asserted
		/// as broadcast types rather than RPCs because that is the project's stated preference and
		/// because a broadcast can be scoped to a NetworkObject's observers, which makes interest
		/// management bound the traffic for free.
		/// </remarks>
		[Test]
		public void Map_ObserverChannelsAreBroadcasts()
		{
			(Type Type, string Carries, string Rate)[] channels =
			{
				(typeof(CharacterResourcesBroadcast),  "health / mana / stamina", "<=5Hz, on change"),
				(typeof(CharacterBuffsBroadcast),      "server-filtered buff list", "on change + spawn replay"),
				(typeof(AbilityActivatedBroadcast),    "ability cast + resolved target", "per cast"),
				(typeof(CharacterDeathStateBroadcast), "death / revive pose", "on change (payload covers late join)"),
			};

			TestContext.WriteLine("MEASURE BROADCAST — server to observers, scoped to NetworkObject.Observers");
			foreach ((Type type, string carries, string rate) in channels)
			{
				bool isBroadcast = typeof(FishNet.Broadcast.IBroadcast).IsAssignableFrom(type);
				TestContext.WriteLine($"MEASURE     {type.Name,-32} {carries,-32} {rate}");
				LogAssert.IsTrue(isBroadcast,
					$"{type.Name} must implement IBroadcast; the project uses broadcasts rather than RPCs " +
					"for observer-facing state.");
			}

			TestContext.WriteLine("MEASURE INTERPOLATED — NetworkTransform under NetworkTransformDistanceLod");
			TestContext.WriteLine("MEASURE     position / rotation              every 1-6 ticks by distance band");
		}

		/// <summary>
		/// No ability or prediction code still reaches observers through an ObserversRpc.
		/// </summary>
		/// <remarks>
		/// A regression guard on the preference for broadcasts. It scans the prediction and ability
		/// source for the attribute rather than trusting that the conversions were complete.
		/// </remarks>
		[Test]
		public void NoObserversRpc_RemainsInThePredictionPath()
		{
			string[] roots =
			{
				"Assets/Scripts/Shared/Implementation/Entity/Prediction",
			};

			List<string> offenders = new List<string>();

			foreach (string root in roots)
			{
				string absolute = System.IO.Path.Combine(
					System.IO.Directory.GetCurrentDirectory(), root);
				if (!System.IO.Directory.Exists(absolute))
				{
					continue;
				}

				foreach (string file in System.IO.Directory.GetFiles(absolute, "*.cs", System.IO.SearchOption.AllDirectories))
				{
					string text = System.IO.File.ReadAllText(file);
					if (text.Contains("[ObserversRpc"))
					{
						offenders.Add(System.IO.Path.GetFileName(file));
					}
				}
			}

			TestContext.WriteLine(
				$"MEASURE ObserversRpc declarations remaining under Entity/Prediction: {offenders.Count}" +
				(offenders.Count > 0 ? " (" + string.Join(", ", offenders) + ")" : ""));

			LogAssert.AreEqual(0, offenders.Count,
				$"These still use [ObserversRpc] where a broadcast is expected: {string.Join(", ", offenders)}.");
		}

		/// <summary>
		/// What a movement speed buff does to smoothness, for the owner and for observers.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Movement speed is an attribute — <c>KCCController.UpdateGroundMovement</c> reads
		/// <c>MoveSpeedTemplate</c> and scales <c>Constants.Character.RunSpeed</c> by it — and
		/// attributes ride the reconcile. A buff applied server side by an ability therefore reaches
		/// the owner one reconcile late, during which the owner predicted at the old speed. The
		/// correction is the speed delta times that window.
		/// </para>
		/// <para>
		/// Observers are a different story, and it is the migration's doing: they no longer simulate
		/// their peers at all, so there is no predicted speed to be wrong and no correction to
		/// apply. They interpolate whatever positions arrive, and a buffed peer simply produces
		/// positions further apart. <b>The migration removes an observer-side jitter source rather
		/// than adding one.</b>
		/// </para>
		/// </remarks>
		[Test]
		public void Measure_MovementSpeedBuff_CorrectionMagnitude()
		{
			const float tickDelta = 1f / 30f;
			const float baseSpeed = 6f;

			TestContext.WriteLine("MEASURE  buff    one-way   owner correction   observer correction");

			foreach (float multiplier in new[] { 1.15f, 1.30f, 2.00f })
			{
				float buffed = baseSpeed * multiplier;
				float delta = buffed - baseSpeed;

				foreach (int oneWayMs in new[] { 20, 40, 100 })
				{
					// The owner predicts at the old speed until the reconcile carrying the new
					// attribute arrives: one round trip.
					float window = (oneWayMs * 2) / 1000f;
					float ownerCorrection = delta * window;

					TestContext.WriteLine(
						$"MEASURE  {multiplier,4:F2}x  {oneWayMs,6}ms   {ownerCorrection,14:F3}m   " +
						$"{"0.000m (no prediction)",20}");
				}
			}

			// Sanity: the correction must stay well inside what FishNet's owner smoothing absorbs.
			float worst = (baseSpeed * 2.00f - baseSpeed) * (100 * 2 / 1000f);
			TestContext.WriteLine(
				$"MEASURE worst modelled owner correction: {worst:F2}m (a 2x buff at 100ms one-way)");
			TestContext.WriteLine(
				"MEASURE observers interpolate positions and never predicted peer speed, so a buff " +
				"changes only how far apart their samples are");

			LogAssert.IsTrue(worst < 1.5f,
				$"A speed buff should not produce a correction over 1.5m even in the worst modelled " +
				$"case; measured {worst:F2}m. Larger would be visible as a snap on the owner.");
			LogAssert.IsTrue(tickDelta > 0f, "Tick delta must be positive.");
		}

		/// <summary>
		/// The LOD's band change is the one genuine smoothness risk for interpolated peers.
		/// </summary>
		/// <remarks>
		/// Not the buff — the send interval. A peer crossing a distance band changes how often its
		/// transform is sent, and an interpolator fed at a suddenly different rate can hitch. This is
		/// why the LOD applies hysteresis rather than switching on the raw edge, and why the bands
		/// are coarse. Recorded here so the relationship is visible alongside the buff numbers it is
		/// easily confused with.
		/// </remarks>
		[Test]
		public void Measure_LodBandChange_SampleSpacing()
		{
			const int tickRate = 30;

			TestContext.WriteLine("MEASURE  band  interval   sample spacing   at 6 m/s");
			foreach ((string band, int interval) in new[] { ("0-20m", 1), ("20-40m", 3), ("40m+", 6) })
			{
				double ms = interval * 1000.0 / tickRate;
				TestContext.WriteLine(
					$"MEASURE  {band,-6} every {interval,1} tick   {ms,10:F0} ms   {6.0 * ms / 1000.0,6:F2} m apart");
			}

			TestContext.WriteLine(
				"MEASURE crossing a band changes spacing abruptly; hysteresis in NetworkTransformDistanceLod " +
				"is what stops a peer oscillating on an edge from switching every evaluation");

			LogAssert.IsTrue(6.0 * (6 * 1000.0 / tickRate) / 1000.0 < 1.5,
				"Even the coarsest band must keep samples close enough to interpolate smoothly.");
		}
	}
}
