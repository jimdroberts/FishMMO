using System;
using NUnit.Framework;
using FishMMO.Shared.Biomes;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The seasonal energy balance on Earth's own inputs, against Earth's zonal climatology: the calibration pinned, so a
	/// change to a constant that moves the climate is seen here before it moves every world.
	/// </summary>
	public class SeasonalEnergyBalanceTests
	{
		/// <summary>Earth's land share by latitude band centre, 85°S to 85°N.</summary>
		private static readonly double[] EarthLatitudes = { -85, -75, -65, -55, -45, -35, -25, -15, -5, 5, 15, 25, 35, 45, 55, 65, 75, 85 };
		private static readonly double[] EarthLand = { 1.0, 0.8, 0.18, 0.01, 0.03, 0.11, 0.23, 0.22, 0.24, 0.23, 0.27, 0.38, 0.43, 0.53, 0.57, 0.71, 0.30, 0.10 };

		private static SeasonalEnergyBalance.Result earth;

		private static SeasonalEnergyBalance.Result Earth()
		{
			if (earth != null)
			{
				return earth;
			}
			var inputs = new SeasonalEnergyBalance.Inputs
			{
				Declination = new double[SeasonalEnergyBalance.StepsPerYear],
				Flux = new double[SeasonalEnergyBalance.StepsPerYear],
				LandFraction = new double[SeasonalEnergyBalance.Bands],
				TargetMeanKelvin = 288.0,
			};
			for (int s = 0; s < SeasonalEnergyBalance.StepsPerYear; s++)
			{
				double t = (double)s / SeasonalEnergyBalance.StepsPerYear;
				inputs.Declination[s] = Math.Asin(Math.Sin(23.44 * Math.PI / 180.0) * Math.Sin(2.0 * Math.PI * t));
				inputs.Flux[s] = SeasonalEnergyBalance.SolarConstant;
			}
			for (int i = 0; i < SeasonalEnergyBalance.Bands; i++)
			{
				double latitude = Math.Asin(-1.0 + (i + 0.5) * 2.0 / SeasonalEnergyBalance.Bands) * 180.0 / Math.PI;
				inputs.LandFraction[i] = Interpolate(latitude);
			}
			earth = SeasonalEnergyBalance.Solve(inputs);
			return earth;
		}

		private static double Interpolate(double latitude)
		{
			if (latitude <= EarthLatitudes[0])
			{
				return EarthLand[0];
			}
			for (int i = 1; i < EarthLatitudes.Length; i++)
			{
				if (latitude <= EarthLatitudes[i])
				{
					double f = (latitude - EarthLatitudes[i - 1]) / (EarthLatitudes[i] - EarthLatitudes[i - 1]);
					return EarthLand[i - 1] + (EarthLand[i] - EarthLand[i - 1]) * f;
				}
			}
			return EarthLand[EarthLand.Length - 1];
		}

		/// <summary>The zonal (land-share weighted) mean and swing at a latitude, °C and K.</summary>
		private static void Zonal(SeasonalEnergyBalance.Result r, int latitude, out double mean, out double swing)
		{
			double f = Interpolate(latitude), sum = 0.0, hi = double.MinValue, lo = double.MaxValue;
			for (int p = 0; p < SeasonalEnergyBalance.Phases; p++)
			{
				int k = SeasonalEnergyBalance.Result.Index(latitude, p);
				double t = f * r.Land[k] + (1.0 - f) * r.Ocean[k] - 273.15;
				sum += t;
				hi = Math.Max(hi, t);
				lo = Math.Min(lo, t);
			}
			mean = sum / SeasonalEnergyBalance.Phases;
			swing = hi - lo;
		}

		[TestCase(0, 26.5, 1.0)]
		[TestCase(30, 20.0, 13.0)]
		[TestCase(45, 9.5, 22.0)]
		[TestCase(60, -1.0, 30.0)]
		[TestCase(80, -18.0, 34.0)]
		public void EarthsZonalYear_IsEarths(int latitude, double meanC, double swingK)
		{
			Zonal(Earth(), latitude, out double mean, out double swing);
			Assert.That(mean, Is.EqualTo(meanC).Within(3.0), $"annual mean at {latitude}°N");
			Assert.That(swing, Is.EqualTo(swingK).Within(4.0), $"seasonal swing at {latitude}°N");
		}

		[Test]
		public void TheWorldsMeanIsExactlyItsOwn()
		{
			SeasonalEnergyBalance.Result r = Earth();
			Assert.That(r.MeanKelvin, Is.EqualTo(288.0).Within(1e-3));
			Assert.That(Math.Abs(r.Residual), Is.LessThan(0.5), "Earth's climate balances at its mean without a shift");
		}

		[Test]
		public void APolarDayIsSunnierThanTheEquator_AndAPolarNightIsDark()
		{
			double delta = 23.44 * Math.PI / 180.0;
			double pole = SeasonalEnergyBalance.DailyInsolation(1361.0, Math.PI / 2.0, delta);
			double equator = SeasonalEnergyBalance.DailyInsolation(1361.0, 0.0, delta);
			Assert.That(pole, Is.GreaterThan(equator), "at midsummer the pole's 24 hours of sun out-shine the equator's 12");
			Assert.That(SeasonalEnergyBalance.DailyInsolation(1361.0, Math.PI / 2.0, -delta), Is.EqualTo(0.0));
		}
	}
}
