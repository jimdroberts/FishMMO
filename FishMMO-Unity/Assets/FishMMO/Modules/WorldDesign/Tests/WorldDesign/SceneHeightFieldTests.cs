using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The scene's ground as one stitched grid: that it is the same ground the tiles used to be
	/// sampled from, that tiles cut from it share their edges, and that sampling it on many threads
	/// changes nothing.
	/// </summary>
	[TestFixture]
	public class SceneHeightFieldTests
	{
		private readonly List<Object> created = new List<Object>();
		private WorldBody body;

		/// <summary>Two by two tiles of 300 m at 17 samples: every shape the real grid has, at a fraction of the cost.</summary>
		private static readonly TerrainTilePlan SmallPlan = new TerrainTilePlan
		{
			CountX = 2,
			CountZ = 2,
			TileMetres = 300f,
			Resolution = 17,
		};

		[SetUp]
		public void SetUp()
		{
			PlanetSurface.ClearCache();
			body = ScriptableObject.CreateInstance<WorldBody>();
			// No authored seed, so the seed comes from the asset's name: the case a worker thread cannot ask about.
			body.name = "Height Field World";
			body.SkyRadiusKm = PlanetSurface.EarthRadiusKm;
			body.Water = 0.7f;
			body.Atmosphere = AtmosphereKind.Standard;
			created.Add(body);
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

		private SceneGenerationRequest Request()
		{
			return new SceneGenerationRequest
			{
				SceneName = "Field",
				Body = body,
				Latitude = 24.0,
				Longitude = -57.0,
				SizeKm = new Vector2(0.6f, 0.6f),
			};
		}

		[Test]
		public void EverySampleIsTheScenesOwnGround()
		{
			/* The grid replaces sampling tile by tile; it must not change the ground. Each sample is
			 * exactly what AltitudeMetres says at its position. */
			SceneGenerationRequest request = Request();
			SceneHeightField field = SceneHeightField.Sample(request, SmallPlan);

			Assert.That(field.Width, Is.EqualTo(2 * 16 + 1));
			Assert.That(field.Depth, Is.EqualTo(2 * 16 + 1));
			for (int z = 0; z < field.Depth; z += 5)
			{
				for (int x = 0; x < field.Width; x += 3)
				{
					float expected = SceneGeneration.AltitudeMetres(request, field.EastOf(x), field.NorthOf(z));
					Assert.That(field.Metres[z * field.Width + x], Is.EqualTo(expected), $"sample ({x}, {z})");
				}
			}
			Assert.That(field.EastOf(0), Is.EqualTo(-SmallPlan.WidthMetres * 0.5f).Within(1e-3f));
			Assert.That(field.EastOf(field.Width - 1), Is.EqualTo(SmallPlan.WidthMetres * 0.5f).Within(1e-3f));
		}

		[Test]
		public void NeighbouringTilesShareTheirEdgeExactly()
		{
			/* What used to hold only because two tiles asked the same function now holds by
			 * construction: the seam is one row of the grid, written into both tiles. */
			SceneHeightField field = SceneHeightField.Sample(Request(), SmallPlan);
			field.Range(out float lowest, out float highest);
			float relief = Mathf.Max(1f, highest - lowest);
			int last = SmallPlan.Resolution - 1;

			float[,] southWest = field.TileHeights(0, 0, lowest, relief);
			float[,] southEast = field.TileHeights(1, 0, lowest, relief);
			float[,] northWest = field.TileHeights(0, 1, lowest, relief);
			for (int i = 0; i < SmallPlan.Resolution; i++)
			{
				Assert.That(southEast[i, 0], Is.EqualTo(southWest[i, last]), $"east seam, row {i}");
				Assert.That(northWest[0, i], Is.EqualTo(southWest[last, i]), $"north seam, column {i}");
			}
		}

		[Test]
		public void TheRangeIsTheGroundsOwnLowestAndHighest()
		{
			/* Measured, not bounded: the tiles span exactly the ground they hold, so the lowest
			 * sample is 0 and the highest 1 somewhere, and nothing is clamped. */
			SceneHeightField field = SceneHeightField.Sample(Request(), SmallPlan);
			field.Range(out float lowest, out float highest);

			float min = float.MaxValue, max = float.MinValue;
			foreach (float metres in field.Metres)
			{
				min = Mathf.Min(min, metres);
				max = Mathf.Max(max, metres);
			}
			Assert.That(lowest, Is.EqualTo(min));
			Assert.That(highest, Is.EqualTo(max));
			Assert.That(highest, Is.GreaterThan(lowest), "the test ground must not be flat");

			float tileMin = 1f, tileMax = 0f;
			for (int tz = 0; tz < SmallPlan.CountZ; tz++)
			{
				for (int tx = 0; tx < SmallPlan.CountX; tx++)
				{
					foreach (float h in field.TileHeights(tx, tz, lowest, highest - lowest))
					{
						tileMin = Mathf.Min(tileMin, h);
						tileMax = Mathf.Max(tileMax, h);
					}
				}
			}
			Assert.That(tileMin, Is.EqualTo(0f));
			Assert.That(tileMax, Is.EqualTo(1f).Within(1e-6f));
		}

		[Test]
		public void SamplingOnManyThreadsGivesTheSameGroundEveryTime()
		{
			SceneHeightField first = SceneHeightField.Sample(Request(), SmallPlan);
			SceneHeightField second = SceneHeightField.Sample(Request(), SmallPlan);
			CollectionAssert.AreEqual(first.Metres, second.Metres);
		}

		[Test]
		public void TheAltitudeCanBeAskedFromAWorkerThread()
		{
			/* The body's seed comes from its asset name here, and Unity only answers a name on the
			 * main thread. Resolved once when the altitude is built, the worker never asks. */
			var altitude = new SceneAltitude(Request());
			float onMain = altitude.At(120f, -80f);
			float onWorker = Task.Run(() => altitude.At(120f, -80f)).Result;
			Assert.That(onWorker, Is.EqualTo(onMain));
		}

		[Test]
		public void TheGroundIsTheGridOnTheSceneAndThePlanetPastIt()
		{
			SceneGenerationRequest request = Request();
			SceneHeightField field = SceneHeightField.Sample(request, SmallPlan);

			int x = 7, z = 20;
			Assert.That(field.MetresAt(field.EastOf(x), field.NorthOf(z)),
				Is.EqualTo(field.Metres[z * field.Width + x]).Within(1e-3f), "on a sample it is the sample");

			float between = field.MetresAt((field.EastOf(x) + field.EastOf(x + 1)) * 0.5f, field.NorthOf(z));
			float a = field.Metres[z * field.Width + x], b = field.Metres[z * field.Width + x + 1];
			Assert.That(between, Is.EqualTo((a + b) * 0.5f).Within(1e-3f), "between samples it is the line between them");

			float east = SmallPlan.WidthMetres * 0.5f + 50f;
			Assert.That(field.MetresAt(east, 0f), Is.EqualTo(SceneGeneration.AltitudeMetres(request, east, 0f)),
				"past the edge it is the planet");
		}
	}
}
