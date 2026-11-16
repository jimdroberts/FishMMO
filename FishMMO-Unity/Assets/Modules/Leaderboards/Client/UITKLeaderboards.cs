using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit Leaderboards window: PvP and PvE boards, a page at a time, with the player's own
	/// standing wherever it falls.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The panel holds no authority and computes no ranks. Every row, rank and count is the
	/// server's, which read them from the database and may be sharing that read with other
	/// players for up to its cache lifetime; the footer shows how old the read is, so a number
	/// that has not moved yet is explained rather than suspicious.
	/// </para>
	/// <para>
	/// Boards are <see cref="LeaderboardTemplate"/> assets, listed under their category in their
	/// authored order. A board is asked for page by page through a
	/// <see cref="LeaderboardRequester"/>, which keeps one request outstanding and folds clicks
	/// made while waiting into the next one.
	/// </para>
	/// <para>
	/// Opened from the game menu, which stays open behind it, so it sits on the popup layer the
	/// menu's other destinations use; on the window layer it would open underneath the menu.
	/// </para>
	/// </remarks>
	public class UITKLeaderboards : UITKCharacterControl
	{
		/// <summary>The panel's GameObject name in ClientWorldGUI, which is its UIManager key.</summary>
		public const string PanelName = "UILeaderboards";

		private const string SUBTITLE_NAME = "leaderboard-subtitle";
		private const string CLOSE_BUTTON_NAME = "leaderboard-close-btn";
		private const string CATEGORY_PVP_NAME = "leaderboard-category-pvp";
		private const string CATEGORY_PVE_NAME = "leaderboard-category-pve";
		private const string BOARDS_NAME = "leaderboard-boards";
		private const string ABOUT_NAME = "leaderboard-about";
		private const string DESCRIPTION_NAME = "leaderboard-description";
		private const string LIST_SCROLL_NAME = "leaderboard-list-scroll";
		private const string LIST_NAME = "leaderboard-list";
		private const string STANDING_NAME = "leaderboard-standing";
		private const string STANDING_TEXT_NAME = "leaderboard-standing-text";
		private const string AGE_NAME = "leaderboard-age";
		private const string PREV_NAME = "leaderboard-prev";
		private const string PAGE_NAME = "leaderboard-page";
		private const string NEXT_NAME = "leaderboard-next";

		private const string TAB_CLASS = "fish-tab";
		private const string TAB_ACTIVE_CLASS = "fish-tab--active";
		private const string BOARD_TAB_CLASS = "leaderboard-board-tab";
		private const string HIDDEN_CLASS = "leaderboard-hidden";
		private const string STANDING_RANKED_CLASS = "leaderboard-standing--ranked";
		private const string ROW_CLASS = "leaderboard-row";
		private const string ROW_PODIUM_CLASS = "leaderboard-row--podium";
		private const string ROW_MINE_CLASS = "leaderboard-row--mine";

		/// <summary>Ranks at or above this carry the podium mark.</summary>
		private const int PodiumRank = 3;

		protected override UITKPanelLayer Layer => UITKPanelLayer.Popup;

		private Label subtitleLabel;
		private Button pvpButton;
		private Button pveButton;
		private VisualElement boardTabs;
		private VisualElement aboutBox;
		private Label descriptionLabel;
		private ScrollView listScroll;
		private VisualElement listBox;
		private VisualElement standingBox;
		private Label standingLabel;
		private Label ageLabel;
		private Button prevButton;
		private Label pageLabel;
		private Button nextButton;

		private LeaderboardCategory category = LeaderboardCategory.PvP;
		private readonly List<LeaderboardTemplate> boards = new List<LeaderboardTemplate>();
		private readonly Dictionary<LeaderboardCategory, int> lastBoardByCategory = new Dictionary<LeaderboardCategory, int>();

		/// <summary>The board on screen. 0 when the category has none.</summary>
		private int selectedTemplateID;

		/// <summary>The page on screen, or being fetched.</summary>
		private int page = 1;

		/// <summary>The last reply shown, for the selected board.</summary>
		private LeaderboardPageBroadcast? shown;

		/// <summary><c>Time.unscaledTime</c> when <see cref="shown"/> arrived, to age it locally.</summary>
		private float shownAt;

		/// <summary>Whole seconds of age last written to the footer, so it is rewritten once a second.</summary>
		private int shownAgeSeconds = -1;

		private readonly LeaderboardRequester requests = new LeaderboardRequester();

		private LeaderboardTemplate SelectedBoard => selectedTemplateID != 0 ? LeaderboardTemplate.Get<LeaderboardTemplate>(selectedTemplateID) : null;

		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			boardTabs = root.Q(BOARDS_NAME);
			aboutBox = root.Q(ABOUT_NAME);
			descriptionLabel = root.Q<Label>(DESCRIPTION_NAME);
			listScroll = root.Q<ScrollView>(LIST_SCROLL_NAME);
			listBox = root.Q(LIST_NAME);
			standingBox = root.Q(STANDING_NAME);
			standingLabel = root.Q<Label>(STANDING_TEXT_NAME);
			ageLabel = root.Q<Label>(AGE_NAME);
			pageLabel = root.Q<Label>(PAGE_NAME);

			Button closeButton = root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}

			pvpButton = root.Q<Button>(CATEGORY_PVP_NAME);
			if (pvpButton != null)
			{
				pvpButton.clicked += () => SelectCategory(LeaderboardCategory.PvP);
			}
			pveButton = root.Q<Button>(CATEGORY_PVE_NAME);
			if (pveButton != null)
			{
				pveButton.clicked += () => SelectCategory(LeaderboardCategory.PvE);
			}

			prevButton = root.Q<Button>(PREV_NAME);
			if (prevButton != null)
			{
				prevButton.clicked += () => SetPage(page - 1);
			}
			nextButton = root.Q<Button>(NEXT_NAME);
			if (nextButton != null)
			{
				nextButton.clicked += () => SetPage(page + 1);
			}
		}

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<LeaderboardPageBroadcast>(OnClientLeaderboardPageReceived);
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<LeaderboardPageBroadcast>(OnClientLeaderboardPageReceived);
		}

		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			Render();
		}

		/// <summary>
		/// Re-reads the board on screen every time the window opens. The server answers from its
		/// cache when it has one, so reopening is cheap and never shows a page older than that.
		/// </summary>
		protected override void OnAfterShow()
		{
			SelectCategory(category);
			requests.Refresh();
			Render();
		}

		/// <summary>Sends what the requester says to, and keeps the footer's age current.</summary>
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}

			bool wasWaiting = requests.Waiting;
			requests.Tick(Time.unscaledTime, SendRequest);
			if (wasWaiting && requests.GaveUp)
			{
				Render();
				return;
			}

			if (shown.HasValue && !shown.Value.Unavailable)
			{
				int age = CurrentAgeSeconds();
				if (age != shownAgeSeconds)
				{
					RenderAge();
				}
			}
		}

		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			requests.Clear();
			shown = null;
			selectedTemplateID = 0;
			page = 1;
			lastBoardByCategory.Clear();
			Render();
		}

		// ──────────────────────────────────────────────────────────────────
		//  Selection
		// ──────────────────────────────────────────────────────────────────

		/// <summary>Shows a category, on the board last viewed in it or else its first.</summary>
		private void SelectCategory(LeaderboardCategory next)
		{
			category = next;
			boards.Clear();
			boards.AddRange(LeaderboardTemplate.InCategory(next));

			int boardID = 0;
			if (lastBoardByCategory.TryGetValue(next, out int remembered) && boards.Exists(b => b.ID == remembered))
			{
				boardID = remembered;
			}
			else if (boards.Count > 0)
			{
				boardID = boards[0].ID;
			}

			if (boardID != selectedTemplateID)
			{
				SelectBoard(boardID);
			}
			else
			{
				Render();
			}
		}

		/// <summary>
		/// Shows a board from its first page. Pressing the board already shown re-reads it, which is
		/// also how a board that could not be loaded is retried.
		/// </summary>
		private void SelectBoard(int templateID)
		{
			if (templateID != 0 && templateID == selectedTemplateID)
			{
				requests.Refresh();
				Render();
				return;
			}

			selectedTemplateID = templateID;
			page = 1;
			shown = null;
			shownAgeSeconds = -1;
			if (templateID != 0)
			{
				lastBoardByCategory[category] = templateID;
				requests.Want(templateID, page);
			}
			else
			{
				requests.Clear();
			}
			ResetScroll();
			Render();
		}

		/// <summary>Moves to another page of the board on screen, within the pages the server reported.</summary>
		private void SetPage(int next)
		{
			if (selectedTemplateID == 0 || !shown.HasValue)
			{
				return;
			}
			int clamped = LeaderboardPaging.ClampPage(next, shown.Value.PageCount);
			if (clamped == page)
			{
				return;
			}
			page = clamped;
			requests.Want(selectedTemplateID, page);
			Render();
		}

		private void SendRequest(int templateID, int requestedPage)
		{
			if (Client == null || Client.NetworkManager == null || !Client.NetworkManager.IsClientStarted)
			{
				return;
			}
			Client.Broadcast(new LeaderboardPageRequestBroadcast { TemplateID = templateID, Page = requestedPage });
		}

		private void OnClientLeaderboardPageReceived(LeaderboardPageBroadcast msg, Channel channel)
		{
			// Replies to the arena board's own requests arrive here too; only the page on screen counts.
			if (!requests.Accept(msg))
			{
				return;
			}
			bool pageChanged = !shown.HasValue || shown.Value.Page != msg.Page;
			shown = msg;
			shownAt = Time.unscaledTime;
			shownAgeSeconds = -1;
			if (pageChanged)
			{
				ResetScroll();
			}
			Render();
		}

		// ──────────────────────────────────────────────────────────────────
		//  Rendering
		// ──────────────────────────────────────────────────────────────────

		private void Render()
		{
			LeaderboardTemplate board = SelectedBoard;

			SetTabActive(pvpButton, category == LeaderboardCategory.PvP);
			SetTabActive(pveButton, category == LeaderboardCategory.PvE);
			BuildBoardTabs();

			if (subtitleLabel != null)
			{
				string season = board != null && shown.HasValue && !string.IsNullOrEmpty(shown.Value.SeasonName) ? $" · {shown.Value.SeasonName}" : string.Empty;
				subtitleLabel.text = board != null ? board.ResolvedDisplayName + season : "No leaderboards";
			}
			if (descriptionLabel != null)
			{
				descriptionLabel.text = board != null ? board.Description ?? string.Empty : string.Empty;
			}
			SetHidden(aboutBox, board == null || string.IsNullOrWhiteSpace(board.Description));

			BuildList(board);
			RenderStanding(board);
			RenderPager();
			RenderAge();
		}

		private void BuildBoardTabs()
		{
			if (boardTabs == null)
			{
				return;
			}
			boardTabs.Clear();
			SetHidden(boardTabs, boards.Count < 2);
			foreach (LeaderboardTemplate candidate in boards)
			{
				int id = candidate.ID;
				Button tab = new Button(() => SelectBoard(id)) { text = candidate.ResolvedDisplayName };
				tab.AddToClassList(TAB_CLASS);
				tab.AddToClassList(BOARD_TAB_CLASS);
				SetTabActive(tab, id == selectedTemplateID);
				boardTabs.Add(tab);
			}
		}

		private void BuildList(LeaderboardTemplate board)
		{
			if (listBox == null)
			{
				return;
			}
			listBox.Clear();

			if (board == null)
			{
				AddEmpty($"No {(category == LeaderboardCategory.PvP ? "PvP" : "PvE")} leaderboards are available.");
				return;
			}
			if (!shown.HasValue)
			{
				AddEmpty(requests.GaveUp ? "The leaderboard could not be loaded. Select the board again to retry." : "Loading…");
				return;
			}

			LeaderboardPageBroadcast reply = shown.Value;
			if (reply.Unavailable)
			{
				AddEmpty("This leaderboard is unavailable right now. Select the board again to retry.");
				return;
			}
			if (reply.Entries == null || reply.Entries.Length == 0)
			{
				if (reply.Page > 1)
				{
					AddEmpty("There are no ranks on this page.");
				}
				else if (board.Source == LeaderboardSource.ArenaSeasonRating && string.IsNullOrEmpty(reply.SeasonName))
				{
					AddEmpty("No ranked arena season is running yet.");
				}
				else if (board.Source == LeaderboardSource.ArenaSeasonRating)
				{
					AddEmpty($"No one has finished {board.MinimumGames} ranked {(board.MinimumGames == 1 ? "game" : "games")} this season yet.");
				}
				else
				{
					AddEmpty("No one is on this board yet.");
				}
				return;
			}

			VisualElement header = new VisualElement();
			header.AddToClassList("leaderboard-list__columns");
			header.Add(MakeHeaderCell("#", "leaderboard-col--rank"));
			header.Add(MakeHeaderCell("Name", "leaderboard-col--name"));
			header.Add(MakeHeaderCell(board.ResolvedScoreLabel, "leaderboard-col--score"));
			if (board.ShowsRecord)
			{
				header.Add(MakeHeaderCell("W", "leaderboard-col--num"));
				header.Add(MakeHeaderCell("L", "leaderboard-col--num"));
			}
			listBox.Add(header);

			long mine = Character != null ? Character.ID : 0;
			foreach (LeaderboardEntry e in reply.Entries)
			{
				VisualElement row = new VisualElement();
				row.AddToClassList(ROW_CLASS);
				if (e.Rank > 0 && e.Rank <= PodiumRank)
				{
					row.AddToClassList(ROW_PODIUM_CLASS);
				}
				if (mine != 0 && e.CharacterID == mine)
				{
					row.AddToClassList(ROW_MINE_CLASS);
				}
				row.Add(MakeCell(e.Rank.ToString(CultureInfo.CurrentCulture), "leaderboard-col--rank"));
				// A player-chosen name: never parsed as markup.
				Label name = MakeCell(e.CharacterName ?? string.Empty, "leaderboard-col--name");
				name.enableRichText = false;
				row.Add(name);
				row.Add(MakeCell(FormatNumber(e.Score), "leaderboard-col--score"));
				if (board.ShowsRecord)
				{
					row.Add(MakeCell(e.Wins.ToString(CultureInfo.CurrentCulture), "leaderboard-col--num"));
					row.Add(MakeCell(e.Losses.ToString(CultureInfo.CurrentCulture), "leaderboard-col--num"));
				}
				listBox.Add(row);
			}
		}

		private void RenderStanding(LeaderboardTemplate board)
		{
			if (standingBox == null || standingLabel == null)
			{
				return;
			}

			bool hasAnswer = board != null && shown.HasValue && !shown.Value.Unavailable;
			SetHidden(standingBox, !hasAnswer);
			if (!hasAnswer)
			{
				standingBox.RemoveFromClassList(STANDING_RANKED_CLASS);
				return;
			}

			LeaderboardPageBroadcast reply = shown.Value;
			if (reply.YourRank > 0)
			{
				standingLabel.text = $"You are #{FormatNumber(reply.YourRank)} of {FormatNumber(reply.TotalRanked)} with {FormatNumber(reply.YourScore)} {board.ResolvedScoreLabel.ToLowerInvariant()}.";
				standingBox.AddToClassList(STANDING_RANKED_CLASS);
				return;
			}

			standingBox.RemoveFromClassList(STANDING_RANKED_CLASS);
			standingLabel.text = board.Source == LeaderboardSource.ArenaSeasonRating
				? $"You are not ranked yet. Players appear after {board.MinimumGames} ranked {(board.MinimumGames == 1 ? "game" : "games")} this season."
				: "You are not on this board yet.";
		}

		private void RenderPager()
		{
			bool hasPages = shown.HasValue && !shown.Value.Unavailable;
			int pageCount = hasPages ? Mathf.Max(1, shown.Value.PageCount) : 1;
			bool waiting = requests.Waiting;

			if (pageLabel != null)
			{
				pageLabel.text = hasPages ? $"Page {page} of {pageCount}" : string.Empty;
			}
			prevButton?.SetEnabled(hasPages && !waiting && page > 1);
			nextButton?.SetEnabled(hasPages && !waiting && page < pageCount);
		}

		private void RenderAge()
		{
			if (ageLabel == null)
			{
				return;
			}
			if (!shown.HasValue || shown.Value.Unavailable)
			{
				ageLabel.text = requests.Waiting ? "Loading…" : string.Empty;
				shownAgeSeconds = -1;
				return;
			}

			int age = CurrentAgeSeconds();
			shownAgeSeconds = age;
			string when = age < 5 ? "just now" : (age < 60 ? $"{age}s ago" : $"{age / 60}m ago");
			ageLabel.text = requests.Waiting ? $"Updated {when} · loading…" : $"Updated {when}";
		}

		/// <summary>The server's age of the read when it sent it, plus the time since it arrived.</summary>
		private int CurrentAgeSeconds()
		{
			if (!shown.HasValue)
			{
				return 0;
			}
			float local = Mathf.Max(0f, Time.unscaledTime - shownAt);
			long age = (long)shown.Value.AgeSeconds + (long)local;
			return age > int.MaxValue ? int.MaxValue : (int)age;
		}

		private void ResetScroll()
		{
			// The scroll offset persists with the tree; a new board or page opens at the top.
			if (listScroll != null)
			{
				listScroll.scrollOffset = Vector2.zero;
			}
		}

		private void AddEmpty(string text)
		{
			Label empty = new Label(text);
			empty.AddToClassList("fish-hint");
			empty.AddToClassList("leaderboard-list__empty");
			listBox.Add(empty);
		}

		private static Label MakeCell(string text, string cls)
		{
			Label cell = new Label(text);
			cell.AddToClassList("fish-label");
			cell.AddToClassList(cls);
			return cell;
		}

		private static Label MakeHeaderCell(string text, string cls)
		{
			Label cell = MakeCell(text, cls);
			cell.AddToClassList("fish-hint");
			return cell;
		}

		private static string FormatNumber(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

		private static void SetTabActive(Button tab, bool active)
		{
			if (tab == null) return;
			if (active) tab.AddToClassList(TAB_ACTIVE_CLASS);
			else tab.RemoveFromClassList(TAB_ACTIVE_CLASS);
		}

		private static void SetHidden(VisualElement element, bool hidden)
		{
			if (element == null) return;
			if (hidden) element.AddToClassList(HIDDEN_CLASS);
			else element.RemoveFromClassList(HIDDEN_CLASS);
		}
	}
}
