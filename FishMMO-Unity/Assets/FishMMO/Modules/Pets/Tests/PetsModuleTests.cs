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
	/// The Pets module (2026-11-20): the module declares itself, the scene server adopts the pet system by label, and a pet is
	/// the NPCs module's <see cref="IAICompanion"/> and the core's <see cref="IPersistedPet"/>.
	/// </summary>
	[TestFixture]
	public class PetsModuleTests
	{
		[Test]
		public void TheModuleIsDeclared()
		{
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(PetsModule).Assembly }, problems);
			LogAssert.IsTrue(found.Any(m => m.Id == PetsModule.ModuleId), "the module declares itself ([assembly: GameModule])");
		}

		[Test]
		public void TheSceneServerAdoptsThePetSystem_ByLabel()
		{
			AssertAdoptedByLabel("Assets/FishMMO/Modules/Pets/Server/PetSystem.asset");
		}

		[Test]
		public void APetIsSeenThroughTheSeams()
		{
			LogAssert.IsTrue(typeof(IAICompanion).IsAssignableFrom(typeof(Pet)), "the AI drives a pet as a companion");
			LogAssert.IsTrue(typeof(IPersistedPet).IsAssignableFrom(typeof(Pet)), "the core save captures a pet");
			LogAssert.IsTrue(typeof(IPetRoster).IsAssignableFrom(typeof(PetController)), "the core save and the summon pre-filter read the roster");
		}

		[Test]
		public void EveryPlayerGetsThePetController_AsAPart()
		{
			LogAssert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(typeof(PetController)), "a player gets one as it wakes");
			LogAssert.IsFalse(CharacterParts.PartsFor(typeof(NPC)).Contains(typeof(PetController)), "an NPC does not");
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path("Assets/FishMMO/Modules/Pets/Runtime/Pet/PetController.cs"));
			foreach (string prefab in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
			{
				LogAssert.IsFalse(ProjectFiles.ReadAllText(prefab).Contains("guid: " + guid), $"{prefab} does not author a PetController");
			}
		}

		[Test]
		public void ThePetControlPanel_IsTheModules()
		{
			foreach ((string prefab, string address, string script) in new[]
			{
				("Assets/FishMMO/Modules/Pets/Client/Pet/UIPetControl.prefab", "UIPetControl", "Assets/FishMMO/Modules/Pets/Client/Pet/UITKPetControl.cs"),
			})
			{
				string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(prefab));
				LogAssert.IsTrue(Regex.IsMatch(Read("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset"),
					$@"- m_GUID: {guid}\n    m_Address: {address}\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
					$"{address} is placed into the world GUI under the name the client opens it by");
				string scriptGuid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(script));
				LogAssert.IsFalse(Read("Assets/Scenes/Client/ClientWorldGUI.unity").Contains(scriptGuid), $"ClientWorldGUI.unity no longer carries {address}");
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
