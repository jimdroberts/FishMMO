using System;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Transporting;
using FishMMO.Logging;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The Leaderboards module's client side, plugged into the game: its game menu entry and the arena board's season
	/// standings. The panel itself is a prefab in this module (<c>UILeaderboards.prefab</c>), placed in ClientWorldGUI by
	/// <see cref="ModulePanelHost"/>.
	/// </summary>
	public static class LeaderboardsClient
	{
		/// <summary>The game menu entry's id.</summary>
		public const string MenuEntryId = "leaderboards";

		/// <summary>Where the entry sits among the modules' entries.</summary>
		public const int MenuOrder = 100;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Install()
		{
			GameMenuEntries.Register(MenuEntryId, "Leaderboards", MenuOrder, OpenPanel);
			ArenaStandings.Source = new LeaderboardArenaStandings();
		}

		/// <summary>Opens the Leaderboards window. The menu stays open behind it.</summary>
		/// <remarks>
		/// Reached from the menu rather than a key binding: boards are looked at now and then, not mid-fight, and the
		/// menu is the one panel a player can always open. The window asks the server for its board as it opens.
		/// </remarks>
		public static void OpenPanel()
		{
			if (UIManager.TryGetTK(UITKLeaderboards.PanelName, out UITKLeaderboards leaderboards))
			{
				leaderboards.Show();
				return;
			}
			Log.Warning("Leaderboards", "Leaderboards are not available right now: no UILeaderboards panel is loaded.");
		}
	}

	/// <summary>
	/// The arena board's season standings: the arena rating board's first page, as <c>LeaderboardSystem</c> serves it
	/// to the Leaderboards window too (one read, one set of rules, whichever window shows it).
	/// </summary>
	public sealed class LeaderboardArenaStandings : IArenaStandingsSource
	{
		private readonly LeaderboardRequester requests = new LeaderboardRequester();
		private LeaderboardPageBroadcast? page;
		private int attached;

		public event Action Changed;

		public void Attach(Client client)
		{
			if (Client.NetworkManager == null)
			{
				return;
			}
			if (attached++ == 0)
			{
				Client.NetworkManager.ClientManager.RegisterBroadcast<LeaderboardPageBroadcast>(OnPage);
			}
		}

		public void Detach(Client client)
		{
			if (Client.NetworkManager == null || attached == 0)
			{
				return;
			}
			if (--attached == 0)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<LeaderboardPageBroadcast>(OnPage);
			}
		}

		public void Open(Client client)
		{
			if (page.HasValue)
			{
				return;
			}
			LeaderboardTemplate board = LeaderboardTemplate.FirstArenaRatingBoard();
			if (board == null)
			{
				requests.Clear();
				return;
			}
			requests.Want(board.ID, 1);
		}

		public void Reset()
		{
			page = null;
			requests.Clear();
		}

		public void Tick(Client client, float now)
		{
			bool wasWaiting = requests.Waiting;
			requests.Tick(now, (templateID, pageNumber) =>
			{
				if (Client.NetworkManager == null || !Client.NetworkManager.IsClientStarted)
				{
					return;
				}
				Client.Broadcast(new LeaderboardPageRequestBroadcast { TemplateID = templateID, Page = pageNumber });
			});
			if (wasWaiting && requests.GaveUp)
			{
				Changed?.Invoke();
			}
		}

		private void OnPage(LeaderboardPageBroadcast msg, Channel channel)
		{
			// Replies for the Leaderboards window's other pages and boards arrive here too.
			if (!requests.Accept(msg))
			{
				return;
			}
			page = msg;
			Changed?.Invoke();
		}

		public ArenaStandingsView Describe(long viewerCharacterID, List<ArenaStandingRow> rows)
		{
			rows.Clear();
			LeaderboardTemplate board = LeaderboardTemplate.FirstArenaRatingBoard();
			if (board == null)
			{
				return new ArenaStandingsView { Notice = "No arena leaderboard is configured." };
			}
			if (!page.HasValue)
			{
				return new ArenaStandingsView { Notice = requests.GaveUp ? "The leaderboard could not be loaded. Reopen the board to try again." : "Loading the season leaderboard…" };
			}
			LeaderboardPageBroadcast reply = page.Value;
			if (reply.Unavailable)
			{
				return new ArenaStandingsView { Notice = "The leaderboard is unavailable right now. Reopen the board to try again." };
			}
			string season = string.IsNullOrEmpty(reply.SeasonName) ? "This season" : reply.SeasonName;
			string games = $"{board.MinimumGames} ranked {(board.MinimumGames == 1 ? "game" : "games")}";
			if (reply.Entries == null || reply.Entries.Length == 0)
			{
				return new ArenaStandingsView
				{
					Notice = string.IsNullOrEmpty(reply.SeasonName)
						? "No ranked season is running yet."
						: $"{season} has no ranked players yet. Players appear after {games}.",
				};
			}

			bool onPage = false;
			foreach (LeaderboardEntry e in reply.Entries)
			{
				bool mine = viewerCharacterID != 0 && e.CharacterID == viewerCharacterID;
				onPage |= mine;
				rows.Add(new ArenaStandingRow { Rank = e.Rank, Name = e.CharacterName, Score = e.Score, Wins = e.Wins, Losses = e.Losses, Mine = mine });
			}

			/* The board shows its first page only; a player further down is told their rank here rather than told they
			 * are unranked, which the old top-50 board did to everyone below fiftieth. The full board, every page of it,
			 * is in the Leaderboards window. */
			string notice = null;
			if (reply.YourRank > 0)
			{
				if (!onPage)
				{
					notice = $"You are #{reply.YourRank} of {reply.TotalRanked} with {board.ResolvedScoreLabel.ToLowerInvariant()} {reply.YourScore}. See every rank under Leaderboards in the game menu.";
				}
			}
			else
			{
				notice = $"You are not ranked yet: players appear after {games} this season.";
			}
			return new ArenaStandingsView { SeasonTitle = season, ScoreTitle = board.ResolvedScoreLabel, Notice = notice };
		}
	}
}
