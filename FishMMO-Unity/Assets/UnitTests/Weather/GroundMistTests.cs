using NUnit.Framework;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// When the ground makes mist of its own (GroundMist): in humid air, on a clear calm night, over wet
	/// ground — never in dry air, never in a fresh wind.
	/// </summary>
	/// <remarks>
	/// The inputs are the ones a meteorologist would name: the dew-point spread, the wind, the wetness of
	/// the ground, the rain, the clear sky and the sun. Pinned as comparisons and as clear-cut cases, not
	/// to whatever the curves happen to produce.
	/// </remarks>
	[TestFixture]
	public class GroundMistTests
	{
		private const float Noon = 60f;
		private const float BeforeDawn = -6f;

		[Test]
		public void DryAir_MakesNone()
		{
			Assert.That(GroundMist.Potential(18f, 1f, 0f, 0f, 1f, BeforeDawn), Is.EqualTo(0f), "a desert night");
			Assert.That(GroundMist.Potential(12f, 0f, 1f, 0f, 1f, BeforeDawn), Is.EqualTo(0f), "dry air over wet ground");
		}

		[Test]
		public void ARainForestFloor_MakesItByDay()
		{
			// Near-saturated air over wet ground, calm, the sun high: the forest's own mist.
			float forest = GroundMist.Potential(1.5f, 1f, 0.6f, 0f, 0.3f, Noon);
			Assert.That(forest, Is.GreaterThan(0.6f));
		}

		[Test]
		public void AClearCalmNight_MakesItWhereTheDayDidNot()
		{
			float night = GroundMist.Potential(4.5f, 0.5f, 0f, 0f, 1f, BeforeDawn);
			float day = GroundMist.Potential(4.5f, 0.5f, 0f, 0f, 1f, Noon);
			Assert.That(day, Is.EqualTo(0f), "the same air by day is a few degrees short");
			Assert.That(night, Is.GreaterThan(0.3f), "the ground's chill closes the gap by dawn");
		}

		[Test]
		public void CloudOverhead_KeepsTheNightWarm()
		{
			float clear = GroundMist.Potential(4f, 0.5f, 0f, 0f, 1f, BeforeDawn);
			float overcast = GroundMist.Potential(4f, 0.5f, 0f, 0f, 0f, BeforeDawn);
			Assert.That(overcast, Is.LessThan(clear));
		}

		[Test]
		public void AFreshWind_TearsItUp()
		{
			Assert.That(GroundMist.Potential(0f, 12f, 1f, 0f, 1f, BeforeDawn), Is.EqualTo(0f), "saturated, but stirred into the air above");
			Assert.That(GroundMist.Potential(1f, 5f, 0.5f, 0f, 1f, BeforeDawn), Is.LessThan(GroundMist.Potential(1f, 0.5f, 0.5f, 0f, 1f, BeforeDawn)));
		}

		[Test]
		public void WetterGround_MakesMore()
		{
			float dry = GroundMist.Potential(3f, 1f, 0f, 0f, 0.5f, Noon);
			float wet = GroundMist.Potential(3f, 1f, 1f, 0f, 0.5f, Noon);
			float raining = GroundMist.Potential(3f, 1f, 1f, 1f, 0.5f, Noon);
			Assert.That(wet, Is.GreaterThan(dry));
			Assert.That(raining, Is.GreaterThanOrEqualTo(wet));
		}

		[Test]
		public void AShoreOrAForest_HoldsMistTheOpenGroundDoesNot()
		{
			// A still evening, the air three degrees short: the open field is clear.
			float deficit = GroundMist.Deficit(3f, 1f, 0f, 0f, 0.5f, Noon);
			float stirred = GroundMist.Stirred(1f);
			float night = GroundMist.NightCalm(1f, 0.5f, Noon);
			Assert.That(GroundMist.Local(deficit, stirred), Is.EqualTo(0f), "open ground");
			float shore = GroundMist.Local(deficit - GroundMist.LocalClosing(0f, 1f, 0f, night), stirred);
			float forest = GroundMist.Local(deficit - GroundMist.LocalClosing(0f, 0f, 1f, night), stirred);
			Assert.That(shore, Is.GreaterThan(0.1f), "the water's edge");
			Assert.That(forest, Is.GreaterThan(0f), "under the canopy");
			Assert.That(GroundMist.BestPotential(deficit, stirred, night), Is.GreaterThan(shore), "the best spot has all three");
		}

		[Test]
		public void AHollowPoolsTheNightsChill_NotTheDays()
		{
			float atNight = GroundMist.LocalClosing(GroundMist.FullHollow, 0f, 0f, 1f);
			float byDay = GroundMist.LocalClosing(GroundMist.FullHollow, 0f, 0f, 0f);
			Assert.That(atNight, Is.EqualTo(GroundMist.HollowCooling).Within(1e-4f));
			Assert.That(byDay, Is.EqualTo(0f));
			Assert.That(GroundMist.LocalClosing(-10f, 0f, 0f, 1f), Is.EqualTo(0f), "a knoll is no colder");
		}

		[Test]
		public void APatchAtFullStrength_IsAThickMist()
		{
			Assert.That(GroundMist.Extinction(0f), Is.EqualTo(0f));
			// Koschmieder: visibility 3.912 / β.
			Assert.That(3.912f / GroundMist.Extinction(1f), Is.EqualTo(GroundMist.ThickVisibility).Within(1f));
			Assert.That(3.912f / GroundMist.Extinction(0.01f), Is.GreaterThan(800f), "the first wisps are thin");
		}

		[Test]
		public void ThePatchesStandFromAFewMetresToTensDeep()
		{
			Assert.That(GroundMist.Depth(0f), Is.LessThan(10f));
			Assert.That(GroundMist.Depth(1f), Is.EqualTo(GroundMist.DeepDepth));
		}
	}
}
