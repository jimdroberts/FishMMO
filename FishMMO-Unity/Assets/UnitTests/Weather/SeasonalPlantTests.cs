using System.Globalization;
using System.Text.RegularExpressions;
using FishMMO.Shared.Weather;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Plants read their season at their own height: the shaders work it out from the season's air (FishWeather.hlsl
	/// FishSeasonGrowing, FishSeasonWinter) with SceneClimate's own arithmetic, which these hold the two to.
	/// </summary>
	public class SeasonalPlantTests
	{
		private const string Weather = "Assets/Prefabs/Client/Weather/Shaders/FishWeather.hlsl";

		[Test]
		public void Growing_RunsFromDormantAtOneDegree_ToFullAtFifteen()
		{
			LogAssert.AreEqual(0f, SceneClimate.GrowingAt(SceneClimate.GrowthC), "the growth threshold is neither growing nor dormant");
			LogAssert.AreEqual(-1f, SceneClimate.GrowingAt(SceneClimate.DormantC), "wholly dormant at the dormancy point");
			LogAssert.AreEqual(-1f, SceneClimate.GrowingAt(-20f), "and colder");
			LogAssert.AreEqual(1f, SceneClimate.GrowingAt(SceneClimate.GrowthC + 10f), "growing fully ten degrees above the threshold");
			LogAssert.IsTrue(SceneClimate.GrowingAt(3f) < SceneClimate.GrowingAt(4f), "warmer is never more dormant");
		}

		[Test]
		public void Winter_StripsAtFiveDegrees_AndSparesBroadleavesAtTwelve()
		{
			LogAssert.AreEqual(1f, SceneClimate.WinterAt(SceneClimate.StripC), "a coldest month at the strip point strips");
			LogAssert.AreEqual(0f, SceneClimate.WinterAt(SceneClimate.LeafyC), "a coldest month at the leafy point keeps the leaves");
		}

		[Test]
		public void TheAir_CoolsUpward_AndNotBelowTheSea()
		{
			var state = new SceneClimate.FoliageState { Celsius = 8f, CoolingPerMetre = 0.0065f };
			LogAssert.IsTrue(System.Math.Abs(state.CelsiusAt(1000f) - 1.5f) < 1e-4f, $"a kilometre up is 6.5 K colder: {state.CelsiusAt(1000f)}");
			LogAssert.AreEqual(8f, state.CelsiusAt(-30f), "below the sea (a cave, a hollow) is the sea's");
		}

		[Test]
		public void TheShader_HoldsSceneClimatesThresholds()
		{
			string source = SourceScanPins.ReadSource(Weather);
			LogAssert.AreEqual(SceneClimate.GrowthC, Define(source, "FISH_GROWTH_C"), "FISH_GROWTH_C");
			LogAssert.AreEqual(SceneClimate.DormantC, Define(source, "FISH_DORMANT_C"), "FISH_DORMANT_C");
			LogAssert.AreEqual(SceneClimate.StripC, Define(source, "FISH_STRIP_C"), "FISH_STRIP_C");
			LogAssert.AreEqual(SceneClimate.LeafyC, Define(source, "FISH_LEAFY_C"), "FISH_LEAFY_C");
			LogAssert.IsTrue(source.Contains("(t - FISH_GROWTH_C) / 10.0") && source.Contains("(t - FISH_GROWTH_C) / (FISH_GROWTH_C - FISH_DORMANT_C)"),
				"FishSeasonGrowing keeps SceneClimate.GrowingAt's two ramps");
		}

		private static float Define(string source, string name)
		{
			Match m = Regex.Match(source, @"#define\s+" + name + @"\s+([-0-9.]+)");
			LogAssert.IsTrue(m.Success, $"{name} is not defined in {Weather}");
			return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
		}
	}
}
