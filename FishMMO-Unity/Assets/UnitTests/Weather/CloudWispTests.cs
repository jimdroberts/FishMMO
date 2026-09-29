using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// A cloud's edge: the mixing shell, the water its mixtures keep, the eddies' pattern and their
	/// average (CloudClimate's twins of FishCloudVolume.hlsl's). Shapes and ranges, not spot values.
	/// </summary>
	/// <remarks>
	/// The clouds were soft blobs because every one of them was all edge — a ramp eight hundred metres
	/// wide — eroded by a noise that barely varied, the same thinning everywhere. These hold the
	/// replacement to what makes an edge look like a cloud's: a core the erosion never touches, a
	/// fringe that is thinner than the cloud, drier air fraying it more, the eddies' average kept at
	/// every distance, and a drift that never makes the pattern jump.
	/// </remarks>
	[TestFixture]
	public class CloudWispTests
	{
		private static PlanetAir Earth => PlanetAir.Earthlike;

		private static readonly float[] Criticals = { 0f, 0.3f, 0.6f, 0.8f, 0.93f, 0.98f };

		// ── The core ────────────────────────────────────────────────────

		private static float Ramp(float critical) => CloudClimate.EdgeRampWidth(critical, 0f);

		[Test]
		public void TheEdgesEddiesNeverTouchACloudsCore()
		{
			// Past the eddies' reach inside the cut every mixture is cloud, whatever the air round it
			// and whatever the eddy there, and the water is all there. Past the ramp and the reach
			// outside, nothing.
			foreach (float c in Criticals)
			{
				float ramp = Ramp(c);
				for (float inside = CloudClimate.EddyReach; inside < 5f; inside += 0.25f)
				{
					foreach (float push in new[] { -1f, -0.5f, 0f, 0.5f, 1f })
					{
						LogAssert.IsTrue(Mathf.Abs(CloudClimate.EdgeWater(inside + CloudClimate.EddyReach * push, ramp) - 1f) < 1e-5f,
							$"the core keeps all its water at χ* {c}, {inside:0.00} shells in, push {push}");
					}
					LogAssert.IsTrue(Mathf.Abs(CloudClimate.ExpectedEdgeWater(inside, CloudClimate.EddyReach, ramp) - 1f) < 1e-4f, $"and so does its average at χ* {c}");
				}
				float outside = -(ramp + CloudClimate.EddyReach) - 0.01f;
				LogAssert.IsTrue(CloudClimate.ExpectedEdgeWater(outside, CloudClimate.EddyReach, ramp) < 1e-6f, "and nothing is made from clear air past the fringe");
			}
		}

		[Test]
		public void ASmallCloudInDryAirKeepsItsWater()
		{
			// The regression this pins: with the shell inside the cut, a cumulus narrower than a shell
			// never reached pure cloud, and dry air evaporated it whole — a scattered sky fell from 7 %
			// cover to 1 %. Anything inside the cut holds the cloud's water, however little inside.
			foreach (float c in Criticals)
			{
				LogAssert.IsTrue(Mathf.Abs(CloudClimate.EdgeWater(0.01f, Ramp(c)) - 1f) < 1e-5f, $"just inside the cut at χ* {c}");
				// A heap whose middle is a third of a shell inside the cut keeps most of its water on
				// average, even in air dry enough to evaporate nearly any mixture.
				float kept = CloudClimate.ExpectedEdgeWater(0.3f, CloudClimate.EddyReach, Ramp(c));
				LogAssert.IsTrue(kept > 0.6f, $"a small heap's middle keeps {kept:0.00} of its water at χ* {c}");
			}
		}

		[Test]
		public void TheWaterFallsOneWayAcrossTheEdge()
		{
			foreach (float c in Criticals)
			{
				float last = -1f;
				for (int k = 0; k <= 60; k++)
				{
					float inside = -2f + k / 20f;
					float kept = CloudClimate.ExpectedEdgeWater(inside, CloudClimate.EddyReach, Ramp(c));
					LogAssert.IsTrue(kept >= last - 1e-5f, $"no dip across the edge at χ* {c}: {kept:0.0000} after {last:0.0000} at {inside:0.00}");
					LogAssert.IsTrue(kept >= 0f && kept <= 1f + 1e-5f, "a share of the cloud's water");
					last = kept;
				}
			}
		}

		// ── The fringe ──────────────────────────────────────────────────

		[Test]
		public void DrierAirLeavesAThinnerFringe()
		{
			// The water outside the cut, summed across the fringe, in shells: what the edge's mixtures
			// keep. Dry air evaporates nearly all of it — the edge is the cut itself, crisp; saturated air
			// keeps a whole shell of it, soft.
			float Fringe(float c)
			{
				float sum = 0f;
				const int N = 400;
				for (int k = 0; k < N; k++)
				{
					float inside = -2f + 2f * (k + 0.5f) / N;
					sum += CloudClimate.EdgeWater(inside, Ramp(c)) * (2f / N);
				}
				return sum;
			}
			float last = float.MaxValue;
			foreach (float c in Criticals)
			{
				float fringe = Fringe(c);
				LogAssert.IsTrue(fringe < last, $"drier air, thinner fringe: {fringe:0.000} shells at χ* {c}, {last:0.000} before");
				last = fringe;
			}
			LogAssert.IsTrue(Fringe(0.93f) < 0.05f, $"a dry day's fringe is a sliver: {Fringe(0.93f):0.000} shells");
			LogAssert.IsTrue(Fringe(0f) > 0.45f, $"a saturated day's is half a shell of water: {Fringe(0f):0.000}");
			// And the pixel's cone is the floor: an edge finer than a pixel is not drawn finer.
			LogAssert.IsTrue(CloudClimate.EdgeRampWidth(0.98f, 0.3f) >= 0.3f - 1e-6f, "never under the cone");
		}

		[Test]
		public void TheCriticalFractionFollowsHowDryTheAirIs()
		{
			// Dry air round a cloud needs a mixture to be nearly all cloud before any water survives;
			// saturated air takes any mixture. Physically: χ* = Δq/(q_c + Δq).
			float last = 1.01f;
			foreach (float humidity in new[] { 0.2f, 0.4f, 0.6f, 0.8f, 0.95f })
			{
				AirColumn air = AirColumn.Of(Earth, 288f, humidity, 0f, 0.35f);
				float c = CloudClimate.ColumnEdgeCritical(air, Earth);
				LogAssert.IsTrue(c < last, $"damper air, a lower critical fraction: {c:0.000} at humidity {humidity}");
				LogAssert.IsTrue(c >= 0f && c <= CloudClimate.MaxCritical, "a fraction");
				last = c;
			}
			AirColumn ordinary = AirColumn.Of(Earth, 288f, 0.5f, 0f, 0.35f);
			float typical = CloudClimate.ColumnEdgeCritical(ordinary, Earth);
			// A cumulus holds a few tenths of a gram of water a kilogram; the air round it at 50–60 %
			// humidity lacks a few grams: nine tenths or more of a mixture must be cloud.
			Assert.That(typical, Is.InRange(0.7f, 0.98f), "an ordinary day's heap");
			AirColumn saturated = AirColumn.Of(Earth, 288f, 1f, 0f, 0.35f);
			LogAssert.IsTrue(CloudClimate.ColumnEdgeCritical(saturated, Earth) < 0.05f, "saturated air evaporates nothing");

			// The formula's own shape: no deficit, no threshold; no condensate, everything evaporates.
			LogAssert.IsTrue(CloudClimate.CriticalMixingFraction(1e-4f, 8e-3f, 1f) < 1e-6f, "no deficit");
			LogAssert.IsTrue(CloudClimate.CriticalMixingFraction(0f, 8e-3f, 0.5f) >= CloudClimate.MaxCritical - 1e-6f, "no water to keep");
		}

		[Test]
		public void IceEdgesAreSofterThanLiquidOnesInTheSameAir()
		{
			// Air saturated over liquid is supersaturated over ice (1.6 times at −40 °C), so the same
			// air round a cirrus is far nearer saturation than round a supercooled layer.
			LogAssert.IsTrue(CloudClimate.IceSupersaturation(233f, Condensate.Water) > 1.4f, "e_l/e_i at −40 °C");
			LogAssert.IsTrue(Mathf.Approximately(CloudClimate.IceSupersaturation(280f, Condensate.Water), 1f), "no ice above freezing");
			AirColumn air = AirColumn.Of(Earth, 288f, 0.5f, 0f, 0.35f);
			float bottom = air.IceLevel, top = air.IceLevel + 800f;
			float extinction = 5e-4f;
			float ice = CloudClimate.LayerEdgeCritical(air, Earth, bottom, top, extinction, 30e-6f, true);
			float liquid = CloudClimate.LayerEdgeCritical(air, Earth, bottom, top, extinction, 30e-6f, false);
			LogAssert.IsTrue(ice < liquid, $"cirrus frays less than drops would at its height: {ice:0.000} against {liquid:0.000}");
		}

		// ── Wisps below, billows above ─────────────────────────────────

		[Test]
		public void WispsLowInTheCloudBillowsHigher()
		{
			LogAssert.IsTrue(Mathf.Approximately(CloudClimate.EddyPolarity(0f), -1f), "engulfing at the base: wisps");
			LogAssert.IsTrue(Mathf.Approximately(CloudClimate.EddyPolarity(0.5f), 1f), "pushing out higher up: billows");
			float last = -2f;
			for (int k = 0; k <= 20; k++)
			{
				float p = CloudClimate.EddyPolarity(k / 20f);
				LogAssert.IsTrue(p >= last, "one way, from the base up");
				last = p;
			}
		}

		// ── Near and far ───────────────────────────────────────────────

		[Test]
		public void TheEddiesAverageIsTheAverageOfTheEddies()
		{
			// What is drawn where the eddies cannot be resolved must be what they do on average, or the
			// cloud would grow or shrink as it drew away. The pattern carries the edge evenly over
			// ± reach; the closed form has to be that average.
			foreach (float c in Criticals)
			{
				float ramp = Ramp(c);
				for (int k = 0; k <= 30; k++)
				{
					float inside = -1.5f + k / 15f;
					double sum = 0.0;
					const int N = 4000;
					for (int j = 0; j < N; j++)
					{
						float push = -1f + 2f * (j + 0.5f) / N;
						sum += CloudClimate.EdgeWater(inside + CloudClimate.EddyReach * push, ramp);
					}
					float numeric = (float)(sum / N);
					float closed = CloudClimate.ExpectedEdgeWater(inside, CloudClimate.EddyReach, ramp);
					LogAssert.IsTrue(Mathf.Abs(numeric - closed) < 2e-3f, $"χ* {c}, {inside:0.00} shells in: {closed:0.0000} against {numeric:0.0000}");
				}
			}
		}

		[Test]
		public void TheDetailSurvivesAsFarAsTheScreenCanDrawIt()
		{
			// Balanced at 1080p, a 60° view: the marched texel's cone, and half of it for what the
			// steadying resolves.
			CloudClimate.Scales scales = CloudClimate.ScalesFor(Earth, 45f, 238u);
			float rows = 1080f * WeatherTierSettings.Balanced().CloudResolution;
			float m11 = 1f / Mathf.Tan(30f * Mathf.Deg2Rad);
			float spread = 2f / (m11 * rows) * SkySystem.CloudDetailConeShare;
			Vector3 At(float metres) => CloudClimate.DetailResolved(metres * spread, scales.LowDetail);

			// The old fade had taken all of it by 11.5 km. The eddies' main octave is still whole there.
			LogAssert.IsTrue(At(11500f).x > 0.99f, $"the main octave at 11.5 km: {At(11500f).x:0.00}");
			LogAssert.IsTrue(At(2500f).z > 0.99f, "every octave near to hand");
			// Finer octaves go first, and nothing is drawn finer than a pixel.
			for (float d = 500f; d < 80000f; d *= 1.3f)
			{
				Vector3 r = At(d);
				LogAssert.IsTrue(r.x >= r.y - 1e-5f && r.y >= r.z - 1e-5f, $"the fine octaves fade before the coarse at {d:0} m");
			}
			LogAssert.IsTrue(At(60000f).x < 0.01f, "past the pixel it is gone, averaged, not aliased");
		}

		[Test]
		public void TheDetailDriftWrapsByWholeTiles()
		{
			// The drift is wrapped on the CPU so a float can hold it. A wrap must move the lookup by a
			// whole number of tiles or the pattern jumps (the 42 × k rule): along the wind the lookup
			// divides by the stretch, so that period is a stretch of tiles.
			CloudClimate.Scales scales = CloudClimate.ScalesFor(Earth, 45f, 238u);
			foreach (float stretch in new[] { 1f, scales.HighStretch, 7f })
			{
				foreach (float tile in new[] { scales.LowDetail, scales.MidDetail, scales.HighDetail })
				{
					Vector2 period = CloudClimate.DetailDriftPeriod(tile, stretch);
					float alongTiles = period.x / Mathf.Max(1f, stretch) / Mathf.Max(20f, tile);
					float acrossTiles = period.y / Mathf.Max(20f, tile);
					LogAssert.IsTrue(Mathf.Abs(alongTiles - Mathf.Round(alongTiles)) < 1e-4f && alongTiles >= 1f,
						$"a wrap along the wind moves the lookup {alongTiles:0.0000} tiles (stretch {stretch}, tile {tile:0})");
					LogAssert.IsTrue(Mathf.Abs(acrossTiles - Mathf.Round(acrossTiles)) < 1e-4f && acrossTiles >= 1f,
						$"and across it {acrossTiles:0.0000}");
				}
			}
			LogAssert.IsTrue(scales.HighStretch == Mathf.Round(scales.HighStretch), "cirrus's stretch is a whole number");
		}
	}
}
