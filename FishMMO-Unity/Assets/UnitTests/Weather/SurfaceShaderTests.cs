using System.IO;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The weather surface shaders are copies of URP's own, with a few edits. Nothing stops a URP
	/// upgrade, or a well-meaning cleanup, from replacing a copy with the stock file again — and
	/// the loss would be silent: the world would simply stop getting wet. These tests are what
	/// notices.
	/// </summary>
	public class SurfaceShaderTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";
		private const string Library = Shaders + "FishSurface.hlsl";
		private const string LitShader = Shaders + "FishWeatherLit.shader";
		private const string LitPass = Shaders + "FishWeatherLitForwardPass.hlsl";
		private const string LitInput = Shaders + "FishWeatherLitInput.hlsl";
		private const string TerrainShader = Shaders + "FishWeatherTerrain.shader";
		private const string TerrainPasses = Shaders + "FishWeatherTerrainPasses.hlsl";
		private const string TerrainInput = Shaders + "FishWeatherTerrainInput.hlsl";
		private const string DayNight = "Assets/Scripts/Shared/Implementation/Entity/WorldSceneDetails/WorldDayNightCycle.cs";
		private const string Migration = "Assets/Scripts/Client/World/Weather/Editor/WeatherSurfaceMigration.cs";

		private static string Read(string path)
		{
			Assert.IsTrue(File.Exists(path), $"{path} is missing. The weather surfaces need it.");
			return File.ReadAllText(path);
		}

		[Test]
		public void TheSurfaceLibrary_DoesWetSnowAndTheDissolve()
		{
			string library = Read(Library);
			Assert.IsTrue(library.Contains("FishWeatherSurface"), "the one entry point the shaders call is gone");
			// Surfaces read the cover *map*, not the scene-wide figure: a storm that passed over one
			// field must leave that field white and the next one bare.
			Assert.IsTrue(library.Contains("FishCoverAt"), "the surfaces must read the cover map");
			// The surfaces read the occlusion map themselves rather than through FishSkyOpen, because
			// a surface needs a softer answer than a raindrop does — but read it they must, or snow
			// settles under roofs.
			Assert.IsTrue(library.Contains("FishSkyOcclusionHeight"), "cover must stop at a roof, which is what the occlusion map is for");
			Assert.IsTrue(library.Contains("FishSurfaceExposure"), "the one place that decides whether the sky reaches a surface is gone");
			Assert.IsTrue(library.Contains("FishDitherClip"), "the day/night dissolve lives here");
		}

		[Test]
		public void BothForks_CallTheSurfaceLibrary()
		{
			foreach (string path in new[] { LitPass, TerrainPasses })
			{
				string pass = Read(path);
				StringAssert.Contains("FishSurface.hlsl", pass, $"{path} no longer includes the surface library");
				StringAssert.Contains("FishWeatherSurface(", pass, $"{path} no longer applies the weather: it is probably the stock URP file again");
				StringAssert.Contains("FishMMO edit", pass, $"{path} has lost its edit marker, so a URP upgrade may have overwritten it");
			}
		}

		[Test]
		public void TheForkedShaders_CarryTheWeatherProperties()
		{
			string lit = Read(LitShader);
			StringAssert.Contains("Shader \"FishMMO/Weather Lit\"", lit);
			StringAssert.Contains("_FishWeatherAmount", lit, "a material must be able to opt out of the weather");
			StringAssert.Contains("_FishVisible", lit, "the day/night dissolve is driven through this property");
			StringAssert.Contains("FishWeatherLitForwardPass.hlsl", lit, "the forward pass points back at URP's own, so nothing is applied");
			// URP's inspector is what sets a material's keywords; without it, assigning a normal map
			// silently does nothing.
			StringAssert.Contains("CustomEditor", lit, "the fork must keep URP's material inspector");

			string terrain = Read(TerrainShader);
			StringAssert.Contains("Shader \"FishMMO/Weather Terrain\"", terrain);
			StringAssert.Contains("_FishSnowDepth", terrain, "deep snow lifts the terrain, on High only");
			StringAssert.Contains("FishWeatherTerrainPasses.hlsl", terrain);
		}

		[Test]
		public void TheForkedInputs_KeepTheWeatherFieldsInTheMaterialBuffer()
		{
			// Outside UnityPerMaterial the SRP batcher drops every material on the shader.
			foreach (string path in new[] { LitInput, TerrainInput })
			{
				string input = Read(path);
				int start = input.IndexOf("CBUFFER_START(UnityPerMaterial)");
				int end = input.IndexOf("CBUFFER_END", start < 0 ? 0 : start);
				Assert.Greater(start, -1, $"{path} has no material constant buffer");
				Assert.Greater(end, start, $"{path} has no material constant buffer end");
				string buffer = input.Substring(start, end - start);
				StringAssert.Contains("_FishWeatherAmount", buffer, $"{path} must declare the weather amount inside UnityPerMaterial");
			}
		}

		[Test]
		public void TheCoverMap_IsBuiltAndPublishedForTheSurfaces()
		{
			string map = Read("Assets/Scripts/Client/World/Weather/Presentation/WeatherCoverMap.cs");
			StringAssert.Contains("_FishCoverTex", map, "the surfaces sample this texture by name");
			StringAssert.Contains("Integrate(", map, "the map must use the same cover integration the server runs");
			StringAssert.Contains("Anchor(", map, "the server's figure has to stay the anchor, or the client drifts");

			string presenter = Read("Assets/Scripts/Client/World/Weather/Presentation/WeatherPresentation.cs");
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
			StringAssert.Contains("\"/Models/\"", source, "character models must keep their own shaders (Q12)");
			StringAssert.Contains("\"/Plugins/\"", source, "third-party content must keep its own shaders");
			StringAssert.Contains("Universal Render Pipeline/Lit", source, "the migration only moves materials off URP's Lit");
		}
	}
}
