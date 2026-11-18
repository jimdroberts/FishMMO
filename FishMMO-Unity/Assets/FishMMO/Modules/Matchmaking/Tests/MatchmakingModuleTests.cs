using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Matchmaking module (Phase 3, 2026-11-17): one MatchmakingSystem runs the group finder, the dungeon finder and
	/// arenas; the interactable system and the world's entrances and boards run without it.
	/// </summary>
	[TestFixture]
	public class MatchmakingModuleTests
	{
		private const string ModuleFolder = "Assets/FishMMO/Modules/Matchmaking";

		[Test]
		public void TheSceneServerAdoptsTheMatchmakingSystem_WithTheWorldScenes()
		{
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(ModuleFolder + "/Server/MatchmakingSystem.asset"));
			string group = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Server_Static_Permanent.asset").Replace("\r\n", "\n");
			LogAssert.IsTrue(Regex.IsMatch(group, $@"- m_GUID: {guid}\n(?:    .*\n)*?    - SceneServer_Systems\n"), "it carries the SceneServer_Systems label");
			string cache = ProjectFiles.ReadAllText("Assets/Prefabs/Server/SceneServer/InteractableSystem.asset");
			string scenes = Regex.Match(cache, @"worldSceneDetailsCache: \{fileID: 11400000, guid: ([0-9a-f]{32})").Groups[1].Value;
			LogAssert.IsTrue(scenes.Length == 32 && ProjectFiles.ReadAllText(ModuleFolder + "/Server/MatchmakingSystem.asset").Contains("guid: " + scenes),
				"it reads the same world scene details the interactable system does");
		}

		[Test]
		public void TheInteractableSystemRunsNoMatchmaking()
		{
			string interactables = ProjectFiles.ReadAllText("Assets/FishMMO/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.cs");
			LogAssert.IsFalse(interactables.Contains("InitializeGroupFinder") || interactables.Contains("DungeonFinderListBroadcast") ||
				interactables.Contains("/closedungeon"), "no group finder, dungeon finder or dungeon command in the interactable system");
			string system = ProjectFiles.ReadAllText(ModuleFolder + "/Server/MatchmakingSystem.cs");
			LogAssert.IsTrue(system.Contains("InitializeGroupFinder();") && system.Contains("RegisterBroadcast<DungeonFinderListBroadcast>") &&
				system.Contains("\"/closedungeon\""), "the matchmaking system runs them");
		}

		[Test]
		public void TheArenaRelationIsTheModules_AndStillAskedFirst()
		{
			var order = HarmRule.Relations.Select(r => r.GetType()).ToList();
			int arena = order.IndexOf(typeof(ArenaHarmRelation));
			int wildlife = order.IndexOf(typeof(WildlifeHarmRelation));
			LogAssert.IsTrue(arena >= 0 && arena < wildlife, "inside an arena the match's sides decide first");
		}

		[Test]
		public void TheWindowsArePlacedIntoTheWorldGui()
		{
			string client = ProjectFiles.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Client_Dynamic.asset").Replace("\r\n", "\n");
			string scene = ProjectFiles.ReadAllText("Assets/Scenes/Client/ClientWorldGUI.unity");
			foreach ((string prefab, string script) in new[]
			{
				("UIArenaBoard", "UITKArenaBoard"), ("UIArenaHud", "UITKArenaHud"), ("UIArenaResults", "UITKArenaResults"),
				("UIArenaScoreboard", "UITKArenaScoreboard"), ("UIDungeonFinder", "UITKDungeonFinder"),
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
		public void TheDungeonFinderPublicToggleIsResetForEveryEntrance()
		{
			// Moved from PanelReopenStateTests with the window.
			string code = string.Join("\n", ProjectFiles.ReadAllText(ModuleFolder + "/Client/UITKDungeonFinder.cs").Replace("\r\n", "\n")
				.Split('\n').Where(l => !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("*")));
			const string reset = "publicToggle?.SetValueWithoutNotify(false);";
			LogAssert.AreEqual(2, Regex.Matches(code, Regex.Escape(reset)).Count,
				"the toggle persists with the tree, so both a fresh open and a cleared dungeon put it back to private");
			int clear = code.IndexOf("private void ClearDungeon()", System.StringComparison.Ordinal);
			LogAssert.IsTrue(clear >= 0, "ClearDungeon exists");
			int depth = 0, open = code.IndexOf('{', clear), close = open;
			for (; close < code.Length; ++close)
			{
				if (code[close] == '{') ++depth;
				else if (code[close] == '}' && --depth == 0) break;
			}
			LogAssert.IsTrue(code.Substring(open, close - open).Contains(reset), "ClearDungeon resets it");
		}
	}
}
