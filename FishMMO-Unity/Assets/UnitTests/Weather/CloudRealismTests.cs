using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The clouds' physics against real clouds' numbers: the wind with height they drift and lean by,
	/// the drops they are made of, how they scatter, and how much light gets through them. Shapes and
	/// ranges, not spot values — the ranges are the measured ones (CloudClimate's remarks give the
	/// sources).
	/// </summary>
	[TestFixture]
	public class CloudRealismTests
	{
		private static PlanetAir Earth => PlanetAir.Earthlike;

		private static AirColumn Ordinary()
		{
			return AirColumn.Of(Earth, 288f, 0.6f, -0.1f, 0.35f);
		}

		// ── Wind with height ─────────────────────────────────────────────

		[Test]
		public void TheBandsDriftFasterWithHeightByTheWorldsOwnWind()
		{
			CloudClimate.Scales scales = CloudClimate.ScalesFor(Earth, 45f, 238u);
			LogAssert.IsTrue(scales.MidWind > 1.2f, $"the middle runs well ahead of the column: {scales.MidWind:0.00}×");
			LogAssert.IsTrue(scales.HighWind > scales.MidWind, $"and the ice faster again: {scales.HighWind:0.00}× against {scales.MidWind:0.00}×");
			// Our world at 45°: a steering wind of 4–12 m/s under a jet of 30–60 m/s at the tropopause
			// puts the cirrus level at two to five times the column's drift.
			Assert.That(scales.HighWind, Is.InRange(1.8f, 5f), "the ice level against the column");

			// The scales are the profile's own ratios, taken from its steady wind at the base.
			WindProfile wind = CloudClimate.ClimatologicalWind(Earth, 45f, WindBelts.Earthlike, scales.SteeringWind, scales.MeanBase, scales.MeanTropopause);
			LogAssert.IsTrue(Mathf.Abs(wind.At(scales.MeanBase) - scales.SteeringWind) < 0.05f * scales.SteeringWind,
				$"the free wind at the base is the world's steady drift: {wind.At(scales.MeanBase):0.0} against {scales.SteeringWind:0.0} m/s");
		}

		[Test]
		public void TheDriftScalesDoNotChangeWithTheWeather()
		{
			// Nothing about the weather goes in: the same world and latitude give the same scales
			// whatever the air is doing, or the bands would jump across the sky when it changed.
			CloudClimate.Scales a = CloudClimate.ScalesFor(Earth, 45f, 238u);
			AirColumn stormy = AirColumn.Of(Earth, 302f, 0.9f, -0.8f, 0.9f);
			AirColumn settled = AirColumn.Of(Earth, 280f, 0.2f, 0.9f, 0.05f);
			LogAssert.IsTrue(stormy.Cape > settled.Cape, "the two airs really are different");
			CloudClimate.Scales b = CloudClimate.ScalesFor(Earth, 45f, 238u);
			LogAssert.AreEqual(a.MidWind, b.MidWind);
			LogAssert.AreEqual(a.HighWind, b.HighWind);
			LogAssert.AreEqual(a.HighStretch, b.HighStretch);

			// The lean, which does not accumulate, may follow the weather: a warmer surface weakens the
			// thermal wind a little, and it is read from the air of the moment.
			WindProfile warm = WindProfile.Of(8f, stormy, Earth, 45f, WindBelts.Earthlike);
			WindProfile cold = WindProfile.Of(8f, settled, Earth, 45f, WindBelts.Earthlike);
			LogAssert.IsTrue(warm.Shear > 0f && cold.Shear > 0f, "both have a thermal wind to lean by");
		}

		[Test]
		public void AWorldThatDoesNotTurnHasNoThermalWindAndItsBandsDriftTogether()
		{
			PlanetAir still = Earth;
			still.RotationRate = 0f;
			CloudClimate.Scales scales = CloudClimate.ScalesFor(still, 45f, 238u);
			Assert.That(scales.MidWind, Is.EqualTo(1f).Within(1e-3f), "no shear, no difference in drift");
			Assert.That(scales.HighWind, Is.EqualTo(1f).Within(1e-3f));
			LogAssert.AreEqual(1f, scales.HighStretch, "and cirrus is not drawn out");
		}

		[Test]
		public void TheDriftWrapStaysWholeForEveryStretch()
		{
			// The drift wraps every 42 tiles along the wind, stretched by the band's stretch; the lift
			// reads at half the tile's frequency (21 × stretch per wrap) and the carve at five times
			// (210). Every one of those must be a whole number of tiles or the sky snaps at the wrap.
			float[] latitudes = { 5f, 20f, 35f, 45f, 60f, 80f };
			foreach (float latitude in latitudes)
			{
				for (uint seed = 1; seed < 6; seed++)
				{
					CloudClimate.Scales scales = CloudClimate.ScalesFor(Earth, latitude, seed * 977u);
					float stretch = scales.HighStretch;
					LogAssert.AreEqual(Mathf.Round(stretch), stretch, $"a whole stretch at {latitude}°: {stretch}");
					Assert.That(stretch, Is.InRange(1f, 12f));
					float liftTiles = 42f * stretch / 2f;
					LogAssert.AreEqual(Mathf.Round(liftTiles), liftTiles, "the lift's tiles per wrap");
				}
			}
		}

		[Test]
		public void TowersLeanDownwindMoreWithHeightAndShearAndLessTheFasterTheyRise()
		{
			WindProfile wind = CloudClimate.ClimatologicalWind(Earth, 45f, WindBelts.Earthlike, 9f, 1000f, 11000f);
			float last = 0f;
			for (float h = 1100f; h <= 10000f; h += 450f)
			{
				float lean = CloudClimate.Lean(wind, 1000f, h, 10f);
				LogAssert.IsTrue(lean > last, $"leaning further with every step up: {lean:0} m at {h:0} m");
				last = lean;
			}
			LogAssert.IsTrue(CloudClimate.Lean(wind, 1000f, 6000f, 20f) < CloudClimate.Lean(wind, 1000f, 6000f, 5f), "a stronger updraught stands straighter");

			PlanetAir spun = Earth;
			spun.RotationRate *= 3f;
			WindProfile weak = CloudClimate.ClimatologicalWind(spun, 45f, WindBelts.Earthlike, 9f, 1000f, 11000f);
			LogAssert.IsTrue(weak.Shear < wind.Shear, "a faster world has the weaker thermal wind");
			LogAssert.IsTrue(CloudClimate.Lean(weak, 1000f, 6000f, 10f) < CloudClimate.Lean(wind, 1000f, 6000f, 10f), "and its towers lean less");

			// A tower at a storm's updraught through our jet latitude's shear leans tens of degrees:
			// kilometres at the tropopause, as a sheared cumulonimbus does.
			float top = CloudClimate.Lean(wind, 1000f, 11000f, CloudClimate.StormUpdraught);
			Assert.That(top, Is.InRange(1500f, 15000f), $"a storm's top leans {top:0} m");
		}

		[Test]
		public void TheShadersClosedFormLeanIsTheIntegral()
		{
			// Above the mixed layer the thermal wind is a straight line, and the closed form the shader
			// uses must be the integral there, heap and tower parts alike; continuous across the
			// ordinary top, and never turning back.
			WindProfile wind = CloudClimate.ClimatologicalWind(Earth, 45f, WindBelts.Earthlike, 9f, 1000f, 11000f);
			float baseM = wind.BoundaryTop;
			for (float h = baseM + 200f; h < baseM + 1500f; h += 200f)
			{
				float numeric = CloudClimate.Lean(wind, baseM, h, CloudClimate.HeapUpdraught);
				float closed = CloudClimate.ShearedLean(wind.Shear, baseM, baseM + 1500f, wind.Tropopause, CloudClimate.HeapUpdraught, CloudClimate.StormUpdraught, h);
				Assert.That(closed, Is.EqualTo(numeric).Within(0.05f * numeric + 1f), $"at {h - baseM:0} m above the base");
			}
			float below = CloudClimate.ShearedLean(wind.Shear, baseM, baseM + 1500f, wind.Tropopause, 3f, 20f, baseM + 1499f);
			float above = CloudClimate.ShearedLean(wind.Shear, baseM, baseM + 1500f, wind.Tropopause, 3f, 20f, baseM + 1501f);
			Assert.That(above, Is.EqualTo(below).Within(0.01f * below + 1f), "continuous at the ordinary top");
			float prior = 0f;
			for (float h = baseM; h < wind.Tropopause + 2000f; h += 250f)
			{
				float lean = CloudClimate.ShearedLean(wind.Shear, baseM, baseM + 1500f, wind.Tropopause, 3f, 20f, h);
				LogAssert.IsTrue(lean >= prior - 1e-3f, $"never turning back: {lean:0} m at {h:0} m");
				prior = lean;
			}
			LogAssert.AreEqual(0f, CloudClimate.ShearedLean(wind.Shear, baseM, baseM + 1500f, wind.Tropopause, 3f, 20f, baseM - 300f), "nothing leans below its base");
		}

		[Test]
		public void CirrusFallStreaksTrailFurtherInShearAndUnderLessPull()
		{
			float here = CloudClimate.IceFallSpeed(Earth);
			Assert.That(here, Is.InRange(0.2f, 0.8f), "cirrus ice falls at a fraction of a metre a second (Heymsfield and Iaquinta 2000)");
			PlanetAir light = Earth;
			light.Gravity *= 0.5f;
			LogAssert.IsTrue(CloudClimate.IceFallSpeed(light) < here, "less pull, slower ice");

			float depth = CloudClimate.FallStreakDepth(Earth);
			LogAssert.IsTrue(CloudClimate.FallStreakLength(4e-3f, depth, here) > CloudClimate.FallStreakLength(2e-3f, depth, here), "more shear, longer trails");
			LogAssert.IsTrue(CloudClimate.FallStreakLength(3e-3f, depth, CloudClimate.IceFallSpeed(light)) > CloudClimate.FallStreakLength(3e-3f, depth, here), "slower ice, longer trails");
			Assert.That(CloudClimate.FallStreakLength(2.85e-3f, depth, here), Is.InRange(1000f, 20000f), "kilometres of trail at our jet latitude");
		}

		// ── What clouds are made of ─────────────────────────────────────

		[Test]
		public void ACumulusHasTheExtinctionAndDropsOfARealOne()
		{
			AirColumn column = Ordinary();
			float c = column.ExtinctionCoefficient(Earth);
			// Hess, Koepke and Schult 1998: cumulus 0.05–0.12 /m, stratus 0.04–0.06 /m.
			Assert.That(c * Mathf.Pow(500f, 2f / 3f), Is.InRange(0.03f, 0.12f), "β half a kilometre above the base");
			LogAssert.IsTrue(c * Mathf.Pow(50f, 2f / 3f) < 0.3f * c * Mathf.Pow(500f, 2f / 3f), "thin at the base, dense in the core");

			float water = CloudClimate.ColumnWater(column, 500f);
			Assert.That(water * 1000f, Is.InRange(0.1f, 1f), "a few tenths of a gram a cubic metre");
			float d = CloudClimate.DropletDiameterMicrons(water, Earth.DropletsPerCubicMetre, Earth.Water);
			Assert.That(d, Is.InRange(8f, 30f), $"drops of {d:0.0} µm");
			// σ = 3·LWC/(2ρ·r_e) and 2πNr_v² describe the same drops, apart by the spread of their sizes.
			float fromRadius = CloudClimate.Extinction(water, 0.5e-6f * d);
			float fromCount = AirPhysics.DropletExtinction(water, Earth.DropletsPerCubicMetre, 1000f);
			Assert.That(fromRadius / fromCount, Is.InRange(0.8f, 1f), "the effective radius is the volume radius and a little");
		}

		[Test]
		public void CleanSeaAirMakesFewerLargerDropsThanContinentalAir()
		{
			float water = 3e-4f;
			float land = CloudClimate.DropletDiameterMicrons(water, 3e8f, 0f);
			float sea = CloudClimate.DropletDiameterMicrons(water, 1e8f, 1f);
			LogAssert.IsTrue(sea > land, $"maritime drops are larger: {sea:0.0} against {land:0.0} µm");
			LogAssert.IsTrue(CloudClimate.Extinction(water, 0.5e-6f * sea) < CloudClimate.Extinction(water, 0.5e-6f * land), "and the same water is less opaque");
		}

		[Test]
		public void OpticalDepthsFallInEachCloudTypesRange()
		{
			float c = Ordinary().ExtinctionCoefficient(Earth);
			float stratus = CloudClimate.ColumnOpticalDepth(c, 300f);
			float cumulus = CloudClimate.ColumnOpticalDepth(c, 1000f);
			float congestus = CloudClimate.ColumnOpticalDepth(c, 3000f);
			Assert.That(stratus, Is.InRange(3f, 30f), $"a stratus deck, τ {stratus:0}");
			Assert.That(cumulus, Is.InRange(15f, 120f), $"a cumulus, τ {cumulus:0}");
			Assert.That(congestus, Is.InRange(100f, 600f), $"a towering cumulus, τ {congestus:0}");

			var air = new WeatherDriver.Synoptic { Humidity = 0.6f, Pressure = -0.1f, Instability = 0.35f };
			var bands = new CloudBand[CloudClimate.BandCount];
			CloudClimate.Bands(CloudClimate.ScalesFor(Earth, 45f, 238u), Earth, air, Ordinary(), default, bands);
			CloudBand ice = bands[2];
			float cirrus = ice.Extinction * (ice.Top - ice.Bottom);
			Assert.That(cirrus, Is.InRange(0.01f, 3f), $"cirrus is optically thin, τ {cirrus:0.00}");
			LogAssert.AreEqual(0f, ice.DropletDiameter, "and made of ice");
			Assert.That(ice.Asymmetry, Is.InRange(0.72f, 0.8f), "which scatters more broadly than drops");
			Assert.That(bands[0].DropletDiameter, Is.InRange(8f, 30f));
			Assert.That(bands[1].DropletDiameter, Is.InRange(5f, 30f), "the middle is supercooled drops");
		}

		[Test]
		public void AStormsGlaciatedCloudIsThinnerThanItsLiquidBase()
		{
			float ratio = CloudClimate.GlaciatedRatio(16f, Earth);
			Assert.That(ratio, Is.InRange(0.15f, 0.6f), $"the same water as ice takes out {ratio:0.00} as much");
		}

		// ── How clouds scatter ──────────────────────────────────────────

		[Test]
		public void TheDropsPhaseFunctionIsNormalisedAndForwardPeakedLikeMie()
		{
			foreach (float d in new[] { 8f, 16f, 30f })
			{
				double total = 0.0, meanCosine = 0.0;
				const int Steps = 20000;
				for (int i = 0; i < Steps; i++)
				{
					double theta = (i + 0.5) * System.Math.PI / Steps;
					float cos = (float)System.Math.Cos(theta);
					double solid = 2.0 * System.Math.PI * System.Math.Sin(theta) * System.Math.PI / Steps;
					double p = CloudClimate.MiePhase(cos, d);
					total += p * solid;
					meanCosine += p * cos * solid;
				}
				Assert.That(total, Is.EqualTo(1.0).Within(0.03), $"all the light goes somewhere, d {d} µm");
				Assert.That(meanCosine, Is.EqualTo(CloudClimate.MieAsymmetry(d)).Within(0.02), "the closed-form asymmetry is the phase function's own");
				Assert.That(CloudClimate.MieAsymmetry(d), Is.InRange(0.84f, 0.9f), "cloud drops keep about 0.87 of their direction");
				LogAssert.IsTrue(CloudClimate.MiePhase(1f, d) > 1e4f * CloudClimate.MiePhase(0f, d), "a diffraction spike toward the light, almost nothing sideways");
				LogAssert.IsTrue(CloudClimate.MiePhase(-1f, d) > CloudClimate.MiePhase(0f, d), "and a rise toward the back");
			}
			LogAssert.IsTrue(CloudClimate.MieAsymmetry(30f) > CloudClimate.MieAsymmetry(8f), "larger drops throw light further forward");
		}

		[Test]
		public void LightDiffusesThroughAThickCloudInsteadOfDyingByBeersLaw()
		{
			float g = CloudClimate.MieAsymmetry(16f);
			LogAssert.AreEqual(1f, CloudClimate.DiffuseTransmission(0f, g), "nothing in the way, everything through");
			float last = 1f;
			foreach (float tau in new[] { 1f, 5f, 20f, 60f, 300f, 1000f })
			{
				float t = CloudClimate.DiffuseTransmission(tau, g);
				LogAssert.IsTrue(t < last, $"less through a deeper cloud: {t:0.000} at τ {tau}");
				LogAssert.IsTrue(t > Mathf.Exp(-tau), "and always more than the unscattered beam");
				last = t;
			}
			Assert.That(CloudClimate.DiffuseTransmission(60f, g), Is.InRange(0.08f, 0.3f), "about a sixth under a cumulus");
			Assert.That(CloudClimate.DiffuseTransmission(300f, g), Is.InRange(0.01f, 0.08f), "a few per cent under a storm: dark grey, not black");
		}

		[Test]
		public void TheGroundUnderACloudGetsWhatScattersThrough()
		{
			float g = CloudClimate.MieAsymmetry(16f);
			Assert.That(CloudClimate.GroundTransmission(0f, 0.8f, g), Is.EqualTo(1f).Within(1e-4f), "an open sky");
			foreach (float tau in new[] { 0.5f, 3f, 30f, 300f })
			{
				float t = CloudClimate.GroundTransmission(tau, 0.8f, g);
				LogAssert.IsTrue(t >= Mathf.Exp(-tau) && t <= 1f, $"between the beam and everything, τ {tau}: {t:0.000}");
			}
			// The same cloud under a higher sun lets more through: its vertical depth is the same and
			// the light comes in steeper.
			float vertical = 20f;
			float high = CloudClimate.GroundTransmission(vertical / 0.9f, 0.9f, g);
			float low = CloudClimate.GroundTransmission(vertical / 0.3f, 0.3f, g);
			LogAssert.IsTrue(high > low, $"a high sun through the same deck: {high:0.000} against {low:0.000}");
		}

		// ── The light around the clouds ─────────────────────────────────

		[Test]
		public void HighCloudSeesAWhiterSunAtDusk()
		{
			float aerosol = CloudClimate.AerosolExtinction(Earth.AirRelative, 0.6f);
			Vector3 noon = CloudClimate.SunGainAloft(Earth, aerosol, 60f, 1500f);
			Assert.That(noon.y, Is.InRange(1f, 1.25f), "at noon the air under a cloud takes only a little");
			Vector3 dusk = CloudClimate.SunGainAloft(Earth, aerosol, 2f, 8000f);
			LogAssert.IsTrue(dusk.y > noon.y, "at dusk far more");
			LogAssert.IsTrue(dusk.z > dusk.x, $"and most in the blue, which the low air takes first: {dusk}");
			LogAssert.IsTrue(CloudClimate.SunGainAloft(Earth, aerosol, 2f, 8000f).y > CloudClimate.SunGainAloft(Earth, aerosol, 2f, 1500f).y, "higher is brighter");
			Vector3 ground = CloudClimate.SunGainAloft(Earth, aerosol, 30f, 0f);
			Assert.That(ground.y, Is.EqualTo(1f).Within(1e-4f), "the ground sees its own sun");
		}

		[Test]
		public void AMoonLightsEveryBandAsItLightsTheGround()
		{
			// The sun's gain aloft takes back the reddening the air gave the ground's sun; a moon's light
			// was never drawn through the air, so it has none to take back.
			float aerosol = CloudClimate.AerosolExtinction(Earth.AirRelative, 0.9f);
			foreach (float altitude in new[] { 52f, 20f, 3f })
			{
				foreach (float height in new[] { 1500f, 4000f, 8000f })
				{
					Vector3 moon = CloudClimate.LightGainAloft(Earth, aerosol, altitude, height, false);
					LogAssert.IsTrue((moon - Vector3.one).sqrMagnitude < 1e-10f, $"the moon at {altitude}° seen from {height} m is the ground's: {moon}");
					Vector3 sun = CloudClimate.LightGainAloft(Earth, aerosol, altitude, height, true);
					LogAssert.IsTrue((sun - CloudClimate.SunGainAloft(Earth, aerosol, altitude, height)).sqrMagnitude < 1e-10f, "the sun keeps its gain aloft");
				}
			}
			Vector3 low = CloudClimate.SunGainAloft(Earth, aerosol, 20f, 8000f);
			LogAssert.IsTrue(low.z > 1.1f, $"which is well over one for a low light and a high band ({low}): what the moon used to be given");
			string sky = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Sky/SkySystem.cs");
			SourceScanPins.HoldsAndFires("the clouds' moon", sky,
				code => code.Contains("CloudClimate.LightGainAloft(planet, aerosol, lightAltitude, layerSunHeight[i], lightCrossedAir)") && code.Contains("Mathf.Clamp(diagnostics.SkyAmbient, 0f, 2f), !moonLit);")
					? null : "SetCloudLight no longer tells the bands' gain whether its light crossed the air",
				SourceScanPins.Replace("Mathf.Clamp(diagnostics.SkyAmbient, 0f, 2f), !moonLit);", "Mathf.Clamp(diagnostics.SkyAmbient, 0f, 2f), true);"),
				"the moon given the sun's gain aloft");
		}

		[Test]
		public void TheCloudsTakeTheSkysRealShareOfTheAmbient()
		{
			// Clear-sky diffuse light against the direct beam: about a fifth with the sun high, a third at
			// ten degrees, half at five, and more than the beam itself near the horizon.
			float overhead = CloudClimate.ClearSkyDiffuseRatio(90f, 1f);
			float ten = CloudClimate.ClearSkyDiffuseRatio(10f, 1f);
			float five = CloudClimate.ClearSkyDiffuseRatio(5f, 1f);
			float two = CloudClimate.ClearSkyDiffuseRatio(2f, 1f);
			LogAssert.IsTrue(overhead > 0.12f && overhead < 0.25f, $"overhead {overhead:0.000}");
			LogAssert.IsTrue(ten > 0.22f && ten < 0.4f, $"at 10° {ten:0.000}");
			LogAssert.IsTrue(five > 0.4f && five < 0.75f, $"at 5° {five:0.000}");
			LogAssert.IsTrue(two > 1f, $"at 2° {two:0.000}");
			LogAssert.IsTrue(CloudClimate.ClearSkyDiffuseRatio(45f, 3f) > CloudClimate.ClearSkyDiffuseRatio(45f, 1f), "thicker air, more of the sun in the sky");
			// Against the sky model's own ambient (AtmosphereModel, standard air: the sun 1.25 and the
			// ambient 0.62 at 60°, 0.85 and 0.60 at 10°, 0.42 and 0.51 at 2°): about a third of it by day,
			// all of it as the light sets, and all of it with the light gone.
			float noon = CloudClimate.SkyShareOfAmbient(60f, 1.238f, 0.617f, 1f);
			float low = CloudClimate.SkyShareOfAmbient(10f, 0.85f, 0.595f, 1f);
			float setting = CloudClimate.SkyShareOfAmbient(2f, 0.424f, 0.509f, 1f);
			LogAssert.IsTrue(noon > 0.25f && noon < 0.45f, $"a third of the ambient at noon: {noon:0.000}");
			LogAssert.IsTrue(low > noon, $"more with the sun low: {low:0.000}");
			LogAssert.IsTrue(setting > 0.9f, $"nearly all of it with the sun setting: {setting:0.000}");
			LogAssert.IsTrue(CloudClimate.SkyShareOfAmbient(-5f, 0f, 0.1f, 1f) > 0.999f, "all of it once the sun is down");
			string volume = SourceScanPins.ReadCode("Assets/Prefabs/Client/Weather/Shaders/FishCloudVolume.hlsl");
			SourceScanPins.HoldsAndFires("the clouds' sky light", volume,
				code => code != null && code.Contains("float3 sky = _FishCloudAmbient.rgb * (_FishCloudLight.y > 0.0 ? _FishCloudLight.y : 1.0);")
					? null : "the march no longer takes the sky's share of the ambient",
				SourceScanPins.Replace("float3 sky = _FishCloudAmbient.rgb * (_FishCloudLight.y > 0.0 ? _FishCloudLight.y : 1.0);", "float3 sky = _FishCloudAmbient.rgb;"),
				"the whole ambient on every cloud");
		}

		// ── The light march ─────────────────────────────────────────────

		/// <summary>The light march's k-th segment, as FishCloudLightDepth lays it out: where it starts and how long it is (m).</summary>
		private static void LightSegment(int k, out float from, out float span)
		{
			from = 120f * k * (1f + 0.6f * (k - 1));
			span = 120f * (1f + 1.2f * k);
		}

		/// <summary>The optical depth a sample at <paramref name="reach"/> weighted by <paramref name="span"/> finds in a slab of cloud along the light's path.</summary>
		private static float SlabSample(float reach, float span, float slabFrom, float slabTo, float beta)
		{
			return reach >= slabFrom && reach < slabTo ? beta * span : 0f;
		}

		[Test]
		public void TheLightMarchTilesItsPathAndFindsEveryBillowOnIt()
		{
			// The segments run end to end from the point to where the old fixed samples reached, so the
			// light is looked for as far as it was and each segment counts its own length.
			float end = 0f;
			for (int k = 0; k < 6; k++)
			{
				LightSegment(k, out float from, out float span);
				LogAssert.IsTrue(Mathf.Abs(from - end) < 1e-3f, $"segment {k} starts where the last ended ({from} against {end})");
				end = from + span;
				LogAssert.IsTrue(Mathf.Abs(end - 120f * (k + 1) * (1f + 0.6f * k)) < 1e-3f, $"and ends where the old sample {k} stood ({end})");
				if (k == 2)
				{
					LogAssert.IsTrue(Mathf.Abs(end - 792f) < 1e-3f, "792 m on the three steps far off or deep in");
				}
			}
			LogAssert.IsTrue(Mathf.Abs(end - 2880f) < 1e-3f, $"2880 m on six ({end})");

			// A billow of cloud on the light's path, 100 m thick, between the old fixed points: they
			// found none of it — its shadow was drawn wherever the points happened to land in cloud
			// instead, a copy of the cloud moved toward the light — and a sample drawn anywhere in each
			// segment finds all of it on average. So does a whole deck 500 m deep, which the old points
			// counted 312 m of.
			foreach (var (slabFrom, slabTo) in new[] { (150f, 250f), (0f, 500f), (400f, 700f), (1000f, 1100f) })
			{
				float beta = 0.01f;
				float truth = beta * (slabTo - slabFrom);
				float old = 0f;
				for (int k = 0; k < 6; k++)
				{
					float grow = 1f + 0.6f * k;
					old += SlabSample(120f * (k + 1) * grow, 120f * grow, slabFrom, slabTo, beta);
				}
				const int phases = 4096;
				float mean = 0f;
				for (int n = 0; n < phases; n++)
				{
					float u = (n + 0.5f) / phases;
					for (int k = 0; k < 6; k++)
					{
						LightSegment(k, out float from, out float span);
						mean += SlabSample(from + u * span, span, slabFrom, slabTo, beta);
					}
				}
				mean /= phases;
				LogAssert.IsTrue(Mathf.Abs(mean - truth) < 0.01f * truth + 1e-4f, $"cloud {slabFrom}–{slabTo} m out: τ {truth:0.00}, stratified {mean:0.000}, the old points {old:0.00}");
			}

			string volume = SourceScanPins.ReadCode("Assets/Prefabs/Client/Weather/Shaders/FishCloudVolume.hlsl");
			string march = SourceScanPins.Body(volume, "float FishCloudLightDepth(");
			SourceScanPins.HoldsAndFires("the light march's segments", march,
				code => code == null ? "FishCloudLightDepth is gone"
					: !code.Contains("float from = step * k * (1.0 + 0.6 * (k - 1));") || !code.Contains("float span = step * (1.0 + 1.2 * k);") ? "the segments are no longer the ones this test mirrors"
					: !code.Contains("float reach = from + phase.x * span;") ? "the sample is no longer drawn from inside its segment"
					: !code.Contains("high01) * span;") ? "a sample no longer counts its own segment's length"
					: !code.Contains("6.2831853 * phase.y") ? "the cone is no longer turned by the ray's phase"
					: null,
				SourceScanPins.Replace("float reach = from + phase.x * span;", "float reach = from + span;"),
				"every pixel sampling the same fixed point");
			LogAssert.IsTrue(volume.Contains("FishCloudLightDepth(position, toSun, footprint, field, at, transmittance, lightPhase)"), "the march hands the light march the pixel's own phase and how dim the ray is");
		}

		[Test]
		public void AnUnbrokenDeckSeesNoSunlitGroundPastItsEdge()
		{
			string volume = SourceScanPins.ReadCode("Assets/Prefabs/Client/Weather/Shaders/FishCloudVolume.hlsl");
			string light = SourceScanPins.Body(volume, "float3 FishCloudLight(");
			SourceScanPins.HoldsAndFires("the ground under a deck", light,
				code => code != null && code.Contains("float groundLit = lerp(1.0, shade + (1.0 - shade) * spot.groundOpen * (1.0 - cover), cover);")
					? null : "the ground past a cloud's shadow edge is no longer covered as the sky is",
				SourceScanPins.Replace(" * spot.groundOpen * (1.0 - cover)", " * spot.groundOpen"),
				"the ring past the edge in full sun under a full deck");
		}

		[Test]
		public void TheGroundUnderTheCloudsReflectsWhatItIsMadeOf()
		{
			PlanetAir ocean = Earth;
			ocean.Water = 1f;
			PlanetAir desert = Earth;
			desert.Water = 0f;
			PlanetAir frozen = Earth;
			frozen.MeanSurfaceKelvin = 240f;
			Assert.That(CloudClimate.GroundAlbedo(ocean), Is.InRange(0.04f, 0.1f), "the sea is dark");
			LogAssert.IsTrue(CloudClimate.GroundAlbedo(desert) > CloudClimate.GroundAlbedo(ocean), "land brighter");
			LogAssert.IsTrue(CloudClimate.GroundAlbedo(frozen) > 0.4f, "snow and ice brightest of all");
		}
	}
}
