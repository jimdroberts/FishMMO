using NUnit.Framework;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// How warm open water is (WaterTemperature): the sea the year's mean air with a damped, late season,
	/// never below sea water's freezing point; a lake closer to the air, frozen at 0 °C.
	/// </summary>
	[TestFixture]
	public class WaterTemperatureTests
	{
		[Test]
		public void WithNoSeason_TheSeaIsTheEditorsIcePlacement()
		{
			// IcePlacer.SeaSurfaceC: the annual mean air, never below −1.8 °C.
			Assert.That(WaterTemperature.SeaSurfaceC(12f, 0f), Is.EqualTo(12f));
			Assert.That(WaterTemperature.SeaSurfaceC(-15f, 0f), Is.EqualTo(WaterTemperature.SeaWaterFreezingC));
		}

		[Test]
		public void TheSea_FollowsAThirdOfTheSeason()
		{
			float winter = WaterTemperature.SeaSurfaceC(10f, -12f);
			Assert.That(winter, Is.EqualTo(10f - WaterTemperature.SeaSeasonalShare * 12f).Within(1e-4f));
			Assert.That(winter, Is.GreaterThan(10f - 12f + 5f), "a winter sea is still warm from the summer");
		}

		[Test]
		public void ALake_FollowsTheAirCloserThanTheSea()
		{
			float sea = WaterTemperature.SeaSurfaceC(12f, 8f);
			float lake = WaterTemperature.InlandSurfaceC(12f, 8f);
			Assert.That(lake, Is.GreaterThan(sea), "warmer in summer");
			Assert.That(WaterTemperature.InlandSurfaceC(12f, -8f), Is.LessThan(WaterTemperature.SeaSurfaceC(12f, -8f)), "colder in winter");
		}

		[Test]
		public void ALake_FreezesAtZero_AndIsNeverColder()
		{
			Assert.That(WaterTemperature.InlandFrozen(4f, -10f), Is.True);
			Assert.That(WaterTemperature.InlandSurfaceC(4f, -10f), Is.EqualTo(WaterTemperature.FreshWaterFreezingC));
			Assert.That(WaterTemperature.InlandFrozen(4f, 2f), Is.False);
		}

		[Test]
		public void TheSea_ClosesUnderPackIceOverAFewKelvin()
		{
			Assert.That(WaterTemperature.SeaOpenWater(5f, 0f), Is.EqualTo(1f));
			Assert.That(WaterTemperature.SeaOpenWater(WaterTemperature.SeaWaterFreezingC - 0.5f * WaterTemperature.PackRampKelvin, 0f), Is.EqualTo(0.5f).Within(1e-4f));
			Assert.That(WaterTemperature.SeaOpenWater(-20f, 0f), Is.EqualTo(0f));
		}

		[Test]
		public void AWinterSea_IsWarmerThanTheWinterAir_ByEnoughToSmoke()
		{
			// A mid-latitude coast: annual mean 8 °C, a winter twelve kelvin under it, a cold air mass five more.
			float sea = WaterTemperature.SeaSurfaceC(8f, -12f);
			float air = 8f - 12f - 5f;
			Assert.That(sea - air, Is.GreaterThan(10f));
			float dew = SteamPhysics.DewPointOf(0.8f * SteamPhysics.SaturationPressure(air));
			Assert.That(SteamPhysics.SteamFogExcess(sea, air, dew, true), Is.GreaterThan(SteamPhysics.OnsetExcess), "sea smoke");
		}
	}
}
