using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using FishNet.Broadcast;
using FishNet.Transporting;
using FishMMO.Auth.Core;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The in-game staff console: a roster of the scene, the support ticket queue, and a form for
	/// every command the server says this account may run.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>An empty shell.</b> Nothing in this class, its UXML or its USS names a staff command, a
	/// sub-command, an argument or a help line. Everything command-specific arrives in a
	/// <see cref="StaffConsoleCatalogBroadcast"/>, which the server sends only to staff and only when
	/// they ask for it. Until one has arrived this session the console refuses to show and sends no
	/// staff request; it is cleared again on client unset, quit to login and disconnect, and it is
	/// never written anywhere.
	/// </para>
	/// <para>
	/// <b>Not the security boundary.</b> Every action is composed into an ordinary chat command line
	/// and sent exactly as the chat panel sends a typed one, so the server's single access gate
	/// checks and audits it as if it had been typed. The read requests are refused server-side for
	/// anyone below GameMaster.
	/// </para>
	/// <para>
	/// The model (catalogue, roster, tickets, form values, output) lives on the component and the
	/// view is rebuilt from it, because <c>UIDocument</c> re-clones the tree on every show.
	/// </para>
	/// </remarks>
	public class UITKStaffConsole : UITKControl
	{
		/// <summary>The tabs the console can show.</summary>
		public enum ConsoleTab
		{
			/// <summary>The scene roster.</summary>
			Players,
			/// <summary>The support ticket queue.</summary>
			Tickets,
			/// <summary>Every command in the catalogue.</summary>
			Commands,
		}

		/// <summary>The view id the catalogue uses for the roster.</summary>
		public const string PlayersViewId = "players";

		/// <summary>The view id the catalogue uses for the ticket queue.</summary>
		public const string TicketsViewId = "tickets";

		/// <summary>Seconds between roster requests while the console is visible.</summary>
		public const float RosterRefreshSeconds = 5f;

		/// <summary>Seconds after sending an action before the affected view is re-requested.</summary>
		public const float ActionRefreshDelaySeconds = 1f;

		/// <summary>Most lines the output log keeps.</summary>
		public const int MaxOutputLines = 100;

		/// <summary>Name the shared confirmation dialog registers under.</summary>
		private const string DialogPanelName = "UIDialogBox";

		/// <summary>Dropdown entry that stands for leaving an optional choice blank.</summary>
		private const string BlankChoice = "(blank)";

		/// <summary>Run button text while a destructive command waits for its second click.</summary>
		public const string ConfirmText = "Click again to confirm";

		/// <summary>Run button text at rest.</summary>
		private const string RunText = "Run";

		/// <summary>The kinds of output line, each styled differently.</summary>
		private enum OutputKind
		{
			System,
			Sent,
			Notice,
			Error,
		}

		/// <summary>One output log line.</summary>
		private struct OutputLine
		{
			public string Text;
			public OutputKind Kind;
		}

		/// <summary>One catalogue entry with its argument spec already read.</summary>
		private sealed class ParsedCommand
		{
			public StaffCommandEntry Entry;
			public List<StaffArgument> Arguments;
			public string SpecError;

			/// <summary>The command as a title: its slash command and sub-command word.</summary>
			public string Title
			{
				get
				{
					string name = (Entry.Name ?? string.Empty).Trim();
					string command = (Entry.Command ?? string.Empty).Trim();
					return name.Length > 0 ? command + " " + name : command;
				}
			}

			/// <summary>The short label for an action button.</summary>
			public string ActionLabel
			{
				get
				{
					string name = (Entry.Name ?? string.Empty).Trim();
					return name.Length > 0 ? name : (Entry.Command ?? string.Empty).Trim();
				}
			}
		}

		// ── Model ───────────────────────────────────────────────────────────────────────────

		/// <summary>The catalogue's commands, grouped by category in first-appearance order.</summary>
		private readonly List<ParsedCommand> commands = new List<ParsedCommand>();

		/// <summary>The catalogue's view ids.</summary>
		private readonly HashSet<string> views = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The access level the catalogue was issued for.</summary>
		private byte accessLevel;

		/// <summary>The current tab.</summary>
		private ConsoleTab activeTab = ConsoleTab.Commands;

		/// <summary>The latest roster.</summary>
		private StaffRosterBroadcast roster;

		/// <summary>True once a roster has arrived for this catalogue.</summary>
		private bool hasRoster;

		/// <summary>The selected roster character's name, or null.</summary>
		private string selectedCharacter;

		/// <summary>The ticket filter in use.</summary>
		private StaffTicketFilter ticketFilter = StaffTicketFilter.Unassigned;

		/// <summary>The ticket page in use, 1-based.</summary>
		private int ticketPage = 1;

		/// <summary>The latest ticket queue page.</summary>
		private StaffTicketQueueBroadcast queue;

		/// <summary>True once a queue page has arrived for the current filter.</summary>
		private bool hasQueue;

		/// <summary>The open ticket's number, or 0.</summary>
		private long selectedTicketID;

		/// <summary>The open ticket.</summary>
		private StaffTicketDetailBroadcast detail;

		/// <summary>True once the open ticket's detail has arrived.</summary>
		private bool hasDetail;

		/// <summary>Index into <see cref="commands"/> of the selected command, or -1.</summary>
		private int selectedCommand = -1;

		/// <summary>One entered value per argument of the selected command.</summary>
		private readonly List<string> formValues = new List<string>();

		/// <summary>The tab an action button opened the form from; the console returns there after sending.</summary>
		private ConsoleTab? formOrigin;

		/// <summary>The destructive line waiting for its second click, or null.</summary>
		private string armedLine;

		/// <summary>The output log.</summary>
		private readonly List<OutputLine> output = new List<OutputLine>();

		/// <summary>When the next roster request is due, in unscaled seconds.</summary>
		private float nextRosterRequestTime;

		/// <summary>When a post-action refresh is due, or a negative number for none.</summary>
		private float pendingRefreshTime = -1f;

		/// <summary>The ticket to re-read when <see cref="pendingRefreshTime"/> comes due, or 0.</summary>
		private long pendingRefreshTicket;

		/// <summary>True when the roster should be re-read when <see cref="pendingRefreshTime"/> comes due.</summary>
		private bool pendingRefreshRoster;

		// ── View ────────────────────────────────────────────────────────────────────────────

		private Label subtitleLabel;
		private Button playersTab;
		private Button ticketsTab;
		private Button commandsTab;
		private VisualElement playersPage;
		private VisualElement ticketsPage;
		private VisualElement commandsPage;

		private Label rosterSummary;
		private VisualElement rosterList;
		private Label rosterEmpty;
		private Label rosterSelected;
		private Label rosterSelectedMeta;
		private VisualElement rosterActions;
		private Label rosterHint;

		private Button filterUnassigned;
		private Button filterMine;
		private Button filterAll;
		private Button pagerPrev;
		private Button pagerNext;
		private Label pagerLabel;
		private VisualElement ticketList;
		private Label ticketEmpty;
		private VisualElement ticketDetail;
		private Label ticketHint;

		private VisualElement commandList;
		private Label commandEmpty;
		private Label commandTitle;
		private Label commandSummary;
		private VisualElement form;
		private Label commandPreview;
		private Label commandStatus;
		private Button runButton;

		private ScrollView outputScroll;
		private VisualElement outputList;

		/// <summary>The roster pickers in the current form, refreshed when a roster arrives.</summary>
		private readonly List<DropdownField> characterPickers = new List<DropdownField>();

		// ── Public surface ──────────────────────────────────────────────────────────────────

		/// <summary>True once a catalogue has been received this session.</summary>
		public bool HasCatalogue { get; private set; }

		/// <summary>The current tab.</summary>
		public ConsoleTab ActiveTab => activeTab;

		/// <summary>How many commands the catalogue holds.</summary>
		public int CommandCount => commands.Count;

		/// <summary>The output log's lines, oldest first.</summary>
		public IReadOnlyList<string> OutputLines
		{
			get
			{
				List<string> lines = new List<string>(output.Count);
				foreach (OutputLine line in output)
				{
					lines.Add(line.Text);
				}
				return lines;
			}
		}

		/// <summary>
		/// Whether a tab is available under the current catalogue.
		/// </summary>
		/// <param name="tab">The tab.</param>
		/// <returns>False for every tab when there is no catalogue.</returns>
		public bool IsTabAvailable(ConsoleTab tab)
		{
			if (!HasCatalogue)
			{
				return false;
			}
			switch (tab)
			{
				case ConsoleTab.Players: return views.Contains(PlayersViewId);
				case ConsoleTab.Tickets: return views.Contains(TicketsViewId);
				default: return true;
			}
		}

		// ── Lifecycle ───────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Refuses to show without a catalogue: an empty console has nothing to offer and must not
		/// look like a feature.
		/// </summary>
		public override void Show()
		{
			if (!HasCatalogue)
			{
				return;
			}
			base.Show();
		}

		/// <inheritdoc/>
		public override void Hide(bool overrideIsAlwaysOpen)
		{
			base.Hide(overrideIsAlwaysOpen);
			if (!overrideIsAlwaysOpen)
			{
				armedLine = null;
			}
		}

		/// <summary>Resolves the tree's elements and wires its fixed controls.</summary>
		public override void OnStarting()
		{
			characterPickers.Clear();

			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			Button close = root.Q<Button>("close-button");
			if (close != null)
			{
				close.clicked += Hide;
			}

			subtitleLabel = root.Q<Label>("staff-subtitle");

			playersTab = root.Q<Button>("staff-tab-players");
			ticketsTab = root.Q<Button>("staff-tab-tickets");
			commandsTab = root.Q<Button>("staff-tab-commands");
			if (playersTab != null) playersTab.clicked += () => SelectTab(ConsoleTab.Players);
			if (ticketsTab != null) ticketsTab.clicked += () => SelectTab(ConsoleTab.Tickets);
			if (commandsTab != null) commandsTab.clicked += () => SelectTab(ConsoleTab.Commands);

			playersPage = root.Q("staff-page-players");
			ticketsPage = root.Q("staff-page-tickets");
			commandsPage = root.Q("staff-page-commands");

			rosterSummary = root.Q<Label>("staff-roster-summary");
			rosterList = root.Q("staff-roster-list");
			rosterEmpty = root.Q<Label>("staff-roster-empty");
			rosterSelected = root.Q<Label>("staff-roster-selected");
			rosterSelectedMeta = root.Q<Label>("staff-roster-selected-meta");
			rosterActions = root.Q("staff-roster-actions");
			rosterHint = root.Q<Label>("staff-roster-hint");

			filterUnassigned = root.Q<Button>("staff-filter-unassigned");
			filterMine = root.Q<Button>("staff-filter-mine");
			filterAll = root.Q<Button>("staff-filter-all");
			if (filterUnassigned != null) filterUnassigned.clicked += () => SetTicketFilter(StaffTicketFilter.Unassigned);
			if (filterMine != null) filterMine.clicked += () => SetTicketFilter(StaffTicketFilter.Mine);
			if (filterAll != null) filterAll.clicked += () => SetTicketFilter(StaffTicketFilter.AllOpen);

			pagerPrev = root.Q<Button>("staff-pager-prev");
			pagerNext = root.Q<Button>("staff-pager-next");
			pagerLabel = root.Q<Label>("staff-pager-label");
			if (pagerPrev != null) pagerPrev.clicked += () => SetTicketPage(ticketPage - 1);
			if (pagerNext != null) pagerNext.clicked += () => SetTicketPage(ticketPage + 1);

			Button refresh = root.Q<Button>("staff-tickets-refresh");
			if (refresh != null)
			{
				refresh.clicked += () =>
				{
					RequestTicketQueue();
					if (selectedTicketID > 0)
					{
						RequestTicketDetail(selectedTicketID);
					}
				};
			}

			ticketList = root.Q("staff-ticket-list");
			ticketEmpty = root.Q<Label>("staff-ticket-empty");
			ticketDetail = root.Q("staff-ticket-detail");
			ticketHint = root.Q<Label>("staff-ticket-hint");

			commandList = root.Q("staff-command-list");
			commandEmpty = root.Q<Label>("staff-command-empty");
			commandTitle = root.Q<Label>("staff-command-title");
			commandSummary = root.Q<Label>("staff-command-summary");
			form = root.Q("staff-form");
			commandPreview = root.Q<Label>("staff-command-preview");
			commandStatus = root.Q<Label>("staff-command-status");
			runButton = root.Q<Button>("staff-run");
			if (runButton != null)
			{
				runButton.clicked += RunSelected;
			}

			outputScroll = root.Q<ScrollView>("staff-output-scroll");
			outputList = root.Q("staff-output-list");

			Button clearOutput = root.Q<Button>("staff-output-clear");
			if (clearOutput != null)
			{
				clearOutput.clicked += () =>
				{
					output.Clear();
					RenderOutput();
				};
			}

			// Labels that carry server or player text never parse markup.
			foreach (Label label in new[] { subtitleLabel, rosterSummary, rosterSelected, rosterSelectedMeta,
				commandTitle, commandSummary, commandPreview, commandStatus, pagerLabel })
			{
				if (label != null)
				{
					label.enableRichText = false;
				}
			}
		}

		/// <summary>Redraws the whole view from the model after the tree was (re)built.</summary>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			RenderAll();
		}

		/// <summary>Redraws and re-requests the visible data on every open.</summary>
		protected override void OnAfterShow()
		{
			RenderAll();

			if (!HasCatalogue)
			{
				return;
			}

			RequestRoster();
			if (activeTab == ConsoleTab.Tickets)
			{
				RequestTicketQueue();
			}
		}

		/// <summary>Drives the roster refresh and the post-action refresh while visible.</summary>
		protected override void OnTick()
		{
			if (!Visible || !HasCatalogue)
			{
				return;
			}

			float now = Time.unscaledTime;

			if (IsTabAvailable(ConsoleTab.Players) && now >= nextRosterRequestTime)
			{
				RequestRoster();
			}

			if (pendingRefreshTime >= 0f && now >= pendingRefreshTime)
			{
				pendingRefreshTime = -1f;

				if (pendingRefreshTicket > 0)
				{
					if (pendingRefreshTicket == selectedTicketID)
					{
						RequestTicketDetail(pendingRefreshTicket);
					}
					RequestTicketQueue();
					pendingRefreshTicket = 0;
				}

				if (pendingRefreshRoster)
				{
					pendingRefreshRoster = false;
					RequestRoster();
				}
			}
		}

		/// <summary>Registers the staff broadcasts and a second chat handler for the output log.</summary>
		/// <remarks>
		/// FishNet keeps a list of handlers per broadcast type (<c>ServerBroadcastHandler&lt;T&gt;</c>
		/// adds with <c>AddUnique</c> and invokes every entry), so this chat handler sits beside
		/// <see cref="UITKChat"/>'s rather than replacing it.
		/// </remarks>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<StaffConsoleCatalogBroadcast>(OnCatalogueReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<StaffRosterBroadcast>(OnRosterReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<StaffTicketQueueBroadcast>(OnTicketQueueReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<StaffTicketDetailBroadcast>(OnTicketDetailReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<ChatBroadcast>(OnChatReceived);
			Client.NetworkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
		}

		/// <summary>Unregisters everything <see cref="OnClientSet"/> registered and forgets the catalogue.</summary>
		public override void OnClientUnset()
		{
			if (Client != null && Client.NetworkManager != null)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<StaffConsoleCatalogBroadcast>(OnCatalogueReceived);
				Client.NetworkManager.ClientManager.UnregisterBroadcast<StaffRosterBroadcast>(OnRosterReceived);
				Client.NetworkManager.ClientManager.UnregisterBroadcast<StaffTicketQueueBroadcast>(OnTicketQueueReceived);
				Client.NetworkManager.ClientManager.UnregisterBroadcast<StaffTicketDetailBroadcast>(OnTicketDetailReceived);
				Client.NetworkManager.ClientManager.UnregisterBroadcast<ChatBroadcast>(OnChatReceived);
				Client.NetworkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
			}
			ClearCatalogue();
		}

		/// <summary>Forgets the catalogue on quit to login.</summary>
		public override void OnQuitToLogin()
		{
			base.OnQuitToLogin();
			ClearCatalogue();
		}

		/// <summary>Forgets the catalogue when the connection it was issued on stops.</summary>
		private void OnClientConnectionState(ClientConnectionStateArgs args)
		{
			if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				ClearCatalogue();
			}
		}

		private void OnCatalogueReceived(StaffConsoleCatalogBroadcast message, Channel channel) => ApplyCatalogue(message);
		private void OnRosterReceived(StaffRosterBroadcast message, Channel channel) => ApplyRoster(message);
		private void OnTicketQueueReceived(StaffTicketQueueBroadcast message, Channel channel) => ApplyTicketQueue(message);
		private void OnTicketDetailReceived(StaffTicketDetailBroadcast message, Channel channel) => ApplyTicketDetail(message);
		private void OnChatReceived(ChatBroadcast message, Channel channel) => ReceiveChat(message);

		// ── Model entry points (the broadcast handlers forward here) ────────────────────────

		/// <summary>
		/// Takes a catalogue: replaces the command list and views, and opens the console when asked.
		/// </summary>
		/// <param name="message">The catalogue.</param>
		public void ApplyCatalogue(StaffConsoleCatalogBroadcast message)
		{
			/* The server never sends a catalogue below GameMaster. One that says otherwise is not a
			 * catalogue this console should present, so it is treated as a withdrawal. */
			if (message.AccessLevel < (byte)AccessLevel.GameMaster)
			{
				ClearCatalogue();
				return;
			}

			string previousSelection = selectedCommand >= 0 && selectedCommand < commands.Count
				? commands[selectedCommand].Title
				: null;
			bool first = !HasCatalogue;

			accessLevel = message.AccessLevel;

			views.Clear();
			if (message.Views != null)
			{
				foreach (string view in message.Views)
				{
					if (!string.IsNullOrWhiteSpace(view))
					{
						views.Add(view.Trim());
					}
				}
			}

			commands.Clear();
			if (message.Commands != null)
			{
				// Grouped by category, categories in the order they first appear, commands in server order.
				List<string> categoryOrder = new List<string>();
				Dictionary<string, List<ParsedCommand>> byCategory = new Dictionary<string, List<ParsedCommand>>(StringComparer.OrdinalIgnoreCase);
				foreach (StaffCommandEntry entry in message.Commands)
				{
					string category = CategoryOf(entry);
					if (!byCategory.TryGetValue(category, out List<ParsedCommand> group))
					{
						group = new List<ParsedCommand>();
						byCategory.Add(category, group);
						categoryOrder.Add(category);
					}

					StaffCommandLine.TryParseSpec(entry.Arguments, out List<StaffArgument> arguments, out string specError);
					group.Add(new ParsedCommand()
					{
						Entry = entry,
						Arguments = arguments,
						SpecError = specError,
					});
				}
				foreach (string category in categoryOrder)
				{
					commands.AddRange(byCategory[category]);
				}
			}

			HasCatalogue = true;

			int reselect = -1;
			if (previousSelection != null)
			{
				for (int i = 0; i < commands.Count; ++i)
				{
					if (commands[i].Title == previousSelection)
					{
						reselect = i;
						break;
					}
				}
			}
			/* A refreshed catalogue keeps what was typed into the open form, but only while the
			 * command still takes the same number of arguments; otherwise the values would land in
			 * the wrong inputs. */
			if (reselect >= 0 && commands[reselect].Arguments.Count == formValues.Count)
			{
				selectedCommand = reselect;
			}
			else
			{
				ResetForm(reselect);
			}

			if (first)
			{
				activeTab = IsTabAvailable(ConsoleTab.Players) ? ConsoleTab.Players
					: IsTabAvailable(ConsoleTab.Tickets) ? ConsoleTab.Tickets
					: ConsoleTab.Commands;
			}
			else if (!IsTabAvailable(activeTab))
			{
				activeTab = ConsoleTab.Commands;
			}

			if (message.Open && !Visible)
			{
				Show();
				return;
			}

			if (message.Open)
			{
				BringToFront();
			}
			RenderAll();
		}

		/// <summary>Takes a roster.</summary>
		/// <param name="message">The roster.</param>
		public void ApplyRoster(StaffRosterBroadcast message)
		{
			if (!HasCatalogue)
			{
				return;
			}

			roster = message;
			hasRoster = true;
			RenderRoster();
			RefreshCharacterPickers();
		}

		/// <summary>Takes a page of the ticket queue. A page for a filter no longer selected is stale and ignored.</summary>
		/// <param name="message">The page.</param>
		public void ApplyTicketQueue(StaffTicketQueueBroadcast message)
		{
			if (!HasCatalogue || message.Filter != ticketFilter)
			{
				return;
			}

			queue = message;
			hasQueue = true;
			ticketPage = Math.Max(1, message.Page);
			RenderTickets();
		}

		/// <summary>Takes a ticket's detail. Detail for a ticket no longer open is stale and ignored.</summary>
		/// <param name="message">The detail.</param>
		public void ApplyTicketDetail(StaffTicketDetailBroadcast message)
		{
			if (!HasCatalogue || selectedTicketID <= 0)
			{
				return;
			}
			if (message.Found && message.Ticket.TicketID != selectedTicketID)
			{
				return;
			}

			detail = message;
			hasDetail = true;
			RenderTicketDetail();
		}

		/// <summary>Adds a System-channel chat line to the output log. Other channels are not the console's business.</summary>
		/// <param name="message">The chat message.</param>
		public void ReceiveChat(ChatBroadcast message)
		{
			if (!HasCatalogue || message.Channel != ChatChannel.System || string.IsNullOrWhiteSpace(message.Text))
			{
				return;
			}
			AppendOutput(message.Text, OutputKind.System);
		}

		/// <summary>
		/// Forgets the catalogue and everything learned under it, and hides the console.
		/// </summary>
		public void ClearCatalogue()
		{
			HasCatalogue = false;
			accessLevel = 0;
			commands.Clear();
			views.Clear();
			roster = default;
			hasRoster = false;
			selectedCharacter = null;
			ticketFilter = StaffTicketFilter.Unassigned;
			ticketPage = 1;
			queue = default;
			hasQueue = false;
			selectedTicketID = 0;
			detail = default;
			hasDetail = false;
			ResetForm(-1);
			output.Clear();
			pendingRefreshTime = -1f;
			pendingRefreshTicket = 0;
			pendingRefreshRoster = false;
			nextRosterRequestTime = 0f;
			activeTab = ConsoleTab.Commands;

			Hide(false);
			RenderAll();
		}

		// ── Navigation ──────────────────────────────────────────────────────────────────────

		/// <summary>Switches tab. An unavailable tab falls back to Commands.</summary>
		/// <param name="tab">The tab.</param>
		public void SelectTab(ConsoleTab tab)
		{
			if (!HasCatalogue)
			{
				return;
			}
			if (!IsTabAvailable(tab))
			{
				tab = ConsoleTab.Commands;
			}

			activeTab = tab;
			RenderTabs();

			if (tab == ConsoleTab.Tickets)
			{
				RequestTicketQueue();
			}
			else if (tab == ConsoleTab.Players && !hasRoster)
			{
				RequestRoster();
			}
		}

		/// <summary>Selects a command by its index in the grouped list and clears the form.</summary>
		/// <param name="index">The index, or -1 for none.</param>
		public void SelectCommand(int index)
		{
			if (!HasCatalogue)
			{
				return;
			}
			ResetForm(index >= 0 && index < commands.Count ? index : -1);
			RenderCommandList();
			RenderCommandDetail();
		}

		/// <summary>
		/// Opens a command's form on the Commands tab with its first argument filled in.
		/// </summary>
		/// <param name="index">The command's index.</param>
		/// <param name="firstArgument">The value for the first argument: a character name or a ticket number.</param>
		/// <param name="origin">The tab to return to after the command is sent.</param>
		public void OpenCommandForm(int index, string firstArgument, ConsoleTab origin)
		{
			if (!HasCatalogue || index < 0 || index >= commands.Count)
			{
				return;
			}

			ResetForm(index);
			if (formValues.Count > 0)
			{
				formValues[0] = firstArgument ?? string.Empty;
			}
			formOrigin = origin;
			SelectTab(ConsoleTab.Commands);
			RenderCommandList();
			RenderCommandDetail();
		}

		/// <summary>The index of the first command with this title (slash command and sub-command word), or -1.</summary>
		/// <param name="title">The title, e.g. the slash command followed by a space and the sub-command word.</param>
		public int FindCommand(string title)
		{
			for (int i = 0; i < commands.Count; ++i)
			{
				if (string.Equals(commands[i].Title, title, StringComparison.Ordinal))
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>Composes the selected command's line from the form as it stands.</summary>
		/// <param name="line">The line, when every value was acceptable.</param>
		/// <param name="error">Why it may not be sent, or null.</param>
		/// <returns>True when the line may be sent.</returns>
		public bool TryComposeSelected(out string line, out string error)
		{
			line = null;
			if (!HasCatalogue || selectedCommand < 0 || selectedCommand >= commands.Count)
			{
				error = "No command is selected.";
				return false;
			}

			ParsedCommand command = commands[selectedCommand];
			if (command.SpecError != null)
			{
				error = "This command's arguments could not be read: " + command.SpecError;
				return false;
			}

			return StaffCommandLine.TryCompose(command.Entry, command.Arguments, formValues, out line, out error);
		}

		/// <summary>
		/// Sends the selected command, confirming first when it is destructive.
		/// </summary>
		public void RunSelected()
		{
			if (!HasCatalogue || selectedCommand < 0 || selectedCommand >= commands.Count)
			{
				return;
			}

			if (!TryComposeSelected(out string line, out string error))
			{
				SetStatus(error, true);
				AppendOutput("Not sent: " + error, OutputKind.Error);
				return;
			}

			int index = selectedCommand;
			ParsedCommand command = commands[index];

			if (command.Entry.Destructive)
			{
				if (armedLine == line)
				{
					armedLine = null;
					RefreshRunState();
					Dispatch(index, line);
					return;
				}

				if (UIManager.TryGetTK(DialogPanelName, out UITKDialogBox dialog) &&
					dialog.Open("This cannot simply be undone. Send it?\n\n" + line, () => Dispatch(index, line), () => { }))
				{
					return;
				}

				// No dialog to ask with (or it is busy with another question): confirm in place.
				armedLine = line;
				RefreshRunState();
				return;
			}

			Dispatch(index, line);
		}

		// ── Sending ─────────────────────────────────────────────────────────────────────────

		/// <summary>Sends a composed line through the chat command pipeline, exactly as the chat panel does.</summary>
		private void Dispatch(int index, string line)
		{
			if (!HasCatalogue || string.IsNullOrEmpty(line))
			{
				return;
			}
			if (line.Length > ChatBroadcast.MaxTextLength)
			{
				AppendOutput($"Not sent: the command is longer than {ChatBroadcast.MaxTextLength} characters.", OutputKind.Error);
				return;
			}
			if (Client == null)
			{
				AppendOutput("Not sent: not connected.", OutputKind.Error);
				return;
			}

			Client.Broadcast(new ChatBroadcast() { Text = line }, Channel.Reliable);
			AppendOutput("> " + line, OutputKind.Sent);

			if (index >= 0 && index < commands.Count)
			{
				StaffCommandEntry entry = commands[index].Entry;
				string first = index == selectedCommand && formValues.Count > 0 ? formValues[0] : null;

				if (entry.TicketAction && long.TryParse((first ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long ticket))
				{
					pendingRefreshTicket = ticket;
					pendingRefreshTime = Time.unscaledTime + ActionRefreshDelaySeconds;
				}
				if (entry.RosterAction)
				{
					pendingRefreshRoster = true;
					pendingRefreshTime = Time.unscaledTime + ActionRefreshDelaySeconds;
				}
			}

			if (formOrigin.HasValue)
			{
				ConsoleTab origin = formOrigin.Value;
				formOrigin = null;
				SelectTab(origin);
			}
		}

		/// <summary>Sends a staff request, but only under a catalogue and on a live client.</summary>
		private bool SendRequest<T>(T request) where T : struct, IBroadcast
		{
			if (!HasCatalogue || Client == null)
			{
				return false;
			}
			Client.Broadcast(request, Channel.Reliable);
			return true;
		}

		private void RequestRoster()
		{
			if (!IsTabAvailable(ConsoleTab.Players))
			{
				return;
			}
			nextRosterRequestTime = Time.unscaledTime + RosterRefreshSeconds;
			SendRequest(new StaffRosterRequestBroadcast());
		}

		private void RequestTicketQueue()
		{
			if (!IsTabAvailable(ConsoleTab.Tickets))
			{
				return;
			}
			SendRequest(new StaffTicketQueueRequestBroadcast()
			{
				Filter = ticketFilter,
				Page = ticketPage,
			});
		}

		private void RequestTicketDetail(long ticketID)
		{
			if (!IsTabAvailable(ConsoleTab.Tickets) || ticketID <= 0)
			{
				return;
			}
			SendRequest(new StaffTicketDetailRequestBroadcast()
			{
				TicketID = ticketID,
			});
		}

		private void SetTicketFilter(StaffTicketFilter filter)
		{
			if (!HasCatalogue)
			{
				return;
			}
			ticketFilter = filter;
			ticketPage = 1;
			hasQueue = false;
			queue = default;
			RenderTickets();
			RequestTicketQueue();
		}

		private void SetTicketPage(int page)
		{
			if (!HasCatalogue)
			{
				return;
			}
			int pages = TotalTicketPages();
			page = Math.Max(1, Math.Min(page, pages));
			if (page == ticketPage)
			{
				return;
			}
			ticketPage = page;
			RenderTickets();
			RequestTicketQueue();
		}

		private int TotalTicketPages()
		{
			if (!hasQueue || queue.TotalCount <= 0)
			{
				return 1;
			}
			return (queue.TotalCount + StaffTicketQueueBroadcast.PageSize - 1) / StaffTicketQueueBroadcast.PageSize;
		}

		private void SelectTicket(long ticketID)
		{
			selectedTicketID = ticketID;
			detail = default;
			hasDetail = false;
			RenderTickets();
			RequestTicketDetail(ticketID);
		}

		private void SelectCharacter(string name)
		{
			selectedCharacter = name;
			RenderRoster();
		}

		// ── Form state ──────────────────────────────────────────────────────────────────────

		private void ResetForm(int index)
		{
			selectedCommand = index;
			formValues.Clear();
			formOrigin = null;
			armedLine = null;

			if (index < 0 || index >= commands.Count)
			{
				return;
			}

			ParsedCommand command = commands[index];
			foreach (StaffArgument argument in command.Arguments)
			{
				// A required choice starts on its first word, so the dropdown and the model agree.
				formValues.Add(argument.Kind == StaffArgumentKind.Choice && !argument.Optional && argument.Choices.Count > 0
					? argument.Choices[0]
					: string.Empty);
			}
		}

		private void OnFormValueChanged(int argumentIndex, string value)
		{
			if (argumentIndex < 0 || argumentIndex >= formValues.Count)
			{
				return;
			}
			formValues[argumentIndex] = value ?? string.Empty;
			armedLine = null;
			RefreshRunState();
		}

		// ── Rendering ───────────────────────────────────────────────────────────────────────

		private void RenderAll()
		{
			RenderHeader();
			RenderTabs();
			RenderRoster();
			RenderTickets();
			RenderCommandList();
			RenderCommandDetail();
			RenderOutput();
		}

		private void RenderHeader()
		{
			if (subtitleLabel == null)
			{
				return;
			}
			subtitleLabel.text = HasCatalogue
				? $"{StaffConsoleFormat.AccessLevelName(accessLevel)} · {commands.Count} {(commands.Count == 1 ? "command" : "commands")}"
				: string.Empty;
		}

		private void RenderTabs()
		{
			SetTab(playersTab, playersPage, ConsoleTab.Players);
			SetTab(ticketsTab, ticketsPage, ConsoleTab.Tickets);
			SetTab(commandsTab, commandsPage, ConsoleTab.Commands);
		}

		private void SetTab(Button tab, VisualElement page, ConsoleTab which)
		{
			bool available = IsTabAvailable(which);
			bool active = available && activeTab == which;
			if (tab != null)
			{
				tab.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
				tab.EnableInClassList("fish-tab--active", active);
			}
			if (page != null)
			{
				page.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		private void RenderRoster()
		{
			if (rosterList == null)
			{
				return;
			}

			rosterList.Clear();

			StaffRosterEntry[] entries = hasRoster && roster.Characters != null ? roster.Characters : Array.Empty<StaffRosterEntry>();
			StaffRosterEntry? selected = null;

			foreach (StaffRosterEntry entry in entries)
			{
				string name = entry.Name ?? string.Empty;
				bool isSelected = selectedCharacter != null && string.Equals(name, selectedCharacter, StringComparison.Ordinal);
				if (isSelected)
				{
					selected = entry;
				}

				VisualElement row = NewRow(isSelected);

				VisualElement top = NewLine();
				top.Add(NewLabel(name, "fish-row__name", "staff-row__name"));
				if (entry.Dead) top.Add(NewLabel("Dead", "fish-badge", "fish-badge--danger", "staff-badge"));
				if (entry.InCombat) top.Add(NewLabel("In combat", "fish-badge", "fish-badge--accent", "staff-badge"));
				if (entry.Muted) top.Add(NewLabel("Muted", "fish-badge", "staff-badge"));
				top.Add(NewLabel(StaffConsoleFormat.FormatDistance(entry.Distance), "fish-row__meta", "staff-row__trail"));
				row.Add(top);

				VisualElement bottom = NewLine();
				bottom.Add(NewLabel($"{entry.Account} · {StaffConsoleFormat.AccessLevelName(entry.AccessLevel)}", "fish-row__meta", "staff-row__meta"));
				row.Add(bottom);

				row.RegisterCallback<ClickEvent>(_ => SelectCharacter(name));
				rosterList.Add(row);
			}

			if (rosterEmpty != null)
			{
				rosterEmpty.text = hasRoster ? "Nobody to list." : "Waiting for the roster.";
				rosterEmpty.style.display = entries.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (rosterSummary != null)
			{
				rosterSummary.text = hasRoster
					? $"{roster.SceneName} · {roster.TotalInScene} here · {roster.TotalOnServer} on server" +
						(roster.TotalInScene > entries.Length ? $" · showing {entries.Length}" : string.Empty)
					: string.Empty;
			}

			RenderRosterActions(selected);
		}

		private void RenderRosterActions(StaffRosterEntry? selected)
		{
			if (rosterActions == null)
			{
				return;
			}

			rosterActions.Clear();

			bool hasSelection = !string.IsNullOrEmpty(selectedCharacter);
			if (rosterSelected != null)
			{
				rosterSelected.text = hasSelection ? selectedCharacter : string.Empty;
			}
			if (rosterSelectedMeta != null)
			{
				rosterSelectedMeta.text = selected.HasValue
					? $"{selected.Value.Account} · {StaffConsoleFormat.AccessLevelName(selected.Value.AccessLevel)} · {StaffConsoleFormat.FormatDistance(selected.Value.Distance)}"
					: hasSelection ? "Not in this roster." : string.Empty;
			}

			int added = 0;
			if (hasSelection)
			{
				string name = selectedCharacter;
				for (int i = 0; i < commands.Count; ++i)
				{
					if (!commands[i].Entry.RosterAction)
					{
						continue;
					}
					int index = i;
					rosterActions.Add(NewActionButton(commands[i], () => OpenCommandForm(index, name, ConsoleTab.Players)));
					++added;
				}
			}

			if (rosterHint != null)
			{
				rosterHint.text = hasSelection ? "No actions are offered for characters." : "Select a character to act on.";
				rosterHint.style.display = added == 0 ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		private void RenderTickets()
		{
			if (filterUnassigned != null) filterUnassigned.EnableInClassList("fish-tab--active", ticketFilter == StaffTicketFilter.Unassigned);
			if (filterMine != null) filterMine.EnableInClassList("fish-tab--active", ticketFilter == StaffTicketFilter.Mine);
			if (filterAll != null) filterAll.EnableInClassList("fish-tab--active", ticketFilter == StaffTicketFilter.AllOpen);

			int pages = TotalTicketPages();
			if (pagerLabel != null)
			{
				pagerLabel.text = hasQueue ? $"Page {ticketPage} of {pages} · {queue.TotalCount}" : $"Page {ticketPage}";
			}
			pagerPrev?.SetEnabled(ticketPage > 1);
			pagerNext?.SetEnabled(ticketPage < pages);

			if (ticketList != null)
			{
				ticketList.Clear();

				StaffTicketSummary[] tickets = hasQueue && queue.Tickets != null ? queue.Tickets : Array.Empty<StaffTicketSummary>();
				long now = DateTime.UtcNow.Ticks;

				foreach (StaffTicketSummary ticket in tickets)
				{
					long id = ticket.TicketID;
					VisualElement row = NewRow(id == selectedTicketID);

					VisualElement top = NewLine();
					top.Add(NewLabel("#" + id.ToString(CultureInfo.InvariantCulture), "fish-row__name", "staff-row__trail"));
					top.Add(NewLabel(ticket.Status, "fish-badge", "fish-badge--accent", "staff-badge"));
					top.Add(NewLabel(ticket.Category, "fish-row__meta", "staff-row__name"));
					top.Add(NewLabel(StaffConsoleFormat.FormatAge(ticket.LastActivityUtcTicks, now), "fish-row__meta", "staff-row__trail"));
					row.Add(top);

					VisualElement middle = NewLine();
					middle.Add(NewLabel(ticket.Subject, "fish-row__name", "staff-row__name"));
					row.Add(middle);

					VisualElement bottom = NewLine();
					string reporter = !string.IsNullOrEmpty(ticket.ReporterCharacter) ? ticket.ReporterCharacter : ticket.ReporterAccount;
					string assignee = !string.IsNullOrEmpty(ticket.AssignedTo) ? ticket.AssignedTo : "Unassigned";
					bottom.Add(NewLabel($"{reporter} → {assignee}", "fish-row__meta", "staff-row__meta"));
					row.Add(bottom);

					row.RegisterCallback<ClickEvent>(_ => SelectTicket(id));
					ticketList.Add(row);
				}

				if (ticketEmpty != null)
				{
					ticketEmpty.text = hasQueue ? "No tickets." : "Waiting for the queue.";
					ticketEmpty.style.display = tickets.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;
				}
			}

			RenderTicketDetail();
		}

		private void RenderTicketDetail()
		{
			if (ticketDetail == null)
			{
				return;
			}

			ticketDetail.Clear();

			if (selectedTicketID <= 0)
			{
				SetHint(ticketHint, "Select a ticket to read it.", true);
				return;
			}
			if (!hasDetail)
			{
				SetHint(ticketHint, "Loading the ticket.", true);
				return;
			}
			if (!detail.Found)
			{
				SetHint(ticketHint, "That ticket was not found.", true);
				return;
			}
			SetHint(ticketHint, string.Empty, false);

			StaffTicketSummary ticket = detail.Ticket;
			long now = DateTime.UtcNow.Ticks;

			Label title = NewLabel($"#{ticket.TicketID.ToString(CultureInfo.InvariantCulture)} {ticket.Subject}", "fish-label--title", "staff-detail-title");
			ticketDetail.Add(title);

			VisualElement actions = new VisualElement();
			actions.AddToClassList("staff-actions");
			string ticketNumber = ticket.TicketID.ToString(CultureInfo.InvariantCulture);
			for (int i = 0; i < commands.Count; ++i)
			{
				if (!commands[i].Entry.TicketAction)
				{
					continue;
				}
				int index = i;
				actions.Add(NewActionButton(commands[i], () => OpenCommandForm(index, ticketNumber, ConsoleTab.Tickets)));
			}
			if (actions.childCount > 0)
			{
				ticketDetail.Add(actions);
			}

			AddKeyValue("Status", ticket.Status);
			AddKeyValue("Category", ticket.Category);
			AddKeyValue("Priority", ticket.Priority.ToString(CultureInfo.InvariantCulture));
			AddKeyValue("Reporter", string.IsNullOrEmpty(ticket.ReporterCharacter)
				? ticket.ReporterAccount
				: $"{ticket.ReporterCharacter} ({ticket.ReporterAccount})");
			if (!string.IsNullOrEmpty(ticket.TargetCharacter))
			{
				AddKeyValue("Reported", ticket.TargetCharacter);
			}
			AddKeyValue("Assigned", string.IsNullOrEmpty(ticket.AssignedTo) ? "Unassigned" : ticket.AssignedTo);
			AddKeyValue("Scene", detail.SceneName);
			AddKeyValue("Activity", StaffConsoleFormat.FormatAge(ticket.LastActivityUtcTicks, now));

			ticketDetail.Add(NewLabel("REPORT", "fish-section", "staff-section"));
			ticketDetail.Add(NewLabel(detail.Body, "staff-body-text"));

			if (!string.IsNullOrEmpty(detail.Resolution))
			{
				ticketDetail.Add(NewLabel("RESOLUTION", "fish-section", "staff-section"));
				ticketDetail.Add(NewLabel(detail.Resolution, "staff-body-text"));
			}

			StaffTicketMessageEntry[] messages = detail.Messages ?? Array.Empty<StaffTicketMessageEntry>();
			ticketDetail.Add(NewLabel($"MESSAGES ({messages.Length})", "fish-section", "staff-section"));
			foreach (StaffTicketMessageEntry message in messages)
			{
				VisualElement box = new VisualElement();
				box.AddToClassList("staff-message");
				box.EnableInClassList("staff-message--internal", message.Internal);

				string author = message.AuthorIsStaff ? $"{message.Author} (staff)" : message.Author;
				box.Add(NewLabel($"{author} · {StaffConsoleFormat.FormatAge(message.CreatedUtcTicks, now)}", "staff-message__head"));
				if (message.Internal)
				{
					box.Add(NewLabel("staff note — player cannot see", "staff-message__tag"));
				}
				box.Add(NewLabel(message.Body, "staff-body-text"));
				ticketDetail.Add(box);
			}
		}

		private void AddKeyValue(string key, string value)
		{
			VisualElement row = new VisualElement();
			row.AddToClassList("staff-kv");
			row.Add(NewLabel(key, "fish-label--caption", "staff-kv__key"));
			row.Add(NewLabel(value, "fish-row__meta", "staff-kv__value"));
			ticketDetail.Add(row);
		}

		private void RenderCommandList()
		{
			if (commandList == null)
			{
				return;
			}

			commandList.Clear();

			string currentCategory = null;
			for (int i = 0; i < commands.Count; ++i)
			{
				ParsedCommand command = commands[i];
				string category = CategoryOf(command.Entry);
				if (!string.Equals(category, currentCategory, StringComparison.OrdinalIgnoreCase))
				{
					currentCategory = category;
					commandList.Add(NewLabel(category.ToUpperInvariant(), "fish-section", "staff-category"));
				}

				int index = i;
				VisualElement row = NewRow(i == selectedCommand);
				row.name = "staff-command-" + i.ToString(CultureInfo.InvariantCulture);
				VisualElement line = NewLine();
				line.Add(NewLabel(command.Title, "fish-row__name", "staff-row__name"));
				if (command.Entry.Destructive)
				{
					line.Add(NewLabel("Confirms", "fish-badge", "fish-badge--danger", "staff-badge"));
				}
				row.Add(line);
				row.tooltip = command.Entry.Summary;
				row.RegisterCallback<ClickEvent>(_ => SelectCommand(index));
				commandList.Add(row);
			}

			if (commandEmpty != null)
			{
				commandEmpty.style.display = commands.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		private void RenderCommandDetail()
		{
			if (form == null)
			{
				return;
			}

			form.Clear();
			characterPickers.Clear();

			bool hasCommand = selectedCommand >= 0 && selectedCommand < commands.Count;
			if (commandTitle != null)
			{
				commandTitle.text = hasCommand ? commands[selectedCommand].Title : string.Empty;
			}
			if (commandSummary != null)
			{
				commandSummary.text = hasCommand ? (commands[selectedCommand].Entry.Summary ?? string.Empty) : "Select a command.";
			}
			if (runButton != null)
			{
				runButton.style.display = hasCommand ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (!hasCommand)
			{
				if (commandPreview != null) commandPreview.text = string.Empty;
				SetStatus(string.Empty, false);
				return;
			}

			ParsedCommand command = commands[selectedCommand];
			if (command.SpecError == null)
			{
				for (int i = 0; i < command.Arguments.Count; ++i)
				{
					form.Add(BuildField(i, command.Arguments[i]));
				}
				if (command.Arguments.Count == 0)
				{
					form.Add(NewLabel("This command takes no arguments.", "fish-empty", "staff-empty"));
				}
			}

			RefreshRunState();
		}

		/// <summary>Builds one argument's input, named <c>staff-arg-N</c>.</summary>
		private VisualElement BuildField(int index, StaffArgument argument)
		{
			VisualElement field = new VisualElement();
			field.AddToClassList("staff-field");

			field.Add(NewLabel(argument.Optional ? argument.Label + " (optional)" : argument.Label, "fish-label--caption", "staff-field__label"));

			VisualElement inputs = new VisualElement();
			inputs.AddToClassList("staff-field__inputs");
			field.Add(inputs);

			string argName = "staff-arg-" + index.ToString(CultureInfo.InvariantCulture);
			string current = index < formValues.Count ? formValues[index] : string.Empty;

			if (argument.Kind == StaffArgumentKind.Choice)
			{
				List<string> choices = new List<string>();
				if (argument.Optional)
				{
					choices.Add(BlankChoice);
				}
				choices.AddRange(argument.Choices);

				DropdownField dropdown = new DropdownField()
				{
					name = argName,
					choices = choices,
				};
				dropdown.AddToClassList("fish-dropdown");
				dropdown.AddToClassList("staff-field__dropdown");
				dropdown.SetValueWithoutNotify(string.IsNullOrEmpty(current) ? choices[0] : current);
				dropdown.RegisterValueChangedCallback(evt =>
					OnFormValueChanged(index, evt.newValue == BlankChoice ? string.Empty : evt.newValue));
				inputs.Add(dropdown);
			}
			else
			{
				TextField text = new TextField()
				{
					name = argName,
					isDelayed = false,
					maxLength = ChatBroadcast.MaxTextLength,
				};
				text.AddToClassList("fish-input");
				text.AddToClassList("fish-input--compact");
				text.AddToClassList("staff-field__text");
				text.SetValueWithoutNotify(current);
				text.RegisterValueChangedCallback(evt => OnFormValueChanged(index, evt.newValue));
				inputs.Add(text);

				if (argument.Kind == StaffArgumentKind.Character)
				{
					DropdownField picker = new DropdownField()
					{
						name = argName + "-pick",
						choices = RosterNames(),
						tooltip = "Pick from the roster",
					};
					picker.AddToClassList("fish-dropdown");
					picker.AddToClassList("staff-field__pick");
					picker.RegisterValueChangedCallback(evt =>
					{
						if (string.IsNullOrEmpty(evt.newValue))
						{
							return;
						}
						text.SetValueWithoutNotify(evt.newValue);
						OnFormValueChanged(index, evt.newValue);
					});
					characterPickers.Add(picker);
					inputs.Add(picker);
				}
			}

			field.Add(NewLabel(KindHint(argument.Kind), "fish-hint", "staff-field__hint"));
			return field;
		}

		private void RefreshCharacterPickers()
		{
			List<string> names = RosterNames();
			foreach (DropdownField picker in characterPickers)
			{
				picker.choices = new List<string>(names);
			}
		}

		private List<string> RosterNames()
		{
			List<string> names = new List<string>();
			if (hasRoster && roster.Characters != null)
			{
				foreach (StaffRosterEntry entry in roster.Characters)
				{
					if (!string.IsNullOrEmpty(entry.Name))
					{
						names.Add(entry.Name);
					}
				}
			}
			return names;
		}

		/// <summary>Updates the preview line, the status line and the Run button from the form.</summary>
		private void RefreshRunState()
		{
			bool ok = TryComposeSelected(out string line, out string error);

			if (commandPreview != null)
			{
				commandPreview.text = line ?? string.Empty;
			}

			if (armedLine != null && armedLine != line)
			{
				armedLine = null;
			}

			if (runButton != null)
			{
				bool armed = armedLine != null;
				runButton.text = armed ? ConfirmText : RunText;
				runButton.EnableInClassList("fish-button--danger", armed);
				runButton.EnableInClassList("fish-button--primary", !armed);
			}

			if (ok)
			{
				SetStatus(armedLine != null
					? "This cannot simply be undone."
					: $"{line.Length}/{ChatBroadcast.MaxTextLength} characters", false);
			}
			else
			{
				SetStatus(error, true);
			}
		}

		private void SetStatus(string text, bool isError)
		{
			if (commandStatus == null)
			{
				return;
			}
			commandStatus.text = text ?? string.Empty;
			commandStatus.EnableInClassList("staff-status--error", isError);
		}

		private void AppendOutput(string text, OutputKind kind)
		{
			output.Add(new OutputLine() { Text = text ?? string.Empty, Kind = kind });
			while (output.Count > MaxOutputLines)
			{
				output.RemoveAt(0);
			}

			if (outputList == null)
			{
				return;
			}

			outputList.Add(NewOutputLabel(output[output.Count - 1]));
			while (outputList.childCount > MaxOutputLines)
			{
				outputList.RemoveAt(0);
			}
			ScrollOutputToEnd();
		}

		private void RenderOutput()
		{
			if (outputList == null)
			{
				return;
			}
			outputList.Clear();
			foreach (OutputLine line in output)
			{
				outputList.Add(NewOutputLabel(line));
			}
			ScrollOutputToEnd();
		}

		private void ScrollOutputToEnd()
		{
			ScrollView scroll = outputScroll;
			if (scroll == null)
			{
				return;
			}
			scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0f, scroll.contentContainer.layout.height));
		}

		private static Label NewOutputLabel(OutputLine line)
		{
			Label label = NewLabel(line.Text, "staff-output__line");
			switch (line.Kind)
			{
				case OutputKind.Sent: label.AddToClassList("staff-output__line--sent"); break;
				case OutputKind.Notice: label.AddToClassList("staff-output__line--notice"); break;
				case OutputKind.Error: label.AddToClassList("staff-output__line--error"); break;
			}
			return label;
		}

		// ── Element helpers ─────────────────────────────────────────────────────────────────

		private static VisualElement NewRow(bool selected)
		{
			VisualElement row = new VisualElement();
			row.AddToClassList("fish-row");
			row.AddToClassList("staff-row");
			row.EnableInClassList("fish-row--selected", selected);
			return row;
		}

		private static VisualElement NewLine()
		{
			VisualElement line = new VisualElement();
			line.AddToClassList("staff-row__line");
			return line;
		}

		/// <summary>A label that never parses markup: much of what the console shows was written by players.</summary>
		private static Label NewLabel(string text, params string[] classes)
		{
			Label label = new Label(text ?? string.Empty)
			{
				enableRichText = false,
			};
			foreach (string cls in classes)
			{
				label.AddToClassList(cls);
			}
			return label;
		}

		private static Button NewActionButton(ParsedCommand command, Action onClick)
		{
			Button button = new Button(onClick)
			{
				text = command.ActionLabel,
				tooltip = command.Entry.Summary,
			};
			button.AddToClassList("fish-button");
			button.AddToClassList("staff-action");
			if (command.Entry.Destructive)
			{
				button.AddToClassList("fish-button--danger");
			}
			return button;
		}

		private static void SetHint(Label hint, string text, bool visible)
		{
			if (hint == null)
			{
				return;
			}
			hint.text = text;
			hint.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
		}

		private static string CategoryOf(StaffCommandEntry entry)
		{
			return string.IsNullOrWhiteSpace(entry.Category) ? "General" : entry.Category.Trim();
		}

		/// <summary>A generic description of what an argument kind accepts.</summary>
		private static string KindHint(StaffArgumentKind kind)
		{
			switch (kind)
			{
				case StaffArgumentKind.Text: return "Free text; the rest of the line.";
				case StaffArgumentKind.Word: return "One word.";
				case StaffArgumentKind.Character: return "A character name.";
				case StaffArgumentKind.Account: return "An account name.";
				case StaffArgumentKind.Integer: return "A whole number.";
				case StaffArgumentKind.Number: return "A number.";
				case StaffArgumentKind.Duration: return "A duration such as 30m, 2h or 7d.";
				case StaffArgumentKind.Ticket: return "A ticket number.";
				case StaffArgumentKind.Choice: return "Pick one.";
				default: return string.Empty;
			}
		}
	}
}
