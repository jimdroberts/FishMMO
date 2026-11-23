using System.IO;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>Split from SurfaceShaderTests: the World Simulation module's half.</summary>
	public class WeatherSurfaceShaderTests
	{
		[Test]
		public void TheCoverMap_IsBuiltAndPublishedForTheSurfaces()
		{
			string map = Read("Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Presentation/WeatherCoverMap.cs");
			StringAssert.Contains("_FishCoverTex", map, "the surfaces sample this texture by name");
			StringAssert.Contains("GroundCover.Advance(", map, "the map must use the same cover integration the server runs");
			StringAssert.Contains("GroundCover.ForcingAt(", map, "each texel is worked out from the world's time as the server works it out, or the client drifts");

			string presenter = Read("Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Presentation/WeatherPresentation.cs");
			StringAssert.Contains("coverMap.Update", presenter, "nothing builds the cover map, so the ground shows the scene average everywhere");

			string weather = Read(Shaders + "FishWeather.hlsl");
			StringAssert.Contains("FishCoverAt", weather, "the lookup the surfaces use is gone");
			StringAssert.Contains("_FishWeatherCover", weather, "the scene-wide figure must remain as the fallback outside the map");
		}

		[Test]
		public void TheDayNightFade_DissolvesOpaqueWeatherMaterials()
		{
			string source = Read(DayNight);
			StringAssert.Contains("_FishVisible", source, "the fade no longer drives the dissolve");
			StringAssert.Contains("SwitchPointOf", source, "the staggered switch is still the fallback for materials that cannot dissolve");
		}

		[Test]
		public void TheMigration_LeavesCharactersAndPluginsAlone()
		{
			string source = Read(Migration);
			StringAssert.Contains("\"/Models/\"", source, "character models must keep their own shaders");
			StringAssert.Contains("\"/Plugins/\"", source, "third-party content must keep its own shaders");
			StringAssert.Contains("Universal Render Pipeline/Lit", source, "the migration only moves materials off URP's Lit");
		}

		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";
		private const string Library = Shaders + "FishSurface.hlsl";
		private const string LitShader = Shaders + "FishWeatherLit.shader";
		private const string LitPass = Shaders + "FishWeatherLitForwardPass.hlsl";
		private const string LitInput = Shaders + "FishWeatherLitInput.hlsl";
		private const string TerrainShader = Shaders + "FishWeatherTerrain.shader";
		private const string TerrainPasses = Shaders + "FishWeatherTerrainPasses.hlsl";
		private const string TerrainInput = Shaders + "FishWeatherTerrainInput.hlsl";
		private const string DayNight = "Assets/FishMMO/Modules/WorldSimulation/Runtime/SceneData/WorldDayNightCycle.cs";
		private const string Migration = "Assets/FishMMO/Modules/WorldSimulation/Editor/Weather/WeatherSurfaceMigration.cs";

		private static string Read(string path)
		{
			Assert.IsTrue(ProjectFiles.Exists(path), $"{path} is missing. The weather surfaces need it.");
			return ProjectFiles.ReadAllText(path);
		}
	}
}
