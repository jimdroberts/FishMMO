using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Core;
using FishMMO.Shared;
using FishMMO.UnitTests.Harness;
using NUnit.Framework;
using UnityEditor;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Map module (2026-11-20): the wiring that lets the folder be removed. Its two windows are placed into the world
	/// GUI by label, and nothing in the core names a client map type; the core keeps the map data, the bake, the explore
	/// event and true north.
	/// </summary>
	[TestFixture]
	public class MapModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/Map";

		[Test]
		public void TheModuleIsDeclared()
		{
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(MapModule).Assembly }, problems);
			LogAssert.IsTrue(found.Any(m => m.Id == MapModule.ModuleId), "the module declares itself ([assembly: GameModule])");
		}

		[Test]
		public void TheWorldGuiPlacesBothWindows_AndTheSceneNoLongerCarriesThem()
		{
			string client = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset").Replace("\r\n", "\n");
			string scene = ProjectFiles.ReadAllText("Assets/Scenes/Client/ClientWorldGUI.unity");
			foreach (string window in new[] { "UIMap", "UIMinimap" })
			{
				string prefab = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Client/{window}.prefab"));
				LogAssert.IsFalse(string.IsNullOrEmpty(prefab), $"{window}.prefab exists");
				LogAssert.IsTrue(Regex.IsMatch(client, $@"- m_GUID: {prefab}\n    m_Address: {window}\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
					$"{window} carries the ClientWorldGUI_Panels label, under its UIManager name");
			}
			foreach (string script in new[] { "UITKMap", "UITKMinimap" })
			{
				string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Client/{script}.cs"));
				LogAssert.IsFalse(scene.Contains(guid), $"ClientWorldGUI.unity no longer carries {script}");
			}
		}

		[Test]
		public void TheCoreNamesNoClientMapType()
		{
			/* The core keeps the data (markers, definitions, points of interest), the bake and the explore event; what draws
			 * them is the module's. A core file naming one of these would not compile with the module removed. */
			string[] moved = Directory.GetFiles(ProjectFiles.Path(ModuleFolder + "/Client"), "*.cs")
				.Select(Path.GetFileNameWithoutExtension).ToArray();
			var named = new Regex(@"\b(" + string.Join("|", moved) + @")\b");
			foreach (string root in new[] { "Assets/FishMMO/Client", "Assets/FishMMO/Shared", "Assets/FishMMO/Server" })
			{
				foreach (string file in Directory.GetFiles(ProjectFiles.Path(root), "*.cs", SearchOption.AllDirectories))
				{
					string code = SourceScanPins.CodeOnly(File.ReadAllText(file));
					Match match = named.Match(code);
					LogAssert.IsFalse(match.Success, $"{file} names the Map module's {match.Value} outside a comment");
				}
			}
		}

		[Test]
		public void TheExploreActionReachesTheMapByItsEvent()
		{
			string action = ProjectFiles.ReadAllText("Assets/FishMMO/Shared/Implementation/Entity/ECA/Actions/Character/ExploreMapAction.cs");
			LogAssert.IsTrue(action.Contains("public static event Action<Vector3, float> OnExploreMap;"), "the core action raises an event");
			LogAssert.IsTrue(ProjectFiles.ReadAllText(ModuleFolder + "/Client/UITKMinimap.cs").Contains("ExploreMapAction.OnExploreMap += MapSystem_OnExploreMap;"),
				"and the module's minimap listens to it");
		}
	}
}
