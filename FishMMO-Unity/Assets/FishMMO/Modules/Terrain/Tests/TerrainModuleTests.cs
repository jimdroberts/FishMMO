using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>The Terrain module's seams into the core.</summary>
	public class TerrainModuleTests
	{
		[Test]
		public void TheModule_InstallsTheGroundCoverAndTheMoss()
		{
			TerrainClient.Install();
			LogAssert.IsTrue(GroundCoverQuery.CoverSource != null && GroundCoverQuery.StampSource != null, "the grass cover");
			LogAssert.IsTrue(GroundCoverQuery.GrassBuildingSource != null && GroundCoverQuery.ScatterBuildingSource != null, "the builders' state");
			LogAssert.IsTrue(WeatherOverlays.Entries.Any(e => e.Id == TerrainClient.MossOverlayId), "the moss, drawn with the weather");
			LogAssert.IsTrue(TerrainModule.ModuleId == "terrain");
		}

		[Test]
		public void TheCoreNamesNoTerrainType()
		{
			// The core reads the grass and the minimap's opt-out through GroundCoverQuery and CameraGroundCover, and the weather's
			// presentation draws the moss as an overlay.
			Regex named = new Regex(@"\b(GrassBladeSystem|DetailScatterSystem|TerrainInstancingShared|TerrainRenderProfile|MossShaderGlobals|MossClimate)\b");
			foreach (string path in new[]
			{
				"Assets/FishMMO/Modules/WorldSimulation/Client/Audio/World/GroundSurface.cs",
				"Assets/FishMMO/Modules/WorldSimulation/Client/Audio/World/WindBedsPresenter.cs",
				"Assets/FishMMO/Modules/Map/Client/MinimapCameraRenderer.cs",
				"Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Presentation/WeatherPresentation.cs",
				"Assets/FishMMO/Modules/WorldSimulation/Client/Looks/CaveSkylights.cs",
			})
			{
				if (InstalledModules.IsAbsent(path))
				{
					continue; // the Map module's minimap renderer, when that module is removed
				}
				LogAssert.IsFalse(named.IsMatch(SourceScanPins.CodeOnly(ProjectFiles.ReadAllText(path))), path + " names no Terrain type");
			}
		}
	}
}
