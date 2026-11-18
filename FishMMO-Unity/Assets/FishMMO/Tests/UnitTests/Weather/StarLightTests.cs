using FishMMO.Shared.Celestial;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// A star's light is the colour of its black body, seen by an eye adapted to it (2026-10-15: "We want realistic/accurate
	/// lighting for our celestial bodies"; Galris's snow read peach under Soluris, its temperature applied twice).
	/// </summary>
	public class StarLightTests
	{
		[Test]
		public void ABlackBodyIsWhiteAt6500KWarmBelowAndBlueAbove()
		{
			Color d65 = StarBody.TemperatureColor(6500f);
			Assert.Greater(Mathf.Min(d65.r, Mathf.Min(d65.g, d65.b)), 0.95f, $"6500 K is white, got {d65}");
			Color sun = StarBody.TemperatureColor(StarBody.ReferenceTemperatureK);
			Assert.That(sun.r, Is.EqualTo(1f).Within(1e-4f));
			Assert.That(sun.b, Is.InRange(0.85f, 0.97f), $"our sun is faintly warm, got {sun}");
			Color candle = StarBody.TemperatureColor(2000f);
			Assert.Greater(candle.r, candle.b + 0.6f, $"2000 K is deep orange, got {candle}");
			Color blue = StarBody.TemperatureColor(20000f);
			Assert.Greater(blue.b, blue.r + 0.25f, $"20000 K is blue, got {blue}");
		}

		[Test]
		public void TheEyeAdaptsMostToLightLikeOurSun()
		{
			Assert.That(StarBody.Adaptation(StarBody.ReferenceTemperatureK), Is.EqualTo(0.95f).Within(1e-4f));
			Assert.Greater(StarBody.Adaptation(4975f), StarBody.Adaptation(3000f));
			Assert.Greater(StarBody.Adaptation(3000f), StarBody.Adaptation(2000f));
			Assert.Less(StarBody.Adaptation(12000f), StarBody.Adaptation(7000f));
			Assert.GreaterOrEqual(StarBody.Adaptation(1000f), 0.5f, "never a camera's white balance alone");
		}

		[Test]
		public void SnowUnderAKStarIsWhite()
		{
			// Soluris: 4975 K. Its raw light against our sun's is (1.05, 0.99, 0.92); adapted, it is all but white.
			Color tint = StarBody.TemperatureTint(4975f);
			Assert.Less(Mathf.Abs(tint.r - 1f), 0.02f, $"got {tint}");
			Assert.Less(Mathf.Abs(tint.g - 1f), 0.02f, $"got {tint}");
			Assert.Less(Mathf.Abs(tint.b - 1f), 0.02f, $"got {tint}");
			Assert.Greater(tint.r, tint.b, "and still a shade warmer than our sun's");
		}

		[Test]
		public void ARedDwarfStillLooksWarmAndABlueStarCool()
		{
			Color red = StarBody.TemperatureTint(3000f), blue = StarBody.TemperatureTint(12000f);
			Assert.Greater(red.r, red.b * 1.15f, $"lamplight warm, got {red}");
			Assert.Less(red.r / red.b, 1.6f, $"but not the camera's orange, got {red}");
			Assert.Greater(blue.b, blue.r * 1.03f, $"a cool cast, got {blue}");
			Assert.Less(blue.b / blue.r, 1.3f, $"but not the camera's blue, got {blue}");
		}

		[Test]
		public void TheReferenceSunTintsNothing()
		{
			Color tint = StarBody.TemperatureTint(StarBody.ReferenceTemperatureK);
			Assert.That(tint.r, Is.EqualTo(1f).Within(1e-4f));
			Assert.That(tint.g, Is.EqualTo(1f).Within(1e-4f));
			Assert.That(tint.b, Is.EqualTo(1f).Within(1e-4f));
		}
	}
}
