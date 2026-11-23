using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Core;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Items module: the player's three containers are character parts every player gets when it wakes (no
	/// race prefab authors them), the equipment still takes part in prediction and still sends its spawn state, the scene
	/// server adopts the inventory system by label, and the world GUI places the three windows by label. The core keeps the
	/// item data, the contracts, the load and the login sends.
	/// </summary>
	[TestFixture]
	public class ItemsModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/Items";

		[Test]
		public void TheModuleIsDeclared()
		{
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(ItemsModule).Assembly }, problems);
			LogAssert.IsTrue(found.Any(m => m.Id == ItemsModule.ModuleId), "the module declares itself ([assembly: GameModule])");
		}

		[Test]
		public void EveryPlayerGetsTheThreeContainers_AndNoPrefabAuthorsThem()
		{
			foreach ((System.Type part, string file) in new[]
			{
				(typeof(InventoryController), "InventoryController"),
				(typeof(BankController), "BankController"),
				(typeof(EquipmentController), "EquipmentController"),
			})
			{
				LogAssert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(part), $"a player gets {file} as a part");
				LogAssert.IsFalse(CharacterParts.PartsFor(typeof(NPC)).Contains(part), $"an NPC does not get {file}");
				string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Runtime/{file}.cs"));
				LogAssert.IsFalse(string.IsNullOrEmpty(guid), $"{file}.cs is in the module");
				foreach (string race in new[] { "Human", "Elf", "Orc" })
				{
					LogAssert.IsFalse(ProjectFiles.ReadAllText($"Assets/Prefabs/Shared/Entity/PlayableCharacters/{race}.prefab").Contains(guid),
						$"{race}.prefab does not author {file}");
				}
				LogAssert.IsFalse(ProjectFiles.ReadAllText("Assets/FishMMO/Shared/Implementation/Entity/PlayerCharacter.cs").Contains($"RequireComponent(typeof({file}))"),
					$"the player no longer requires {file}");
			}
		}

		[Test]
		public void TheEquipment_SendsItsSpawnStateAsAPart_UnderAnIdOfItsOwn()
		{
			uint id = EquipmentController.EquipmentPayloadId;
			LogAssert.AreNotEqual(0u, id, "a part with spawn state has a non-zero payload id (0 means none)");
			LogAssert.AreEqual(CharacterParts.IdOf("items.equipment"), id, "from a name that never changes");

			var go = new GameObject("ItemsModule_EquipmentPayload");
			try
			{
				EquipmentController equipment = go.AddComponent<EquipmentController>();
				LogAssert.AreEqual(id, equipment.PayloadId, "the part reports it");
				// No other part of a player claims the same block.
				foreach (System.Type part in CharacterParts.PartsFor(typeof(PlayerCharacter)))
				{
					if (part == typeof(EquipmentController) || part.IsAbstract)
					{
						continue;
					}
					var probe = new GameObject("ItemsModule_PartProbe");
					try
					{
						CharacterPart other = (CharacterPart)probe.AddComponent(part);
						LogAssert.AreNotEqual(id, other.PayloadId, $"{part.Name} does not share the equipment's payload id");
					}
					finally
					{
						Object.DestroyImmediate(probe);
					}
				}
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void PredictionFindsTheEquipment_AfterThePartsAreAttached()
		{
			string prediction = Read("Assets/FishMMO/Shared/Implementation/Entity/Prediction/CharacterPredictionController.cs");
			int start = prediction.IndexOf("public override void OnStartNetwork()", System.StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "the prediction controller starts on the network");
			string body = prediction.Substring(start, prediction.IndexOf("\n\t\t}", start, System.StringComparison.Ordinal) - start);
			LogAssert.IsTrue(body.Contains("DiscoverControllers();"),
				"it finds its controllers again at network start: the equipment is a part BaseCharacter.Awake adds, and the two Awakes run in no fixed order");
			LogAssert.IsTrue(typeof(IPredictableController).IsAssignableFrom(typeof(EquipmentController)), "the equipment is a predicted controller");
		}

		[Test]
		public void AnObserverClientListensForSlotChanges_FromTheFirstEquipmentItReads()
		{
			string equipment = Read(ModuleFolder + "/Runtime/EquipmentController.cs");
			int read = equipment.IndexOf("public override void ReadPayload(NetworkConnection conn, Reader reader)", System.StringComparison.Ordinal);
			LogAssert.IsTrue(read >= 0, "the equipment reads its spawn payload");
			int open = equipment.IndexOf('{', read);
			string head = equipment.Substring(open, 120);
			LogAssert.IsTrue(head.Contains("RegisterObservedSlotHandlerOnce();"),
				"the observer handler is registered as the payload is read: a player's OnStartCharacter runs for the local player only");
			LogAssert.IsFalse(equipment.Contains("override void OnStartNetwork"), "a part has no OnStartNetwork");
		}

		[Test]
		public void TheSceneServerAdoptsTheInventorySystem_AndTheWorldGuiPlacesTheWindows()
		{
			string system = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Server/CharacterInventorySystem.asset"));
			string server = Read("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset");
			LogAssert.IsTrue(Regex.IsMatch(server, $@"- m_GUID: {system}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "the system carries the SceneServer_Systems label");
			LogAssert.IsFalse(Read("Assets/Scenes/Server/SceneServer.unity").Contains(system), "the scene server's own list does not name it");

			string client = Read("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset");
			string scene = Read("Assets/Scenes/Client/ClientWorldGUI.unity");
			foreach ((string prefab, string script) in new[] { ("UIInventory", "UITKInventory"), ("UIBank", "UITKBank"), ("UIEquipment", "UITKEquipment") })
			{
				string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Client/{prefab}.prefab"));
				LogAssert.IsTrue(Regex.IsMatch(client, $@"- m_GUID: {guid}\n    m_Address: {prefab}\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
					$"{prefab} carries the ClientWorldGUI_Panels label under its own name (the core opens it by that name)");
				string scriptGuid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Client/{script}.cs"));
				LogAssert.IsFalse(scene.Contains(scriptGuid), $"ClientWorldGUI.unity no longer carries {prefab}");
			}
		}

		[Test]
		public void TheCoreOpensTheWindowsByName()
		{
			string input = Read("Assets/FishMMO/Client/Input/PlayerInputController.cs");
			LogAssert.IsTrue(input.Contains("UIManager.ToggleVisibility(\"UIEquipment\");"), "the equipment key toggles the window by name");
			LogAssert.IsTrue(input.Contains("UIManager.ToggleVisibility(\"UIInventory\");"), "and the inventory key");
			LogAssert.IsFalse(Regex.IsMatch(input, @"\bUITK(Equipment|Inventory|Bank)\b"), "the input names none of the module's windows");
		}

		private static string Read(string path) => ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
	}
}
