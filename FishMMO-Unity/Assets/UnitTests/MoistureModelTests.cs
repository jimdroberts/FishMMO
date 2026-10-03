using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Wind-driven moisture: humidity as a second climate axis. The same point always reads the
	/// same; ground in the wind's way dries the air behind it; the subtropical highs are drier than
	/// the equatorial trough; a coast the wind blows onto from the sea is wetter than a deep
	/// interior; a world with nothing to evaporate is dry; and the result stays on the scale.
	/// </summary>
	/// <remarks>
	/// Most of these run the walk over ground built by hand (<see cref="StripTerrain"/>) — an ocean
	/// on one side of a meridian, a continent on the other, an optional ridge — so they test the
	/// physics rather than whatever a particular seed happened to put where.
	/// </remarks>
	[TestFixture]
	public class MoistureModelTests
	{
		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (Object o in created)
			{
				Object.DestroyImmediate(o);
			}
			created.Clear();
			PlanetAir.ClearCache();
		}

		/// <summary>
		/// Ground by longitude: sea on one side of <see cref="CoastLongitude"/>, flat land at
		/// <see cref="LandMetres"/> on the other, and a ridge if asked.
		/// </summary>
		private struct StripTerrain : IMoistureTerrain
		{
			public float CoastLongitude;
			/// <summary>True: the sea is east of the coast. False: west.</summary>
			public bool SeaEast;
			/// <summary>True: no land anywhere.</summary>
			public bool AllSea;
			public float LandMetres;
			public float RidgeLongitude;
			public float RidgeHalfWidth;
			public float RidgeMetres;

			public void Sample(Vector3 direction, out float altitudeMetres, out bool ocean)
			{
				float longitude = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
				ocean = AllSea || (SeaEast ? longitude > CoastLongitude : longitude < CoastLongitude);
				altitudeMetres = ocean ? 0f : LandMetres;
				if (!ocean && RidgeMetres > 0f && Mathf.Abs(longitude - RidgeLongitude) <= RidgeHalfWidth)
				{
					altitudeMetres = RidgeMetres;
				}
			}
		}

		/// <summary>A wet Earth-sized world with our own belts and our own vapour scale height.</summary>
		private static MoistureModel EarthModel(bool liquid = true)
		{
			PlanetSurface.PlanetProfile profile = PlanetSurface.Profile(1u, 0.7f);
			return MoistureModel.Of(1u, profile, PlanetSurface.ReliefMetresForRadius(PlanetSurface.EarthRadiusKm),
				PlanetSurface.EarthRadiusKm, WindBelts.Earthlike, true, liquid, 0.7f, 2400f);
		}

		private static Vector3 At(double latitude, double longitude) => PlanetSurface.Direction(latitude, longitude);

		private WorldBody MakeBody(AtmosphereKind atmosphere, float water)
		{
			var body = ScriptableObject.CreateInstance<WorldBody>();
			body.Atmosphere = atmosphere;
			body.Water = water;
			body.SkyRadiusKm = PlanetSurface.EarthRadiusKm;
			body.RotationHours = 24f;
			body.TerrainSeed = 12345u;
			created.Add(body);
			return body;
		}

		// ── Determinism ──────────────────────────────────────────────

		[Test]
		public void TheSamePointAlwaysReadsTheSame()
		{
			PlanetClimateField a = PlanetClimateField.For(null, null);
			PlanetClimateField b = PlanetClimateField.For(null, null);
			for (int i = 0; i < 400; i++)
			{
				Vector3 d = PlanetSurface.FibonacciDirection(i, 400);
				PlanetSurface.LatLong(d, out double latitude, out _);
				float first = a.HumidityAt(latitude, d, 0.3f, 0.5f);
				Assert.AreEqual(first, a.HumidityAt(latitude, d, 0.3f, 0.5f), "asked twice");
				Assert.AreEqual(first, b.HumidityAt(latitude, d, 0.3f, 0.5f), "asked of a second field built the same way");
			}
		}

		// ── The wind ─────────────────────────────────────────────────

		[Test]
		public void TheClimateReadsTheWeathersWind()
		{
			// The moisture walk takes its upwind direction from the same function the weather drifts on.
			Assert.Less(WeatherDriver.ZonalWind(15f, WindBelts.Earthlike), 0f, "trades blow toward the west");
			Assert.Greater(WeatherDriver.ZonalWind(45f, WindBelts.Earthlike), 0f, "westerlies blow toward the east");
			Assert.Less(WeatherDriver.ZonalWind(75f, WindBelts.Earthlike), 0f, "polar easterlies");
			for (float latitude = -89f; latitude <= 89f; latitude += 7f)
			{
				Vector2 wind = WeatherDriver.PrevailingWind(latitude, WindBelts.Earthlike);
				float zonal = WeatherDriver.ZonalWind(latitude, WindBelts.Earthlike, out float poleward);
				Vector2 expected = new Vector2(zonal, Mathf.Sign(latitude) * poleward * 0.25f).normalized;
				Assert.AreEqual(expected.x, wind.x, 1e-6f, $"prevailing wind east at {latitude}");
				Assert.AreEqual(expected.y, wind.y, 1e-6f, $"prevailing wind north at {latitude}");
			}
		}

		// ── Rain shadow ──────────────────────────────────────────────

		[Test]
		public void AMountainUpwindDriesTheGroundBehindIt()
		{
			MoistureModel model = EarthModel();
			// 45°N: westerlies, so the air comes from the west. Sea west of 0°, lowland east of it.
			var open = new StripTerrain { CoastLongitude = 0f, SeaEast = false, LandMetres = 200f };
			var ridged = open;
			ridged.RidgeLongitude = 6f;
			ridged.RidgeHalfWidth = 1.5f;
			ridged.RidgeMetres = 3000f;

			// 12° east of the coast at 45° is about 940 km inland, the ridge 470 km upwind of it.
			Vector3 behind = At(45.0, 12.0);
			float without = model.Supply(45f, behind, open, out _);
			float with = model.Supply(45f, behind, ridged, out _);
			Assert.Less(with, without * 0.6f, $"a 3 km ridge upwind must wring the air out: {with:0.000} behind it against {without:0.000} without");
			Assert.Less(model.Anomaly(45.0, behind, ridged), model.Anomaly(45.0, behind, open), "and the humidity anomaly follows");

			// The windward face, on the other hand, is where that water fell.
			Vector3 face = At(45.0, 4.6);
			Vector3 plainBefore = At(45.0, 3.0);
			ridged.RidgeLongitude = 5.5f;
			ridged.RidgeHalfWidth = 1f;
			Assert.Greater(model.Precipitation(45.0, face, ridged), model.Precipitation(45.0, plainBefore, ridged),
				"the windward slope is wetter than the plain in front of it");
		}

		// ── Circulation ──────────────────────────────────────────────

		[Test]
		public void TheSubtropicsAreDrierThanTheEquator_AtEqualTemperatureAndHeight()
		{
			// On an all-ocean world the fetch is saturated everywhere, so only the circulation differs.
			MoistureModel model = EarthModel();
			var sea = new StripTerrain { AllSea = true };
			float equator = model.Anomaly(0.0, At(0.0, 0.0), sea);
			Assert.Greater(equator, model.Anomaly(30.0, At(30.0, 0.0), sea), "30°N drier than the equator");
			Assert.Greater(equator, model.Anomaly(-30.0, At(-30.0, 0.0), sea), "30°S drier than the equator");
			Assert.Greater(MoistureModel.Circulation(0f, WindBelts.Earthlike), 0.9f, "the trough lifts");
			Assert.Less(MoistureModel.Circulation(30f, WindBelts.Earthlike), -0.5f, "the high sinks");
			Assert.Less(MoistureModel.Circulation(90f, WindBelts.Earthlike), -0.5f, "the pole sinks");

			// And on a real world, averaged round the latitude circle, with temperature and height held equal.
			PlanetClimateField field = PlanetClimateField.For(null, null);
			float atEquator = 0f, atNorth = 0f, atSouth = 0f;
			for (int i = 0; i < 360; i++)
			{
				atEquator += field.HumidityAt(0.0, At(0.0, i), 0.3f, 0.5f);
				atNorth += field.HumidityAt(30.0, At(30.0, i), 0.3f, 0.5f);
				atSouth += field.HumidityAt(-30.0, At(-30.0, i), 0.3f, 0.5f);
			}
			Assert.Greater(atEquator, atNorth, "the real field: 30°N drier than the equator");
			Assert.Greater(atEquator, atSouth, "the real field: 30°S drier than the equator");
		}

		[Test]
		public void TheFieldIsContinuousAcrossTheBeltEdges()
		{
			// The upwind direction flips at 30° and 60°; the walk is weighted by the zonal wind, which is zero there.
			PlanetClimateField field = PlanetClimateField.For(null, null);
			foreach (double edge in new[] { 30.0, 60.0, -30.0, -60.0 })
			{
				for (int lon = -180; lon < 180; lon += 15)
				{
					float a = field.Moisture.Anomaly(edge - 0.02, At(edge - 0.02, lon));
					float b = field.Moisture.Anomaly(edge + 0.02, At(edge + 0.02, lon));
					Assert.AreEqual(a, b, 0.06f, $"no step across the belt edge at {edge}°, longitude {lon}");
				}
			}
		}

		// ── Fetch ────────────────────────────────────────────────────

		[Test]
		public void AnOceanFetchCoastIsWetterThanADeepInterior()
		{
			MoistureModel model = EarthModel();
			// Westerlies at 45°N: sea to the west.
			var west = new StripTerrain { CoastLongitude = 0f, SeaEast = false, LandMetres = 200f };
			float coast = model.Anomaly(45.0, At(45.0, 1.0), west);
			float interior = model.Anomaly(45.0, At(45.0, 60.0), west);
			Assert.Greater(coast, interior + 0.2f, $"westerly coast {coast:0.00} against interior {interior:0.00}");

			// Trades at 15°N: the air comes from the east, so the wet coast is the east coast.
			var east = new StripTerrain { CoastLongitude = 0f, SeaEast = true, LandMetres = 200f };
			float tradeCoast = model.Anomaly(15.0, At(15.0, -1.0), east);
			float tradeInterior = model.Anomaly(15.0, At(15.0, -40.0), east);
			Assert.Greater(tradeCoast, tradeInterior + 0.2f, $"trade-wind coast {tradeCoast:0.00} against interior {tradeInterior:0.00}");
		}

		// ── Dry worlds ───────────────────────────────────────────────

		[Test]
		public void AWorldWithNothingToEvaporateIsDry()
		{
			MoistureModel frozen = EarthModel(liquid: false);
			var sea = new StripTerrain { AllSea = true };
			Assert.AreEqual(0f, frozen.Precipitation(0.0, At(0.0, 0.0), sea), "no surface liquid, no rain");
			Assert.Less(frozen.Anomaly(0.0, At(0.0, 0.0), sea), -1f, "and the full dry anomaly, past the bottom of the scale");

			PlanetClimateField dry = PlanetClimateField.For(null, MakeBody(AtmosphereKind.Standard, 0f));
			PlanetClimateField airless = PlanetClimateField.For(null, MakeBody(AtmosphereKind.None, 0.7f));
			Assert.IsFalse(dry.Moisture.SurfaceLiquid, "a world with no water has none to evaporate");
			for (int i = 0; i < 300; i++)
			{
				Vector3 d = PlanetSurface.FibonacciDirection(i, 300);
				PlanetSurface.LatLong(d, out double latitude, out _);
				Assert.LessOrEqual(dry.HumidityAt(latitude, d, 0.2f, 0.45f), -0.95f, "a world with no water is dry everywhere");
				Assert.AreEqual(-1f, airless.HumidityAt(latitude, d, 0.2f, 0.45f), 1e-6f, "an airless world is as dry as the scale goes");
			}
		}

		// ── Range ────────────────────────────────────────────────────

		[Test]
		public void HumidityStaysOnTheScale()
		{
			var fields = new[]
			{
				PlanetClimateField.For(null, null),
				PlanetClimateField.For(null, MakeBody(AtmosphereKind.Standard, 0f)),
				PlanetClimateField.For(null, MakeBody(AtmosphereKind.Thick, 1f)),
				PlanetClimateField.For(null, MakeBody(AtmosphereKind.None, 0.7f)),
			};
			float[] temperatures = { -1f, -0.3f, 0f, 0.5f, 1f };
			float[] heights = { 0f, 0.42f, 0.6f, 1f };
			foreach (PlanetClimateField field in fields)
			{
				for (int i = 0; i < 600; i++)
				{
					Vector3 d = PlanetSurface.FibonacciDirection(i, 600);
					PlanetSurface.LatLong(d, out double latitude, out _);
					float anomaly = field.Moisture.Anomaly(latitude, d);
					Assert.IsFalse(float.IsNaN(anomaly) || float.IsInfinity(anomaly), "the anomaly is finite");
					foreach (float t in temperatures)
					{
						foreach (float h in heights)
						{
							float humidity = field.HumidityAt(latitude, d, t, h);
							Assert.That(humidity, Is.InRange(-1f, 1f), $"humidity {humidity} at T {t}, h {h}");
						}
					}
				}
			}
		}

		// ── The coarse ground ────────────────────────────────────────

		[Test]
		public void TheCoarseGroundKeepsTheCoastlines()
		{
			// Measured 2026-10-02: 2.2% of seed 1's globe changes between land and sea, against 6.9% without the residual.
			PlanetSurface.PlanetProfile profile = PlanetSurface.Profile(1u, 0.7f);
			int disagree = 0;
			const int Count = 20000;
			for (int i = 0; i < Count; i++)
			{
				Vector3 d = PlanetSurface.FibonacciDirection(i, Count);
				bool fullSea = PlanetSurface.Height(1u, d) <= profile.SeaLevel;
				bool coarseSea = PlanetSurface.CoarseHeight(1u, d) <= profile.SeaLevel;
				if (fullSea != coarseSea)
				{
					disagree++;
				}
			}
			Assert.Less(disagree / (float)Count, 0.04f, $"{100f * disagree / Count:0.0}% of the globe changed between land and sea");
		}

		// ── A running scene ──────────────────────────────────────────

		[Test]
		public void AGeneratedScene_ReadsTheGlobesMoistureAndTrueLatitude()
		{
			var footprint = new AtlasFootprint(10.0, 20.0, 30f, new Vector2(40f, 30f));
			const double Radius = 500.0;
			ScenePlacementClimate placement = ScenePlacementClimate.For(null, null, footprint, Radius, true);
			PlanetClimateField field = PlanetClimateField.For(null, null);
			Assert.IsTrue(placement.Generated);

			for (int j = 0; j < ScenePlacementClimate.GridSize; j += 4)
			{
				for (int i = 0; i < ScenePlacementClimate.GridSize; i += 4)
				{
					placement.NodeKm(i, j, out double xKm, out double zKm);
					Vector3 unit = AtlasGeometry.SceneToUnit(footprint, xKm, zKm, Radius).ToVector3().normalized;
					double latitude = PlanetClimateField.LatitudeOf(unit);
					float moisture = placement.MoistureAt((float)(xKm * 1000.0), (float)(zKm * 1000.0));
					Assert.AreEqual(field.Moisture.Anomaly(latitude, unit), moisture, 1e-4f, $"node {i},{j}: the globe's moisture");
					Assert.AreEqual(latitude, placement.LatitudeAt((float)(xKm * 1000.0), (float)(zKm * 1000.0)), 1e-3, $"node {i},{j}: the true latitude");
				}
			}

			// A hand-made scene that is only placed takes the moisture at its centre and keeps its authored latitude.
			ScenePlacementClimate placed = ScenePlacementClimate.For(null, null, footprint, 0.0, false);
			Assert.IsFalse(placed.Generated);
			Assert.AreEqual(1f, placed.VerticalScale, "a hand-made scene's Y was never scaled");
			Assert.AreEqual(field.Moisture.Anomaly(10.0, AtlasGeometry.ToUnit(10.0, 20.0).ToVector3()), placed.CentreMoisture, 1e-5f);
			Assert.AreEqual(placed.CentreMoisture, placed.MoistureAt(12345f, -6789f));
			Assert.AreEqual(10.0, placed.LatitudeAt(12345f, -6789f));
		}

		// ── The measurement ──────────────────────────────────────────

		/// <summary>
		/// Runs the moisture probe at a reduced sample count and logs what it found. The numbers are
		/// the point; the assertion is only that it ran. For the full report run
		/// <c>FishMMO.Shared.WorldDesign.MoistureProbe.Run</c>.
		/// </summary>
		[Test]
		public void Probe_ReportsTheDistribution()
		{
			string report = MoistureProbe.Run(20000);
			StringAssert.Contains("=== Earth-like reference", report);
			StringAssert.Contains("land H after", report);
		}
	}
}
