using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FishMMO.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The World Interactables module: the wiring that lets the folder be removed (a label-adopted system,
	/// label-placed windows, actions that keep resolving in the trigger assets), and pins moved here with the kinds they
	/// pin (from SpawnTableBakeTests and ObserverPayloadShapingTests).
	/// </summary>
	[TestFixture]
	public class WorldInteractablesModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/WorldInteractables";

		[Test]
		public void TheModuleIsDeclared()
		{
			var problems = new List<string>();
			List<GameModule> found = ModuleCatalog.Discover(new[] { typeof(WorldInteractablesModule).Assembly }, problems);
			LogAssert.IsTrue(found.Any(m => m.Id == WorldInteractablesModule.ModuleId), "the module declares itself ([assembly: GameModule])");
		}

		[Test]
		public void TheSceneServerAdoptsTheSystem_AndTheWorldGuiPlacesTheWindows()
		{
			string system = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Server/WorldInteractableSystem.asset"));
			string server = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(server, $@"- m_GUID: {system}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "the system carries the SceneServer_Systems label");

			string client = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset").Replace("\r\n", "\n");
			string scene = ProjectFiles.ReadAllText("Assets/Scenes/Client/ClientWorldGUI.unity");
			foreach (string window in new[] { "Container", "NPCDialogue", "Lore", "Shrine", "Gathering", "CapturePoint" })
			{
				string prefab = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Client/UI{window}.prefab"));
				LogAssert.IsTrue(Regex.IsMatch(client, $@"- m_GUID: {prefab}\n    m_Address: UI{window}\n(?:    .*\n)*?    - ClientWorldGUI_Panels\n"),
					$"UI{window} carries the ClientWorldGUI_Panels label, under its UIManager name");
				string script = AssetDatabase.AssetPathToGUID(ProjectFiles.Path($"{ModuleFolder}/Client/UITK{window}.cs"));
				LogAssert.IsFalse(scene.Contains(script), $"ClientWorldGUI.unity no longer carries UITK{window}");
			}
		}

		[Test]
		public void TheMovedActionsKeepResolvingInTheTriggerAssets()
		{
			/* A trigger stores its actions by class, namespace and assembly; without MovedFrom every trigger naming one of
			 * these would lose it the first time it loaded after the move. */
			foreach (System.Type action in new[]
			{
				typeof(BindstoneAction), typeof(CapturePointAction), typeof(GatheringNodeAction), typeof(LoreObjectAction),
				typeof(ShrineAction), typeof(SwitchAction), typeof(SendContainerOpenBroadcastAction), typeof(DisplayDialogueAction),
			})
			{
				var moved = action.GetCustomAttribute<UnityEngine.Scripting.APIUpdating.MovedFromAttribute>();
				LogAssert.IsNotNull(moved, $"{action.Name} declares where it moved from");
			}
		}

		[Test]
		public void TheCoreInteractionSystemNamesNoDialogueOrContainer()
		{
			string core = SourceScanPins.CodeOnly(ProjectFiles.ReadAllText("Assets/FishMMO/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.cs"));
			LogAssert.IsFalse(Regex.IsMatch(core, @"\b(Dialogue\w*|Container\w*Broadcast)\b"), "the core system handles the interaction request only");
			string system = ProjectFiles.ReadAllText(ModuleFolder + "/Server/WorldInteractableSystem.cs");
			LogAssert.IsTrue(system.Contains("interactableSystem.TryBeginInteraction(connectionId, out guardKey)"),
				"the module's requests take the core's one interaction guard");
			LogAssert.IsTrue(system.Contains("interactableSystem.TryResolveSceneObject(sceneObjectID, characterSceneHandle, out sceneObject)"),
				"and resolve object IDs the way the core does");
		}

		// ── Moved from SpawnTableBakeTests ───────────────────────────────────────

		[Test]
		public void TheDungeonChest_IsAContainerThatOpensAndHoldsLoot()
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectFiles.Path("Assets/FishMMO/Modules/WorldInteractables/Prefabs/Containers/Dungeon Chest.prefab"));
			Assert.IsNotNull(prefab, "the Dungeon chest prefab is missing");

			Container container = prefab.GetComponent<Container>();
			Assert.IsNotNull(container);
			Assert.IsNull(prefab.GetComponent<WorldItem>(), "the chest was cloned from a world item and must not still be one");
			Assert.IsNotNull(container.Template, "a container without a template refuses every interaction");
			Assert.IsNotNull(container.Template.LootTable, "a chest without a loot table spawns empty");
			Assert.IsNotEmpty(container.Template.LootTable.Entries);
			Assert.IsTrue(container.Template.LootTable.Entries.All(e => e != null && e.ItemTemplate != null));
			Assert.Greater(container.Template.SlotCount, 0);

			Assert.IsTrue(container.OnInteractTriggers.Any(t => t != null &&
					t.OnConditionsMetActions.Any(a => a is SendContainerOpenBroadcastAction)),
				"nothing would open the chest's window");

			SceneObjectNamer namer = prefab.GetComponent<SceneObjectNamer>();
			Assert.AreEqual(SceneObjectNamingMode.Authored, namer.Settings.Mode,
				"a chest has no race or biome to be named after; any other mode logs a naming failure every spawn");

			/* The client resolves the window's template by ID, which only exists for a template
			 * loaded through the shared addressable label. */
			string groups = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Shared_Static_Permanent.asset");
			Assert.That(groups, Does.Contain(AssetDatabase.AssetPathToGUID(ProjectFiles.Path(AssetDatabase.GetAssetPath(container.Template)))),
				"the container template must be in the shared addressable group");
			Assert.That(groups, Does.Contain(AssetDatabase.AssetPathToGUID(ProjectFiles.Path(AssetDatabase.GetAssetPath(prefab)))),
				"the chest prefab must be in the shared addressable group");
			Assert.IsTrue(prefab.GetComponent<FishNet.Object.NetworkObject>().GetIsSpawnable(), "a spawner can only spawn a spawnable prefab");
		}

		// ── Moved from ObserverPayloadShapingTests ───────────────────────────────

		/// <summary>Declared-only lookup, so an inherited implementation does not count as one.</summary>
		private static MethodInfo DeclaredMethod(System.Type type, string name)
		{
			return type.GetMethod(name,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
		}

		[Test]
		public void WorldContainerContents_AreNotInTheSpawnPayload()
		{
			Assert.IsNull(DeclaredMethod(typeof(Container), "WritePayload"),
				"A chest's contents are viewer-scoped through ContainerOpenBroadcast; nothing on a client reads a world Container's Items.");
			Assert.IsNull(DeclaredMethod(typeof(Container), "ReadPayload"),
				"Writes and reads are paired: neither half may come back on its own.");

			// The scene-object ID still travels, which is what a client names to interact at all.
			Assert.IsNotNull(DeclaredMethod(typeof(Interactable), "WritePayload"));
			Assert.IsNotNull(DeclaredMethod(typeof(Interactable), "ReadPayload"));
		}

		[Test]
		public void GatheringNodeCharges_AreNotInTheSpawnPayload()
		{
			Assert.IsNull(DeclaredMethod(typeof(GatheringNode), "WritePayload"),
				"RemainingUses answered a question the node's existence already answers, and went stale the moment anyone gathered.");
			Assert.IsNull(DeclaredMethod(typeof(GatheringNode), "ReadPayload"));
		}

		[Test]
		public void GatheringNode_AllowsGathering_AsksTheChargesOnlyWhereTheyAreTrue()
		{
			// Not authoritative: defer, whatever the local count happens to say.
			Assert.IsTrue(GatheringNode.AllowsGathering(0, false));
			Assert.IsTrue(GatheringNode.AllowsGathering(-5, false));
			Assert.IsTrue(GatheringNode.AllowsGathering(3, false));

			// Authoritative: the count decides.
			Assert.IsTrue(GatheringNode.AllowsGathering(1, true));
			Assert.IsFalse(GatheringNode.AllowsGathering(0, true));
			Assert.IsFalse(GatheringNode.AllowsGathering(-1, true));
		}
	}
}
