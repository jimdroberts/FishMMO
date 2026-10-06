using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The simulation's water: a sea at its tide for a synced tick, worked out alike by any two peers and to the
	/// millimetre; lakes and rivers standing where they are, the highest water answering; a lava sea told apart.
	/// </summary>
	[TestFixture]
	public class WaterQueryTests
	{
		private const double TickDelta = 1.0 / 30.0;

		private sealed class Pond : SurfaceWater.IAreaSource
		{
			public Rect Area;
			public float Height;
			public Vector2 Flow;

			public float Level => float.NegativeInfinity;
			public float WaveHeight => 0f;
			public bool IsUnder(Vector3 point) => TryGetSurface(point.x, point.z, out float level) && point.y < level;
			public bool TryGetSurface(float x, float z, out float level)
			{
				level = Area.Contains(new Vector2(x, z)) ? Height : float.NegativeInfinity;
				return Area.Contains(new Vector2(x, z));
			}
			public Vector2 CurrentAt(float x, float z) => Area.Contains(new Vector2(x, z)) ? Flow : Vector2.zero;
		}

		private static WorldClock Clock()
		{
			var clock = new WorldClock { TickDelta = TickDelta };
			clock.Propose(0, 1000.0 * 3600.0, verified: true);
			return clock;
		}

		private static SeaTide Tide(WorldClock clock) => new SeaTide
		{
			MeanLevel = 2f,
			Amplification = 2.5f,
			MaximumMetres = 2.5f,
			Latitude = 20.0,
			Longitude = 40.0,
			Clock = clock,
		};

		private static Scene Here => SceneManager.GetActiveScene();

		[TearDown]
		public void TearDown() => WaterQuery.Clear();

		[Test]
		public void TwoPeersReadTheSameTideAtTheSameTick()
		{
			WorldClock clock = Clock();
			SeaTide server = Tide(clock), client = Tide(clock);
			// The client walks in from elsewhere: its cache holds another grid cell first.
			client.TideAt(123456u);
			foreach (uint tick in new uint[] { 0u, 1u, 299u, 300u, 301u, 45017u, 1_000_003u })
			{
				Assert.That(client.LevelAt(tick), Is.EqualTo(server.LevelAt(tick)), $"tick {tick}");
			}
		}

		[Test]
		public void TheTideIsKeptToTheMillimetreAndWithinItsMaximum()
		{
			WorldClock clock = Clock();
			SeaTide tide = Tide(clock);
			for (uint tick = 0; tick < 30u * 3600u * 26u; tick += 30u * 397u)
			{
				float metres = tide.TideAt(tick);
				Assert.That(metres * 1000f, Is.EqualTo(Mathf.Round(metres * 1000f)).Within(1e-3f), $"tick {tick}");
				Assert.That(Mathf.Abs(metres), Is.LessThanOrEqualTo(tide.MaximumMetres + 1e-4f), $"tick {tick}");
			}
		}

		[Test]
		public void OnItsGridTheTideIsTheRenderedOne()
		{
			WorldClock clock = Clock();
			SeaTide tide = Tide(clock);
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = system != null ? system.HomeWorld : null;
			uint tick = SeaTide.GridTicks * 77u;
			float drawn = SeaTide.TideMetres(system, body, clock.WorldHoursAt(tick), tide.Latitude, tide.Longitude, tide.Amplification, tide.MaximumMetres);
			Assert.That(tide.TideAt(tick), Is.EqualTo(drawn).Within(0.0006f), "WaterEnvironment draws the sea from the same function");
		}

		[Test]
		public void AnUndrivenTideStandsAtMeanLevel()
		{
			SeaTide tide = Tide(Clock());
			tide.Drive = false;
			Assert.That(tide.LevelAt(98765u), Is.EqualTo(2f));
		}

		[Test]
		public void TheSeaAnswersEverywhereAndALakeAboveItWins()
		{
			SeaTide sea = Tide(Clock());
			sea.Drive = false;
			var lake = new Pond { Area = new Rect(0f, 0f, 10f, 10f), Height = 30f };
			WaterQuery.RegisterSea(Here, sea, lava: false);
			WaterQuery.RegisterInland(Here, lake);

			WaterSample open = WaterQuery.Sample(Here, 100f, 100f, 0u);
			Assert.That(open.Body, Is.EqualTo(WaterBody.Sea));
			Assert.That(open.Surface, Is.EqualTo(2f));
			Assert.That(open.DepthOf(-3f), Is.EqualTo(5f));

			WaterSample inland = WaterQuery.Sample(Here, 5f, 5f, 0u);
			Assert.That(inland.Body, Is.EqualTo(WaterBody.Lake));
			Assert.That(inland.Surface, Is.EqualTo(30f));
		}

		[Test]
		public void ARiverCarriesItsCurrent()
		{
			var river = new Pond { Area = new Rect(0f, 0f, 4f, 50f), Height = 12f, Flow = new Vector2(0f, 1.5f) };
			WaterQuery.RegisterInland(Here, river);
			WaterSample sample = WaterQuery.Sample(Here, 2f, 20f, 0u);
			Assert.That(sample.Body, Is.EqualTo(WaterBody.River));
			Assert.That(sample.Current, Is.EqualTo(new Vector2(0f, 1.5f)));
			Assert.That(WaterQuery.Sample(Here, 20f, 20f, 0u).Present, Is.False, "dry beside it");
		}

		[Test]
		public void ALavaSeaIsLavaAndUnregisteringLeavesTheSceneDry()
		{
			SeaTide sea = Tide(Clock());
			WaterQuery.RegisterSea(Here, sea, lava: true);
			Assert.That(WaterQuery.Sample(Here, 0f, 0f, 0u).Body, Is.EqualTo(WaterBody.Lava));
			WaterQuery.UnregisterSea(Here, sea);
			Assert.That(WaterQuery.HasWater(Here), Is.False);
			Assert.That(WaterQuery.Sample(Here, 0f, 0f, 0u).Present, Is.False);
		}
	}
}
