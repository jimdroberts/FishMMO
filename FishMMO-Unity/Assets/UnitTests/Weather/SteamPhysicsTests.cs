using NUnit.Framework;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// When water and hot ground make steam that can be seen (SteamPhysics): the mixing line against the
	/// saturation curve for steam fog, and how far a plume climbs before the air has evaporated it.
	/// </summary>
	/// <remarks>
	/// Pinned to the forecasters' facts — steam fog needs water some 8–10 K warmer than air of ordinary
	/// humidity, drier air needs more, a fumarole is a wisp in summer and a column in winter — and as
	/// comparisons, not to whatever the curves happen to produce.
	/// </remarks>
	[TestFixture]
	public class SteamPhysicsTests
	{
		private static float Dew(float airC, float relativeHumidity)
		{
			return SteamPhysics.DewPointOf(relativeHumidity * SteamPhysics.SaturationPressure(airC));
		}

		private static float Onset(float airC, float relativeHumidity)
		{
			for (int i = 0; i < 400; i++)
			{
				float difference = i * 0.1f;
				if (SteamPhysics.SteamFogExcess(airC + difference, airC, Dew(airC, relativeHumidity), false) > SteamPhysics.OnsetExcess)
				{
					return difference;
				}
			}
			return float.PositiveInfinity;
		}

		[Test]
		public void TheDewPoint_ReadsTheSaturationCurveBack()
		{
			foreach (float c in new[] { -40f, -10f, 0f, 15f, 60f, 95f })
			{
				Assert.That(SteamPhysics.DewPointOf(SteamPhysics.SaturationPressure(c)), Is.EqualTo(c).Within(0.01f), $"{c} °C");
			}
		}

		[Test]
		public void SteamFog_StartsAtTheForecastersDifference()
		{
			// 80 % humidity at 0–10 °C: the line first crosses the curve some 8–10 K up.
			Assert.That(Onset(5f, 0.8f), Is.InRange(8f, 10.5f), "ordinary air");
			Assert.That(SteamPhysics.SteamFogExcess(11f, 5f, Dew(5f, 0.8f), false), Is.LessThanOrEqualTo(SteamPhysics.OnsetExcess), "6 K is not enough");
			Assert.That(SteamPhysics.SteamFogExcess(19f, 5f, Dew(5f, 0.8f), false), Is.GreaterThan(0.3f), "14 K smokes");
		}

		[Test]
		public void DrierAir_NeedsWarmerWater()
		{
			Assert.That(Onset(5f, 0.6f), Is.GreaterThan(Onset(5f, 0.9f) + 3f));
		}

		[Test]
		public void ColderAir_SmokesSoonerAndThicker()
		{
			Assert.That(Onset(-30f, 0.8f), Is.LessThan(Onset(10f, 0.8f)), "the curve bends hardest in the cold");
			float arctic = SteamPhysics.SteamFogExcess(-10f, -30f, Dew(-30f, 0.8f), true);
			float autumn = SteamPhysics.SteamFogExcess(30f, 10f, Dew(10f, 0.8f), false);
			Assert.That(arctic, Is.GreaterThan(autumn), "the same twenty kelvin over a winter sea smokes harder");
		}

		[Test]
		public void WaterNoWarmerThanTheAir_NeverSmokes()
		{
			Assert.That(SteamPhysics.SteamFogExcess(5f, 5f, 5f, false), Is.LessThan(0f));
			Assert.That(SteamPhysics.SteamFogExcess(2f, 10f, 9f, true), Is.LessThan(0f));
			Assert.That(SteamPhysics.SteamFogReadiness(SteamPhysics.SteamFogExcess(2f, 10f, 9f, true), 0f), Is.EqualTo(0f));
		}

		[Test]
		public void SaltWater_SmokesALittleLess()
		{
			float dew = Dew(0f, 0.8f);
			Assert.That(SteamPhysics.SteamFogExcess(15f, 0f, dew, true), Is.LessThan(SteamPhysics.SteamFogExcess(15f, 0f, dew, false)));
		}

		[Test]
		public void Readiness_GrowsWithTheExcess_AndAGaleHalvesIt()
		{
			Assert.That(SteamPhysics.SteamFogReadiness(0f, 0f), Is.EqualTo(0f));
			float some = SteamPhysics.SteamFogReadiness(0.6f, 0f);
			float full = SteamPhysics.SteamFogReadiness(SteamPhysics.FullExcess, 0f);
			Assert.That(some, Is.GreaterThan(0f).And.LessThan(full));
			Assert.That(full, Is.EqualTo(1f).Within(1e-4f));
			Assert.That(SteamPhysics.SteamFogReadiness(SteamPhysics.FullExcess, 20f), Is.EqualTo(0.5f).Within(1e-3f), "a gale leaves streaks");
		}

		[Test]
		public void TheSteam_StandsDeeperTheWarmerTheWater_AndLowerInAWind()
		{
			float shallow = SteamPhysics.SteamFogDepth(0.3f, 0f);
			float deep = SteamPhysics.SteamFogDepth(5f, 0f);
			Assert.That(deep, Is.GreaterThan(shallow * 3f));
			Assert.That(SteamPhysics.SteamFogDepth(5f, 12f), Is.LessThan(deep * 0.5f));
			Assert.That(SteamPhysics.SteamFogDepth(0f, 0f), Is.EqualTo(0f));
		}

		[Test]
		public void TheSteam_LeansFurtherInAStrongerWind()
		{
			Assert.That(SteamPhysics.SteamLean(0f, 15f), Is.EqualTo(0f));
			Assert.That(SteamPhysics.SteamLean(6f, 15f), Is.GreaterThan(SteamPhysics.SteamLean(2f, 15f)));
			Assert.That(SteamPhysics.SteamLean(40f, 15f), Is.LessThanOrEqualTo(6f));
		}

		[Test]
		public void AFumarole_IsAWispInSummer_AndAColumnInWinter()
		{
			float summer = SteamPhysics.VisibleLength(1f, 93f, 25f, Dew(25f, 0.4f));
			float winter = SteamPhysics.VisibleLength(1f, 93f, -20f, Dew(-20f, 0.8f));
			Assert.That(summer, Is.InRange(3f, 20f), "a few metres on a dry afternoon");
			Assert.That(winter, Is.InRange(80f, SteamPhysics.MaxVisibleMetres), "a hundred metres and more in the cold");
			Assert.That(winter, Is.GreaterThan(5f * summer));
		}

		[Test]
		public void APlume_IsVisibleLongerInHumidAir()
		{
			float dry = SteamPhysics.VisibleLength(1f, 93f, 10f, Dew(10f, 0.3f));
			float humid = SteamPhysics.VisibleLength(1f, 93f, 10f, Dew(10f, 0.95f));
			Assert.That(humid, Is.GreaterThan(dry));
		}

		[Test]
		public void APlume_InSaturatedAir_NeverClears()
		{
			Assert.That(SteamPhysics.VisibleDilution(93f, SteamPhysics.SaturationPressure(93f), 5f, SteamPhysics.SaturationPressure(5f)), Is.LessThanOrEqualTo(1e-6f));
			Assert.That(SteamPhysics.VisibleLength(1f, 93f, 5f, 5f), Is.EqualTo(SteamPhysics.MaxVisibleMetres));
		}

		[Test]
		public void NoSource_NoPlume()
		{
			Assert.That(SteamPhysics.VisibleLength(0f, 0.5f), Is.EqualTo(0f));
			Assert.That(SteamPhysics.VisibleLength(1f, 1f), Is.EqualTo(0f));
		}

		[Test]
		public void WaterBoils_NearAHundred_AndLowerUpAMountain()
		{
			float sea = SteamPhysics.BoilingC(101325f);
			float high = SteamPhysics.BoilingC(70000f);
			// The weather's curve holds the latent heat constant: 368 K at an atmosphere against a true 373.
			Assert.That(sea, Is.InRange(92f, 101f));
			Assert.That(high, Is.LessThan(sea - 5f));
		}
	}
}
