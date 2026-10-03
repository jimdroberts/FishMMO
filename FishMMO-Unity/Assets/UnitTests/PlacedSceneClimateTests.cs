using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// One climate model for a scene cut from the globe: at runtime it evaluates exactly the body's
	/// <see cref="PlanetClimateField"/> at each position's true direction and altitude — what the
	/// generator painted its biomes from — while a scene with no place on a body keeps its authored
	/// climate unchanged.
	/// </summary>
	[TestFixture]
	public class PlacedSceneClimateTests
	{
		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
			PlanetAir.ClearCache();
		}

		/// <summary>A one-star system whose home world is Earth-like, tilted, on a slightly eccentric orbit.</summary>
		private SolarSystemProfile MakeSystem(out WorldBody home)
		{
			var star = ScriptableObject.CreateInstance<StarBody>();
			star.Luminosity = 1f;
			star.SkyRadiusKm = 696000f;
			created.Add(star);

			home = ScriptableObject.CreateInstance<WorldBody>();
			home.Parent = star;
			home.Orbit = new OrbitSettings { Distance = 1f, Eccentricity = 0.0167f, PeriodDays = 365f };
			home.Atmosphere = AtmosphereKind.Standard;
			home.Water = 0.7f;
			home.SkyRadiusKm = PlanetSurface.EarthRadiusKm;
			home.RotationHours = 24f;
			home.AxialTiltDegrees = 23.4f;
			home.TerrainSeed = 4242u;
			created.Add(home);

			var system = ScriptableObject.CreateInstance<SolarSystemProfile>();
			system.Bodies = new List<CelestialBody> { star, home };
			system.HomeWorld = home;
			created.Add(system);
			return system;
		}

		[Test]
		public void AGeneratedScene_SamplesExactlyThePlanetFieldAtItsDirectionAndAltitude()
		{
			SolarSystemProfile system = MakeSystem(out WorldBody home);
			var footprint = new AtlasFootprint(25.0, 40.0, 30f, new Vector2(6f, 4f));
			const double Radius = 30.0;
			ScenePlacementClimate placement = ScenePlacementClimate.For(system, home, footprint, Radius, true);
			PlanetClimateField field = PlanetClimateField.For(system, home);

			Assert.IsTrue(placement.Generated);
			Assert.AreEqual(PlanetSurface.SceneVerticalScale(home, Radius), placement.VerticalScale, "the generator's own vertical scale");
			Assert.AreEqual(field.Conditions.Atmosphere, placement.Conditions.Atmosphere);
			Assert.AreEqual(field.Conditions.Water, placement.Conditions.Water);

			// Below the sea, at it, on a coastal plain and up a mountain, in scene metres.
			float[] heights = { -250f, 0f, 40f, 900f };
			int checkedPoints = 0;
			for (int j = 0; j < ScenePlacementClimate.GridSize; j += 8)
			{
				for (int i = 0; i < ScenePlacementClimate.GridSize; i += 8)
				{
					placement.NodeKm(i, j, out double xKm, out double zKm);
					float x = (float)(xKm * 1000.0), z = (float)(zKm * 1000.0);
					foreach (float y in heights)
					{
						// Exactly the call SceneBiomeField makes for a cell: SceneToUnit of the scene
						// position, and the scene's Y over its vertical scale.
						Vector3 direction = AtlasGeometry.SceneToUnit(footprint, x / 1000.0, z / 1000.0, Radius).ToVector3();
						PlanetSurfacePoint expected = field.At(direction, y / placement.VerticalScale);

						ClimateSample actual = placement.SampleAt(new Vector3(x, y, z), out float normalized);
						string at = $"node {i},{j} at y {y}";
						Assert.AreEqual(expected.Climate.Temperature, actual.Temperature, at + ": temperature");
						Assert.AreEqual(expected.Climate.ElevationTier, actual.ElevationTier, at + ": tier");
						Assert.AreEqual(expected.NormalizedHeight, normalized, at + ": the height the tier was chosen from");
						// The moisture grid's node is the same direction to float rounding.
						Assert.AreEqual(expected.Climate.Humidity, actual.Humidity, 1e-4f, at + ": humidity");
						checkedPoints++;
					}
				}
			}
			Assert.Greater(checkedPoints, 0);
		}

		[Test]
		public void AGeneratedScene_BetweenTheGridNodes_OnlyTheMoistureIsInterpolated()
		{
			SolarSystemProfile system = MakeSystem(out WorldBody home);
			var footprint = new AtlasFootprint(-12.0, 100.0, 75f, new Vector2(8f, 8f));
			const double Radius = 30.0;
			ScenePlacementClimate placement = ScenePlacementClimate.For(system, home, footprint, Radius, true);
			PlanetClimateField field = PlanetClimateField.For(system, home);

			var rng = new System.Random(238);
			const int Points = 400;
			double humidityError = 0.0;
			for (int k = 0; k < Points; k++)
			{
				float x = (float)((rng.NextDouble() - 0.5) * 8000.0);
				float z = (float)((rng.NextDouble() - 0.5) * 8000.0);
				float y = (float)(rng.NextDouble() * 1500.0 - 200.0);
				Vector3 direction = AtlasGeometry.SceneToUnit(footprint, x / 1000.0, z / 1000.0, Radius).ToVector3();
				PlanetSurfacePoint expected = field.At(direction, y / placement.VerticalScale);
				ClimateSample actual = placement.SampleAt(new Vector3(x, y, z), out float normalized);

				Assert.AreEqual(expected.Climate.Temperature, actual.Temperature, $"point {k}: temperature is never interpolated");
				Assert.AreEqual(expected.Climate.ElevationTier, actual.ElevationTier, $"point {k}: tier");
				Assert.AreEqual(expected.NormalizedHeight, normalized, $"point {k}: height");
				humidityError += Mathf.Abs(expected.Climate.Humidity - actual.Humidity);
			}
			/* Only the wind's share of the humidity comes off the 32-cell grid. Measured offline on an
			 * Earth-like body and on the home world: a mean error of 0.0002–0.009, with single points up
			 * to about 0.3 where a coastline or a rain shadow's edge in the moisture field crosses a
			 * cell — the field is sharp there and bilinear is not. The mean is what weather reads. */
			Assert.Less(humidityError / Points, 0.02, "mean humidity error from the interpolated moisture");
		}

		[Test]
		public void AHandMadeScene_OnlyPlaced_KeepsItsAuthoredClimate()
		{
			SolarSystemProfile system = MakeSystem(out WorldBody home);
			var footprint = new AtlasFootprint(25.0, 40.0, 0f, new Vector2(2f, 2f));
			ScenePlacementClimate placed = ScenePlacementClimate.For(system, home, footprint, 0.0, false);
			Assert.IsFalse(placed.Generated, "no cut radius: not cut from the globe");
			Assert.AreEqual(placed.CentreMoisture, placed.MoistureAt(500f, -700f), "a hand-made scene takes the moisture at its centre");
			Assert.AreEqual(25.0, placed.LatitudeAt(500f, -700f), "and keeps its own latitude");
		}

		[Test]
		public void AnUnplacedScene_ReadsTheAuthoredClimateExactlyAsBefore()
		{
			var go = new GameObject("Unplaced scene settings");
			created.Add(go);
			var settings = go.AddComponent<WorldSceneSettings>();
			Assume.That(settings.AtlasEntry, Is.Null, "the test scene must have no atlas entry");
			Assert.IsNull(settings.PlacementClimate, "no atlas entry, no placement");

			foreach (float height in new[] { 0.1f, 0.42f, 0.6f, 0.95f })
			{
				ClimateSample authored = settings.SampleClimate(height, 0.5f);
				ClimateSample sampled = settings.SampleClimateAt(new Vector3(120f, 999f, -40f), height, out float resolverHeight);
				Assert.AreEqual(authored.Temperature, sampled.Temperature, $"height {height}: temperature");
				Assert.AreEqual(authored.Humidity, sampled.Humidity, $"height {height}: humidity");
				Assert.AreEqual(authored.ElevationTier, sampled.ElevationTier, $"height {height}: tier");
				Assert.AreEqual(height, resolverHeight, "an unplaced scene's height is its own");
			}

			// The weather's and the tests' hooks still apply on top.
			ClimateSample before = settings.SampleClimateAt(Vector3.zero, 0.5f);
			settings.RuntimeTemperatureOffset = 0.1f;
			settings.RuntimeHumidityOffset = -0.2f;
			ClimateSample after = settings.SampleClimateAt(Vector3.zero, 0.5f);
			Assert.AreEqual(Mathf.Clamp(before.Temperature + 0.1f, -1f, 1f), after.Temperature, 1e-6f);
			Assert.AreEqual(Mathf.Clamp(before.Humidity - 0.2f, -1f, 1f), after.Humidity, 1e-6f);
			Assert.AreEqual(BiomeWorldConditions.Earthlike.Atmosphere, settings.WorldConditions.Atmosphere, "no body: the home world's conditions");
		}
	}
}
