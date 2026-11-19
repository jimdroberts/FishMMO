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
		private const string DayNight = "Assets/FishMMO/Modules/WorldSimulation/Runtime/SceneData/WorldDayNightCycle.cs";
		private const string Migration = "Assets/FishMMO/Modules/WorldSimulation/Editor/Weather/WeatherSurfaceMigration.cs";

		private static string Read(string path)
		{
			Assert.IsTrue(ProjectFiles.Exists(path), $"{path} is missing. The weather surfaces need it.");
			return ProjectFiles.ReadAllText(path);
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
			// settles under roofs. They read the ground layer (FishSkyOcclusionGround) and handle the
			// sea separately through FishWaterOpen, so the sea bed holds no cover past the map's edge.
			Assert.IsTrue(library.Contains("FishSkyOcclusionGround"), "cover must stop at a roof, which is what the occlusion map is for");
			Assert.IsTrue(library.Contains("FishWaterOpen"), "nothing settles under the sea");
			Assert.IsTrue(library.Contains("FishSurfaceExposure"), "the one place that decides whether the sky reaches a surface is gone");
			Assert.IsTrue(library.Contains("#include \"Assets/FishMMO/Shaders/FishDither.hlsl\""), "the day/night dissolve comes from the core dither");
			Assert.IsTrue(ProjectFiles.ReadAllText("Assets/FishMMO/Shaders/FishDither.hlsl").Contains("void FishDitherClip"), "which has the clip");
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
		public void TheInstancedLodFade_IsReadInEveryPassThatWritesDepth()
		{
			// The instanced tree channels cross-fade LOD levels through _FishLodFade. A pass that does
			// not read it keeps drawing both levels there, which shows as doubled shadows or depth.
			string lodFade = Read(Shaders + "FishLodFade.hlsl");
			StringAssert.Contains("UNITY_INSTANCING_BUFFER_START(FishLodFadeProps)", lodFade, "the fade must live in its own instancing buffer");
			StringAssert.Contains("UNITY_DEFINE_INSTANCED_PROP(float, _FishLodFade)", lodFade, "the renderer sets this name");
			StringAssert.Contains("UNITY_INSTANCING_ENABLED", lodFade, "the buffer must exist only in the instanced variants, or the SRP batcher layout changes");

			string litInput = Read(LitInput);
			Assert.IsFalse(litInput.Contains("_FishLodFade"), "the per-instance fade must stay out of UnityPerMaterial");

			string lit = Read(LitShader);
			foreach (string entry in new[] { "FishShadowPassFragment", "FishLitGBufferPassFragment", "FishDepthOnlyFragment", "FishDepthNormalsFragment", "FishMotionVectorsFragment" })
			{
				StringAssert.Contains("#pragma fragment " + entry, lit, $"{entry} is no longer the pass's fragment, so that pass ignores the LOD fade");
			}
			StringAssert.Contains("FishLodFadeClip(", Read(LitPass), "the forward pass ignores the LOD fade");

			string vegetation = Read(Shaders + "FishVegetationPasses.hlsl");
			int calls = vegetation.Split(new[] { "FishLodFadeClip(" }, System.StringSplitOptions.None).Length - 1;
			Assert.AreEqual(4, calls, "forward, depth-normals, the shared shadow/depth fragment and the depth prime each read the LOD fade");
		}

		[Test]
		public void TheSnowLift_ComesDownAtTheTerrainsHoles()
		{
			// A hole is cut where rock is the ground (a cave's mouth, a canyon wall), flush with its edge. Lifted to the
			// snow's full depth there, the terrain's edge stands over the rock with a gap under it. The fade is a per-vertex
			// map: a scan from the vertex reached three cells and collapsed into a square plate on a far patch.
			string holes = Read(Shaders + "FishTerrainSnowHoles.hlsl");
			StringAssert.Contains("_FishSnowHoleFade.Load(", holes, "the fade must be each vertex's own value, loaded, not filtered between vertices");
			string passes = Read(Shaders + "FishWeatherTerrainArrayPasses.hlsl");
			StringAssert.Contains("#include \"FishTerrainSnowHoles.hlsl\"", passes, "the array terrain lifts the snow without thinning it at the holes");
			StringAssert.Contains("lift *= FishSnowHoleFade(", passes, "the array terrain lifts the snow without thinning it at the holes");
			StringAssert.Contains("_FishSnowHoleFade_TexelSize", Read(Shaders + "FishWeatherTerrainArrayInput.hlsl"), "the fade map's size is read from the tile's own buffer");

			string binder = Read("Assets/FishMMO/Shared/Implementation/Entity/WorldSceneDetails/Terrain/TerrainArrayBinder.cs");
			StringAssert.Contains("TerrainSnowHoleFade.Build(", binder, "nothing makes the tiles' fade maps");
			StringAssert.Contains("SnowHoleFadeId", binder, "nothing hands a tile its fade map");
		}

	}
}
