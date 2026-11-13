using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The ground is drawn after the other opaques (TerrainHeightmapSystem.DrawnLastQueue): the same picture, measured
	/// cheaper in the browser (2026-11-05). Pinned so a refactor that drops the queue (back to the shader's Geometry−100)
	/// shows here rather than as a lost millisecond.
	/// </summary>
	public class GroundDrawOrderTests
	{
		[Test]
		public void TheGround_IsDrawnAfterTheOtherOpaques_ByDefault()
		{
			string system = ProjectFiles.ReadAllText("Assets/Scripts/Client/World/Terrain/Heightmap/TerrainHeightmapSystem.cs");
			LogAssert.IsTrue(system.Contains("public const int DrawnLastQueue = 2450;") && system.Contains("public static int QueueOverride = DrawnLastQueue;"),
				"the ground's queue defaults to after the opaques (2450, still opaque: under 2500)");
			string field = ProjectFiles.ReadAllText("Assets/Scripts/Client/World/Terrain/Heightmap/TerrainHeightmapField.cs");
			LogAssert.IsTrue(field.Contains("solid.renderQueue = holed.renderQueue = TerrainHeightmapSystem.QueueOverride;"), "both ground materials take it");
		}

		/// <summary>The owner's picks from the same A/B: the strongest 3 layers a pixel, layer detail faded out 40–80 m.</summary>
		[Test]
		public void TheGround_BlendsThreeLayers_AndFadesItsDetailFrom40To80Metres_ByDefault()
		{
			string input = ProjectFiles.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishWeatherTerrainArrayInput.hlsl");
			LogAssert.IsTrue(input.Contains("#define FISH_TERRAIN_DEFAULT_LAYERS 3.0"), "three layers unless a global says otherwise");
			LogAssert.IsTrue(input.Contains("#define FISH_TERRAIN_DETAIL_FAR float2(40.0, 80.0)"), "detail faded 40 to 80 m unless a global says otherwise");
			LogAssert.IsTrue(input.Contains("_FishTerrainMaxLayers > 0.5 ? _FishTerrainMaxLayers : FISH_TERRAIN_DEFAULT_LAYERS")
				&& input.Contains("_FishTerrainDetailFar.y != 0.0 ? _FishTerrainDetailFar.xy : FISH_TERRAIN_DETAIL_FAR"), "an unset global (0) means the default");
		}
	}
}
