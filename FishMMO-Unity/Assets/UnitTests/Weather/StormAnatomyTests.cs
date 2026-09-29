using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// A storm's anatomy from the air it grows in: the cloud, the rain core, the wall cloud a tornado
	/// hangs from, the shelf ahead of a squall line, the anvil at the tropopause.
	/// </summary>
	[TestFixture]
	public class StormAnatomyTests
	{
		/// <summary>Sultry, unstable air in a weak low, the wind from the south-west.</summary>
		private static WeatherSample Sultry()
		{
			PlanetAir planet = PlanetAir.Earthlike;
			var air = new WeatherDriver.Synoptic { Humidity = 0.7f, Pressure = -0.2f, Instability = 0.5f, Wind = new Vector2(6f, 6f), LocalTime01 = 0.65f };
			AirColumn column = AirColumn.Of(planet, 300f, air.Humidity, air.Pressure, air.Instability);
			return new WeatherSample { Planet = planet, OpenAir = air, Air = air, OpenColumn = column, Column = column, Temperature = 0.6f };
		}

		[Test]
		public void AStormsBaseIsItsOwnAndItsTopTheTropopause()
		{
			WeatherSample around = Sultry();
			StormAnatomy storm = StormAnatomy.Of(StormKind.Thunderstorm, around, Vector2.zero, 35f, 2000f);
			LogAssert.IsTrue(storm.Valid);
			LogAssert.IsTrue(storm.BaseMetres <= around.OpenColumn.Base, $"its moist inflow condenses lower: {storm.BaseMetres:0} vs {around.OpenColumn.Base:0} m");
			LogAssert.IsTrue(storm.TopMetres >= around.OpenColumn.Tropopause, $"and it climbs to the top of the weather: {storm.TopMetres:0} m");
			Assert.That(storm.CoreRadius, Is.InRange(2000f, 8000f), "a cumulonimbus some kilometres across");
			LogAssert.IsTrue(storm.AnvilRadius >= 2f * storm.CoreRadius, "its anvil spreads well beyond its tower");
			LogAssert.IsTrue(storm.AnvilBase < storm.AnvilTop && storm.AnvilTop <= storm.TopMetres, "at the tropopause, under the overshoot");
			LogAssert.IsTrue(Vector2.Dot(storm.AnvilOffset, around.OpenAir.Wind) > 0f, "and streams downwind");
		}

		[Test]
		public void ASupercellsTornadoHangsFromAWallCloudOnItsRearFlank()
		{
			WeatherSample around = Sultry();
			var motion = new Vector2(8f, 4f);
			StormAnatomy storm = StormAnatomy.Of(StormKind.Supercell, around, motion, 35f, 120f);
			Assert.That(storm.MesoRadius, Is.InRange(1500f, 5000f), "a rotating updraught two to six miles across");
			Assert.That(2f * storm.WallCloudRadius, Is.InRange(1000f, 8000f), "a wall cloud a fraction of a mile to a few miles across");
			LogAssert.IsTrue(storm.WallCloudBase < storm.BaseMetres && storm.WallCloudBase > 0f, "a lowering under the base, clear of the ground");
			// The tornado (the cell) is behind the storm's body: the body lies ahead along the motion,
			// and to the left of it in the northern hemisphere.
			LogAssert.IsTrue(Vector2.Dot(storm.BodyOffset, motion) > 0f, "the storm's body is ahead of the tornado");
			var left = new Vector2(-motion.y, motion.x);
			LogAssert.IsTrue(Vector2.Dot(storm.BodyOffset, left) > 0f, "and to its left: the tornado is on the right rear flank");
			LogAssert.IsTrue(Vector2.Dot(storm.RainOffset, motion) > 0f, "the rain and hail fall on the forward flank");

			StormAnatomy south = StormAnatomy.Of(StormKind.Supercell, around, motion, -35f, 120f);
			LogAssert.IsTrue(Vector2.Dot(south.BodyOffset, left) < 0f, "in the southern hemisphere the other way round");
		}

		[Test]
		public void ASquallLineIsLedByAShelfCloud()
		{
			WeatherSample around = Sultry();
			StormAnatomy line = StormAnatomy.Of(StormKind.SquallLine, around, new Vector2(10f, 0f), 35f, 5000f);
			LogAssert.IsTrue(line.ShelfDrop > 0f && line.ShelfDrop < line.BaseMetres, "a lowering along the gust front, above the ground");
			LogAssert.IsTrue(line.ShelfWidth >= 1000f, "reaching a kilometre or more ahead of the rain");
		}

		[Test]
		public void DustAndAshHaveNoCloudOfTheirOwn()
		{
			WeatherSample around = Sultry();
			LogAssert.IsFalse(StormAnatomy.Of(StormKind.Haboob, around, Vector2.zero, 35f, 3000f).Valid);
			LogAssert.IsFalse(StormAnatomy.Of(StormKind.DustDevil, around, Vector2.zero, 35f, 8f).Valid);
			LogAssert.IsFalse(StormAnatomy.Of(StormKind.Thunderstorm, default, Vector2.zero, 35f, 2000f).Valid, "no air, no storm");
		}

		[Test]
		public void StormsAreAsBigAsTheirAir()
		{
			WeatherSample around = Sultry();
			StormPhysics.Dimensions(StormKind.SquallLine, around.OpenColumn, 0.5f, 0.5f, out float depth, out float half, out _);
			Assert.That(depth, Is.InRange(2500f, 8000f), "a rain band kilometres deep");
			Assert.That(2f * half, Is.GreaterThanOrEqualTo(24000f), "and tens of kilometres long");
			StormPhysics.Dimensions(StormKind.Thunderstorm, around.OpenColumn, 0.5f, 0.5f, out float rain, out _, out _);
			Assert.That(rain, Is.InRange(1500f, 4000f), "a thunderstorm's rain area");
		}
	}
}
