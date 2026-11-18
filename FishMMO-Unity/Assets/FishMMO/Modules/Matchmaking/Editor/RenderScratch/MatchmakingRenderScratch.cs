using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.RenderScratch
{
	/// <summary>The Matchmaking module's part of the panel render rig: the dungeon finder window and the arena HUD.</summary>
	public static class MatchmakingRenderScratch
	{
		/// <summary>The dungeon finder's layout.</summary>
		public const string DungeonFinderUxml = "Assets/FishMMO/Modules/Matchmaking/Client/UIDungeonFinder.uxml";

		/// <summary>The arena HUD's layout.</summary>
		public const string ArenaHudUxml = "Assets/FishMMO/Modules/Matchmaking/Client/UIArenaHud.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			AllPanelsRender.ExtraPopulators["UIDungeonFinder"] = DungeonFinderPanel;
			if (!InterfaceScalingRender.Hud.Exists(m => m.Uxml == ArenaHudUxml))
			{
				InterfaceScalingRender.Hud.Add(new InterfaceScalingRender.HudMount { Uxml = ArenaHudUxml, PanelType = typeof(UITKArenaHud) });
			}
			if (!HudOverlapProbe.Mounts.Exists(m => m.Name == "UIArenaHud"))
			{
				HudOverlapProbe.Mounts.Add(new HudOverlapProbe.Mount { Name = "UIArenaHud", Uxml = ArenaHudUxml, Selector = "arenahud-root", PanelType = typeof(UITKArenaHud) });
			}
		}

		/// <summary>Dungeon Finder showing a populated instance list at a chosen difficulty.</summary>
		public static void DungeonFinderPanel(GameObject h, UIDocument d)
		{
			UITKDungeonFinder panel = Panels.Character<UITKDungeonFinder>(h, d);
			DungeonTemplate dungeon = Panels.SeedDungeon();

			// The entrance the player interacted with, then the list the server answers with.
			Panels.BroadcastOffline(panel, "OnClientDungeonFinderBroadcastReceived", new DungeonFinderBroadcast
			{
				InteractableID = 77001,
				DungeonTemplateID = dungeon != null ? dungeon.ID : 0,
			}, sendSite: "UITKDungeonFinder.RequestList");

			/* The panel resets to the first difficulty on every open, and discards a reply whose
			 * difficulty is not the one it is showing. Echo back whatever it settled on rather
			 * than naming an index and having the list silently dropped. */
			int difficulty = Panels.Read<int>(panel, "selectedDifficulty");

			Panels.Broadcast(panel, "OnClientDungeonFinderListResultBroadcastReceived",
				new DungeonFinderListResultBroadcast
				{
					InteractableID = 77001,
					Difficulty = difficulty,
					Reason = DungeonListFailureReason.None,
					Instances = new[]
					{
						Instance(88001, "Brynn", 4, 5, 1820, false, true),
						Instance(88002, "Kaelen Duskwater", 5, 5, 640, false, false),
						Instance(88003, "Vaskir", 2, 5, 3540, true, false),
						Instance(88004, "Elowen", 1, 5, 2995, false, false),
					},
				});

			Panels.Tick(panel);
		}

		private static DungeonInstanceEntry Instance(long id, string leader, int members, int max,
			int remaining, bool loading, bool ownParty)
		{
			return new DungeonInstanceEntry
			{
				InstanceID = id, LeaderName = leader, MemberCount = members, MaxMembers = max,
				RemainingSeconds = remaining, IsLoading = loading, IsOwnParty = ownParty,
			};
		}
	}
}
