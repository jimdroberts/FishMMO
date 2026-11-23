using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The World Design module: the world's editor tools live in
	/// <c>Assets/FishMMO/Modules/WorldDesign</c> and plug into the core editor through its seams; the dashboards, the weather
	/// content generator and the core tools name none of them.
	/// </summary>
	[TestFixture]
	public class WorldDesignModuleTests
	{
		private const string CoreEditor = "Assets/FishMMO/Shared/Implementation/Tools/Extensions/Unity/Editor";

		[Test]
		public void TheDashboardsPagesAndLockComeFromTheModule()
		{
			WorldDesignEditor.Install();
			LogAssert.IsTrue(DashboardFullPages.TryGet(WorldDesignEditor.SolarSystemPageName, out DashboardFullPages.Page solar) && solar.Group == "World",
				"the Solar System page, under World");
			LogAssert.IsTrue(DashboardFullPages.TryGet(WorldDesignEditor.WorldAtlasPageName, out _), "the World Atlas page");
			LogAssert.IsFalse(DashboardJobs.IsBusy, "nothing generating");
		}

		[Test]
		public void TheGeneratedArtStaysOutOfTheCommittedGroups()
		{
			WorldDesignEditor.Install();
			LogAssert.IsTrue(AddressableGeneratedContent.IsGenerated(PlanetSurfaceBaker.BakedDirectory + "/Home/Surface.asset"), "the planet surfaces");
			LogAssert.IsTrue(AddressableGeneratedContent.IsGenerated(ProceduralArtCatalogue.PayloadRoot + "/Meshes/Rock.asset") ==
				ProceduralArtBuildGroup.IsGenerated(ProceduralArtCatalogue.PayloadRoot + "/Meshes/Rock.asset"), "the procedural art, as its build group says");
			LogAssert.IsFalse(AddressableGeneratedContent.IsGenerated("Assets/Prefabs/Shared/Character/Human.prefab"), "nothing else");
		}

		[Test]
		public void TheWeatherContentFillsTheBiomesFromTheSpec()
		{
			WorldDesignEditor.Install();
			LogAssert.IsTrue(WeatherContentGenerator.BiomeGround != null, "the module installs the spec lookup");
			LogAssert.IsTrue(WeatherContentGenerator.BiomeGround("Volcanic", out _, out string emits, out float rate) && emits != null && rate > 0f,
				"the volcanic biome's spec names what its ground gives the air");
		}

		[Test]
		public void TheCoreEditorNamesNoWorldDesignType()
		{
			// The core editor tools keep their World/ helpers (the shared asset helpers, the backdrop scenes, the bakers the
			// weather renders with); everything else of the world's design is the module's.
			string[] moduleTypes = { "BiomeArtGenerator", "ProceduralArtBuildGroup", "PlanetSurfaceBaker", "SolarSystemPage", "WorldAtlasPage", "BiomeArtSpec",
				"ScenePropBaker", "TerrainTileLayerSync", "ProceduralArtCatalogue", "ProceduralArtVariants", "PointOfInterestGenerator", "SceneGenerator" };
			var named = new Regex(@"\b(" + string.Join("|", moduleTypes) + @")\b");
			var hits = Directory.GetFiles(ProjectFiles.Folder(CoreEditor), "*.cs", SearchOption.AllDirectories)
				.Concat(new[] { "Assets/FishMMO/Modules/WorldSimulation/Editor/Weather/WeatherRenderAssets.cs", "Assets/FishMMO/Modules/Terrain/Editor/TerrainDetailBaker.cs",
					"Assets/FishMMO/Client/GUI/Editor/ShaderPrewarm.cs" }.Select(ProjectFiles.Path))
				.SelectMany(f => named.Matches(SourceScanPins.CodeOnly(File.ReadAllText(f))).Cast<Match>().Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
				.ToList();
			LogAssert.IsTrue(hits.Count == 0, "the core editor reaches World Design through a seam, but names:\n" + string.Join("\n", hits));
		}

		[Test]
		public void ThePointOfInterestTemplatesNameTheModulesAssembly()
		{
			string template = ProjectFiles.ReadAllText("Assets/FishMMO/Modules/WorldDesign/Templates/PointsOfInterest/Settlement/Keep - Palisade.asset");
			LogAssert.IsFalse(template.Contains("FishMMO.Shared.Tools.Editor"), "no feature is recorded under the old assembly");
			LogAssert.IsTrue(template.Contains("asm: FishMMO.WorldDesign.Editor"), "the features name the module's assembly");
		}
	}
}
