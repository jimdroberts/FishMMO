using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Ability Crafting module (Phase 3, 2026-11-17): AbilityCraftingSystem answers craft requests; the interactable
	/// system, the crafters placed in the world and abilities themselves run without it.
	/// </summary>
	[TestFixture]
	public class AbilityCraftingModuleTests
	{
		private const string ModuleFolder = "Assets/Modules/AbilityCrafting";

		private static string Read(string path)
		{
			return ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
		}

		[Test]
		public void TheSceneServerAdoptsTheCraftingSystem()
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Server/AbilityCraftingSystem.asset"));
			string group = Read("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset");
			LogAssert.IsTrue(Regex.IsMatch(group, $@"- m_GUID: {guid}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "it carries the SceneServer_Systems label");
			string system = Read(ModuleFolder + "/Server/AbilityCraftingSystem.asset");
			string currency = Regex.Match(system, @"currencyTemplate: \{fileID: 11400000, guid: ([0-9a-f]{32})").Groups[1].Value;
			LogAssert.IsNotNull(AssetDatabase.LoadAssetAtPath<CharacterAttributeTemplate>(AssetDatabase.GUIDToAssetPath(currency)),
				"crafting is paid in a character attribute (the currency)");
		}

		[Test]
		public void TheInteractableSystemCraftsNothing_AndTheCrafterStaysInTheCore()
		{
			string interactables = Read("Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.cs");
			LogAssert.IsFalse(interactables.Contains("AbilityCraftBroadcast") || interactables.Contains("maxAbilityCraftEvents") ||
				interactables.Contains("currencyTemplate"), "no craft handler, craft limits or currency in the interactable system");
			LogAssert.IsTrue(Read("Assets/Scripts/Shared/Implementation/Network/Interactable/InteractableBroadcasts.cs").Contains("struct AbilityCrafterBroadcast"),
				"the message that opens the window stays with the crafter's ECA action in the core");
			LogAssert.IsTrue(Read(ModuleFolder + "/Server/AbilityCraftingSystem.cs").Contains("RegisterBroadcast<AbilityCraftBroadcast>"),
				"the crafting system answers craft requests");
		}

		[Test]
		public void TheWindowIsPlacedIntoTheWorldGui()
		{
			string client = Read("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset");
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Client/UIAbilityCraft.prefab"));
			LogAssert.IsTrue(Regex.IsMatch(client, $@"- m_GUID: {guid}\n    m_Address: UIAbilityCraft\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
				"UIAbilityCraft is labelled for ClientWorldGUI under its UIManager key");
			string script = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Client/UITKAbilityCraft.cs"));
			LogAssert.IsFalse(Read("Assets/Scenes/Client/ClientWorldGUI.unity").Contains(script), "the world GUI scene no longer holds the window");
		}
	}
}
