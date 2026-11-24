using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Auction House module's wiring: the scene server adopts its system, the world GUI gets its window, the
	/// Auctioneer's interaction opens it, and a right-click in the bags sells.
	/// </summary>
	[TestFixture]
	public class AuctionHouseModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/AuctionHouse";

		private static string Group(string name)
		{
			return ProjectFiles.ReadAllText($"Assets/AddressableAssetsData/AssetGroups/{name}.asset").Replace("\r\n", "\n");
		}

		[Test]
		public void TheSceneServerAdoptsTheAuctionHouseSystem()
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Server/AuctionHouseSystem.asset"));
			LogAssert.IsFalse(string.IsNullOrEmpty(guid), "the system asset exists");
			LogAssert.IsTrue(Regex.IsMatch(Group("Server_Static_Permanent"), $@"- m_GUID: {guid}\n(?:    .*\n)*?    - SceneServer_Systems\n"),
				"it carries the SceneServer_Systems label");
		}

		[Test]
		public void TheWindowIsPlacedIntoTheWorldGui()
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Client/UIAuctionHouse.prefab"));
			LogAssert.IsTrue(Regex.IsMatch(Group("Client_Dynamic"), $@"- m_GUID: {guid}\n    m_Address: {UITKAuctionHouse.ControlName}\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
				"labelled for ClientWorldGUI under its UIManager key");
		}

		[Test]
		public void TheAuctioneersInteractionOpensTheWindow()
		{
			string path = ModuleFolder + "/Templates/Interactions/Auctioneer Interact.asset";
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(path));
			LogAssert.IsTrue(Regex.IsMatch(Group("Shared_Static_Permanent"), $@"- m_GUID: {guid}\n    m_Address: Auctioneer Interact\n"),
				"the trigger is addressable with the other interactions");
			Trigger trigger = AssetDatabase.LoadAssetAtPath<Trigger>(path);
			LogAssert.IsNotNull(trigger, "it loads as a trigger");
			LogAssert.IsTrue(trigger.OnConditionsMetActions.Exists(a => a is SendAuctioneerBroadcastAction), "it sends the open message");
		}

		[Test]
		public void ARightClickInTheBagsIsOfferedForSale()
		{
			AuctionHouseClient.Install(); // [RuntimeInitializeOnLoadMethod] does not run in EditMode.
			LogAssert.IsTrue(InventorySlotClaims.Unregister(AuctionHouseClient.ClaimId), "the module registers its claim");
			AuctionHouseClient.Install();
		}

		[Test]
		public void TheWindowIsWired()
		{
			string uxml = ProjectFiles.ReadAllText(ModuleFolder + "/Client/UIAuctionHouse.uxml");
			string code = ProjectFiles.ReadAllText(ModuleFolder + "/Client/UITKAuctionHouse.cs");
			foreach (Match m in Regex.Matches(code, "Q<\\w+>\\(\"([a-z-]+)\"\\)"))
			{
				LogAssert.IsTrue(uxml.Contains($"name=\"{m.Groups[1].Value}\""), $"the window looks up #{m.Groups[1].Value}, which the layout has");
			}
		}
	}
}
