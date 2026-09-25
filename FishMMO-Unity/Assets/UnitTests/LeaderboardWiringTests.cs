using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The leaderboards (issue #261) as shipped content: the boards, what they read, where the
	/// window lives, how the server loads the system, and the kill count the PvE board ranks.
	/// </summary>
	/// <remarks>
	/// Each of these fails silently in the game rather than loudly in a build: a board that is not
	/// addressable is never offered, a panel missing from the scene opens nothing, a system missing
	/// from the scene server's list answers nothing, and a kill trigger missing from a prefab leaves
	/// a board that is always empty.
	/// </remarks>
	[TestFixture]
	public class LeaderboardWiringTests
	{
		private const string BoardsFolder = "Assets/Templates/Entity/Leaderboards";
		private const string AddressablesGroup = "Assets/AddressableAssetsData/AssetGroups/Shared_Static_Permanent.asset";
		private const string WorldGuiScene = "Assets/Scenes/Client/ClientWorldGUI.unity";
		private const string SceneServerScene = "Assets/Scenes/Server/SceneServer.unity";
		private const string SystemAsset = "Assets/Prefabs/Server/SceneServer/LeaderboardSystem.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Leaderboard/UILeaderboards.uxml";
		private const string MenuUxmlPath = "Assets/Scripts/Client/GUI/World/Menu/UIMenu.uxml";
		private const string KillTriggerPath = "Assets/Templates/Entity/ECA/Combat/Count Monster Kill.asset";
		private const string KillsAchievementPath = "Assets/Templates/Entity/Achievements/Combat/Kills.asset";

		private static readonly string[] PlayablePrefabs =
		{
			"Assets/Prefabs/Shared/Entity/PlayableCharacters/Human.prefab",
			"Assets/Prefabs/Shared/Entity/PlayableCharacters/Elf.prefab",
			"Assets/Prefabs/Shared/Entity/PlayableCharacters/Orc.prefab",
		};

		private static string Read(string path) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), path)).Replace("\r\n", "\n");

		private static List<LeaderboardTemplate> ShippedBoards()
		{
			return AssetDatabase.FindAssets("t:LeaderboardTemplate", new[] { BoardsFolder })
				.Select(AssetDatabase.GUIDToAssetPath)
				.Select(AssetDatabase.LoadAssetAtPath<LeaderboardTemplate>)
				.Where(b => b != null)
				.ToList();
		}

		/// <summary>Whether an asset is an entry of the permanent shared group, which both peers load and cache by ID.</summary>
		private static bool IsAddressable(Object asset, string group)
		{
			string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
			return Regex.IsMatch(group, $@"- m_GUID: {guid}\n(?:    .*\n)*?    - Shared_Static_Permanent\n");
		}

		// ── Boards ──────────────────────────────────────────────────────────────────────────

		[Test]
		public void EveryShippedBoard_IsConfigured_AndLoadedByBothPeers()
		{
			List<LeaderboardTemplate> boards = ShippedBoards();
			LogAssert.IsTrue(boards.Count > 0, $"boards ship in {BoardsFolder}");

			string group = Read(AddressablesGroup);
			foreach (LeaderboardTemplate board in boards)
			{
				LogAssert.IsTrue(board.IsConfigured(out string problem), $"{board.name} is {problem}");
				LogAssert.IsTrue(IsAddressable(board, group), $"{board.name} is in the Shared_Static_Permanent group, or no peer ever lists it");

				/* The attribute or achievement a board reads must be loaded by the same group: its
				 * cached ID is what the character save wrote as template_id, and an asset that was
				 * never loaded has no ID at all. */
				Object source = board.Source == LeaderboardSource.CharacterAttribute ? (Object)board.AttributeTemplate
					: board.Source == LeaderboardSource.Achievement ? board.AchievementTemplate : null;
				if (source != null)
				{
					LogAssert.IsTrue(IsAddressable(source, group), $"{board.name} reads {source.name}, which must be in the same group");
				}
			}
		}

		[Test]
		public void BothCategories_HaveABoard_AndPvPHasTheArenaSeason()
		{
			List<LeaderboardTemplate> boards = ShippedBoards();
			LogAssert.IsTrue(boards.Any(b => b.Category == LeaderboardCategory.PvP), "a PvP board ships");
			LogAssert.IsTrue(boards.Any(b => b.Category == LeaderboardCategory.PvE), "a PvE board ships");
			LogAssert.IsTrue(boards.Any(b => b.Category == LeaderboardCategory.PvP && b.Source == LeaderboardSource.ArenaSeasonRating),
				"the arena board's own leaderboard view reads the first arena rating board");
		}

		[Test]
		public void TheArenaBoard_NeverRanksARatingItsOwnerIsShownAsProvisional()
		{
			List<LeaderboardTemplate> arenaBoards = ShippedBoards().Where(b => b.Source == LeaderboardSource.ArenaSeasonRating).ToList();
			List<ArenaTemplate> arenas = AssetDatabase.FindAssets("t:ArenaTemplate")
				.Select(AssetDatabase.GUIDToAssetPath)
				.Select(AssetDatabase.LoadAssetAtPath<ArenaTemplate>)
				.Where(a => a != null)
				.ToList();
			LogAssert.IsTrue(arenas.Count > 0, "an arena ships to compare against");

			foreach (LeaderboardTemplate board in arenaBoards)
			{
				foreach (ArenaTemplate arena in arenas)
				{
					LogAssert.IsTrue(board.MinimumGames >= arena.PlacementGames,
						$"{board.name} ranks after {board.MinimumGames} games, but {arena.name}'s profile hides a rating until {arena.PlacementGames}");
				}
			}
		}

		// ── The window ──────────────────────────────────────────────────────────────────────

		[Test]
		public void TheWorldGuiScene_CarriesTheLeaderboardsWindow_StartingClosed()
		{
			UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
				WorldGuiScene, UnityEditor.SceneManagement.OpenSceneMode.Additive);
			try
			{
				LogAssert.IsTrue(scene.IsValid() && scene.isLoaded, $"{WorldGuiScene} must load");

				UITKLeaderboards found = null;
				foreach (GameObject rootObject in scene.GetRootGameObjects())
				{
					if (rootObject.name == UITKLeaderboards.PanelName)
					{
						found = rootObject.GetComponent<UITKLeaderboards>();
					}
				}

				LogAssert.IsNotNull(found, $"a root GameObject named {UITKLeaderboards.PanelName} carries the leaderboards component");
				LogAssert.IsNotNull(found.Document, "its Document is assigned");
				LogAssert.AreSame(found.gameObject, found.Document.gameObject, "to the UIDocument on the same object");
				LogAssert.AreEqual(UxmlPath, AssetDatabase.GetAssetPath(found.Document.visualTreeAsset), "rendering the leaderboards markup");
				LogAssert.IsFalse(found.StartOpen, "it starts closed");
				LogAssert.IsTrue(found.CloseOnEscape, "Escape closes it");
			}
			finally
			{
				UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
			}
		}

		[Test]
		public void TheWorldGuiScenesRoots_AreAllTransforms()
		{
			/* SceneRoots lists each root's Transform. The four arena panels were once listed by
			 * their GameObject IDs instead, which Unity tolerates on load and silently rewrites on
			 * the next save — a hand-edit mistake this pins so the next hand-added panel copies a
			 * correct list. */
			string yaml = Read(WorldGuiScene);
			int rootsAt = yaml.IndexOf("\nSceneRoots:\n");
			LogAssert.IsTrue(rootsAt >= 0, "the scene has a SceneRoots block");

			var transforms = new HashSet<string>(Regex.Matches(yaml, @"^--- !u!4 &(\d+)$", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value));
			foreach (Match root in Regex.Matches(yaml.Substring(rootsAt), @"- \{fileID: (\d+)\}"))
			{
				LogAssert.IsTrue(transforms.Contains(root.Groups[1].Value), $"root {root.Groups[1].Value} is a Transform");
			}
		}

		[Test]
		public void TheGameMenu_OpensTheWindow()
		{
			string menu = Read(MenuUxmlPath);
			LogAssert.IsTrue(menu.Contains("name=\"menu-leaderboards-btn\""), "the game menu has a Leaderboards button");
		}

		// ── The server ──────────────────────────────────────────────────────────────────────

		[Test]
		public void TheSceneServer_LoadsTheLeaderboardSystem()
		{
			string guid = AssetDatabase.AssetPathToGUID(SystemAsset);
			LogAssert.IsFalse(string.IsNullOrEmpty(guid), $"{SystemAsset} exists");
			LogAssert.IsTrue(Read(SceneServerScene).Contains($"  - {{fileID: 11400000, guid: {guid}, type: 2}}\n"),
				"the scene server's behaviour list includes it, or no board request is ever answered");
		}

		// ── The PvE count ───────────────────────────────────────────────────────────────────

		[Test]
		public void EveryPlayableCharacter_CountsTheMonstersItKills_IntoTheAchievementTheBoardRanks()
		{
			Trigger killTrigger = AssetDatabase.LoadAssetAtPath<Trigger>(KillTriggerPath);
			AchievementTemplate kills = AssetDatabase.LoadAssetAtPath<AchievementTemplate>(KillsAchievementPath);
			LogAssert.IsNotNull(killTrigger, $"{KillTriggerPath} exists");
			LogAssert.IsNotNull(kills, $"{KillsAchievementPath} exists");

			// What the trigger does: a kill of an NPC adds one to Kills.
			LogAssert.AreEqual(1, killTrigger.Conditions.Count, "one condition");
			IsCharacterNPCCondition npc = killTrigger.Conditions[0] as IsCharacterNPCCondition;
			LogAssert.IsNotNull(npc, "the victim must be an NPC: player kills belong to the PvP boards");
			LogAssert.IsFalse(npc.Invert, "not inverted");
			LogAssert.IsNull(npc.TargetSelector, "it tests the event's target, which on a kill is the victim");
			LogAssert.AreEqual(1, killTrigger.OnConditionsMetActions.Count, "one action");
			AchievementIncrementAction increment = killTrigger.OnConditionsMetActions[0] as AchievementIncrementAction;
			LogAssert.IsNotNull(increment, "the action increments an achievement");
			LogAssert.AreSame(kills, increment.AchievementTemplate, "the Kills achievement");
			LogAssert.IsTrue(increment.AmountValue is ConstantValue one && one.Amount == 1, "by one per kill");

			// Who runs it: every playable character, as the credited killer.
			foreach (string path in PlayablePrefabs)
			{
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				LogAssert.IsNotNull(prefab, $"{path} exists");
				CharacterDamageController damage = prefab.GetComponentInChildren<CharacterDamageController>(true);
				LogAssert.IsNotNull(damage, $"{prefab.name} has a damage controller");
				LogAssert.IsTrue(damage.OnKillTriggers.Contains(killTrigger), $"{prefab.name} counts its kills");
			}

			// And the board reads what is counted.
			LogAssert.IsTrue(ShippedBoards().Any(b => b.Category == LeaderboardCategory.PvE && b.Source == LeaderboardSource.Achievement && b.AchievementTemplate == kills),
				"a PvE board ranks that same achievement");
		}
	}
}
