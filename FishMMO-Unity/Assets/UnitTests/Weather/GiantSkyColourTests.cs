using FishMMO.Shared.Celestial;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// A gas giant's scenes are lit in the giant's own colour (2026-10-14: "Adjust scenes to fit their atmosphere
	/// colorization properly"): AtmosphereModel.EvaluateVeiled. Before it, every giant's habitable layer was lit as one
	/// thick air, which reddened the light of a cyan giant and a tan one alike to the same sepia.
	/// </summary>
	public class GiantSkyColourTests
	{
		private static readonly Color Cyan = new Color(0.54f, 0.86f, 0.94f);
		private static readonly Color Tan = new Color(0.86f, 0.72f, 0.52f);

		[Test]
		public void ACyanGiantsLayerIsLitCyan()
		{
			SkySample noon = AtmosphereModel.EvaluateVeiled(Cyan, 1f, 60f);
			Assert.Greater(noon.Horizon.b, noon.Horizon.r, "horizon");
			Assert.Greater(noon.SunLight.b, noon.SunLight.r, "sunlight");
			Assert.Greater(noon.Fog.b, noon.Fog.r, "fog");
			Assert.Greater(noon.CloudLit.b, noon.CloudLit.r, "the clouds' lit side");
		}

		[Test]
		public void ATanGiantsLayerIsLitWarm()
		{
			SkySample noon = AtmosphereModel.EvaluateVeiled(Tan, 1f, 60f);
			Assert.Greater(noon.Horizon.r, noon.Horizon.b, "horizon");
			Assert.Greater(noon.SunLight.r, noon.SunLight.b, "sunlight");
			Assert.Greater(noon.Zenith.r, noon.Zenith.b * 0.9f, "overhead is the veil's warm underside, not a blue sky");
		}

		[Test]
		public void TwoGiantsOfDifferentColoursHaveDifferentSkies()
		{
			// The thick-air sky they had gave both the same horizon to within a hundredth.
			SkySample cyan = AtmosphereModel.EvaluateVeiled(Cyan, 1f, 30f), tan = AtmosphereModel.EvaluateVeiled(Tan, 1f, 30f);
			float apart = Mathf.Abs(cyan.Horizon.r - tan.Horizon.r) + Mathf.Abs(cyan.Horizon.g - tan.Horizon.g) + Mathf.Abs(cyan.Horizon.b - tan.Horizon.b);
			Assert.Greater(apart, 0.3f);
		}

		[Test]
		public void TheVeilDimsTheSunButLeavesDay()
		{
			SkySample veiled = AtmosphereModel.EvaluateVeiled(Color.white, 1f, 60f);
			SkySample open = AtmosphereModel.Evaluate(AtmosphereKind.Standard, 1f, Color.white, 60f);
			Assert.Less(veiled.SunIntensity, open.SunIntensity);
			Assert.Greater(veiled.SunIntensity, open.SunIntensity * 0.6f);
			Assert.That(veiled.StarVisibility, Is.LessThan(0.1f), "no stars at noon");
		}

		[Test]
		public void NightUnderTheVeilIsDark()
		{
			SkySample night = AtmosphereModel.EvaluateVeiled(Cyan, 1f, -30f);
			Assert.Less(night.Horizon.maxColorComponent, 0.08f);
			Assert.AreEqual(0f, night.SunIntensity, 1e-4f);
		}
	}
}
