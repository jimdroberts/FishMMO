using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Server.Implementation.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Housing module (Phase 3, 2026-11-17): the housing system, its windows, its operator commands and the bank's
	/// vault link live in <c>Assets/Modules/Housing</c>; the plot foundations in the world scenes stay in the core.
	/// </summary>
	[TestFixture]
	public class HousingModuleTests
	{
		private const string ModuleFolder = "Assets/Modules/Housing";

		[Test]
		public void TheSceneServerAdoptsTheHousingSystem()
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Server/HousingSystem.asset"));
			string group = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(group, $@"- m_GUID: {guid}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "it carries the SceneServer_Systems label");
			LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/Scenes/Server/SceneServer.unity").Contains(guid), "the scene server's own list does not name it");
		}

		[Test]
		public void BothWindowsArePlacedIntoTheWorldGui()
		{
			string client = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset").Replace("\r\n", "\n");
			string scene = ProjectFiles.ReadAllText("Assets/Scenes/Client/ClientWorldGUI.unity");
			foreach ((string prefab, string script) in new[] { ("UIHousingPlot", "UITKHousingPlot"), ("UIHousingVault", "UITKHousingVault") })
			{
				string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Client/{prefab}.prefab"));
				LogAssert.IsTrue(Regex.IsMatch(client, $@"- m_GUID: {guid}\n    m_Address: {prefab}\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
					$"{prefab} is labelled for ClientWorldGUI under its UIManager key");
				string scriptGuid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Client/{script}.cs"));
				LogAssert.IsFalse(scene.Contains(scriptGuid), $"the world GUI scene no longer holds {prefab}");
			}
		}

		[Test]
		public void ThePlotCommandsComeFromTheModule()
		{
			LogAssert.IsTrue(OperatorCommandProviders.All.Any(p => p is HousingOperatorCommands), "the module adds /gm plot and /admin plot");
			HousingOperatorCommands commands = new HousingOperatorCommands();
			LogAssert.IsTrue(commands.GameMasterCommands(null).Any(c => c.Name == "plot") &&
				commands.AdministratorCommands(null).Any(c => c.Name == "plot" && c.Destructive), "both tiers, the administrator's marked destructive");

			// The /gm and /admin sets are the Staff module's; checked when it is installed.
			const string AdminPath = "Assets/Modules/Staff/Server/StaffSystem.AdminCommands.cs";
			const string GameMasterPath = "Assets/Modules/Staff/Server/StaffSystem.GameMasterCommands.cs";
			if (ProjectFiles.Exists(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), AdminPath)))
			{
				string admin = ProjectFiles.ReadAllText(AdminPath);
				string gm = ProjectFiles.ReadAllText(GameMasterPath);
				LogAssert.IsFalse(admin.Contains("BuildAdminHousingCommands"), "the admin set names nothing of housing");
				LogAssert.IsTrue(admin.Contains("provider.AdministratorCommands(this)") && gm.Contains("provider.GameMasterCommands(this)"),
					"both sets take the modules' commands");
			}
		}

		[Test]
		public void TheBankOffersTheVaultThroughAModuleLink()
		{
			HousingClient.Install(); // [RuntimeInitializeOnLoadMethod] does not run in EditMode.
			LogAssert.IsTrue(BankWindowLinks.All.Any(l => l.Id == HousingClient.VaultLinkId && l.Label == "House vault"), "the module registers the link");

			string bank = ProjectFiles.ReadAllText("Assets/Scripts/Client/GUI/World/Bank/UITKBank.cs");
			string bankUxml = ProjectFiles.ReadAllText("Assets/Scripts/Client/GUI/World/Bank/UIBank.uxml");
			LogAssert.IsFalse(bank.Contains("UITKHousingVault") || bankUxml.Contains("bank-vault-button"), "the bank names nothing of housing");
			LogAssert.IsTrue(bankUxml.Contains("name=\"bank-module-links\""), "it has a place for module links");
		}
	}
}
