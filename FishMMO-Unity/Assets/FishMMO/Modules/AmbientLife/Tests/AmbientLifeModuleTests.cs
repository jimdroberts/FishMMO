using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Ambient Life module (Phase 4 of the core and modules plan, 2026-11-17): every ambient system lives in
	/// <c>Assets/FishMMO/Modules/AmbientLife</c> and plugs in through the core's seams; the weather, the terrain and the
	/// test beds name none of it.
	/// </summary>
	[TestFixture]
	public class AmbientLifeModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/AmbientLife";

		[Test]
		public void TheAirMotes_AreTheWeathersOverlay()
		{
			AmbientLifeClient.Install(); // [RuntimeInitializeOnLoadMethod] does not run in EditMode.
			LogAssert.IsTrue(WeatherOverlays.Entries.Any(e => e.Id == AmbientLifeClient.AirMotesOverlayId), "the module registers its overlay");
			using (IWeatherOverlay overlay = WeatherOverlays.Entries.First(e => e.Id == AmbientLifeClient.AirMotesOverlayId).Create())
			{
				LogAssert.IsTrue(overlay is AirMotesField, "made as the air motes");
			}
			string presentation = ProjectFiles.ReadAllText("Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Presentation/WeatherPresentation.cs");
			LogAssert.IsFalse(Regex.IsMatch(SourceScanPins.CodeOnly(presentation), @"\b(AirMotesField|AmbientLifeRenderProfile)\b"), "the weather names no ambient life");
		}

		[Test]
		public void TheTestBedSwitchesAndReport_ComeFromTheModule()
		{
			AmbientLifeClient.Install();
			LogAssert.IsTrue(new[] { "ambientLife", "ambientLifeMax", "ambientHighlight" }.All(n => RenderSwitches.Names.Contains(n)), "its three switches");
			LogAssert.IsTrue(RenderReports.All.Any(r => r.Key == "ambient life"), "its perf report line");
			if (InstalledModules.Has("WorldTestBed"))
			{
				string harness = ProjectFiles.ReadAllText("Assets/FishMMO/Modules/WorldTestBed/World/HarnessSettings.cs");
				LogAssert.IsFalse(harness.Contains("AmbientLifeRenderProfile"), "the test bed names no ambient life");
			}
		}

		[Test]
		public void TheProfileShipsWithTheClient()
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Settings/Ambient Life Render Profile.asset"));
			LogAssert.IsTrue(!string.IsNullOrEmpty(guid), "the profile is in the module");
			string group = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Client_Static_Permanent.asset");
			LogAssert.IsTrue(group.Contains("m_GUID: " + guid), "it is a client static addressable, loaded with the client");
			var profile = AssetDatabase.LoadAssetAtPath<AmbientLifeRenderProfile>(ProjectFiles.Path(AmbientLifeRenderAssets.ProfilePath));
			LogAssert.IsTrue(profile != null && profile.AmbientLifeShader != null && profile.SeaLifeShader != null && profile.AirMotesShader != null,
				"its shaders are referenced, so a client build includes them");
		}
	}
}
