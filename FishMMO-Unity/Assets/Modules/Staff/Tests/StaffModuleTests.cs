using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Staff and Support module (Phase 3, 2026-11-17): one StaffSystem hosts the operator sets, the console, game
	/// master presence, support and reports; the scene and chat systems run without it.
	/// </summary>
	[TestFixture]
	public class StaffModuleTests
	{
		private const string ModuleFolder = "Assets/Modules/Staff";

		[Test]
		public void TheSceneServerAdoptsTheStaffSystem_WithTheCurrencyTheGoldCommandsUse()
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Server/StaffSystem.asset"));
			string group = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(group, $@"- m_GUID: {guid}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "it carries the SceneServer_Systems label");
			string currency = AssetDatabase.AssetPathToGUID(ProjectFiles.Path("Assets/Templates/Entity/CharacterAttributes/Misc/Currency.asset"));
			LogAssert.IsTrue(ProjectFiles.ReadAllText(ModuleFolder + "/Server/StaffSystem.asset").Contains("currencyTemplate: {fileID: 11400000, guid: " + currency),
				"/admin gold acts on the Currency attribute, as it did from the scene server's asset");
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Prefabs/Server/SceneServer/SceneServerSystem.asset").Contains("currencyTemplate"),
				"the scene server no longer holds it");
		}

		[Test]
		public void TheSceneAndChatSystemsRegisterNoStaffTools()
		{
			string scene = ProjectFiles.ReadAllText("Assets/Scripts/Server/Implementation/World/SceneServer/SceneServer/SceneServerSystem.cs");
			string chat = ProjectFiles.ReadAllText("Assets/Scripts/Server/Implementation/World/SceneServer/Chat/ChatSystem.cs");
			LogAssert.IsFalse(scene.Contains("RegisterAdminCommands") || scene.Contains("RegisterGameMasterCommands") ||
				scene.Contains("InitializeGameMasterPresence") || scene.Contains("UpdateSimulationRun"), "the scene server registers none of them");
			LogAssert.IsFalse(chat.Contains("RegisterSupportCommands"), "nor does the chat system");
			string staff = ProjectFiles.ReadAllText(ModuleFolder + "/Server/StaffSystem.cs");
			LogAssert.IsTrue(staff.Contains("RegisterAdminCommands();") && staff.Contains("RegisterGameMasterCommands();") &&
				staff.Contains("InitializeGameMasterPresence(characterSystem);") && staff.Contains("RegisterSupportCommands();") &&
				staff.Contains("UpdateSimulationRun(deltaTime);"), "the staff system does");
		}

		[Test]
		public void BothWindowsArePlacedIntoTheWorldGui()
		{
			string client = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset").Replace("\r\n", "\n");
			string scene = ProjectFiles.ReadAllText("Assets/Scenes/Client/ClientWorldGUI.unity");
			foreach ((string prefab, string script) in new[] { ("UIStaffConsole", "UITKStaffConsole"), ("UIReportPlayer", "UITKReportPlayer") })
			{
				string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Client/{prefab}.prefab"));
				LogAssert.IsTrue(Regex.IsMatch(client, $@"- m_GUID: {guid}\n    m_Address: {prefab}\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
					$"{prefab} is labelled for ClientWorldGUI under its UIManager key");
				LogAssert.IsFalse(scene.Contains(AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Client/{script}.cs"))),
					$"the world GUI scene no longer holds {prefab}");
			}
		}

		[Test]
		public void ReportIsOfferedWhereItAlwaysWas_AfterTrade()
		{
			StaffClient.Install();
			CharacterMenuEntries.Entry report = CharacterMenuEntries.All.First(e => e.Id == StaffClient.ReportEntryId);
			LogAssert.IsTrue(report.Order > 120, "Report comes last, after Trade (the Commerce module's entry, order 120)");
			foreach (CharacterMenuSource source in new[] { CharacterMenuSource.World, CharacterMenuSource.Party, CharacterMenuSource.Friends })
			{
				LogAssert.IsTrue(report.Applies(new CharacterMenuTarget(4, "Al", source)), $"offered on the {source} menu");
			}
			LogAssert.IsFalse(report.Applies(new CharacterMenuTarget(4, "Al", CharacterMenuSource.Guild)), "not on the guild roster, which never offered it");
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Scripts/Client/Input/PlayerInputController.cs").Contains("UITKReportPlayer"),
				"the core input names nothing of the report window");
		}
	}
}
