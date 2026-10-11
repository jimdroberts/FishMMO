using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The heat shimmer's air (<see cref="HeatShimmer"/>): when the surface layer boils and how hard, from the
	/// surface energy budget and convective similarity. Ranges from the measurements in its remarks, not spot values.
	/// </summary>
	[TestFixture]
	public class HeatShimmerTests
	{
		private const string ShimmerShader = "Assets/Prefabs/Client/Weather/Shaders/FishHeatShimmer.shader";

		// A clear, still desert noon: the strongest shimmer there is.
		private static float DesertNoon(float sun = 75f, float cloud = 0f, float dryness = 1f, float wet = 0f, float snow = 0f)
		{
			return HeatShimmer.SensibleHeat(sun, cloud, 1f, dryness, wet, snow);
		}

		[Test]
		public void ClearSkySunlight_FollowsHaurwitz()
		{
			// ~1000 W/m² with the sun high, a few hundred at 15°, nothing once it has set.
			Assert.That(HeatShimmer.ClearSkySunlight(90f), Is.InRange(1000f, 1060f));
			Assert.That(HeatShimmer.ClearSkySunlight(15f), Is.InRange(200f, 350f));
			Assert.AreEqual(0f, HeatShimmer.ClearSkySunlight(0f));
			Assert.AreEqual(0f, HeatShimmer.ClearSkySunlight(-10f));
		}

		[Test]
		public void NoShimmerAtNight()
		{
			// After sunset the ground cools below the air: the layer is stable and nothing boils.
			Assert.AreEqual(0f, DesertNoon(sun: -5f));
			Assert.AreEqual(0f, DesertNoon(sun: 0f));
			Assert.AreEqual(0f, HeatShimmer.Turbulence(0f, 0f));
		}

		[Test]
		public void DesertNoon_IsFullStrength()
		{
			// 400–600 W/m² of sensible heat is what a clear desert noon measures.
			float heat = DesertNoon();
			Assert.That(heat, Is.GreaterThan(0.85f));
			Assert.That(HeatShimmer.Turbulence(heat, 0f), Is.GreaterThan(0.8f));
		}

		[Test]
		public void StrengthGrowsWithTheSun()
		{
			float last = 0f;
			for (float sun = 2f; sun <= 70f; sun += 4f)
			{
				float heat = DesertNoon(sun: sun);
				Assert.That(heat, Is.GreaterThanOrEqualTo(last), $"sun at {sun}°");
				last = heat;
			}
			// A low morning sun barely heats the ground past what it radiates away.
			Assert.That(DesertNoon(sun: 8f), Is.LessThan(0.25f * DesertNoon()));
		}

		[Test]
		public void OvercastCutsIt()
		{
			Assert.That(DesertNoon(cloud: 1f), Is.LessThan(0.35f * DesertNoon()));
			// A few fair-weather clouds hardly matter (Kasten–Czeplak's N^3.4).
			Assert.That(DesertNoon(cloud: 0.3f), Is.GreaterThan(0.85f * DesertNoon()));
		}

		[Test]
		public void WetOrGreenGround_SpendsTheSunOnEvaporation()
		{
			Assert.That(DesertNoon(wet: 1f), Is.LessThan(0.25f * DesertNoon()));
			// A lush meadow (Bowen ratio ~0.3) against bare sand (~5).
			Assert.That(DesertNoon(dryness: 0f), Is.LessThan(0.4f * DesertNoon()));
			Assert.That(DesertNoon(dryness: 0f), Is.GreaterThan(0f), "a green field still shimmers a little on a hot day");
		}

		[Test]
		public void SnowNeverShimmers()
		{
			Assert.AreEqual(0f, DesertNoon(snow: 1f));
		}

		[Test]
		public void WindMixesTheLayerAway()
		{
			// Free convection below the mixing wind; Cn² falls as the square of the wind above it.
			float calm = HeatShimmer.Turbulence(1f, 0f);
			Assert.That(HeatShimmer.Turbulence(1f, 1f), Is.GreaterThan(0.85f * calm));
			Assert.That(HeatShimmer.Turbulence(1f, 10f), Is.LessThan(0.1f * calm));
			float last = calm;
			for (float u = 0.5f; u <= 20f; u += 0.5f)
			{
				float t = HeatShimmer.Turbulence(1f, u);
				Assert.That(t, Is.LessThan(last), $"wind {u} m/s");
				last = t;
			}
		}

		[Test]
		public void TurbulenceGoesAsTheHeatToFourThirds()
		{
			// Free-convection similarity (Wyngaard 1971): Cn² ∝ H^(4/3).
			float ratio = HeatShimmer.Turbulence(0.5f, 0f) / HeatShimmer.Turbulence(1f, 0f);
			Assert.AreEqual(Mathf.Pow(0.5f, 4f / 3f), ratio, 1e-4f);
		}

		[Test]
		public void SkinExcess_AndTheMirageAngle()
		{
			// A sunlit desert surface runs 20–40 K over the air a metre up in a calm, a few in a stiff breeze.
			Assert.That(HeatShimmer.SkinExcessKelvin(1f, 0f), Is.InRange(20f, 40f));
			Assert.That(HeatShimmer.SkinExcessKelvin(1f, 10f), Is.LessThan(8f));
			// The inferior mirage lies within a few tenths of a degree of the horizon.
			float degrees = HeatShimmer.MirageAngle(25f) * Mathf.Rad2Deg;
			Assert.That(degrees, Is.InRange(0.3f, 0.5f));
			Assert.AreEqual(0f, HeatShimmer.MirageAngle(0f));
		}

		[Test]
		public void GroundDryness_FromTheBiomesClimate()
		{
			Assert.AreEqual(1f, HeatShimmer.GroundDryness(true, 0.5f, 1f), "loose ground (sand, dust) is bare whatever the climate");
			Assert.That(HeatShimmer.GroundDryness(false, -1f, -0.6f), Is.GreaterThan(0.9f), "desert");
			Assert.That(HeatShimmer.GroundDryness(false, -0.4f, 0.2f), Is.InRange(0.2f, 0.6f), "steppe and grassland");
			Assert.AreEqual(0f, HeatShimmer.GroundDryness(false, 0.5f, 1f), "rainforest");
			Assert.That(HeatShimmer.EffectiveDryness(1f, 1f), Is.LessThan(HeatShimmer.EffectiveDryness(1f, 0f)), "humid air means damp ground");
		}

		[Test]
		public void Shader_GuardsTheForegroundAndIsPathIntegrated()
		{
			string shader = SourceScanPins.ReadCode(ShimmerShader);
			// Strength taken at the nearest depth round the pixel, and the displaced sample tested for nearer things.
			StringAssert.Contains("variance *= guard * guard * guard * guard;", shader);
			StringAssert.Contains("HeatNearest(there)", shader);
			// Jitter is the square root of the path through the layer.
			StringAssert.Contains("float sigma = sqrt(variance);", shader);
			StringAssert.Contains("depth / max(sinIncidence, 1e-3)", shader);
		}
	}
}
