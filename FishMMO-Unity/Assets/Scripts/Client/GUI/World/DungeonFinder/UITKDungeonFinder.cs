using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit dungeon finder: describes a dungeon, offers its difficulties, lists the runs
	/// currently open at the chosen one, and lets the player join one or start their own.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The panel holds no authority.</b> It draws what the server told it and every control it
	/// offers is a request the server re-validates from scratch — which difficulties exist, whether
	/// a run may be joined, whether the player is allowed to open another. Nothing here is a
	/// permission; a drawn button is at most a prediction that a request will be accepted.
	/// </para>
	/// <para>
	/// <b>It never polls.</b> A list is asked for when the panel opens, when the difficulty tab
	/// changes, and when Refresh is pressed, and at no other time. A finder that refreshed on a
	/// timer would turn every open panel on the shard into standing database load for a list
	/// nobody is necessarily reading.
	/// </para>
	/// </remarks>
	public class UITKDungeonFinder : UITKCharacterControl
	{
		private const string SUBTITLE_NAME = "dungeonfinder-subtitle";
		private const string IMAGE_NAME = "dungeonfinder-image";
		private const string IMAGE_LABEL_NAME = "dungeonfinder-image-label";
		private const string DUNGEON_NAME_NAME = "dungeonfinder-name";
		private const string DESCRIPTION_NAME = "dungeonfinder-description";
		private const string TABS_NAME = "dungeonfinder-tabs";
		private const string RULES_NAME = "dungeonfinder-rules";
		private const string RULES_TEXT_NAME = "dungeonfinder-rules-text";
		private const string LIST_NAME = "dungeonfinder-list";
		private const string STATUS_NAME = "dungeonfinder-status";
		private const string PUBLIC_TOGGLE_NAME = "dungeonfinder-public";
		private const string REFRESH_BUTTON_NAME = "dungeonfinder-refresh-btn";
		private const string FIND_GROUP_BUTTON_NAME = "dungeonfinder-findgroup-btn";
		private const string START_BUTTON_NAME = "dungeonfinder-start-btn";
		private const string CLOSE_BUTTON_NAME = "dungeonfinder-close-btn";

		private const string TAB_CLASS = "fish-tab";
		private const string TAB_LAYOUT_CLASS = "dungeonfinder-tab";
		private const string TAB_ACTIVE_CLASS = "fish-tab--active";
		private const string TABS_SINGLE_CLASS = "dungeonfinder-tabs--single";
		private const string RULES_EMPTY_CLASS = "dungeonfinder-rules--empty";
		private const string ROW_CLASS = "fish-row";
		private const string ROW_LAYOUT_CLASS = "dungeonfinder-row";
		private const string ROW_OWN_CLASS = "dungeonfinder-row--own";

		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Window;

		/// <summary>
		/// How long the panel disables Refresh after asking for a list.
		/// </summary>
		/// <remarks>
		/// Matched to the server's own debounce on the same request, so a player pressing the
		/// button as fast as they can is stopped by a greyed-out control rather than by a silent
		/// refusal. The server's limit is the one that is enforced; this one exists so it is never
		/// reached in ordinary use.
		/// </remarks>
		private const float RefreshCooldownSeconds = 2.0f;

		/// <summary>
		/// How long the panel waits for a list before saying the request went unanswered.
		/// </summary>
		private const float RequestTimeoutSeconds = 8.0f;

		private Label subtitleLabel;
		private VisualElement dungeonImage;
		private Label imagePlaceholderLabel;
		private Label dungeonNameLabel;
		private Label descriptionLabel;
		private VisualElement tabStrip;
		private VisualElement rulesBox;
		private Label rulesLabel;
		private VisualElement listContainer;
		private Label statusLabel;
		private Toggle publicToggle;
		private Button refreshButton;
		private Button findGroupButton;
		private Button startButton;

		/// <summary>Entrance the panel is currently describing. 0 when it has none.</summary>
		private long currentInteractableID;

		/// <summary>Dungeon template resolved from the open message, or null when unset.</summary>
		private DungeonTemplate currentTemplate;

		/// <summary>Difficulty tab currently selected.</summary>
		private int selectedDifficulty;

		/// <summary>Instances last received, for the difficulty in <see cref="listedDifficulty"/>.</summary>
		private readonly List<DungeonInstanceEntry> listedInstances = new List<DungeonInstanceEntry>();

		/// <summary>Difficulty the list in <see cref="listedInstances"/> belongs to.</summary>
		private int listedDifficulty = -1;

		/// <summary>True while a list request is outstanding.</summary>
		private bool awaitingList;

		/// <summary>Unscaled time at which an outstanding request is given up on.</summary>
		private float requestTimeoutAt;

		/// <summary>Unscaled time before which Refresh stays disabled.</summary>
		private float refreshAllowedAt;

		/// <summary>Message shown in place of the list, when there is one.</summary>
		private string statusText;

		/// <summary>
		/// Queries the elements and wires the controls that do not depend on a dungeon.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			dungeonImage = root.Q(IMAGE_NAME);
			imagePlaceholderLabel = root.Q<Label>(IMAGE_LABEL_NAME);
			dungeonNameLabel = root.Q<Label>(DUNGEON_NAME_NAME);
			descriptionLabel = root.Q<Label>(DESCRIPTION_NAME);
			tabStrip = root.Q(TABS_NAME);
			rulesBox = root.Q(RULES_NAME);
			rulesLabel = root.Q<Label>(RULES_TEXT_NAME);
			listContainer = root.Q(LIST_NAME);
			statusLabel = root.Q<Label>(STATUS_NAME);
			publicToggle = root.Q<Toggle>(PUBLIC_TOGGLE_NAME);

			refreshButton = root.Q<Button>(REFRESH_BUTTON_NAME);
			if (refreshButton != null)
			{
				refreshButton.clicked += OnClick_Refresh;
			}

			findGroupButton = root.Q<Button>(FIND_GROUP_BUTTON_NAME);
			if (findGroupButton != null)
			{
				findGroupButton.clicked += OnClick_FindGroup;
			}

			startButton = root.Q<Button>(START_BUTTON_NAME);
			if (startButton != null)
			{
				startButton.clicked += OnClick_Start;
			}

			Button closeButton = root.Q<Button>(CLOSE_BUTTON_NAME);
			if (closeButton != null)
			{
				closeButton.clicked += Hide;
			}
		}

		/// <summary>
		/// Registers the server's messages when the client is set.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<DungeonFinderBroadcast>(OnClientDungeonFinderBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<DungeonFinderListResultBroadcast>(OnClientDungeonFinderListResultBroadcastReceived);
		}

		/// <summary>
		/// Unregisters the server's messages when the client is unset.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DungeonFinderBroadcast>(OnClientDungeonFinderBroadcastReceived);
			Client.NetworkManager.ClientManager.UnregisterBroadcast<DungeonFinderListResultBroadcast>(OnClientDungeonFinderListResultBroadcastReceived);
		}

		/// <summary>
		/// Opens the panel for one dungeon entrance.
		/// </summary>
		/// <param name="msg">The entrance and the dungeon it leads to.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientDungeonFinderBroadcastReceived(DungeonFinderBroadcast msg, Channel channel)
		{
			if (Character == null)
			{
				Hide();
				return;
			}

			currentInteractableID = msg.InteractableID;
			currentTemplate = msg.DungeonTemplateID != 0
				? DungeonTemplate.Get<DungeonTemplate>(msg.DungeonTemplateID)
				: null;

			if (msg.DungeonTemplateID != 0 && currentTemplate == null)
			{
				/* The entrance names a template this client cannot resolve — an asset that failed
				 * to load, or a build mismatch. The panel still works: an unconfigured dungeon has
				 * exactly one difficulty and the list below is the server's answer either way. */
				Log.Debug("UITKDungeonFinder", $"Unknown dungeon template {msg.DungeonTemplateID}; showing the dungeon without its description.");
			}

			/* Reset to the first difficulty on every open rather than remembering the last one.
			 *
			 * The panel is opened by walking up to an entrance, and index 2 of one dungeon has
			 * nothing to do with index 2 of another — carrying a selection across would silently
			 * preselect Hardcore at a dungeon whose Hardcore is a different proposition entirely. */
			selectedDifficulty = 0;
			ClearList();

			/* Show first, then render. Enabling the document re-clones the UXML, so anything
			 * written before this line belonged to a tree that was discarded microseconds later
			 * and the panel opened blank. Show() calls OnAfterShow, which does the writing. */
			Show();

			// Already visible: Show is a no-op and OnAfterShow never ran, so render directly.
			ApplyDungeon();
			RequestList(force: true);
		}

		/// <summary>
		/// Draws the pending dungeon into the tree the player will actually see.
		/// </summary>
		protected override void OnAfterShow()
		{
			ApplyDungeon();
		}

		/// <summary>
		/// Draws the pending dungeon again after the visual tree has been rebuilt.
		/// </summary>
		/// <remarks>
		/// Both hooks are needed: on a panel's first open <c>hasStarted</c> is still false and the
		/// tree-replacement check bails out before <c>OnAfterShow</c> would help, while
		/// <c>OnAfterStarting</c> alone misses every later reopen.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyDungeon();
		}

		/// <summary>
		/// Writes the dungeon's identity, artwork, tabs, rules and list onto the live elements.
		/// </summary>
		private void ApplyDungeon()
		{
			string dungeonName = currentTemplate != null ? currentTemplate.ResolvedDisplayName : null;

			if (subtitleLabel != null)
			{
				subtitleLabel.text = string.IsNullOrEmpty(dungeonName) ? "Select a dungeon" : dungeonName;
			}
			if (dungeonNameLabel != null)
			{
				dungeonNameLabel.text = dungeonName ?? string.Empty;
			}
			if (descriptionLabel != null)
			{
				descriptionLabel.text = currentTemplate != null && !string.IsNullOrWhiteSpace(currentTemplate.Description)
					? currentTemplate.Description
					: string.Empty;
			}

			if (dungeonImage != null)
			{
				Sprite icon = currentTemplate != null ? currentTemplate.Icon : null;

				/* The null case writes an empty background rather than skipping the assignment.
				 * Leaving the previous sprite in place is what made a cleared panel still show the
				 * last dungeon's artwork under a new name, and it also held a reference to that
				 * sprite for as long as the element lived. */
				dungeonImage.style.backgroundImage = icon != null
					? new StyleBackground(icon)
					: new StyleBackground();

				if (imagePlaceholderLabel != null)
				{
					imagePlaceholderLabel.style.display = icon != null ? DisplayStyle.None : DisplayStyle.Flex;
				}
			}

			BuildTabs();
			ApplyRules();
			ApplyList();
			ApplyControls();
		}

		/// <summary>
		/// Rebuilds the difficulty tabs from the dungeon's own list.
		/// </summary>
		/// <remarks>
		/// One tab per declared difficulty, in the dungeon's order, and no tabs at all when a
		/// dungeon offers a single way to play it — a lone tab is a control that cannot be used
		/// and reads as though something failed to load.
		/// </remarks>
		private void BuildTabs()
		{
			if (tabStrip == null)
			{
				return;
			}

			tabStrip.Clear();

			int count = currentTemplate != null ? currentTemplate.DifficultyCount : 1;
			if (count < 2)
			{
				tabStrip.AddToClassList(TABS_SINGLE_CLASS);
				return;
			}

			tabStrip.RemoveFromClassList(TABS_SINGLE_CLASS);

			for (int i = 0; i < count; ++i)
			{
				int difficulty = i;
				Button tab = new Button(() => OnClick_Difficulty(difficulty))
				{
					text = currentTemplate.GetDifficultyName(i),
				};
				tab.AddToClassList(TAB_CLASS);
				tab.AddToClassList(TAB_LAYOUT_CLASS);
				if (i == selectedDifficulty)
				{
					tab.AddToClassList(TAB_ACTIVE_CLASS);
				}
				tabStrip.Add(tab);
			}
		}

		/// <summary>
		/// Writes the selected difficulty's rules into the panel.
		/// </summary>
		/// <remarks>
		/// Generated from the difficulty's own values rather than from a hand-written blurb, so
		/// the panel can never describe a ruleset the server is not enforcing. A difficulty that
		/// changes nothing produces no text and the box is hidden — an empty well headed "rules"
		/// invites the player to look for something that is not there.
		/// </remarks>
		private void ApplyRules()
		{
			if (rulesBox == null || rulesLabel == null)
			{
				return;
			}

			string text = string.Empty;
			if (currentTemplate != null)
			{
				DungeonDifficultyDefinition difficulty = currentTemplate.GetDifficulty(selectedDifficulty);
				if (difficulty != null)
				{
					string summary = difficulty.BuildRulesSummary();
					string flavour = difficulty.Description;

					if (!string.IsNullOrWhiteSpace(flavour) && !string.IsNullOrEmpty(summary))
					{
						text = flavour.Trim() + "\n\n" + summary;
					}
					else if (!string.IsNullOrWhiteSpace(flavour))
					{
						text = flavour.Trim();
					}
					else
					{
						text = summary;
					}
				}
			}

			rulesLabel.text = text;
			if (string.IsNullOrEmpty(text))
			{
				rulesBox.AddToClassList(RULES_EMPTY_CLASS);
			}
			else
			{
				rulesBox.RemoveFromClassList(RULES_EMPTY_CLASS);
			}
		}

		/// <summary>
		/// Rebuilds the instance rows from the last list the server sent.
		/// </summary>
		private void ApplyList()
		{
			if (listContainer == null)
			{
				return;
			}

			listContainer.Clear();

			bool listMatchesTab = listedDifficulty == selectedDifficulty;

			if (listMatchesTab)
			{
				for (int i = 0; i < listedInstances.Count; ++i)
				{
					listContainer.Add(BuildRow(listedInstances[i]));
				}
			}

			if (statusLabel != null)
			{
				string text = ResolveStatusText(listMatchesTab);
				statusLabel.text = text ?? string.Empty;
				statusLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
			}
		}

		/// <summary>
		/// What to show in place of the list, or null when the rows speak for themselves.
		/// </summary>
		private string ResolveStatusText(bool listMatchesTab)
		{
			if (awaitingList)
			{
				return "Looking for open dungeons…";
			}

			if (!listMatchesTab)
			{
				return "Press Refresh to see what is open.";
			}

			if (!string.IsNullOrEmpty(statusText))
			{
				return statusText;
			}

			return listedInstances.Count > 0
				? null
				: "No one has this dungeon open. Start one and others can join you.";
		}

		/// <summary>
		/// Builds one instance row.
		/// </summary>
		/// <remarks>
		/// The Join control is drawn for every listed run, including one that is still loading:
		/// the server queues a joiner into a loading instance rather than refusing them, and the
		/// window in which a run is loading is exactly when a straggler is most likely to be
		/// looking for it.
		/// </remarks>
		private VisualElement BuildRow(DungeonInstanceEntry entry)
		{
			VisualElement row = new VisualElement();
			row.AddToClassList(ROW_CLASS);
			row.AddToClassList(ROW_LAYOUT_CLASS);
			if (entry.IsOwnParty)
			{
				row.AddToClassList(ROW_OWN_CLASS);
			}

			Label leader = new Label(string.IsNullOrEmpty(entry.LeaderName)
				? "An unnamed group"
				: entry.LeaderName + "'s group");
			leader.AddToClassList("fish-row__name");
			leader.AddToClassList("dungeonfinder-row__leader");
			row.Add(leader);

			Label size = new Label(entry.MaxMembers > 0
				? $"{entry.MemberCount}/{entry.MaxMembers}"
				: entry.MemberCount.ToString());
			size.AddToClassList("fish-row__value");
			size.AddToClassList("dungeonfinder-row__size");
			row.Add(size);

			Label status = new Label(entry.IsLoading ? "Loading" : "Open");
			status.AddToClassList("fish-row__meta");
			status.AddToClassList("dungeonfinder-row__status");
			row.Add(status);

			long instanceID = entry.InstanceID;
			Button join = new Button(() => OnClick_Join(instanceID))
			{
				text = entry.IsOwnParty ? "Enter" : "Join",
			};
			join.AddToClassList("fish-button");
			join.AddToClassList("dungeonfinder-row__action");
			row.Add(join);

			return row;
		}

		/// <summary>
		/// Enables or disables the footer controls for the panel's current state.
		/// </summary>
		private void ApplyControls()
		{
			if (refreshButton != null)
			{
				refreshButton.SetEnabled(!awaitingList && Time.unscaledTime >= refreshAllowedAt);
			}

			if (startButton != null)
			{
				startButton.SetEnabled(currentInteractableID != 0);
			}

			if (findGroupButton != null)
			{
				findGroupButton.SetEnabled(currentInteractableID != 0 && IsGroupFinderOffered());
			}

			if (publicToggle != null)
			{
				publicToggle.SetEnabled(currentInteractableID != 0);
			}
		}

		/// <summary>
		/// Re-enables Refresh once its cooldown expires, and gives up on a request that went
		/// unanswered.
		/// </summary>
		/// <remarks>
		/// The timeout is what stops an unanswered request leaving the panel saying "Looking for
		/// open dungeons…" for as long as it stays open. Every server exit does reply, so reaching
		/// this means a dropped message or a disconnect — and in both cases the honest thing is to
		/// hand the player Refresh back rather than a spinner that will never stop.
		/// </remarks>
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}

			if (awaitingList && Time.unscaledTime >= requestTimeoutAt)
			{
				awaitingList = false;
				statusText = "The dungeon list did not arrive. Try Refresh.";
				ApplyList();
			}

			ApplyControls();
		}

		/// <summary>
		/// Switches difficulty tab and asks for that difficulty's list.
		/// </summary>
		/// <remarks>
		/// The list is requested per difficulty rather than fetched wholesale, which is why the
		/// tabs exist at all: a shard may have many open runs of a popular dungeon and only the
		/// ones at the difficulty being looked at are worth sending.
		/// </remarks>
		private void OnClick_Difficulty(int difficulty)
		{
			if (difficulty == selectedDifficulty)
			{
				return;
			}

			selectedDifficulty = difficulty;

			/* The rows on screen belong to the previous tab, and leaving them under the new one
			 * would offer runs at a difficulty the player did not choose. Cleared immediately;
			 * ApplyList then says to press Refresh until an answer for this tab arrives. */
			listedInstances.Clear();
			listedDifficulty = -1;
			statusText = null;

			BuildTabs();
			ApplyRules();
			ApplyList();
			RequestList(force: false);
		}

		/// <summary>
		/// Asks the server for the current difficulty's list.
		/// </summary>
		/// <param name="force">
		/// True to bypass the panel's own cooldown, for the request that accompanies an open or a
		/// tab change. Those are not repeatable by holding a button down, and making the player
		/// wait out a cooldown they did not cause would leave a panel that opened empty.
		/// </param>
		private void RequestList(bool force)
		{
			if (currentInteractableID == 0 || awaitingList)
			{
				return;
			}

			if (!force && Time.unscaledTime < refreshAllowedAt)
			{
				return;
			}

			awaitingList = true;
			statusText = null;
			requestTimeoutAt = Time.unscaledTime + RequestTimeoutSeconds;
			refreshAllowedAt = Time.unscaledTime + RefreshCooldownSeconds;

			ApplyList();
			ApplyControls();

			Client.Broadcast(new DungeonFinderListBroadcast()
			{
				InteractableID = currentInteractableID,
				Difficulty = selectedDifficulty,
			});
		}

		/// <summary>
		/// Receives a list of joinable instances.
		/// </summary>
		/// <param name="msg">The instances, and why there are none when there are none.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientDungeonFinderListResultBroadcastReceived(DungeonFinderListResultBroadcast msg, Channel channel)
		{
			/* A reply for an entrance or a tab the player has since left is discarded.
			 *
			 * Both are reachable in ordinary play — walk away and open another entrance, or click
			 * a second tab while the first is still in flight — and drawing a late reply would
			 * silently present another dungeon's runs, or another difficulty's, under the heading
			 * the player is actually looking at. */
			if (msg.InteractableID != currentInteractableID ||
				msg.Difficulty != selectedDifficulty)
			{
				return;
			}

			awaitingList = false;
			listedInstances.Clear();
			listedDifficulty = selectedDifficulty;
			statusText = DescribeListFailure(msg.Reason);

			if (msg.Instances != null)
			{
				listedInstances.AddRange(msg.Instances);
			}

			if (msg.Reason == DungeonListFailureReason.OnCooldown)
			{
				/* Refused for asking too soon, so nothing was read and the rows in the message are
				 * empty. The previous list is still the current one as far as the server is
				 * concerned — but this panel has already cleared it, so it says so rather than
				 * showing "nobody has this open", which would be a claim the server did not make. */
				refreshAllowedAt = Time.unscaledTime + RefreshCooldownSeconds;
			}

			ApplyList();
			ApplyControls();
		}

		/// <summary>
		/// Turns a refusal into something worth reading, or null when the list is simply the answer.
		/// </summary>
		private static string DescribeListFailure(DungeonListFailureReason reason)
		{
			switch (reason)
			{
				case DungeonListFailureReason.NoEntrance:
					return "You are too far from the dungeon entrance.";
				case DungeonListFailureReason.UnknownDifficulty:
					return "This dungeon does not offer that difficulty.";
				case DungeonListFailureReason.OnCooldown:
					return "Checking again too quickly. Try Refresh in a moment.";
				case DungeonListFailureReason.ServerError:
					return "The dungeon list could not be read. Try Refresh.";
				default:
					return null;
			}
		}

		/// <summary>
		/// Asks for a fresh list.
		/// </summary>
		public void OnClick_Refresh()
		{
			RequestList(force: false);
		}

		/// <summary>
		/// Asks to open a new instance at the selected difficulty, and closes the panel.
		/// </summary>
		/// <remarks>
		/// <para><b>One request per opening.</b> The request is answered by a disconnect-and-
		/// reroute that takes a database round trip to arrange — so there is a window, seconds
		/// long on a loaded server, in which the player sees nothing happen and clicks again. The
		/// server's ingress guard debounces those, but it debounces them into
		/// <c>SceneTransferRefusalReason.OnCooldown</c>, so the reward for an impatient second
		/// click would be "You are travelling too often" on top of a request that was already
		/// succeeding. Clearing the entrance and closing removes the second click rather than
		/// punishing it.</para>
		/// <para>Closing loses nothing: the entrance re-sends <see cref="DungeonFinderBroadcast"/>
		/// on the next interaction, which reopens the panel with fresh data. Nothing is armed
		/// server-side by having the panel open, and nothing is left armed by closing it — the
		/// request only exists once this method has sent it, and from that point it is the
		/// server's, cancelled only by its own refusal.</para>
		/// </remarks>
		public void OnClick_Start()
		{
			if (currentInteractableID == 0)
			{
				return;
			}

			long requestedID = currentInteractableID;
			int difficulty = selectedDifficulty;
			bool isPublic = publicToggle == null || publicToggle.value;

			ClearDungeon();
			Hide();

			Client.Broadcast(new DungeonFinderCreateBroadcast()
			{
				InteractableID = requestedID,
				Difficulty = difficulty,
				IsPrivate = !isPublic,
			});
		}

		/// <summary>
		/// Whether the selected difficulty offers Find Group, as far as this client can tell.
		/// </summary>
		/// <remarks>
		/// Only the author's on/off switch is read here. The rest of
		/// <see cref="GroupFinderRules.ResolveGroupSize"/> needs the scene's capacity, which the
		/// server has and the client does not, so a difficulty this enables can still be refused
		/// as unavailable by the server — a dungeon that seats one, say. A dungeon with no template
		/// has no ruleset for the finder to serve, and the server refuses it for the same reason.
		/// </remarks>
		private bool IsGroupFinderOffered()
		{
			if (currentTemplate == null)
			{
				return false;
			}

			DungeonDifficultyDefinition difficulty = currentTemplate.GetDifficulty(selectedDifficulty);
			return difficulty != null && difficulty.GroupFinderEnabled;
		}

		/// <summary>
		/// Asks the group finder to find the player a group at the selected difficulty, and
		/// closes the panel.
		/// </summary>
		/// <remarks>
		/// Closes on send like Open and Join, though the answer is not a transfer: the queue is
		/// server state that outlives the panel, and it is reported by the group finder HUD widget
		/// from here on. Leaving the panel up would only invite the player to press the button
		/// again, which the server's ingress guard would refuse as a duplicate.
		/// </remarks>
		private void OnClick_FindGroup()
		{
			if (currentInteractableID == 0)
			{
				return;
			}

			long requestedID = currentInteractableID;
			int difficulty = selectedDifficulty;

			ClearDungeon();
			Hide();

			Client.Broadcast(new GroupFinderQueueBroadcast()
			{
				InteractableID = requestedID,
				Difficulty = difficulty,
			});
		}

		/// <summary>
		/// Asks to join one of the listed instances, and closes the panel.
		/// </summary>
		/// <remarks>
		/// Closes for the same reason opening a new one does — the answer is a transfer, not a UI
		/// update — and because joining another group's run also joins their party, which is not
		/// something to leave a second button live for.
		/// </remarks>
		private void OnClick_Join(long instanceID)
		{
			if (currentInteractableID == 0 || instanceID <= 0)
			{
				return;
			}

			long requestedID = currentInteractableID;

			ClearDungeon();
			Hide();

			Client.Broadcast(new DungeonFinderJoinBroadcast()
			{
				InteractableID = requestedID,
				InstanceID = instanceID,
			});
		}

		/// <summary>
		/// Drops the listed instances without forgetting which dungeon is being shown.
		/// </summary>
		private void ClearList()
		{
			listedInstances.Clear();
			listedDifficulty = -1;
			awaitingList = false;
			statusText = null;
		}

		/// <summary>
		/// Drops the pending dungeon so a stale entrance cannot be re-sent.
		/// </summary>
		private void ClearDungeon()
		{
			currentInteractableID = 0;
			currentTemplate = null;
			selectedDifficulty = 0;
			ClearList();
		}

		/// <summary>
		/// Drops the pending dungeon when the character goes away.
		/// </summary>
		/// <remarks>
		/// <c>currentInteractableID</c> is a scene-object handle belonging to the scene the
		/// previous character was standing in. Carrying it across a character switch or a scene
		/// transfer meant a reopened panel showed the old dungeon and its buttons sent an ID that
		/// means something different — or nothing — on the new server. The server validates the
		/// handle against the character's own scene and refuses, so this was never exploitable; it
		/// was a panel showing one dungeon and a button asking for another.
		/// </remarks>
		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();
			ClearDungeon();
			ApplyDungeon();
		}
	}
}
