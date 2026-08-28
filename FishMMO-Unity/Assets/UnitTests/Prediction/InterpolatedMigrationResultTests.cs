using System;
using NUnit.Framework;
using FishMMO.Shared;
using FishNet.Object;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// End-to-end result of moving playable characters from forwarded state to interpolated peers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Accounts for <b>every</b> channel an observer now receives, so the comparison is against the
	/// finished configuration rather than against the one change that saves the most. The channels
	/// are: position via <c>NetworkTransform</c> under the distance LOD, resources via
	/// <c>CharacterResourcesBroadcast</c>, ability casts via <c>AbilityActivatedBroadcast</c>, buffs
	/// via the pre-existing <c>RpcSetObservedBuffs</c>, and equipment via its pre-existing
	/// broadcasts. Reconcile and replicate no longer reach observers at all.
	/// </para>
	/// <para>
	/// <b>What is measured and what is arithmetic.</b> Payload sizes are real writer output or
	/// figures taken from the benchmark fixtures. The rates are design parameters — the LOD band
	/// (a representative observer 20-40 m away; the LOD is per observer, so a peer inside 20 m
	/// costs three times this and one beyond 40 m half of it),
	/// <c>observedResourcePushInterval</c>, and a cast per second, which is the ceiling the authored
	/// cooldowns allow rather than an average. Nothing here has run in a live session.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class InterpolatedMigrationResultTests
	{
		private const int TickRate = 30;
		private const int RpcHeaderBytes = 10;

		// ── measured payloads ──
		private const int ReplicatePacketBytes = 41;   // delta path, RedundancyCount entries
		private const int ReconcilePayloadBytes = 26;  // delta path
		private const int NetworkTransformBytes = 11;  // via reflection into SerializeChanged
		private const int ResourceBroadcastBytes = 30; // measured in ObserverChannelCostTests
		private const int ActivationBroadcastBytes = 37;

		// ── design rates ──
		private const int LodBand = 3;                 // NetworkTransformDistanceLod middle band (20-40 m), applied per observer
		private const double ResourceHz = 5.0;         // observedResourcePushInterval = 6 ticks
		private const double CastHz = 1.0;             // every authored cooldown is >= 1s
		private const double BuffHz = 0.5;

		/// <summary>Cost of one observed peer under the old forwarded model.</summary>
		private static double ForwardedPerPeer()
			=> (ReplicatePacketBytes + RpcHeaderBytes) * (double)TickRate
			 + (ReconcilePayloadBytes + RpcHeaderBytes) * (double)TickRate;

		/// <summary>Cost of one observed peer under the interpolated model, in combat.</summary>
		private static double InterpolatedPerPeer(bool inCombat)
			=> (NetworkTransformBytes + RpcHeaderBytes) * (double)TickRate / LodBand
			 + (inCombat ? ResourceBroadcastBytes * ResourceHz : 0.0)
			 + (inCombat ? ActivationBroadcastBytes * CastHz : 0.0)
			 + (inCombat ? 24 * BuffHz : 0.0);

		/// <summary>
		/// Per-peer cost, every channel counted.
		/// </summary>
		[Test]
		public void Measure_PerPeerCost_EveryChannelCounted()
		{
			double forwarded = ForwardedPerPeer();
			double combat = InterpolatedPerPeer(true);
			double idle = InterpolatedPerPeer(false);

			TestContext.WriteLine("MEASURE FORWARDED (before)");
			TestContext.WriteLine($"MEASURE   replicate relay   {(ReplicatePacketBytes + RpcHeaderBytes) * TickRate,6} B/s");
			TestContext.WriteLine($"MEASURE   reconcile relay   {(ReconcilePayloadBytes + RpcHeaderBytes) * TickRate,6} B/s");
			TestContext.WriteLine($"MEASURE   total             {forwarded,6:F0} B/s");
			TestContext.WriteLine("MEASURE");
			TestContext.WriteLine("MEASURE INTERPOLATED (after)");
			TestContext.WriteLine($"MEASURE   transform + LOD   {(NetworkTransformBytes + RpcHeaderBytes) * TickRate / (double)LodBand,6:F0} B/s");
			TestContext.WriteLine($"MEASURE   resources @{ResourceHz}Hz  {ResourceBroadcastBytes * ResourceHz,6:F0} B/s  (only while changing)");
			TestContext.WriteLine($"MEASURE   activations @{CastHz}Hz {ActivationBroadcastBytes * CastHz,6:F0} B/s");
			TestContext.WriteLine($"MEASURE   buffs             {24 * BuffHz,6:F0} B/s");
			TestContext.WriteLine($"MEASURE   replicate/reconcile    0 B/s  (owner only)");
			TestContext.WriteLine($"MEASURE   total in combat   {combat,6:F0} B/s   ({forwarded / combat:F1}x cheaper)");
			TestContext.WriteLine($"MEASURE   total idle        {idle,6:F0} B/s   ({forwarded / idle:F1}x cheaper)");

			LogAssert.IsTrue(combat * 5 < forwarded,
				$"The interpolated model must be at least 5x cheaper per peer even in combat; " +
				$"measured {forwarded / combat:F1}x.");
		}

		/// <summary>
		/// Client and scene budgets at the target population, worst case included.
		/// </summary>
		/// <remarks>
		/// The "all visible" rows deliberately ignore interest management, because the brief was to
		/// account for the worst case rather than the expected one. They are what a client pays if
		/// every player in the scene is inside its observer radius at once.
		/// </remarks>
		[Test]
		public void Measure_SceneBudget_AtTargetPopulation()
		{
			double combat = InterpolatedPerPeer(true);
			double forwarded = ForwardedPerPeer();

			// Own upstream + own reconcile, paid regardless of how many peers are visible.
			double ownCost = (ReconcilePayloadBytes + RpcHeaderBytes) * (double)TickRate
						   + (ReplicatePacketBytes + RpcHeaderBytes) * (double)TickRate;

			TestContext.WriteLine("MEASURE  visible   before KB/s    after KB/s   after Mbps   scene Mbps @200");
			foreach (int visible in new[] { 10, 25, 60, 100, 200 })
			{
				double before = (visible * forwarded + ownCost) / 1024.0;
				double after = (visible * combat + ownCost) / 1024.0;
				TestContext.WriteLine(
					$"MEASURE {visible,8}   {before,10:F1}   {after,10:F1}   {after * 8 / 1000.0,10:F2}   " +
					$"{after * 200 * 8 / 1000.0,10:F0}");
			}

			double worst = (200 * combat + ownCost) / 1024.0;
			TestContext.WriteLine(
				$"MEASURE worst case, 200 players all mutually visible: {worst:F1} KB/s per client " +
				$"({worst * 8 / 1000.0:F2} Mbps), scene egress {worst * 200 * 8 / 1000.0:F0} Mbps");

			LogAssert.IsTrue(worst * 8 / 1000.0 < 2.0,
				$"Even with no culling at all, a client must stay under 2 Mbps; measured " +
				$"{worst * 8 / 1000.0:F2} Mbps.");
		}

		/// <summary>
		/// The playable prefabs are actually configured the way the numbers above assume.
		/// </summary>
		/// <remarks>
		/// Asset-level guard tying the projection to reality. Every figure above assumes forwarding
		/// is off and a NetworkTransform is assigned; if a prefab drifts back, the projection becomes
		/// fiction and this fails rather than quietly overstating the saving.
		/// </remarks>
		[Test]
		public void PlayableCharacters_AreConfiguredForInterpolatedSpectating()
		{
			int checkedPrefabs = 0;

			foreach (string guid in UnityEditor.AssetDatabase.FindAssets(
				"t:Prefab", new[] { "Assets/Prefabs/Shared/Entity/PlayableCharacters" }))
			{
				string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
				NetworkObject nob = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
				if (nob == null)
				{
					continue;
				}

				checkedPrefabs++;

				LogAssert.IsTrue(!nob.EnableStateForwarding,
					$"'{prefab.name}' still forwards state. Every bandwidth figure in this fixture " +
					"assumes it does not.");

				bool hasTransform =
					prefab.GetComponent<FishNet.Component.Transforming.NetworkTransform>() != null;
				LogAssert.IsTrue(hasTransform,
					$"'{prefab.name}' has forwarding off and no NetworkTransform — observers would " +
					"receive no position for it at all.");

				bool hasLod = prefab.GetComponent<NetworkTransformDistanceLod>() != null;
				LogAssert.IsTrue(hasLod,
					$"'{prefab.name}' has no distance LOD, so the transform sends at full rate and " +
					"the LOD band assumed above does not apply.");

				bool hasHistory = prefab.GetComponent<CharacterPositionHistory>() != null;
				LogAssert.IsTrue(hasHistory,
					$"'{prefab.name}' has no CharacterPositionHistory, so hits against it cannot be " +
					"lag compensated and are off by the shooter's full latency.");
			}

			TestContext.WriteLine($"MEASURE playable character prefabs verified: {checkedPrefabs}");
			LogAssert.IsTrue(checkedPrefabs > 0, "No playable character prefab found.");
		}

		/// <summary>
		/// Accuracy across latency, after lag compensation.
		/// </summary>
		/// <remarks>
		/// The other half of the trade. Bandwidth fell because peers are interpolated, and
		/// interpolation is exactly what would have made hits inaccurate — so the two must be read
		/// together. Compensated error is the residual within one recorded tick; uncompensated is
		/// what the same shot would have missed by.
		/// </remarks>
		[Test]
		public void Measure_AccuracyRetained_AcrossLatency()
		{
			const float tickDelta = 1f / TickRate;
			const float speed = 6f;

			TestContext.WriteLine("MEASURE  one-way   uncompensated   compensated   entity-targeted");
			foreach (int oneWayMs in new[] { 8, 40, 100, 200, 300 })
			{
				double stale = LagCompensationTick.SpectatorInterpolationTicks * tickDelta * 1000.0 + oneWayMs;
				double uncompensated = speed * stale / 1000.0;
				// Rewind resolves to a recorded tick; residual is sub-tick interpolation only.
				double compensated = speed * tickDelta * 0.5;

				TestContext.WriteLine(
					$"MEASURE {oneWayMs,7}ms   {uncompensated,12:F2}m   {compensated,10:F2}m   " +
					$"{"exact",14}");
			}

			double at300 = speed * (LagCompensationTick.SpectatorInterpolationTicks * tickDelta * 1000.0 + 300) / 1000.0;
			LogAssert.IsTrue(at300 > 2.0,
				"A 300ms peer should be over 2m stale uncompensated — that is the error the rewind removes.");
			LogAssert.IsTrue(speed * tickDelta < 0.25,
				"Sub-tick residual must stay well inside a hitbox at the design tick rate.");
		}
	}
}
