using NUnit.Framework;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The wind with height: slowed by the ground, free above the boundary layer, strongest at the
	/// jet under the tropopause, dying away above it.
	/// </summary>
	[TestFixture]
	public class WindProfileTests
	{
		private static WindProfile Midlatitude(float surface = 6f)
		{
			return WindProfile.Climatological(PlanetAir.Earthlike, 45f, WindBelts.Earthlike, surface, 1000f, 11000f);
		}

		[Test]
		public void TheWindRisesAsTheLogarithmOfHeightNearTheGround()
		{
			WindProfile wind = Midlatitude();
			Assert.That(wind.At(10f), Is.EqualTo(6f).Within(0.3f), "the ten-metre wind is the surface wind");
			// The law of the wall: every doubling of height adds the same speed.
			float a = wind.At(40f) - wind.At(20f);
			float b = wind.At(160f) - wind.At(80f);
			Assert.That(b, Is.EqualTo(a).Within(0.25f * a), $"each doubling adds alike: {a:0.00} and {b:0.00} m/s");
			Assert.That(wind.At(1000f), Is.InRange(9f, 13f), "the free wind above the mixed layer, nearly twice the ten-metre wind");
		}

		[Test]
		public void TheWindGrowsThroughTheTroposphereToAJet()
		{
			WindProfile wind = Midlatitude();
			// Earth at 45°: a thermal wind of a few metres a second per kilometre, a jet of thirty to sixty.
			Assert.That(wind.Shear * 1000f, Is.InRange(1.5f, 5f), $"shear {wind.Shear * 1000f:0.00} (m/s)/km");
			Assert.That(wind.Jet, Is.InRange(30f, 60f), $"the jet {wind.Jet:0} m/s");
			float last = 0f;
			for (float z = 10f; z <= 11000f; z += 500f)
			{
				float here = wind.At(z);
				LogAssert.IsTrue(here >= last - 1e-3f, $"never slackening on the way up: {here:0.0} at {z:0} m after {last:0.0}");
				last = here;
			}
			LogAssert.IsTrue(wind.At(16000f) < 0.7f * wind.Jet, "and falling away above the tropopause");
		}

		[Test]
		public void TheJetRunsWhereTheBeltsSayAndWeakensOnAFasterWorld()
		{
			WindProfile jet = WindProfile.Climatological(PlanetAir.Earthlike, 45f, WindBelts.Earthlike, 6f, 1000f, 11000f);
			WindProfile tropics = WindProfile.Climatological(PlanetAir.Earthlike, 10f, WindBelts.Earthlike, 6f, 1000f, 11000f);
			LogAssert.IsTrue(tropics.Shear < jet.Shear, $"weak aloft in the tropics: {tropics.Shear * 1000f:0.00} vs {jet.Shear * 1000f:0.00}");

			PlanetAir fast = PlanetAir.Earthlike;
			fast.RotationRate *= 3f;
			WindProfile spun = WindProfile.Climatological(fast, 45f, WindBelts.Earthlike, 6f, 1000f, 11000f);
			LogAssert.IsTrue(spun.Shear < jet.Shear, "a faster spin balances the same gradient with a weaker wind");

			PlanetAir none = PlanetAir.Earthlike;
			none.HasAir = false;
			Assert.That(WindProfile.ThermalShear(none, 45f, WindBelts.Earthlike, 288f), Is.EqualTo(0f), "no air, no thermal wind");
		}

		[Test]
		public void TheBandsDriftTheSameWhateverTheWeather()
		{
			// The climatological profile is the world's and the latitude's only: a gale at the surface
			// today must not speed up the cloud bands tomorrow's sky drifts by.
			WindProfile calm = Midlatitude(6f);
			WindProfile again = Midlatitude(6f);
			Assert.That(again.At(6000f), Is.EqualTo(calm.At(6000f)));
		}
	}
}
