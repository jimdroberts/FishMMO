using NUnit.Framework;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// How warm open water is (WaterTemperature): the sea its climate table's own sea column — whose season is already the
	/// sea's, damped and late (<see cref="SeasonalEnergyBalance"/>) — never below sea water's freezing point; a lake the
	/// ground's, a little damped, frozen at 0 °C.
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
		public void TheSea_TakesItsOwnColumnsSeasonWhole()
		{
			// The sea column's season is the sea surface's already: nothing more is damped or delayed here.
			Assert.That(WaterTemperature.SeaSeasonalShare, Is.EqualTo(1f));
			Assert.That(WaterTemperature.SeaLagYears, Is.EqualTo(0f));
			Assert.That(WaterTemperature.SeaSurfaceC(10f, -4f), Is.EqualTo(6f).Within(1e-4f));
		}

		[Test]
		public void ALake_IsAShadeMilderThanTheGroundsOwnYear()
		{
			Assert.That(WaterTemperature.InlandSurfaceC(12f, 8f), Is.LessThan(12f + 8f), "a lake lags and damps the ground's summer");
			Assert.That(WaterTemperature.InlandSurfaceC(12f, 8f), Is.GreaterThan(12f), "but warms in it");
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
		public void AWinterSea_IsWarmerThanTheWinterAirOverTheLand_ByEnoughToSmoke()
		{
			// Earth at 50°N from the climate model itself: the sea column's coldest point against the land column's.
			var inputs = new SeasonalEnergyBalance.Inputs
			{
				Declination = new double[SeasonalEnergyBalance.StepsPerYear],
				Flux = new double[SeasonalEnergyBalance.StepsPerYear],
				LandFraction = new double[SeasonalEnergyBalance.Bands],
				TargetMeanKelvin = 288.0,
			};
			for (int i = 0; i < SeasonalEnergyBalance.StepsPerYear; i++)
			{
				double t = (double)i / SeasonalEnergyBalance.StepsPerYear;
				inputs.Declination[i] = System.Math.Asin(System.Math.Sin(23.44 * System.Math.PI / 180.0) * System.Math.Sin(2.0 * System.Math.PI * t));
				inputs.Flux[i] = SeasonalEnergyBalance.SolarConstant;
			}
			for (int i = 0; i < SeasonalEnergyBalance.Bands; i++)
			{
				inputs.LandFraction[i] = 0.4;
			}
			SeasonalEnergyBalance.Result r = SeasonalEnergyBalance.Solve(inputs);
			float seaCold = float.MaxValue, landCold = float.MaxValue;
			for (int p = 0; p < SeasonalEnergyBalance.Phases; p++)
			{
				int k = SeasonalEnergyBalance.Result.Index(50, p);
				seaCold = System.Math.Min(seaCold, r.Ocean[k] - 273.15f);
				landCold = System.Math.Min(landCold, r.Land[k] - 273.15f);
			}
			float sea = WaterTemperature.SeaSurfaceC(seaCold, 0f);
			// A cold air mass off the continent, five kelvin under its winter mean.
			float air = landCold - 5f;
			Assert.That(sea - air, Is.GreaterThan(10f), $"winter sea {sea:0.0} °C against continental air {air:0.0} °C");
			float dew = SteamPhysics.DewPointOf(0.8f * SteamPhysics.SaturationPressure(air));
			Assert.That(SteamPhysics.SteamFogExcess(sea, air, dew, true), Is.GreaterThan(SteamPhysics.OnsetExcess), "sea smoke");
		}
	}
}
