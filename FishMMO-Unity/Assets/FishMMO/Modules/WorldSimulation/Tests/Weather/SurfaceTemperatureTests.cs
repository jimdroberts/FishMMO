using NUnit.Framework;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>The hour's and the air mass's share of the air's temperature (<see cref="SurfaceTemperature"/>).</summary>
	public class SurfaceTemperatureTests
	{
		[Test]
		public void TheDay_RangesWideOverDryLand_LittleOverTheSea_AndNotAtAllInPolarDayOrNight()
		{
			float land = SurfaceTemperature.DayRangeKelvin(AtmosphereKind.Standard, 1f, 0.2f, 0.5f, 24.0);
			float sea = SurfaceTemperature.DayRangeKelvin(AtmosphereKind.Standard, 0f, 0.2f, 0.5f, 24.0);
			Assert.That(land, Is.InRange(10f, 16f), "a clear dry day inland");
			Assert.That(sea, Is.LessThan(2f), "the sea barely notices the day");
			Assert.That(SurfaceTemperature.DayRangeKelvin(AtmosphereKind.Standard, 1f, 0.2f, 1f, 24.0), Is.EqualTo(0f), "polar day");
			Assert.That(SurfaceTemperature.DayRangeKelvin(AtmosphereKind.Standard, 1f, 0.2f, 0f, 24.0), Is.EqualTo(0f), "polar night");
			Assert.That(SurfaceTemperature.DayRangeKelvin(AtmosphereKind.Standard, 1f, 0.95f, 0.5f, 24.0), Is.LessThan(land * 0.5f), "cloud and damp hold it in");
			Assert.That(SurfaceTemperature.DayRangeKelvin(AtmosphereKind.None, 1f, 0f, 0.5f, 24.0), Is.GreaterThan(100f), "no air: scorched by day, frozen by night");
			Assert.That(SurfaceTemperature.DayRangeKelvin(AtmosphereKind.Thick, 1f, 0f, 0.5f, 24.0), Is.LessThan(2f), "a thick air evens it out");
		}

		[Test]
		public void TheAfternoonIsWarmest_AndTheSmallHoursColdest()
		{
			float afternoon = SurfaceTemperature.DayKelvin(AtmosphereKind.Standard, 1f, 0.3f, SurfaceTemperature.WarmestHour01, 0.5f, 24.0);
			float night = SurfaceTemperature.DayKelvin(AtmosphereKind.Standard, 1f, 0.3f, SurfaceTemperature.WarmestHour01 - 0.5f, 0.5f, 24.0);
			Assert.That(afternoon, Is.GreaterThan(0f));
			Assert.That(night, Is.EqualTo(-afternoon).Within(1e-4f));
		}

		[Test]
		public void AnAirMass_SwingsLittleInTheTropics_AndFarInAMidLatitudeWinter()
		{
			float tropics = SurfaceTemperature.AirMassKelvin(SurfaceTemperature.AirMassExtreme, 0.05f, 0.5f);
			float winter = SurfaceTemperature.AirMassKelvin(SurfaceTemperature.AirMassExtreme, 1.0f, 0.5f);
			Assert.That(tropics, Is.EqualTo(SurfaceTemperature.AirMassFloorKelvin).Within(1e-4f));
			Assert.That(winter, Is.InRange(8f, 10f));
			Assert.That(SurfaceTemperature.AirMassKelvin(SurfaceTemperature.AirMassExtreme, 1.0f, 1f), Is.GreaterThan(winter), "nothing tempers it inland");
		}
	}
}
