using System;
using NUnit.Framework;
using FishMMO.Shared.Biomes;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>The baked climate table (<see cref="PlanetClimateTableData"/>): what goes onto a body's asset comes back as it was.</summary>
	public class PlanetClimateTableDataTests
	{
		[Test]
		public void APackedTable_ComesBackWithinAFractionOfAKelvin()
		{
			int n = PlanetClimateTableData.Count / 2;
			var land = new float[n];
			var ocean = new float[n];
			var random = new Random(7);
			for (int i = 0; i < n; i++)
			{
				// From a polar night to a desert noon about an Earth-like mean.
				land[i] = 288f + (float)(random.NextDouble() * 120.0 - 60.0);
				ocean[i] = 288f + (float)(random.NextDouble() * 60.0 - 30.0);
			}
			PlanetClimateTableData data = PlanetClimateTableData.Of(42L, 8760.0, 288.0, 0.5, land, ocean);
			Assert.That(data.HasTable, Is.True);
			Assert.That(data.TryUnpack(out float[] l, out float[] o), Is.True);
			double worst = 0.0;
			for (int i = 0; i < n; i++)
			{
				worst = Math.Max(worst, Math.Abs(l[i] - land[i]));
				worst = Math.Max(worst, Math.Abs(o[i] - ocean[i]));
			}
			Assert.That(worst, Is.LessThan(0.035), "within ±64 K of the mean a half holds a value to under 0.032 K");
		}

		[Test]
		public void TheHalfConversion_IsExactWhereAHalfIsExact_AndRoundsToNearest()
		{
			foreach (float exact in new[] { 0f, 1f, -2.5f, 0.125f, 65504f, -0.000061035156f })
			{
				Assert.That(PlanetClimateTableData.FromHalf(PlanetClimateTableData.ToHalf(exact)), Is.EqualTo(exact));
			}
			Assert.That(PlanetClimateTableData.FromHalf(PlanetClimateTableData.ToHalf(1f + 1f / 4096f)), Is.EqualTo(1f), "ties to even");
			Assert.That(float.IsPositiveInfinity(PlanetClimateTableData.FromHalf(PlanetClimateTableData.ToHalf(1e6f))), Is.True);
		}

		[Test]
		public void NothingBaked_IsNoTable()
		{
			Assert.That(new PlanetClimateTableData().HasTable, Is.False);
			Assert.That(new PlanetClimateTableData().TryUnpack(out _, out _), Is.False);
		}
	}
}
