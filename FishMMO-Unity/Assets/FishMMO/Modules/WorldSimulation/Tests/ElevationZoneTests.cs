using NUnit.Framework;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A body's elevation tiers are landform bands at physical altitudes — the shore to 30 m, lowland
	/// to 1.5 km, highland to 2.5 km, mountain to 3.5 km, alpine to 4.5 km, nival above — not fractions
	/// of its summit, and the runtime's <see cref="ClimateSettings.TierForHeight(float, float[])"/> reads
	/// exactly those bands back from the normalised height the field hands out.
	/// </summary>
	/// <remarks>
	/// The regression these guard: land used to be spread evenly from the water line to the summit, so
	/// on an Earth-sized world the coast tier ran from 0 to 460 m and a third of all land could only
	/// ever be beach, estuary or rocky coast.
	/// </remarks>
	[TestFixture]
	public class ElevationZoneTests
	{
		private static int TierAt(float earthMetres)
		{
			float[] boundaries = ClimateSettings.DefaultElevationBoundaries;
			float height = PlanetClimateField.LandHeight(earthMetres, 8848f, boundaries);
			return ClimateSettings.TierForHeight(height, boundaries);
		}

		[TestCase(0f, 3)]
		[TestCase(10f, 3)]
		[TestCase(29f, 3)]
		[TestCase(31f, 4)]
		[TestCase(460f, 4)]
		[TestCase(1499f, 4)]
		[TestCase(1501f, 5)]
		[TestCase(2499f, 5)]
		[TestCase(2501f, 6)]
		[TestCase(3499f, 6)]
		[TestCase(3501f, 7)]
		[TestCase(4499f, 7)]
		[TestCase(4501f, 8)]
		[TestCase(8848f, 8)]
		[TestCase(12000f, 8)]
		public void EachAltitude_FallsInItsLandformTier(float earthMetres, int tier)
		{
			Assert.AreEqual(tier, TierAt(earthMetres), $"{earthMetres} m");
		}

		[Test]
		public void TheTierEdges_AreTheMetreTable()
		{
			float[] metres = ClimateSettings.TierEdgeMetres;
			Assert.AreEqual(10, metres.Length, "one altitude per boundary");
			for (int i = 1; i < metres.Length; i++)
			{
				Assert.Greater(metres[i], metres[i - 1], $"edge {i} rises");
			}
			Assert.AreEqual(0f, metres[3], "the bottom of the shore tier is the water line");
			Assert.AreEqual(-PlanetClimateField.ShelfEdgeMetres, metres[2], "the shelf break, as the sea floor's zones use it");
			Assert.AreEqual(-PlanetClimateField.AbyssalMetres, metres[1]);
		}

		[Test]
		public void TheLandHeight_RisesWithTheGround_AndStartsAtTheWaterLine()
		{
			float[] boundaries = ClimateSettings.DefaultElevationBoundaries;
			Assert.AreEqual(ClimateModel.DefaultWaterSurfaceHeight, PlanetClimateField.LandHeight(0f, 8848f, boundaries), 1e-6f);
			Assert.AreEqual(1f, PlanetClimateField.LandHeight(8848f, 8848f, boundaries), 1e-6f, "the summit is the top of the scale");
			float previous = -1f;
			for (float m = 0f; m <= 9000f; m += 7f)
			{
				float h = PlanetClimateField.LandHeight(m, 8848f, boundaries);
				Assert.GreaterOrEqual(h, previous, $"{m} m");
				Assert.That(h, Is.InRange(0f, 1f));
				previous = h;
			}
		}

		[Test]
		public void AClimateThatMovesItsBoundaries_StillGetsAMonotoneHeight()
		{
			// Out of order and below the water line: the height must still only rise with the ground.
			float[] odd = { 0f, 0.2f, 0.35f, 0.42f, 0.40f, 0.7f, 0.65f, 0.9f, 0.95f, 1f };
			float previous = -1f;
			for (float m = 0f; m <= 9000f; m += 11f)
			{
				float h = PlanetClimateField.LandHeight(m, 8848f, odd);
				Assert.GreaterOrEqual(h, previous, $"{m} m");
				previous = h;
			}
		}

		[Test]
		public void TheField_ScalesTheBandsByTheBodysRelief()
		{
			// No body: an Earth-sized world, so its metres are Earth's.
			PlanetClimateField earth = PlanetClimateField.For(null, null);
			float scale = earth.ReliefMetres / PlanetSurface.EarthReliefMetres;
			float[] boundaries = ClimateSettings.DefaultElevationBoundaries;
			Assert.AreEqual(3, ClimateSettings.TierForHeight(earth.HeightOfAltitude(20f * scale), boundaries), "the shore");
			Assert.AreEqual(4, ClimateSettings.TierForHeight(earth.HeightOfAltitude(460f * scale), boundaries),
				"460 m was the top of the coast tier when tiers were fractions of the summit; it is lowland");
			Assert.AreEqual(6, ClimateSettings.TierForHeight(earth.HeightOfAltitude(3000f * scale), boundaries), "mountain");
			Assert.AreEqual(2, ClimateSettings.TierForHeight(earth.HeightOfAltitude(-100f * scale), boundaries), "the shelf is untouched");
			Assert.AreEqual(0, ClimateSettings.TierForHeight(earth.HeightOfAltitude(-5000f * scale), boundaries), "and the abyss");
		}
	}
}
