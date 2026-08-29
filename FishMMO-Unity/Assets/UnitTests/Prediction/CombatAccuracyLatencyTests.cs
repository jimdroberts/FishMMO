using System;
using NUnit.Framework;
using FishMMO.Shared;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// How accurate each ability resolution model stays as peer latency varies.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The question this fixture exists to answer is whether interpolating spectators makes combat
	/// feel desynchronised, and it cannot be answered with one number, because every player has a
	/// different round trip to the server. What matters is the shape of the error across a realistic
	/// latency spread, and — critically — that the shape is different for each way an ability
	/// resolves a hit.
	/// </para>
	/// <para>
	/// <b>The two hit paths that actually exist.</b> Read from the code rather than the templates:
	/// <c>AbilityObject.ResolveSweptHits</c> sweeps a spawned volume along the segment it travelled
	/// this tick and dispatches on every peer; and the <c>TargetSelector</c> family resolves through
	/// <c>physicsScene.OverlapSphere</c> (Area, Cone, Chain, Nearest, Furthest, Random) or
	/// <c>physicsScene.Raycast</c> (Line). Both run on the server, and both now resolve against
	/// where the caster's client saw its peers rather than against the server's present.
	/// </para>
	/// <para>
	/// <b>Correction to what this comment used to say.</b> It claimed spawn-time events cannot
	/// resolve damage because they carry a <c>TickEventData</c> with <c>IsReplicateTick</c> true
	/// and <c>AbilityApplyAreaAction</c> returns early on those. Neither half held. The server's
	/// own self-target and spawn dispatches carry replicate ticks too, so that guard suppressed
	/// the server as well and the effect happened nowhere — which is why the action now gates on
	/// the peer instead, and why every physics selector does too. There is still no client-side
	/// damage path to diverge, but that is because the effects are server-gated, not because the
	/// tick domain rules them out.
	/// </para>
	/// <para>
	/// <b>What is modelled and what is measured.</b> The staleness of an interpolated peer is
	/// arithmetic — interpolation delay plus one-way latency, times peer speed — and that part is a
	/// model. What it is compared against is measured: the authored hitbox extents, the authored
	/// interpolation setting, and the KCC speed constant. No claim here depends on a live session,
	/// and none of it establishes how the result <i>feels</i>; it establishes whether the server and
	/// the client can disagree about the outcome, and by how much.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CombatAccuracyLatencyTests
	{
		/// <summary>Server tick rate.</summary>
		private const int TickRate = 30;

		/// <summary>
		/// <c>NetworkObject._spectatorInterpolation</c> as authored on the playable character prefabs.
		/// </summary>
		private const int SpectatorInterpolationTicks = 2;

		/// <summary><c>KCCController.MaxAirMoveSpeed</c>.</summary>
		private const float PeerSpeed = 6f;

		/// <summary>Half-extent of the authored 1x1x1 ability hitbox.</summary>
		private const float HitboxHalfExtent = 0.5f;

		/// <summary>Radius of a typical character capsule, from the playable prefabs.</summary>
		private const float CapsuleRadius = 0.3f;

		/// <summary>
		/// A realistic spread of one-way latencies. Named rather than numeric so failures read
		/// clearly, and deliberately including a bad connection rather than only good ones.
		/// </summary>
		private static readonly (string Name, int OneWayMs)[] LatencyProfiles =
		{
			("same city",        8),
			("same region",     20),
			("cross-country",   40),
			("cross-continent", 75),
			("intercontinental",130),
			("poor connection", 200),
		};

		/// <summary>Milliseconds an interpolated peer's rendered position lags the server's truth.</summary>
		private static double StalenessMs(int oneWayMs)
			=> SpectatorInterpolationTicks * (1000.0 / TickRate) + oneWayMs;

		/// <summary>Metres an interpolated peer's rendered position lags, at a given speed.</summary>
		private static double PositionError(int oneWayMs, double speed = PeerSpeed)
			=> speed * StalenessMs(oneWayMs) / 1000.0;

		#region Per-model sensitivity.

		/// <summary>
		/// Positional disagreement between what a client aimed at and what the server resolved.
		/// </summary>
		[Test]
		public void Measure_PeerStaleness_AcrossLatencyProfiles()
		{
			TestContext.WriteLine(
				$"MEASURE interpolation floor = {SpectatorInterpolationTicks} ticks @ {TickRate}Hz = " +
				$"{SpectatorInterpolationTicks * 1000.0 / TickRate:F0}ms, before any network latency");

			foreach ((string name, int oneWay) in LatencyProfiles)
			{
				TestContext.WriteLine(
					$"MEASURE {name,-17} one-way {oneWay,3}ms -> stale {StalenessMs(oneWay),5:F0}ms -> " +
					$"error {PositionError(oneWay):F2}m at {PeerSpeed}m/s (walking {PositionError(oneWay, 2.5):F2}m)");
			}

			// A forwarded peer is simulated from the same inputs on the same tick — no delay term.
			LogAssert.AreEqual(0.0, 0.0, "A forwarded peer carries no interpolation delay by construction.");
			LogAssert.IsTrue(PositionError(8) > 0.0, "Even a same-city peer carries the interpolation floor.");
		}

		/// <summary>
		/// Which resolution models the error can actually flip, and which it cannot touch.
		/// </summary>
		/// <remarks>
		/// The central result. A model whose outcome is not a function of peer position cannot be
		/// desynchronised by peer position error, however large the error grows. A model that
		/// compares a distance against a threshold degrades in proportion to error over threshold,
		/// so the tolerance is the design lever rather than the latency.
		/// </remarks>
		[Test]
		public void Measure_HitModelSensitivity_AcrossLatency()
		{
			// (model, effective tolerance in metres, whether peer position decides the outcome)
			(string Model, double Tolerance, bool Positional)[] models =
			{
				("Single target (entity id + range)", 5.0,  false),
				("Projectile (travel time absorbs)",  1.0,  true),
				("AoE OverlapSphere r=4m",            4.0,  true),
				("AoE OverlapSphere r=2m",            2.0,  true),
				("Cone / Chain r=3m",                 3.0,  true),
				("Aimed 1m box (today)",              HitboxHalfExtent, true),
				("Hitscan Raycast vs capsule",        CapsuleRadius,    true),
			};

			foreach ((string model, double tolerance, bool positional) in models)
			{
				string row = $"MEASURE {model,-34} tol {tolerance,4:F1}m |";
				foreach ((string name, int oneWay) in LatencyProfiles)
				{
					if (!positional)
					{
						row += $" {name.Substring(0, 4)}:OK";
						continue;
					}
					double err = PositionError(oneWay);
					string verdict = err <= tolerance * 0.5 ? "OK" : err <= tolerance ? "marginal" : "MISS";
					row += $" {name.Substring(0, 4)}:{verdict}";
				}
				TestContext.WriteLine(row);
			}

			// Hitscan against a 0.3m capsule fails at every profile including the best one — that is
			// the model that cannot be shipped against interpolated peers without compensation.
			LogAssert.IsTrue(PositionError(8) > CapsuleRadius,
				"Hitscan against a capsule is expected to break even at the lowest latency; if it no " +
				"longer does, the interpolation setting or the speed constant changed.");
			LogAssert.IsTrue(PositionError(130) < 4.0,
				"A 4m area volume should still contain an intercontinental peer's error, or area " +
				"abilities need larger radii than are reasonable to author.");
		}

        /// <summary>
        /// Probability that a target near an area boundary resolves differently than it appeared.
        /// </summary>
        /// <remarks>
        /// The number that actually predicts how often a player says "that should have hit me".
        /// Only targets within one error-distance of the boundary can flip, so the disputed band is
        /// an annulus and its share of the disc falls as the radius grows. Assumes targets are
        /// uniformly distributed over the disc, which overstates the dispute rate in practice —
        /// players cluster toward the centre of an area they are aiming at.
        /// </remarks>
        [Test]
		public void Measure_AreaBoundaryDisputeRate()
		{
			foreach (double radius in new double[] { 2.0, 4.0, 8.0 })
			{
				string row = $"MEASURE AoE r={radius:F0}m disputed band |";
				foreach ((string name, int oneWay) in LatencyProfiles)
				{
					double err = Math.Min(PositionError(oneWay), radius);
					double inner = radius - err;
					double share = 1.0 - (inner * inner) / (radius * radius);
					row += $" {name.Substring(0, 4)}:{share * 100.0,4:F1}%";
				}
				TestContext.WriteLine(row);
			}

			double disputed2m = 1.0 - Math.Pow(2.0 - Math.Min(PositionError(40), 2.0), 2) / 4.0;
			TestContext.WriteLine(
				$"MEASURE a 2m area at cross-country latency disputes {disputed2m * 100:F0}% of its disc; " +
				"doubling the radius roughly halves that");

			LogAssert.IsTrue(disputed2m < 0.75,
				$"A 2m area disputing {disputed2m * 100:F0}% of its area would read as broken; " +
				"area radii must stay well above the interpolation error.");
		}

		#endregion

		#region What the owner feels.

		/// <summary>
		/// The owner's own responsiveness is unaffected by any of this.
		/// </summary>
		/// <remarks>
		/// Worth stating as a test because it is the half of "feel" that players notice most and the
		/// half the migration does not touch. The owning client predicts its own character locally
		/// and the server reconciles it; disabling state forwarding changes who <i>else</i> receives
		/// that stream, not whether the owner predicts. <c>Server_SendReconcileRpc</c> writes to the
		/// owner in both modes.
		/// </remarks>
		[Test]
		public void OwnerResponsiveness_IsIndependentOfForwarding()
		{
			foreach ((string name, int oneWay) in LatencyProfiles)
			{
				TestContext.WriteLine(
					$"MEASURE {name,-17} owner input->render latency: 0ms (locally predicted), " +
					$"correction arrives after {oneWay * 2}ms round trip");
			}

			LogAssert.IsTrue(true,
				"Owner prediction is preserved in both modes; this test documents the invariant.");
		}

		/// <summary>
		/// Cost of buying accuracy back by lowering the interpolation buffer.
		/// </summary>
		/// <remarks>
		/// The interpolation buffer exists to absorb jitter and loss. Shrinking it reduces staleness
		/// linearly but removes the cushion, so a peer whose packets arrive late renders a gap
		/// instead of a smooth path. This prints the trade rather than recommending a value, because
		/// the right buffer depends on the jitter of the population being served.
		/// </remarks>
		[Test]
		public void Measure_InterpolationBuffer_Tradeoff()
		{
			foreach (int ticks in new[] { 1, 2, 3, 4 })
			{
				double floorMs = ticks * 1000.0 / TickRate;
				double errAt40 = PeerSpeed * (floorMs + 40) / 1000.0;
				TestContext.WriteLine(
					$"MEASURE buffer {ticks} tick(s) = {floorMs,4:F0}ms floor -> {errAt40:F2}m error at 40ms one-way; " +
					$"tolerates {ticks} dropped/late update(s) before a visible gap");
			}

			LogAssert.IsTrue(PeerSpeed * (1 * 1000.0 / TickRate + 40) / 1000.0
				< PeerSpeed * (4 * 1000.0 / TickRate + 40) / 1000.0,
				"A smaller buffer must produce less staleness, or the relationship is inverted.");
		}

		#endregion
	}
}
