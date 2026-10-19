using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The seasonal snow line: one shape in C# (<see cref="SnowLineShape"/>, what footprints and the server read) and in
	/// HLSL (FishSnowLine.hlsl, what every surface and the grass draw), a crisp line that wanders, and a pack that deepens
	/// with height above it.
	/// </summary>
	[TestFixture]
	public class SnowLineTests
	{
		private const string Include = "Assets/Prefabs/Client/Weather/Shaders/FishSnowLine.hlsl";

		private static float Define(string source, string name)
		{
			Match match = Regex.Match(source, @"#define\s+" + name + @"\s+([0-9.]+)");
			Assert.IsTrue(match.Success, $"{name} is gone from {Include}");
			return float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
		}

		[Test]
		public void TheShaderAndTheCSharp_ShareTheirConstantsAndHash()
		{
			Assert.IsTrue(File.Exists(Include), $"{Include} is missing");
			string hlsl = File.ReadAllText(Include);
			Assert.AreEqual(SnowLineShape.SpanKelvin, Define(hlsl, "FISH_PACK_SPAN_K"), 1e-6f, "the span the published line stands for");
			Assert.AreEqual(SnowLineShape.WanderKelvin, Define(hlsl, "FISH_SNOW_WANDER_K"), 1e-6f, "the wander");
			foreach (string constant in new[] { "0x8DA6B343u", "0xD8163841u", "0x2C1B3C6Du", "0x297A2D39u", "0xFFFFFFu" })
			{
				Assert.IsTrue(hlsl.Contains(constant), $"the hash's {constant} is gone from the shader: the two would wander apart");
			}
			Assert.IsTrue(hlsl.Contains("xz / 240.0") && hlsl.Contains("xz / 80.0 + 31.7"), "the wander's scales");
			Assert.AreEqual(240f, SnowLineShape.WanderCoarseMetres);
			Assert.AreEqual(80f, SnowLineShape.WanderFineMetres);
		}

		[Test]
		public void TheSpan_IsThePacksOwnWarmToCold()
		{
			float span = (GroundCover.PackWarm - GroundCover.PackCold) * (float)ClimateModel.KelvinPerUnit;
			Assert.AreEqual(SnowLineShape.SpanKelvin, span, 1e-4f);
		}

		[Test]
		public void TheLine_IsBareBelowWholeAboveAndEasedBetween()
		{
			const float Start = 900f, Whole = 930f, Share = 0.8f;
			Assert.AreEqual(0f, SnowLineShape.CoverAt(Start - 1f, 0f, Start, Whole, Share));
			Assert.AreEqual(Share, SnowLineShape.CoverAt(Whole + 1f, 0f, Start, Whole, Share), 1e-6f);
			Assert.AreEqual(Share * 0.5f, SnowLineShape.CoverAt((Start + Whole) * 0.5f, 0f, Start, Whole, Share), 1e-5f);
			// Lifted, the whole line moves with it.
			Assert.AreEqual(0f, SnowLineShape.CoverAt(Whole + 1f, 40f, Start, Whole, Share), "lifted 40 m, the line stands above this");
		}

		[Test]
		public void ThePack_DeepensWithHeightAboveTheLine()
		{
			const float Whole = 930f, Deep = 1300f, Share = 1f, Metres = 1f;
			Assert.AreEqual(0f, SnowLineShape.DepthAt(Whole, 0f, Whole, Deep, Share, Metres), "a dusting at the line");
			float previous = 0f;
			for (float y = Whole + 50f; y <= Deep; y += 50f)
			{
				float depth = SnowLineShape.DepthAt(y, 0f, Whole, Deep, Share, Metres);
				Assert.Greater(depth, previous, $"deeper at {y} than below it");
				previous = depth;
			}
			Assert.AreEqual(Metres, SnowLineShape.DepthAt(Deep + 200f, 0f, Whole, Deep, Share, Metres), 1e-6f, "deepest on the summits");
		}

		[Test]
		public void TheWander_StaysInRangeAndIsAPureFunctionOfThePlace()
		{
			float least = 1f, most = -1f;
			for (int i = 0; i < 4000; i++)
			{
				float x = (i % 64) * 37.3f - 1200f, z = (i / 64) * 41.9f - 1300f;
				float w = SnowLineShape.Wander(x, z);
				Assert.AreEqual(w, SnowLineShape.Wander(x, z), "the same place, the same wander");
				least = Mathf.Min(least, w);
				most = Mathf.Max(most, w);
			}
			Assert.GreaterOrEqual(least, -1f);
			Assert.LessOrEqual(most, 1f);
			Assert.Greater(most - least, 0.8f, "it wanders: a line ruled across the hills reads as a contour");
		}

		[Test]
		public void TheLift_IsTheClimatesKelvinAtTheLinesMetresPerKelvin()
		{
			const float Start = 900f, Whole = 935f;
			float perKelvin = (Whole - Start) / SnowLineShape.SpanKelvin;
			float wander = SnowLineShape.Wander(123f, 456f) * SnowLineShape.WanderKelvin;
			Assert.AreEqual((2f + wander) * perKelvin, SnowLineShape.LiftAt(123f, 456f, 2f, Start, Whole), 1e-3f,
				"a place 2 K warmer at sea level carries its line 2 K of lapse higher");
		}

		[Test]
		public void TheSnowLine_StartsThenIsWholeThenLiesDeepest()
		{
			var forcing = new GroundCover.Forcing
			{
				PackTemperature = 0.1f,
				PackMiddle = 0.1f,
				PackSeaLevel = 0.3f,
				PackShare = 1f,
				// 6.5 K/km over a vertical scale of 0.3, on the climate scale per world metre.
				PackLapse = (float)(0.0065 / 0.3 / ClimateModel.KelvinPerUnit),
			};
			Assert.IsTrue(GroundCover.SnowLine(forcing, out float start, out float whole, out float deep, out float share));
			Assert.Less(start, whole);
			Assert.Less(whole, deep);
			Assert.AreEqual(1f, share);
			Assert.AreEqual(SnowLineShape.SpanKelvin / 0.0065f * 0.3f, whole - start, 0.05f, "the span in world metres");
			Assert.AreEqual(GroundCover.PackDeepKelvin / 0.0065f * 0.3f, deep - whole, 0.5f, "the deepening in world metres");
		}
	}
}
