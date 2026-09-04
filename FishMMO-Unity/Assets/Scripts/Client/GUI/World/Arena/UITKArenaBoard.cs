using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit arena board: lists the arenas a board offers, their modes and formats, and
	/// queues the player — alone or with their party — for one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Built on the same contract as the dungeon finder. The panel holds no authority and draws
	/// only what the server said; queuing is a request the server re-validates; the wait is shown
	/// in a strip from <see cref="GroupFinderStatusBroadcast"/> alone; closing the panel leaves
	/// the queue, and the server drops a waiter who walks away from the board.
	/// </para>
	/// <para>
	/// Statuses of the dungeon kind are ignored here, and the finder ignores the arena kind. A
	/// character is in at most one queue, so each panel showing only its own is enough.
	/// </para>
	/// </remarks>
	public class UITKArenaBoard : UITKCharacterControl
	{
		private const string SUBTITLE_NAME = "arena-subtitle";
		private const string ARENA_TABS_NAME = "arena-tabs";
		private const string IMAGE_NAME = "arena-image";
		private const string IMAGE_LABEL_NAME = "arena-image-label";
		private const string NAME_NAME = "arena-name";
		private const string MODE_NAME = "arena-mode";
		private const string DESCRIPTION_NAME = "arena-description";
		private const string FORMAT_TABS_NAME = "arena-formats";
		private const string RULES_NAME = "arena-rules";
		private const string RULES_TEXT_NAME = "arena-rules-text";
		private const string QUEUE_NAME = "arena-queue";
		private const string QUEUE_TEXT_NAME = "arena-queue-text";
		private const string QUEUE_BUTTON_NAME = "arena-queue-btn";
		private const string PARTY_BUTTON_NAME = "arena-party-btn";
		private const string CLOSE_BUTTON_NAME = "arena-close-btn";
		private const string VIEW_QUEUE_NAME = "arena-view-queue";
		private const string VIEW_HISTORY_NAME = "arena-view-history";
		private const string VIEW_BOARD_NAME = "arena-view-board";
		private const string PROFILE_NAME = "arena-profile";
		private const string PROFILE_TEXT_NAME = "arena-profile-text";
		private const string LIST_SCROLL_NAME = "arena-list-scroll";
		private const string LIST_NAME = "arena-list";
		private const string ABOUT_NAME = "arena-about";
		private const string FORMAT_TAB_RANKED_CLASS = "arena-format-tab--ranked";
		private const string LIST_HIDDEN_CLASS = "arena-list-scroll--hidden";
		private const string HIDDEN_CLASS = "arena-hidden";
		private const string PROFILE_LOCKED_CLASS = "arena-profile--locked";

		/// <summary>Which of the board's three views is showing.</summary>
		private enum BoardView : byte { Queue, History, Leaderboard }

		private const string TAB_CLASS = "fish-tab";
		private const string TAB_ACTIVE_CLASS = "fish-tab--active";
		private const string ARENA_TAB_CLASS = "arena-tab";
		private const string FORMAT_TAB_CLASS = "arena-format-tab";
		private const string TABS_SINGLE_CLASS = "arena-tabs--single";
		private const string QUEUE_HIDDEN_CLASS = "arena-queue--hidden";
		private const string QUEUE_MATCHED_CLASS = "arena-queue--matched";

		protected override UITKPanelLayer Layer => UITKPanelLayer.Window;

		/// <summary>How long the queue buttons stay disabled after a press.</summary>
		private const float QueueCooldownSeconds = 2.0f;

		private Label subtitleLabel;
		private VisualElement arenaTabs;
		private VisualElement arenaImage;
		private Label imagePlaceholderLabel;
		private Label nameLabel;
		private Label modeLabel;
		private Label descriptionLabel;
		private VisualElement formatTabs;
		private VisualElement rulesBox;
		private Label rulesLabel;
		private VisualElement queueBox;
		private Label queueLabel;
		private Button queueButton;
		private Button partyButton;
		private Button viewQueueButton;
		private Button viewHistoryButton;
		private Button viewBoardButton;
		private VisualElement profileBox;
		private Label profileLabel;
		private ScrollView listScroll;
		private VisualElement listBox;
		private VisualElement aboutBox;

		private BoardView view = BoardView.Queue;

		/// <summary>The server's last word on the player's season standing, if it has spoken.</summary>
		private ArenaProfileBroadcast? profile;
		private float profileReceivedAt;
		private ArenaHistoryBroadcast? history;
		private ArenaLeaderboardBroadcast? leaderboard;

		/// <summary>Board the panel is describing. 0 when it has none.</summary>
		private long currentInteractableID;

		/// <summary>Arenas the board offers, resolved from the open message.</summary>
		private readonly List<ArenaTemplate> arenas = new List<ArenaTemplate>();

		private int selectedArena;
		private int selectedFormat;

		/// <summary>The server's last word on the arena queue. Default when not queued.</summary>
		private GroupFinderStatusBroadcast queueStatus;

		private bool IsQueued => queueStatus.State != GroupFinderState.None;

		private float queueAllowedAt;

		private ArenaTemplate CurrentArena => selectedArena >= 0 && selectedArena < arenas.Count ? arenas[selectedArena] : null;

		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			arenaTabs = root.Q(ARENA_TABS_NAME);
			arenaImage = root.Q(IMAGE_NAME);
			imagePlaceholderLabel = root.Q<Label>(IMAGE_LABEL_NAME);
			nameLabel = root.Q<Label>(NAME_NAME);
			modeLabel = root.Q<Label>(MODE_NAME);
			descriptionLabel = root.Q<Label>(DESCRIPTION_NAME);
			formatTabs = root.Q(FORMAT_TABS_NAME);
			rulesBox = root.Q(RULES_NAME);
			rulesLabel = root.Q<Label>(RULES_TEXT_NAME);
			queueBox = root.Q(QUEUE_NAME);
			queueLabel = root.Q<Label>(QUEUE_TEXT_NAME);

			queueButton = root.Q<Button>(QUEUE_BUTTON_NAME);
			if (queueButton != null)
			{
				queueButton.clicked += () => OnClick_Queue(asParty: false);
			}

			partyButton = root.Q<Button>(PARTY_BUTTON_NAME);
			if (partyButton != null)
			{
				partyButton.clicked += () => OnClick_Queue(asParty: true);
			}

			Button closeButton = root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}

			viewQueueButton = root.Q<Button>(VIEW_QUEUE_NAME);
			viewHistoryButton = root.Q<Button>(VIEW_HISTORY_NAME);
			viewBoardButton = root.Q<Button>(VIEW_BOARD_NAME);
			if (viewQueueButton != null) viewQueueButton.clicked += () => SetView(BoardView.Queue);
			if (viewHistoryButton != null) viewHistoryButton.clicked += () => SetView(BoardView.History);
			if (viewBoardButton != null) viewBoardButton.clicked += () => SetView(BoardView.Leaderboard);

			profileBox = root.Q(PROFILE_NAME);
			profileLabel = root.Q<Label>(PROFILE_TEXT_NAME);
			listScroll = root.Q<ScrollView>(LIST_SCROLL_NAME);
			listBox = root.Q(LIST_NAME);
			aboutBox = root.Q(ABOUT_NAME);
		}

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<ArenaBoardBroadcast>(OnClientArenaBoardBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<GroupFinderStatusBroadcast>(OnClientGroupFinderStatusBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<ArenaProfileBroadcast>(OnClientArenaProfileReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<ArenaHistoryBroadcast>(OnClientArenaHistoryReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<ArenaLeaderboardBroadcast>(OnClientArenaLeaderboardReceived);
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ArenaBoardBroadcast>(OnClientArenaBoardBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<GroupFinderStatusBroadcast>(OnClientGroupFinderStatusBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ArenaProfileBroadcast>(OnClientArenaProfileReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ArenaHistoryBroadcast>(OnClientArenaHistoryReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ArenaLeaderboardBroadcast>(OnClientArenaLeaderboardReceived);
		}

		/// <summary>Opens the panel for one board.</summary>
		private void OnClientArenaBoardBroadcastReceived(ArenaBoardBroadcast msg, Channel channel)
		{
			if (Character == null)
			{
				Hide();
				return;
			}

			currentInteractableID = msg.InteractableID;

			arenas.Clear();
			if (msg.ArenaTemplateIDs != null)
			{
				foreach (int id in msg.ArenaTemplateIDs)
				{
					ArenaTemplate template = id != 0 ? ArenaTemplate.Get<ArenaTemplate>(id) : null;
					if (template != null)
					{
						arenas.Add(template);
					}
				}
			}

			/* Keep the queued arena and format selected while queued; otherwise start at the
			 * first. A queued player cannot be at another board: the leash drops them. */
			if (IsQueued)
			{
				int queuedIndex = arenas.FindIndex(a => a.ID == queueStatus.ArenaTemplateID);
				selectedArena = queuedIndex >= 0 ? queuedIndex : 0;
				selectedFormat = queuedIndex >= 0 ? queueStatus.Difficulty : 0;
			}
			else
			{
				selectedArena = 0;
				selectedFormat = 0;
			}

			view = BoardView.Queue;
			history = null;
			leaderboard = null;
			Show();
			ApplyBoard();
			RequestProfile();
		}

		protected override void OnAfterShow()
		{
			ApplyBoard();
		}

		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyBoard();
		}

		private void ApplyBoard()
		{
			ArenaTemplate arena = CurrentArena;

			if (subtitleLabel != null)
			{
				subtitleLabel.text = arena != null ? arena.ResolvedDisplayName : "Select an arena";
			}

			BuildArenaTabs();

			if (nameLabel != null)
			{
				nameLabel.text = arena != null ? arena.ResolvedDisplayName : string.Empty;
			}
			if (modeLabel != null)
			{
				modeLabel.text = arena != null ? ArenaTemplate.DescribeMode(arena.Mode) : string.Empty;
			}
			if (descriptionLabel != null)
			{
				descriptionLabel.text = arena != null && !string.IsNullOrWhiteSpace(arena.Description) ? arena.Description : string.Empty;
			}

			if (arenaImage != null)
			{
				Sprite icon = arena != null ? arena.Icon : null;
				arenaImage.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground();
				if (imagePlaceholderLabel != null)
				{
					imagePlaceholderLabel.style.display = icon != null ? DisplayStyle.None : DisplayStyle.Flex;
				}
			}

			BuildFormatTabs();
			ApplyRules();
			ApplyQueue();
			ApplyControls();
			ApplyProfile();
			ApplyView();
		}

		// ──────────────────────────────────────────────────────────────────
		//  Views: queue, history, leaderboard
		// ──────────────────────────────────────────────────────────────────

		private void SetView(BoardView next)
		{
			if (view == next)
			{
				return;
			}
			view = next;
			if (next == BoardView.History && !history.HasValue) RequestHistory();
			if (next == BoardView.Leaderboard && !leaderboard.HasValue) RequestLeaderboard();
			ApplyView();
		}

		/// <summary>Shows the chosen view. The queue view is the board as it always was; the others replace its middle with a list.</summary>
		private void ApplyView()
		{
			SetTabActive(viewQueueButton, view == BoardView.Queue);
			SetTabActive(viewHistoryButton, view == BoardView.History);
			SetTabActive(viewBoardButton, view == BoardView.Leaderboard);

			bool queueView = view == BoardView.Queue;
			SetHidden(arenaTabs, !queueView || arenas.Count < 2);
			SetHidden(aboutBox, !queueView);
			SetHidden(formatTabs, !queueView || (CurrentArena?.FormatCount ?? 0) < 2);
			SetHidden(rulesBox, !queueView || CurrentArena == null);

			if (listScroll != null)
			{
				if (queueView) listScroll.AddToClassList(LIST_HIDDEN_CLASS);
				else listScroll.RemoveFromClassList(LIST_HIDDEN_CLASS);
			}

			if (view == BoardView.History) BuildHistory();
			else if (view == BoardView.Leaderboard) BuildLeaderboard();
		}

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

		private void RequestProfile()
		{
			if (currentInteractableID == 0 || Client == null || Client.NetworkManager == null || !Client.NetworkManager.IsClientStarted)
			{
				return;
			}
			Client.Broadcast(new ArenaProfileRequestBroadcast { InteractableID = currentInteractableID });
		}

		private void RequestHistory()
		{
			if (currentInteractableID == 0) return;
			Client.Broadcast(new ArenaHistoryRequestBroadcast { InteractableID = currentInteractableID });
		}

		private void RequestLeaderboard()
		{
			if (currentInteractableID == 0) return;
			Client.Broadcast(new ArenaLeaderboardRequestBroadcast { InteractableID = currentInteractableID });
		}

		private void OnClientArenaProfileReceived(ArenaProfileBroadcast msg, Channel channel)
		{
			profile = msg;
			profileReceivedAt = Time.unscaledTime;
			ApplyProfile();
		}

		private void OnClientArenaHistoryReceived(ArenaHistoryBroadcast msg, Channel channel)
		{
			history = msg;
			if (view == BoardView.History) BuildHistory();
		}

		private void OnClientArenaLeaderboardReceived(ArenaLeaderboardBroadcast msg, Channel channel)
		{
			leaderboard = msg;
			if (view == BoardView.Leaderboard) BuildLeaderboard();
		}

		/// <summary>The player's season standing and any queue lock, as one line.</summary>
		private void ApplyProfile()
		{
			if (profileBox == null || profileLabel == null)
			{
				return;
			}

			if (!profile.HasValue)
			{
				profileLabel.text = "Season standing: …";
				profileBox.RemoveFromClassList(PROFILE_LOCKED_CLASS);
				return;
			}

			ArenaProfileBroadcast p = profile.Value;
			ArenaTemplate arena = CurrentArena;
			int placementTotal = arena != null ? arena.PlacementGames : 10;
			int placementLeft = ArenaRating.PlacementGamesRemaining(p.Games, placementTotal);
			string season = string.IsNullOrEmpty(p.SeasonName) ? "Season" : p.SeasonName;
			string rating = placementLeft > 0
				? $"rating hidden until {placementLeft} more placement {(placementLeft == 1 ? "game" : "games")}"
				: $"rating {p.Rating} (peak {p.PeakRating})";
			string text = $"{season}: {rating} · {p.Wins}W {p.Losses}L";

			int lockLeft = p.QueueLockSeconds > 0 ? Mathf.Max(0, Mathf.CeilToInt(p.QueueLockSeconds - (Time.unscaledTime - profileReceivedAt))) : 0;
			if (lockLeft > 0)
			{
				string why = string.IsNullOrEmpty(p.QueueLockReason) ? "Locked out of the queue" : p.QueueLockReason;
				text += $"\n{why}: {lockLeft / 60}:{lockLeft % 60:00} remaining.";
				profileBox.AddToClassList(PROFILE_LOCKED_CLASS);
			}
			else
			{
				profileBox.RemoveFromClassList(PROFILE_LOCKED_CLASS);
			}
			profileLabel.text = text;
		}

		private void BuildHistory()
		{
			if (listBox == null) return;
			listBox.Clear();

			if (!history.HasValue)
			{
				AddListEmpty("Loading your recent matches…");
				return;
			}
			ArenaHistoryEntry[] entries = history.Value.Entries;
			if (entries == null || entries.Length == 0)
			{
				AddListEmpty("No finished matches yet. Queue up!");
				return;
			}

			AddListColumns(("Result", "arena-col--wide"), ("Arena", "arena-col--name"), ("K", "arena-col--num"), ("D", "arena-col--num"), ("Score", "arena-col--num"), ("Rating", "arena-col--num"));
			foreach (ArenaHistoryEntry e in entries)
			{
				ArenaTemplate template = e.ArenaTemplateID != 0 ? ArenaTemplate.Get<ArenaTemplate>(e.ArenaTemplateID) : null;
				string arenaName = template != null ? $"{template.ResolvedDisplayName} {template.GetFormatName(e.Format)}" : "Arena";
				string result = e.Deserted ? "Deserted" : (e.WinnerTeam < 0 ? "Draw" : (e.WinnerTeam == e.Team ? "Victory" : "Defeat"));
				if (e.Ranked) result += " ★";
				string rating = e.Ranked ? (e.RatingDelta > 0 ? $"+{e.RatingDelta}" : e.RatingDelta.ToString()) : "—";

				VisualElement row = new VisualElement();
				row.AddToClassList("arena-list__row");
				Color teamColor = template != null ? template.GetTeamColor(e.Team) : ArenaTeamColors.Default(e.Team);
				row.style.borderLeftColor = teamColor;
				row.tooltip = System.DateTimeOffset.FromUnixTimeSeconds(e.EndedUnix).ToLocalTime().ToString("g");

				Label resultLabel = MakeCell(result, "arena-col--wide");
				if (e.Deserted) resultLabel.AddToClassList("fish-label--danger");
				else if (e.WinnerTeam == e.Team) resultLabel.AddToClassList("fish-label--good");
				else if (e.WinnerTeam >= 0) resultLabel.AddToClassList("fish-label--danger");
				row.Add(resultLabel);
				row.Add(MakeCell(arenaName, "arena-col--name"));
				row.Add(MakeCell(e.Kills.ToString(), "arena-col--num"));
				row.Add(MakeCell(e.Deaths.ToString(), "arena-col--num"));
				row.Add(MakeCell(e.Score.ToString(), "arena-col--num"));
				row.Add(MakeCell(rating, "arena-col--num"));
				listBox.Add(row);
			}
		}

		private void BuildLeaderboard()
		{
			if (listBox == null) return;
			listBox.Clear();

			if (!leaderboard.HasValue)
			{
				AddListEmpty("Loading the season leaderboard…");
				return;
			}
			ArenaLeaderboardBroadcast board = leaderboard.Value;
			if (board.Entries == null || board.Entries.Length == 0)
			{
				AddListEmpty($"{(string.IsNullOrEmpty(board.SeasonName) ? "This season" : board.SeasonName)} has no rated players yet.");
				return;
			}

			AddListColumns(("#", "arena-col--rank"), (string.IsNullOrEmpty(board.SeasonName) ? "Player" : $"{board.SeasonName}", "arena-col--name"), ("Rating", "arena-col--num"), ("W", "arena-col--num"), ("L", "arena-col--num"));
			for (int i = 0; i < board.Entries.Length; ++i)
			{
				ArenaLeaderboardEntry e = board.Entries[i];
				VisualElement row = new VisualElement();
				row.AddToClassList("arena-list__row");
				if (Character != null && e.CharacterID == Character.ID)
				{
					row.AddToClassList("arena-list__row--mine");
				}
				row.style.borderLeftColor = i < 3 ? new Color(0.95f, 0.78f, 0.25f) : Color.clear;
				row.Add(MakeCell((i + 1).ToString(), "arena-col--rank"));
				row.Add(MakeCell(e.CharacterName ?? string.Empty, "arena-col--name"));
				row.Add(MakeCell(e.Rating.ToString(), "arena-col--num"));
				row.Add(MakeCell(e.Wins.ToString(), "arena-col--num"));
				row.Add(MakeCell(e.Losses.ToString(), "arena-col--num"));
				listBox.Add(row);
			}

			if (board.YourRank == 0)
			{
				AddListEmpty("You are not on the board yet. Play a ranked match to be rated.");
			}
		}

		private void AddListColumns(params (string text, string cls)[] columns)
		{
			VisualElement header = new VisualElement();
			header.AddToClassList("arena-list__columns");
			foreach ((string text, string cls) in columns)
			{
				Label cell = MakeCell(text, cls);
				cell.AddToClassList("fish-hint");
				header.Add(cell);
			}
			listBox.Add(header);
		}

		private void AddListEmpty(string text)
		{
			Label empty = new Label(text);
			empty.AddToClassList("fish-hint");
			empty.AddToClassList("arena-list__empty");
			listBox.Add(empty);
		}

		private static Label MakeCell(string text, string cls)
		{
			Label cell = new Label(text);
			cell.AddToClassList("fish-label");
			cell.AddToClassList(cls);
			return cell;
		}

		private void BuildArenaTabs()
		{
			if (arenaTabs == null)
			{
				return;
			}

			arenaTabs.Clear();
			if (arenas.Count < 2)
			{
				arenaTabs.AddToClassList(TABS_SINGLE_CLASS);
				return;
			}
			arenaTabs.RemoveFromClassList(TABS_SINGLE_CLASS);

			for (int i = 0; i < arenas.Count; ++i)
			{
				int index = i;
				Button tab = new Button(() => OnClick_Arena(index)) { text = arenas[i].ResolvedDisplayName };
				tab.AddToClassList(TAB_CLASS);
				tab.AddToClassList(ARENA_TAB_CLASS);
				if (i == selectedArena)
				{
					tab.AddToClassList(TAB_ACTIVE_CLASS);
				}
				arenaTabs.Add(tab);
			}
		}

		private void BuildFormatTabs()
		{
			if (formatTabs == null)
			{
				return;
			}

			formatTabs.Clear();
			ArenaTemplate arena = CurrentArena;
			int count = arena != null ? arena.FormatCount : 0;
			if (count < 2)
			{
				formatTabs.AddToClassList(TABS_SINGLE_CLASS);
				return;
			}
			formatTabs.RemoveFromClassList(TABS_SINGLE_CLASS);

			for (int i = 0; i < count; ++i)
			{
				int index = i;
				bool ranked = arena.IsRankedFormat(i);
				Button tab = new Button(() => OnClick_Format(index)) { text = ranked ? $"{arena.GetFormatName(i)} ★" : arena.GetFormatName(i) };
				tab.AddToClassList(TAB_CLASS);
				tab.AddToClassList(FORMAT_TAB_CLASS);
				if (ranked)
				{
					tab.AddToClassList(FORMAT_TAB_RANKED_CLASS);
					tab.tooltip = "Ranked: moves your season rating.";
				}
				if (i == selectedFormat)
				{
					tab.AddToClassList(TAB_ACTIVE_CLASS);
				}
				formatTabs.Add(tab);
			}
		}

		/// <summary>Writes the arena's rules, generated from its own values so they cannot go stale.</summary>
		private void ApplyRules()
		{
			if (rulesBox == null || rulesLabel == null)
			{
				return;
			}

			ArenaTemplate arena = CurrentArena;
			if (arena == null)
			{
				rulesLabel.text = string.Empty;
				rulesBox.AddToClassList("arena-rules--empty");
				return;
			}

			rulesBox.RemoveFromClassList("arena-rules--empty");
			int teamSize = arena.GetTeamSize(selectedFormat);
			var lines = new List<string>(6)
			{
				$"• {arena.TeamCount} teams of {teamSize}.",
			};
			if (arena.ScoreLimit > 0)
			{
				lines.Add($"• First to {arena.ScoreLimit} wins.");
			}
			if (arena.MatchMinutes > 0)
			{
				lines.Add($"• {arena.MatchMinutes} minute limit; the higher score wins.");
			}
			lines.Add(arena.RespawnSeconds > 0
				? $"• Respawn {arena.RespawnSeconds} seconds after death."
				: "• No respawns: one life each.");
			lines.Add($"• Win +{arena.WinRankPoints} rank, loss -{arena.LossRankPoints}.");
			if (arena.IsRankedFormat(selectedFormat))
			{
				lines.Add($"• Ranked: season rating moves with each result; {arena.PlacementGames} placement games. Parties must fill a team.");
			}
			if (arena.ReadyCheckSeconds > 0)
			{
				lines.Add($"• Ready check: {arena.ReadyCheckSeconds} seconds to accept once everyone has arrived.");
			}
			if (arena.DeserterLockMinutes > 0)
			{
				lines.Add($"• Leaving a live match locks you out of the queue for {arena.DeserterLockMinutes} minutes.");
			}
			rulesLabel.text = string.Join("\n", lines);
		}

		private void ApplyQueue()
		{
			if (queueBox == null || queueLabel == null)
			{
				return;
			}

			if (!IsQueued)
			{
				queueBox.AddToClassList(QUEUE_HIDDEN_CLASS);
				queueBox.RemoveFromClassList(QUEUE_MATCHED_CLASS);
				queueLabel.text = string.Empty;
				return;
			}

			queueBox.RemoveFromClassList(QUEUE_HIDDEN_CLASS);

			if (queueStatus.State == GroupFinderState.Matched)
			{
				queueBox.AddToClassList(QUEUE_MATCHED_CLASS);
				queueLabel.text = "Match found! Entering the arena…";
				return;
			}

			queueBox.RemoveFromClassList(QUEUE_MATCHED_CLASS);

			ArenaTemplate queued = queueStatus.ArenaTemplateID != 0 ? ArenaTemplate.Get<ArenaTemplate>(queueStatus.ArenaTemplateID) : null;
			string what = queued != null
				? $"{queued.ResolvedDisplayName} {queued.GetFormatName(queueStatus.Difficulty)}"
				: (string.IsNullOrEmpty(queueStatus.SceneName) ? "this arena" : queueStatus.SceneName);
			string progress = queueStatus.GroupSize > 0
				? $" — {Mathf.Clamp(queueStatus.WaitingCount, 0, queueStatus.GroupSize)}/{queueStatus.GroupSize} ready"
				: string.Empty;
			queueLabel.text = $"Waiting for {what}{progress}.\nStay at the board and keep this window open, or you will leave the queue.";
		}

		private void ApplyControls()
		{
			bool matched = queueStatus.State == GroupFinderState.Matched;
			bool cooledDown = Time.unscaledTime >= queueAllowedAt;
			bool canRequest = currentInteractableID != 0 && CurrentArena != null;

			bool inParty = Character != null && Character.TryGet(out IPartyController party) && party.ID != 0;

			if (queueButton != null)
			{
				queueButton.text = IsQueued ? "Leave Queue" : "Queue";
				queueButton.SetEnabled(cooledDown && (IsQueued ? !matched : canRequest));
			}

			if (partyButton != null)
			{
				// Only meaningful with a party and while not yet queued; leaving is one button.
				partyButton.style.display = !IsQueued && inParty ? DisplayStyle.Flex : DisplayStyle.None;
				partyButton.SetEnabled(cooledDown && canRequest && inParty);
			}

			if (formatTabs != null)
			{
				formatTabs.SetEnabled(!IsQueued);
			}
			if (arenaTabs != null)
			{
				arenaTabs.SetEnabled(!IsQueued);
			}
		}

		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}
			ApplyControls();
			if (profile.HasValue && profile.Value.QueueLockSeconds > 0)
			{
				ApplyProfile();
			}
		}

		private void OnClick_Arena(int index)
		{
			if (index == selectedArena || IsQueued)
			{
				return;
			}
			selectedArena = index;
			selectedFormat = 0;
			ApplyBoard();
		}

		private void OnClick_Format(int index)
		{
			if (index == selectedFormat || IsQueued)
			{
				return;
			}
			selectedFormat = index;
			BuildFormatTabs();
			ApplyRules();
			ApplyControls();
		}

		/// <summary>Queues, queues the party, or leaves. The panel stays open either way.</summary>
		private void OnClick_Queue(bool asParty)
		{
			queueAllowedAt = Time.unscaledTime + QueueCooldownSeconds;
			ApplyControls();

			if (IsQueued)
			{
				if (queueStatus.State == GroupFinderState.Waiting)
				{
					Client.Broadcast(new GroupFinderLeaveBroadcast());
				}
				return;
			}

			ArenaTemplate arena = CurrentArena;
			if (currentInteractableID == 0 || arena == null)
			{
				return;
			}

			Client.Broadcast(new ArenaQueueBroadcast
			{
				InteractableID = currentInteractableID,
				ArenaTemplateID = arena.ID,
				Format = selectedFormat,
				AsParty = asParty,
			});
		}

		private void OnClientGroupFinderStatusBroadcastReceived(GroupFinderStatusBroadcast msg, Channel channel)
		{
			if (msg.Kind != SceneType.PvP)
			{
				return;
			}

			if (msg.State == GroupFinderState.None)
			{
				queueStatus = default;
				Announce(msg.Reason);
				if (msg.Reason == GroupFinderRefusalReason.QueueLocked)
				{
					RequestProfile();
				}
			}
			else
			{
				queueStatus = msg;
				if (!Visible)
				{
					Show();
					return;
				}
			}

			ApplyQueue();
			ApplyControls();
		}

		private static void Announce(GroupFinderRefusalReason reason)
		{
			string text = DescribeReason(reason, out ToastSeverity severity);
			if (!string.IsNullOrEmpty(text) && UIManager.TryGetTK("UIToast", out UITKToast toast))
			{
				toast.Show(text, severity);
			}
		}

		private static string DescribeReason(GroupFinderRefusalReason reason, out ToastSeverity severity)
		{
			severity = ToastSeverity.Info;
			switch (reason)
			{
				case GroupFinderRefusalReason.NoEntrance:
					severity = ToastSeverity.Warning; return "You are too far from the arena board.";
				case GroupFinderRefusalReason.UnknownDifficulty:
					severity = ToastSeverity.Warning; return "This arena does not offer that format.";
				case GroupFinderRefusalReason.NotAvailable:
					severity = ToastSeverity.Warning; return "That arena is not available right now.";
				case GroupFinderRefusalReason.InInstance:
					severity = ToastSeverity.Warning; return "You are already inside an instance.";
				case GroupFinderRefusalReason.InParty:
					severity = ToastSeverity.Warning; return "You are in a party. Queue as a party, or leave it to queue alone.";
				case GroupFinderRefusalReason.NotPartyLeader:
					severity = ToastSeverity.Warning; return "Only the party leader can queue the party.";
				case GroupFinderRefusalReason.PartyTooLarge:
					severity = ToastSeverity.Warning; return "Your party is larger than a team in that format.";
				case GroupFinderRefusalReason.PartyNotPresent:
					severity = ToastSeverity.Warning; return "Every party member must be standing at this board.";
				case GroupFinderRefusalReason.PartyMemberBusy:
					severity = ToastSeverity.Warning; return "A party member is in a dungeon or arena, or has one open.";
				case GroupFinderRefusalReason.QueueLocked:
					severity = ToastSeverity.Warning; return "You are locked out of the arena queue for leaving a match or declining a ready check. Check the board for how long.";
				case GroupFinderRefusalReason.PartyMustFillTeam:
					severity = ToastSeverity.Warning; return "Ranked: your party must fill a whole team of that format.";
				case GroupFinderRefusalReason.HoldsInstance:
					severity = ToastSeverity.Warning; return "You have a dungeon or arena open. Finish or close it first.";
				case GroupFinderRefusalReason.OnCooldown:
					return "Asking too quickly. Try again in a moment.";
				case GroupFinderRefusalReason.ServerError:
					severity = ToastSeverity.Error; return "The arena could not take your request. Please try again.";
				case GroupFinderRefusalReason.Left:
					return "You left the arena queue.";
				case GroupFinderRefusalReason.EnteredInstance:
					return "Left the arena queue: you entered an instance.";
				case GroupFinderRefusalReason.LeftEntrance:
					severity = ToastSeverity.Warning; return "You walked away from the board and left the arena queue.";
				case GroupFinderRefusalReason.GroupLeftWithoutYou:
					severity = ToastSeverity.Warning; return "Your match could not wait any longer and started without you.";
				case GroupFinderRefusalReason.Removed:
					severity = ToastSeverity.Warning; return "You are no longer in the arena queue. Queue again at the board.";
				default:
					return null;
			}
		}

		/// <summary>Leaves the queue when the panel closes, then closes.</summary>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			if (IsQueued)
			{
				bool wasWaiting = queueStatus.State == GroupFinderState.Waiting;
				queueStatus = default;
				if (wasWaiting && Client != null && Client.NetworkManager != null && Client.NetworkManager.IsClientStarted)
				{
					Client.Broadcast(new GroupFinderLeaveBroadcast());
				}
			}

			base.Hide(overrideIsAlwaysOpen);
		}

		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			queueStatus = default;
			currentInteractableID = 0;
			profile = null;
			history = null;
			leaderboard = null;
			view = BoardView.Queue;
			arenas.Clear();
			ApplyBoard();
		}
	}
}
