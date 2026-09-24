using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Celestial;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.Celestial
{
	/// <summary>
	/// The terrain of a world, generated from its seed alone.
	/// </summary>
	/// <remarks>
	/// The function is the source of truth for every coastline, altitude and mountain in the game,
	/// and textures are only bakes of it — so what is pinned here is that it answers the same way
	/// everywhere, that it has no seam or pole to fall down, and that the numbers it produces are
	/// the size of a real planet's.
	/// </remarks>
	[TestFixture]
	public class PlanetSurfaceTests
	{
		private const uint Seed = 20260923u;

		private WorldBody body;

		[SetUp]
		public void SetUp()
		{
			PlanetSurface.ClearCache();
			body = ScriptableObject.CreateInstance<WorldBody>();
			body.name = "Test World";
			body.SkyRadiusKm = PlanetSurface.EarthRadiusKm;
			body.Water = 0.7f;
			body.Atmosphere = AtmosphereKind.Standard;
		}

		[TearDown]
		public void TearDown()
		{
			Object.DestroyImmediate(body);
			PlanetSurface.ClearCache();
		}

		[Test]
		public void TheSameSeedAndPlaceAlwaysGiveTheSameGround()
		{
			/* The whole architecture rests on this. The server decides a scene's altitude from it,
			 * a client bakes a picture from it, and the scene generator carves terrain from it — if
			 * they could disagree, the coastline you see from orbit would not be the one you stand
			 * on. */
			for (int i = 0; i < 64; i++)
			{
				Vector3 direction = PlanetSurface.FibonacciDirection(i, 64);
				Assert.That(PlanetSurface.Height(Seed, direction),
					Is.EqualTo(PlanetSurface.Height(Seed, direction)).Within(0f),
					"the same direction must give bit-identical ground");
			}
		}

		[Test]
		public void ADifferentSeedIsADifferentWorld()
		{
			int same = 0;
			for (int i = 0; i < 256; i++)
			{
				Vector3 direction = PlanetSurface.FibonacciDirection(i, 256);
				if (Mathf.Abs(PlanetSurface.Height(Seed, direction) - PlanetSurface.Height(Seed + 1u, direction)) < 0.001f)
				{
					same++;
				}
			}
			Assert.That(same, Is.LessThan(32), "two seeds must not produce nearly the same planet");
		}

		[Test]
		public void ThereIsNoSeamAtTheAntimeridianAndNoSpikeAtThePoles()
		{
			/* The reason the noise is sampled in three dimensions on a direction rather than on a
			 * lat/long grid. A grid wrapped round a ball has a seam where 180 meets -180 and a
			 * singularity at each pole, and terrain generated on one shows both: a cliff down one
			 * meridian and a spike at each end of the axis. A direction has neither. */
			for (double latitude = -80; latitude <= 80; latitude += 20)
			{
				float west = PlanetSurface.HeightAt(Seed, latitude, -179.999);
				float east = PlanetSurface.HeightAt(Seed, latitude, 180.0);
				Assert.That(west, Is.EqualTo(east).Within(0.002f),
					$"the antimeridian must not be a cliff at latitude {latitude}");
			}

			// Every longitude at a pole is the same point, so every reading there must agree.
			float first = PlanetSurface.HeightAt(Seed, 90.0, 0.0);
			for (double longitude = -180; longitude < 180; longitude += 45)
			{
				Assert.That(PlanetSurface.HeightAt(Seed, 90.0, longitude), Is.EqualTo(first).Within(1e-4f),
					"the north pole is one point and must have one height");
			}
		}

		[Test]
		public void TheGroundIsContinuous()
		{
			// A step between neighbouring points would be a wall across the world.
			float previous = PlanetSurface.HeightAt(Seed, 12.0, 0.0);
			for (double longitude = 0.05; longitude <= 5.0; longitude += 0.05)
			{
				float here = PlanetSurface.HeightAt(Seed, 12.0, longitude);
				Assert.That(Mathf.Abs(here - previous), Is.LessThan(0.02f),
					$"the ground stepped between longitudes near {longitude}");
				previous = here;
			}
		}

		[Test]
		public void SeaLevelPutsExactlyTheAuthoredFractionUnderWater()
		{
			/* The reason sea level is derived rather than authored: WorldBody.Water already says
			 * how much of a world is ocean, so the height that produces it is simply that quantile.
			 * Move the ocean fraction and every coastline moves with it, with no second number to
			 * keep in step. */
			foreach (float water in new[] { 0.2f, 0.5f, 0.7f, 0.95f })
			{
				float sea = PlanetSurface.SeaLevel(Seed, water);
				int under = 0;
				const int Samples = 4096;
				for (int i = 0; i < Samples; i++)
				{
					if (PlanetSurface.Height(Seed, PlanetSurface.FibonacciDirection(i, Samples)) <= sea)
					{
						under++;
					}
				}
				Assert.That(under / (float)Samples, Is.EqualTo(water).Within(0.02f),
					$"an ocean fraction of {water} must actually cover that much of the world");
			}
		}

		[Test]
		public void AnEarthlikeWorldHasEarthlikeMountainsAndTrenches()
		{
			/* Checked against the real planet rather than against itself. Octaves of noise pile up
			 * around their midpoint instead of filling 0..1 — a world uses about 40% of the range —
			 * so reading the raw value as a fraction of the relief budget put the highest summit at
			 * 2.8 km and no scene would ever have been in real mountains. */
			float highest = float.MinValue;
			float lowest = float.MaxValue;
			const int Samples = 4096;
			for (int i = 0; i < Samples; i++)
			{
				float altitude = PlanetSurface.AltitudeMetres(Seed, body, PlanetSurface.FibonacciDirection(i, Samples));
				highest = Mathf.Max(highest, altitude);
				lowest = Mathf.Min(lowest, altitude);
			}

			Assert.That(highest, Is.InRange(4000f, 12000f), $"Earth's highest is 8848 m; this world's is {highest:0}");
			Assert.That(lowest, Is.InRange(-16000f, -6000f), $"Earth's deepest is -10994 m; this world's is {lowest:0}");
		}

		[Test]
		public void AnEarthlikeWorldHasAnEarthlikeOceanFloor()
		{
			/* Checked against Earth's measured ocean (NOAA's ETOPO1 curve), not against itself. A
			 * straight line from the water line to the deepest trench put 2.2% of a world's ocean
			 * on shelves where Earth has 7%, and a third of it deeper than seven kilometres where
			 * Earth has a fraction of a percent — which is how a scene cut 141 km offshore came out
			 * a kilometre deep and named for the shallows. Measured here: 6.5%, 74%, 0.3%, 3681 m. */
			const int Samples = 20000;
			int ocean = 0, shelf = 0, abyssal = 0, hadal = 0;
			double total = 0.0;
			for (int i = 0; i < Samples; i++)
			{
				float altitude = PlanetSurface.AltitudeMetres(Seed, body, PlanetSurface.FibonacciDirection(i, Samples));
				if (altitude >= 0f)
				{
					continue;
				}
				float depth = -altitude;
				ocean++;
				total += depth;
				if (depth < 200f)
				{
					shelf++;
				}
				else if (depth >= 3000f && depth < 6000f)
				{
					abyssal++;
				}
				else if (depth >= 7000f)
				{
					hadal++;
				}
			}

			Assert.That(shelf / (float)ocean, Is.EqualTo(0.071f).Within(0.015f), "Earth's continental shelves are about 7% of its ocean");
			Assert.That(abyssal / (float)ocean, Is.EqualTo(0.74f).Within(0.04f), "about three quarters of Earth's ocean is abyssal plain, 3 to 6 km down");
			Assert.That(hadal / (float)ocean, Is.LessThan(0.01f), "trenches deeper than 7 km are a sliver of Earth's ocean");
			Assert.That((float)(total / ocean), Is.EqualTo(3680f).Within(200f), "Earth's mean ocean depth is 3686 m");
		}

		[Test]
		public void TheSeaFloorMeetsTheShoreWithoutAStep()
		{
			/* Land and sea floor are two curves joined at the water line. A join with a step in it
			 * is a cliff along every coastline on the planet. */
			PlanetSurface.PlanetProfile profile = PlanetSurface.ProfileOf(Seed, body);
			float relief = PlanetSurface.ReliefMetres(body);
			float below = PlanetSurface.AltitudeFromHeight(profile.SeaLevel - 1e-5f, profile, relief);
			float above = PlanetSurface.AltitudeFromHeight(profile.SeaLevel + 1e-5f, profile, relief);

			Assert.That(below, Is.LessThanOrEqualTo(0f), "just under the water line is under water");
			Assert.That(above - below, Is.LessThan(1f), $"the shore steps {above - below:0.00} m across a hundred-thousandth of the field");
		}

		[Test]
		public void FlatSeaFloorTakesTheShallowestDepthItReaches()
		{
			/* The field clamps at 0, and on some worlds a percent of the ocean lies on the clamp, so
			 * the deepest knots share one height. Taking the deepest of them laid that percent flat
			 * at trench depth; the shallowest makes it an abyssal plain. */
			var floor = new float[PlanetSurface.OceanAreaShallower.Length];
			for (int i = 0; i < floor.Length; i++)
			{
				floor[i] = Mathf.Max(0f, 0.5f - i * 0.05f);   // on the clamp from knot 10 down
			}

			Assert.That(PlanetSurface.OceanDepthMetres(0f, floor), Is.EqualTo(PlanetSurface.OceanKnotDepthMetres[10]),
				"ground on the clamp is as deep as the first knot to reach it");
			Assert.That(PlanetSurface.OceanDepthMetres(0.5f - 3.5f * 0.05f, floor), Is.EqualTo(350f).Within(0.01f),
				"between knots the depth is interpolated");
			Assert.That(PlanetSurface.OceanDepthMetres(0.6f, floor), Is.EqualTo(0f), "above the water line there is no depth");
		}

		[Test]
		public void ReliefGrowsWithSizeButShrinksAgainstTheBodysOwnRadius()
		{
			/* Bodies here run from about 5 km to 1000 km of radius, so this has to hold over three
			 * orders of magnitude, and the two halves pull opposite ways: absolute relief grows
			 * with size, relative relief falls steeply. A 5 km rock is a lumpy potato whose hills
			 * are a third of its radius but only 1.5 km tall; Earth has 20 km that is a third of
			 * one percent of it.
			 *
			 * Two earlier models were wrong in opposite directions. 1/radius, from the strength
			 * limit, gave the Moon 73 km of mountains. Capping that at a flat 30 km then gave a
			 * 5 km body 30 km of relief — six times its own radius. */
			var sizes = new[] { 5f, 50f, 263f, 1000f, 1737f, 6371f };
			float previousRelief = -1f;
			float previousFraction = float.MaxValue;

			foreach (float radiusKm in sizes)
			{
				var world = ScriptableObject.CreateInstance<WorldBody>();
				try
				{
					world.SkyRadiusKm = radiusKm;
					float relief = PlanetSurface.ReliefMetres(world);
					float fraction = relief / (radiusKm * 1000f);

					Assert.That(relief, Is.GreaterThan(previousRelief),
						$"a bigger body must have more relief, not less (R={radiusKm} km)");
					Assert.That(fraction, Is.LessThan(previousFraction),
						$"but less of it relative to itself (R={radiusKm} km)");
					Assert.That(fraction, Is.LessThanOrEqualTo(PlanetSurface.MaximumReliefFraction + 1e-4f),
						$"past a third of its radius a body stops being a globe (R={radiusKm} km)");

					previousRelief = relief;
					previousFraction = fraction;
				}
				finally
				{
					Object.DestroyImmediate(world);
				}
			}
		}

		[Test]
		public void ReliefMatchesRealBodiesAcrossThreeOrdersOfMagnitude()
		{
			/* Checked against the solar system rather than against itself, which is the only way a
			 * derived model can be wrong in a way anybody notices. */
			var cases = new[]
			{
				new { Name = "Phobos", RadiusKm = 11f, RealKm = 2.0f, ToleranceKm = 1.5f },
				new { Name = "Moon", RadiusKm = 1737f, RealKm = 20f, ToleranceKm = 10f },
				new { Name = "Earth", RadiusKm = PlanetSurface.EarthRadiusKm, RealKm = 20f, ToleranceKm = 1f },
			};

			foreach (var c in cases)
			{
				var world = ScriptableObject.CreateInstance<WorldBody>();
				try
				{
					world.SkyRadiusKm = c.RadiusKm;
					float km = PlanetSurface.ReliefMetres(world) / 1000f;
					Assert.That(km, Is.EqualTo(c.RealKm).Within(c.ToleranceKm),
						$"{c.Name}: derived {km:0.00} km against a measured {c.RealKm} km");
				}
				finally
				{
					Object.DestroyImmediate(world);
				}
			}
		}

		[Test]
		public void ATinyBodyIsNeverGivenMoreReliefThanItHasRadius()
		{
			/* The bug this replaced: a 5 km body was handed 30 km of relief. A scene on it would
			 * have stood in a mountain six times taller than the world it was on. */
			var rock = ScriptableObject.CreateInstance<WorldBody>();
			try
			{
				rock.SkyRadiusKm = 5f;
				float relief = PlanetSurface.ReliefMetres(rock);
				Assert.That(relief, Is.LessThan(5000f * PlanetSurface.MaximumReliefFraction + 1f),
					$"a 5 km body was given {relief:0} m of relief");
				Assert.That(relief, Is.GreaterThan(200f), "but it is still a rock, not a billiard ball");
			}
			finally
			{
				Object.DestroyImmediate(rock);
			}
		}

		[Test]
		public void ADryWorldMeasuresItsAltitudesFromItsMiddle()
		{
			/* With no ocean there is no sea level to find, but altitudes still need a datum. Taking
			 * the lowest point put every square metre of a dry world above its own datum, so a
			 * Mars-like body read as 38 km of unbroken highland. Mars quotes elevations against its
			 * mean radius for exactly this reason. */
			body.Water = 0f;
			int above = 0;
			const int Samples = 2048;
			for (int i = 0; i < Samples; i++)
			{
				if (PlanetSurface.AltitudeMetres(Seed, body, PlanetSurface.FibonacciDirection(i, Samples)) > 0f)
				{
					above++;
				}
			}
			Assert.That(above / (float)Samples, Is.EqualTo(0.5f).Within(0.05f),
				"a dry world's datum is its median ground, so about half of it is above sea level");
		}

		[Test]
		public void ABodyWithNoSeedStillHasAWorldOfItsOwn()
		{
			// Zero would otherwise mean every unseeded body in the project shared one planet.
			var other = ScriptableObject.CreateInstance<WorldBody>();
			try
			{
				other.name = "Another World";
				Assert.That(body.ResolvedTerrainSeed, Is.Not.Zero);
				Assert.That(other.ResolvedTerrainSeed, Is.Not.EqualTo(body.ResolvedTerrainSeed),
					"two unnamed-seed bodies must not be the same planet");
			}
			finally
			{
				Object.DestroyImmediate(other);
			}
		}
	}
}
