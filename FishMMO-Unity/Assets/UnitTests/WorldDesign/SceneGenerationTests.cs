using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Cutting a new scene out of a planet: what may be cut, where the tiles land, and whether the
	/// ground agrees with the globe it came from.
	/// </summary>
	[TestFixture]
	public class SceneGenerationTests
	{
		private readonly List<Object> created = new List<Object>();
		private WorldBody body;
		private WorldAtlasLayer surface;
		private WorldAtlasLayer underworld;

		[SetUp]
		public void SetUp()
		{
			PlanetSurface.ClearCache();
			body = Make<WorldBody>("Test World");
			body.SkyRadiusKm = PlanetSurface.EarthRadiusKm;
			body.Water = 0.7f;
			body.Atmosphere = AtmosphereKind.Standard;
			surface = Make<WorldAtlasLayer>("Overworld");
			underworld = Make<WorldAtlasLayer>("Underworld");
			underworld.Underground = true;
		}

		[TearDown]
		public void TearDown()
		{
			foreach (Object o in created)
			{
				Object.DestroyImmediate(o);
			}
			created.Clear();
			PlanetSurface.ClearCache();
		}

		private T Make<T>(string name) where T : ScriptableObject
		{
			T asset = ScriptableObject.CreateInstance<T>();
			asset.name = name;
			created.Add(asset);
			return asset;
		}

		private WorldAtlasScene Placed(string name, double latitude, double longitude, WorldAtlasLayer layer, float sizeKm = 2f)
		{
			WorldAtlasScene entry = Make<WorldAtlasScene>(name);
			entry.SceneName = name;
			entry.Placed = true;
			entry.Body = body;
			entry.Layer = layer;
			entry.Latitude = latitude;
			entry.Longitude = longitude;
			entry.SizeKm = new Vector2(sizeKm, sizeKm);
			return entry;
		}

		private SceneGenerationRequest Request(string name, double latitude, double longitude, WorldAtlasLayer layer, float sizeKm = 2f)
		{
			return new SceneGenerationRequest
			{
				SceneName = name,
				Body = body,
				Layer = layer,
				Latitude = latitude,
				Longitude = longitude,
				SizeKm = new Vector2(sizeKm, sizeKm),
			};
		}

		// ── Names ─────────────────────────────────────────────────────

		[Test]
		public void ANameAlreadyUsedByAnyWorldIsRefused()
		{
			/* Not just the world being cut into. A scene is identified by NAME everywhere that
			 * matters — the details cache is keyed by it, the atlas entry finds a scene by it, and
			 * a character's location names it — so two worlds each holding a "Coast" is not a
			 * collision that per-world folders resolve. */
			var existing = new[] { "Coast", "Tutorial Regions" };

			Assert.That(SceneGeneration.NameProblem("Coast", existing), Is.Not.Null,
				"a name used on another world must still be refused");
			Assert.That(SceneGeneration.NameProblem("Harbour", existing), Is.Null);
		}

		[Test]
		public void ANameThatDiffersOnlyInCaseIsRefused()
		{
			/* The runtime compares ordinally, so it would think these distinct — but a Windows file
			 * system would refuse the second file, and the project would build on one machine and
			 * not another. */
			Assert.That(SceneGeneration.NameProblem("COAST", new[] { "Coast" }), Is.Not.Null);
		}

		[Test]
		public void ANameAFileCannotHaveIsRefused()
		{
			var none = new string[0];
			Assert.That(SceneGeneration.NameProblem("", none), Is.Not.Null, "empty");
			Assert.That(SceneGeneration.NameProblem("   ", none), Is.Not.Null, "blank");
			Assert.That(SceneGeneration.NameProblem(" Coast", none), Is.Not.Null, "leading space");
			Assert.That(SceneGeneration.NameProblem("Coast ", none), Is.Not.Null, "trailing space");
			Assert.That(SceneGeneration.NameProblem("Co/ast", none), Is.Not.Null, "a separator is not a name");
			Assert.That(SceneGeneration.NameProblem("Coast of Yrois", none), Is.Null, "spaces inside are fine");
		}

		// ── Collisions ────────────────────────────────────────────────

		[Test]
		public void ARectangleOnTopOfAnotherSceneIsReported()
		{
			WorldAtlasScene neighbour = Placed("Neighbour", 10.0, 20.0, surface);
			List<WorldAtlasScene> hits = SceneGeneration.Collisions(
				Request("New", 10.0, 20.0, surface), new[] { neighbour }, body.SkyRadiusKm);

			Assert.That(hits, Has.Count.EqualTo(1), "a rectangle on the same spot collides");
			Assert.That(hits[0], Is.SameAs(neighbour));
		}

		[Test]
		public void ARectangleWellAwayFromEverythingIsClear()
		{
			WorldAtlasScene neighbour = Placed("Neighbour", 10.0, 20.0, surface);
			Assert.That(SceneGeneration.Collisions(
				Request("New", -40.0, -120.0, surface), new[] { neighbour }, body.SkyRadiusKm),
				Is.Empty);
		}

		[Test]
		public void ACaveUnderAForestIsNotACollision()
		{
			/* Layers are how an underworld sits beneath a surface zone without the two being the
			 * same place. Reporting that as a collision would make a dungeon impossible to put
			 * anywhere its entrance is. */
			WorldAtlasScene forest = Placed("Forest", 10.0, 20.0, surface);
			Assert.That(SceneGeneration.Collisions(
				Request("Cave", 10.0, 20.0, underworld), new[] { forest }, body.SkyRadiusKm),
				Is.Empty);
		}

		[Test]
		public void AnUnplacedSceneCannotBeCollidedWith()
		{
			// It is in the library, not on the globe, so it is nowhere to be in the way of.
			WorldAtlasScene waiting = Placed("Waiting", 10.0, 20.0, surface);
			waiting.Placed = false;
			Assert.That(SceneGeneration.Collisions(
				Request("New", 10.0, 20.0, surface), new[] { waiting }, body.SkyRadiusKm),
				Is.Empty);
		}

		// ── Tiles ─────────────────────────────────────────────────────

		[Test]
		public void TilesCoverTheSceneAndStayNearAKilometre()
		{
			foreach (float sizeKm in new[] { 1f, 2.8f, 4.5f, 8f })
			{
				TerrainTilePlan plan = SceneGeneration.PlanTiles(new Vector2(sizeKm, sizeKm));

				Assert.That(plan.WidthMetres, Is.GreaterThanOrEqualTo(sizeKm * 1000f - 1f),
					$"{sizeKm} km scene must be covered, not cropped");
				Assert.That(plan.TileMetres, Is.InRange(500f, 1500f),
					$"{sizeKm} km scene produced {plan.TileMetres:0} m tiles");
				Assert.That(plan.TotalTiles, Is.GreaterThan(0));
			}
		}

		[Test]
		public void ASmallSceneIsStillOneWholeTile()
		{
			TerrainTilePlan plan = SceneGeneration.PlanTiles(new Vector2(0.3f, 0.3f));
			Assert.That(plan.TotalTiles, Is.EqualTo(1), "a scene smaller than a tile is one tile");
			Assert.That(plan.WidthMetres, Is.GreaterThanOrEqualTo(300f));
		}

		[Test]
		public void TilesAreSquareSoTheHeightmapIsNotWasted()
		{
			/* Unity's terrain heightmap is square. A rectangular tile would either spend samples it
			 * does not need on one axis or under-sample the other. */
			TerrainTilePlan plan = SceneGeneration.PlanTiles(new Vector2(4f, 1f));
			Assert.That(plan.CountX, Is.GreaterThan(plan.CountZ), "a wide scene needs more tiles across");
			Assert.That(plan.WidthMetres, Is.GreaterThanOrEqualTo(4000f - 1f));
			Assert.That(plan.DepthMetres, Is.GreaterThanOrEqualTo(1000f - 1f));
		}

		// ── The ground agrees with the globe ──────────────────────────

		[Test]
		public void TheSceneAndTheGlobeUseOneConventionForLongitude()
		{
			/* The bug this pins was silent and total. AtlasGeometry.ToUnit and an independently
			 * written PlanetSurface.Direction used opposite axes for longitude — exactly a quarter
			 * turn apart — so a baked surface would have shown one hemisphere while scenes were cut
			 * from another, with nothing reporting a fault. PlanetSurface now delegates. */
			foreach (double longitude in new[] { -180.0, -90.0, 0.0, 45.0, 179.0 })
			{
				foreach (double latitude in new[] { -60.0, 0.0, 30.0 })
				{
					Vector3 mine = PlanetSurface.Direction(latitude, longitude);
					Vector3 atlas = AtlasGeometry.ToUnit(latitude, longitude).ToVector3();
					Assert.That(Vector3.Distance(mine, atlas), Is.LessThan(1e-5f),
						$"the two disagree at {latitude}, {longitude}");
				}
			}
		}

		[Test]
		public void LatitudeAndLongitudeSurviveARoundTrip()
		{
			foreach (double latitude in new[] { -75.0, -12.0, 0.0, 33.0, 80.0 })
			{
				foreach (double longitude in new[] { -170.0, -45.0, 0.0, 91.0, 175.0 })
				{
					PlanetSurface.LatLong(PlanetSurface.Direction(latitude, longitude), out double lat, out double lon);
					Assert.That(lat, Is.EqualTo(latitude).Within(1e-3));
					Assert.That(lon, Is.EqualTo(longitude).Within(1e-3));
				}
			}
		}

		[Test]
		public void TheSceneCentreStandsAtTheAltitudeTheGlobeSaysItDoes()
		{
			/* Without this the scene could be perfectly detailed and in the wrong place: a coast on
			 * the map and a mountain underfoot. */
			var request = Request("Cut", 24.0, -57.0, surface, 3f);
			request.FineDetail = false;

			float fromScene = SceneGeneration.AltitudeMetres(request, 0f, 0f);
			float fromGlobe = PlanetSurface.AltitudeMetresAt(body.ResolvedTerrainSeed, body, 24.0, -57.0);

			Assert.That(fromScene, Is.EqualTo(fromGlobe).Within(1f));
		}

		[Test]
		public void FineDetailRoughensTheGroundWithoutMovingIt()
		{
			/* It averages to zero on purpose: the scene must still sit where the globe says, and
			 * the coastline must still be where the map shows it. */
			var plain = Request("Cut", 24.0, -57.0, surface, 3f);
			plain.FineDetail = false;
			var detailed = Request("Cut", 24.0, -57.0, surface, 3f);
			detailed.FineDetail = true;

			float plainSum = 0f, detailedSum = 0f, biggestDifference = 0f;
			int samples = 0;
			for (float east = -1200f; east <= 1200f; east += 75f)
			{
				for (float north = -1200f; north <= 1200f; north += 75f)
				{
					float a = SceneGeneration.AltitudeMetres(plain, east, north);
					float b = SceneGeneration.AltitudeMetres(detailed, east, north);
					plainSum += a;
					detailedSum += b;
					biggestDifference = Mathf.Max(biggestDifference, Mathf.Abs(b - a));
					samples++;
				}
			}

			float meanShift = Mathf.Abs(detailedSum - plainSum) / samples;
			Assert.That(biggestDifference, Is.GreaterThan(1f), "detail must actually do something");
			Assert.That(meanShift, Is.LessThan(biggestDifference * 0.5f),
				$"detail moved the ground by {meanShift:0.0} m on average; it must average to nothing");
		}

		[Test]
		public void TheGroundIsContinuousAcrossATileSeam()
		{
			/* Tiles are sampled from one function in world coordinates, so a seam is only a seam in
			 * the asset layout. If this ever steps, the tiles are being sampled in their own frames
			 * instead of the scene's. */
			var request = Request("Cut", 5.0, 5.0, surface, 3f);
			TerrainTilePlan plan = SceneGeneration.PlanTiles(request.SizeKm);
			float seam = plan.TileMetres - plan.WidthMetres * 0.5f;

			float before = SceneGeneration.AltitudeMetres(request, seam - 0.05f, 0f);
			float after = SceneGeneration.AltitudeMetres(request, seam + 0.05f, 0f);
			Assert.That(Mathf.Abs(after - before), Is.LessThan(1f), "the ground stepped at a tile seam");
		}
	}
}
