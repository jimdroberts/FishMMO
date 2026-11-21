using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEditor;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The NPCs module (2026-11-20): the module declares itself, the scene server adopts the AI and spawner systems by label,
	/// and the AI reads pets and wildlife only through seams (<see cref="IAICompanion"/>, <see cref="IWildlifeSpecies"/>), so
	/// neither module is referenced by this one.
	/// </summary>
	[TestFixture]
	public class NpcsModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/NPCs";

		[Test]
		public void TheModuleIsDeclared()
		{
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(NpcsModule).Assembly }, problems);
			LogAssert.IsTrue(found.Any(m => m.Id == NpcsModule.ModuleId), "the module declares itself ([assembly: GameModule])");
		}

		[Test]
		public void TheSceneServerAdoptsTheAIAndSpawnerSystems_ByLabel()
		{
			AssertAdoptedByLabel(ModuleFolder + "/Server/AISystem.asset");
			AssertAdoptedByLabel(ModuleFolder + "/Server/SpawnerSystem.asset");
		}

		[Test]
		public void TheModuleReferencesNeitherPetsNorWildlife()
		{
			foreach (string asmdef in System.IO.Directory.GetFiles(ProjectFiles.Path(ModuleFolder), "*.asmdef", System.IO.SearchOption.AllDirectories))
			{
				if (asmdef.Replace('\\', '/').Contains("/Tests/"))
				{
					continue;
				}
				string text = System.IO.File.ReadAllText(asmdef);
				LogAssert.IsFalse(text.Contains("\"FishMMO.Pets"), $"{asmdef} does not reference the Pets module");
				LogAssert.IsFalse(text.Contains("\"FishMMO.Wildlife"), $"{asmdef} does not reference the Wildlife module");
			}
		}

		private static string Read(string path) => ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");

		private static void AssertAdoptedByLabel(string assetPath)
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(assetPath));
			LogAssert.IsFalse(string.IsNullOrEmpty(guid), $"{assetPath} exists");
			LogAssert.IsTrue(Regex.IsMatch(Read("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset"),
				$@"- m_GUID: {guid}\n(?:    .*\n)*?    - SceneServer_Systems\n"), $"{assetPath} carries the SceneServer_Systems label");
			LogAssert.IsFalse(Read("Assets/Scenes/Server/SceneServer.unity").Contains(guid), $"SceneServer.unity no longer lists {assetPath}");
		}
	}
}
