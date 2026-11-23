using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Commerce module: CommerceSystem runs merchants, mail and corpse looting, TradeSystem runs
	/// player trade, and the interactable system, the inventory and the character menus run without them.
	/// </summary>
	[TestFixture]
	public class CommerceModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/Commerce";

		private static string Read(string path)
		{
			return ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
		}

		[Test]
		public void TheSceneServerAdoptsBothSystems_AndTheSceneListsNeither()
		{
			string group = Read("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset");
			string scene = Read("Assets/Scenes/Server/SceneServer.unity");
			foreach (string asset in new[] { "Server/CommerceSystem.asset", "Server/TradeSystem.asset" })
			{
				string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/{asset}"));
				LogAssert.IsTrue(guid.Length == 32, $"{asset} exists");
				LogAssert.IsTrue(Regex.IsMatch(group, $@"- m_GUID: {guid}\n(?:    .*\n)*?    - SceneServer_Systems\n"), $"{asset} carries the SceneServer_Systems label");
				LogAssert.IsFalse(scene.Contains("guid: " + guid), $"the scene server scene no longer lists {asset}");
			}
			string interactables = Read("Assets/Prefabs/Server/SceneServer/InteractableSystem.asset");
			string commerce = Read(ModuleFolder + "/Server/CommerceSystem.asset");
			string scenes = Regex.Match(interactables, @"worldSceneDetailsCache: \{fileID: 11400000, guid: ([0-9a-f]{32})").Groups[1].Value;
			LogAssert.IsTrue(scenes.Length == 32 && commerce.Contains($"worldSceneDetailsCache: {{fileID: 11400000, guid: {scenes}"),
				"CommerceSystem reads the same world scene details the interactable system does");
			string currency = Regex.Match(commerce, @"currencyTemplate: \{fileID: 11400000, guid: ([0-9a-f]{32})").Groups[1].Value;
			LogAssert.IsTrue(AssetDatabase.LoadAssetAtPath<FishMMO.Shared.CharacterAttributeTemplate>(AssetDatabase.GUIDToAssetPath(currency)) != null,
				"CommerceSystem is paid in a character attribute (the currency)");
		}

		[Test]
		public void TheInteractableSystemRunsNoCommerce_AndLetsTheModuleOpenCorpses()
		{
			string interactables = Read("Assets/FishMMO/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.cs");
			foreach (string gone in new[] { "MerchantPurchaseBroadcast", "MailFetchBroadcast", "CorpseLootTakeItemBroadcast", "OpenCorpseLoot", "ILootableCorpse" })
			{
				LogAssert.IsFalse(interactables.Contains(gone), $"the interactable system names no {gone}");
			}
			LogAssert.IsTrue(interactables.Contains("RaisePlayerInteracting(character, interactable);"), "it raises the interaction for modules");

			string system = Read(ModuleFolder + "/Server/CommerceSystem.cs");
			LogAssert.IsTrue(system.Contains("interactableSystem.OnPlayerInteracting += InteractableSystem_OnPlayerInteracting;") &&
				system.Contains("interactableSystem.OnPlayerInteracting -= InteractableSystem_OnPlayerInteracting;"),
				"the commerce system subscribes, and lets go on shutdown");
			LogAssert.IsTrue(system.Contains("interactable is ILootableCorpse corpse") && system.Contains("OpenCorpseLoot("),
				"and opens a corpse's loot on any accepted interaction, triggers or none");
		}

		[Test]
		public void CurrencyBookkeepingIsOneCopy_OnTheBase()
		{
			foreach (string path in new[]
			{
				ModuleFolder + "/Server/CommerceSystem.Merchant.cs",
				"Assets/FishMMO/Modules/AbilityCrafting/Server/AbilityCraftingSystem.Craft.cs",
			})
			{
				if (!ProjectFiles.Exists(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path)))
				{
					continue;
				}
				string source = Read(path);
				LogAssert.IsFalse(source.Contains("void RecordCurrencyMovement(") || source.Contains("bool TryPersistCharacterAttributes("),
					$"{path} declares no copy of the shared currency helpers");
			}
			string shared = Read("Assets/FishMMO/Server/Implementation/ServerBehaviour.Currency.cs");
			LogAssert.IsTrue(shared.Contains("protected void RecordCurrencyMovement(") && shared.Contains("protected bool TryPersistCharacterAttributes("),
				"ServerBehaviour carries them");
		}

		[Test]
		public void TheWindowsArePlacedIntoTheWorldGui()
		{
			string client = Read("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset");
			string scene = Read("Assets/Scenes/Client/ClientWorldGUI.unity");
			foreach ((string prefab, string script) in new[]
			{
				("UIMerchant", "UITKMerchant"), ("UIMail", "UITKMail"), ("UILoot", "UITKLoot"), ("UITrade", "UITKTrade"),
			})
			{
				string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Client/{prefab}.prefab"));
				LogAssert.IsTrue(Regex.IsMatch(client, $@"- m_GUID: {guid}\n    m_Address: {prefab}\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
					$"{prefab} is labelled for ClientWorldGUI under its UIManager key");
				LogAssert.IsFalse(scene.Contains(AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Client/{script}.cs"))),
					$"the world GUI scene no longer holds {prefab}");
			}
		}

		[Test]
		public void TradeIsOfferedOnTheWorldAndPartyMenus_AndClaimsInventoryClicksOnlyWhileOpen()
		{
			TradeClient.Install();
			CharacterMenuEntries.Entry trade = CharacterMenuEntries.All.First(e => e.Id == TradeClient.EntryId);
			LogAssert.IsTrue(trade.Applies(new CharacterMenuTarget(4, "Al", CharacterMenuSource.World)), "offered on a player in the world");
			LogAssert.IsTrue(trade.Applies(new CharacterMenuTarget(4, "Al", CharacterMenuSource.Party)), "and on a party row");
			LogAssert.IsFalse(trade.Applies(new CharacterMenuTarget(4, "Al", CharacterMenuSource.Guild)), "not on the guild roster");
			LogAssert.IsFalse(InventorySlotClaims.TryClaimRightClick(0), "with no trade window open, the inventory keeps its click");
			const string inventoryWindow = "Assets/FishMMO/Modules/Items/Client/UITKInventory.cs";
			if (ProjectFiles.Exists(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), inventoryWindow)))
			{
				LogAssert.IsFalse(Read(inventoryWindow).Contains("UITKTrade"),
					"the inventory window (the Items module's) names nothing of the trade window");
			}
		}

		[Test]
		public void TheMailPanelWiresItsButtonsOnce()
		{
			// Moved from PanelReopenStateTests with the window.
			string code = string.Join("\n", Read(ModuleFolder + "/Client/UITKMail.cs").Split('\n')
				.Where(l => !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("*")));
			LogAssert.AreEqual(1, Regex.Matches(code, Regex.Escape("WireControls();")).Count,
				"WireControls is called from exactly one place; every call adds another handler to the same buttons");
			int starting = code.IndexOf("public override void OnStarting()", StringComparison.Ordinal);
			int wire = code.IndexOf("WireControls();", StringComparison.Ordinal);
			int next = code.IndexOf("\n\t\t}", starting, StringComparison.Ordinal);
			LogAssert.IsTrue(starting >= 0 && wire > starting && wire < next, "and that place is OnStarting, which runs once per tree");
		}
	}
}
