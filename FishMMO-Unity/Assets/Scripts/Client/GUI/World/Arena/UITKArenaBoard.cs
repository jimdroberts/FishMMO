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
		}

		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<ArenaBoardBroadcast>(OnClientArenaBoardBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<GroupFinderStatusBroadcast>(OnClientGroupFinderStatusBroadcastReceived);
		}

		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ArenaBoardBroadcast>(OnClientArenaBoardBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<GroupFinderStatusBroadcast>(OnClientGroupFinderStatusBroadcastReceived);
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

			Show();
			ApplyBoard();
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
				Button tab = new Button(() => OnClick_Format(index)) { text = arena.GetFormatName(i) };
				tab.AddToClassList(TAB_CLASS);
				tab.AddToClassList(FORMAT_TAB_CLASS);
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
			arenas.Clear();
			ApplyBoard();
		}
	}
}
