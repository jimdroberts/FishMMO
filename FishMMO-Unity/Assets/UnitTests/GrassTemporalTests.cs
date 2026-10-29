using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The blade grass under a temporal resolve (STP at 0.67 on High Fidelity, 2026-10-27): the blades write their own
	/// motion, rebuilt a frame back on the shared motion clock with the forward pass's own wind function, so the upscaler
	/// keeps its history where the tips sway; a sliver under a RENDERED pixel is drawn a pixel wide with its true width as
	/// a dither that turns every frame; the cylinder rounding is shown only where a blade is wide enough to show it.
	/// </summary>
	public class GrassTemporalTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";
		private const string RendererPath = "Assets/Scripts/Client/World/Terrain/Grass/GrassBladeRenderer.cs";

		private static string Read(string path)
		{
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		[Test]
		public void MotionPass_CompilesWithDxc_AndRebuildsTheBladeAFrameBackOnTheSharedClock()
		{
			string pass = Read(Shaders + "FishGrassBlades.shader");
			string blades = Read(Shaders + "FishGrassBlades.hlsl");
			LogAssert.IsTrue(pass.Contains("Tags { \"LightMode\" = \"MotionVectors\" }"), "the shader has URP's motion vectors pass");
			LogAssert.IsTrue(pass.Contains("#pragma vertex GrassMotionVertex") && pass.Contains("#define FISH_GRASS_PASS_MOTION"), "drawn by the blades' own motion vertex shader");
			LogAssert.AreEqual(Regex.Matches(pass, "HLSLPROGRAM").Count, Regex.Matches(pass, @"#pragma use_dxc vulkan").Count, "every pass compiles with DXC");
			LogAssert.IsTrue(blades.Contains("before = GrassBuildAt(input.instanceID, input.positionOS.xy, _GrassTemporal.y).positionWS;"), "the previous position is the same blade function a frame back");
			LogAssert.IsTrue(blades.Contains("o.positionBefore = mul(_PrevViewProjMatrix, float4(before, 1.0));"), "seen through the frame before's camera");
			LogAssert.IsTrue(blades.Contains("o.positionNow = mul(_NonJitteredViewProjMatrix, float4(v.positionWS, 1.0));"), "against the unjittered camera now");
			// Every clock the wind reads is the shared one, moved back by the same seconds: nothing else may drive the sway.
			LogAssert.IsTrue(blades.Contains("float time = _FishWeatherMisc.w - back;"), "the flutter/swell clock");
			LogAssert.IsTrue(blades.Contains(", lambda, _FishWindHold.x - back);"), "the gust bands' travel, held per window");
			LogAssert.IsFalse(Regex.IsMatch(blades, @"FishWindTravel\("), "no travel left on the window's present moment");
			LogAssert.IsFalse(Regex.IsMatch(blades, @"\b_Time\b|_TimeParameters"), "no engine clock: every player's grass moves on the shared one");
			string weather = Read(Shaders + "FishWeather.hlsl");
			LogAssert.IsTrue(weather.Contains("return FishWindTravelAt(rate, period, _FishWindHold.x);"), "FishWindTravel is FishWindTravelAt now, one formula");
		}

		[Test]
		public void Renderer_GivesTheNearSlotsPerObjectMotion_AndMeasuresRenderedPixels()
		{
			string renderer = Read(RendererPath);
			LogAssert.IsTrue(renderer.Contains("motionVectorMode = !shadowSlot && SlotLod[slot] < motionLevels ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera,"),
				"per-object motion (URP then draws the MotionVectors pass) for the camera slots within MotionVectorLevels");
			LogAssert.IsTrue(renderer.Contains("motionLevels = temporal ? Mathf.Clamp(s.MotionVectorLevels, 0, 3) : 0;"), "only when the camera resolves temporally");
			LogAssert.IsTrue(renderer.Contains("float pixelsHigh = Mathf.Max(1f, camera.pixelHeight * renderScale);"), "a pixel is a rendered pixel under a render scale");
			LogAssert.IsTrue(renderer.Contains("float bladePixels = Mathf.Max(0.1f, s.MaxBladePixels) * renderScale;"), "MaxBladePixels keeps counting screen pixels");
			LogAssert.IsTrue(renderer.Contains("step += (float)FishMMO.Shared.Celestial.WorldMotion.ShaderWrapSeconds;"), "a clock step across the shader wrap is still a step");
		}

		[Test]
		public void Slivers_TakeCoverage_AndTheRoundingFadesUnderTwoPixels()
		{
			string blades = Read(Shaders + "FishGrassBlades.hlsl");
			LogAssert.IsTrue(blades.Contains("if (_GrassScreen.w > 0.5 && pixelMetres > 1e-5 && acrossPixels < 1.0 && !isHead)"), "row by row, MSAA or temporal, never a head's cards");
			LogAssert.IsTrue(blades.Contains("o.fade.x *= saturate(acrossPixels);"), "the true width is coverage");
			LogAssert.IsTrue(blades.Contains("o.side = isHead ? u * 0.3 : u * saturate(acrossPixels - 1.0);"), "the rounding fades from two rendered pixels to one");
			LogAssert.IsTrue(blades.Contains("if (abs(_GrassScreen.w - 1.0) < 0.5)"), "only MSAA takes coverage as alpha to coverage");
			LogAssert.IsTrue(blades.Contains("return frac(n + _GrassTemporal.x * 0.61803399 + shift * 0.38196601);"), "the temporal dither turns every frame");
		}
	}
}
