using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit guild panel. Two pages — a filterable member roster and an information page
	/// carrying the guild's notice, message of the day and leadership actions — plus a per-member
	/// context menu and hover card. The shared dialog and context-menu overlays are resolved by
	/// name through the <see cref="UIManager"/> rather than referenced directly.
	/// </summary>
	/// <remarks>
	/// The roster is held in two halves that must not be confused:
	///
	/// * <see cref="roster"/> is the MODEL — plain data, no <c>VisualElement</c> anywhere in it.
	///   It is owned by the character, not by the visual tree, so it survives a tree rebuild and
	///   is cleared only when the character is unset.
	/// * <see cref="rows"/> is the VIEW — the elements currently rendering the model. Every entry
	///   in it belongs to one specific visual tree.
	///
	/// <c>UIDocument</c> re-clones the UXML every time the document is enabled, so the entire view
	/// is thrown away on each hide/show. The previous version of this panel kept ONLY the view;
	/// after one close the rows were orphaned in a dead tree and the roster could never come back,
	/// because the data that would have rebuilt it had gone with the elements. That is why
	/// <see cref="OnStarting"/> drops the view and <see cref="OnAfterStarting"/> rebuilds it from
	/// the model — and why the guild text, the active tab and the filter state are all fields
	/// rather than element reads.
	/// </remarks>
	public class UITKGuild : UITKCharacterControl
	{
		/// <summary>Name of the guild name label element.</summary>
		private const string GUILD_LABEL_NAME = "guild-name";

		/// <summary>Name of the container that holds the generated member rows.</summary>
		private const string MEMBER_LIST_NAME = "guild-member-list";

		/// <summary>Name of the create-guild button.</summary>
		private const string CREATE_BUTTON_NAME = "guild-create";

		/// <summary>Name of the leave-guild button.</summary>
		private const string LEAVE_BUTTON_NAME = "guild-leave";

		/// <summary>Name of the invite-to-guild button.</summary>
		private const string INVITE_BUTTON_NAME = "guild-invite";

		/// <summary>USS class applied to each generated member row.</summary>
		private const string ROW_CLASS = "guild-member";

		/// <summary>USS class applied to a member row's presence dot.</summary>
		private const string ROW_DOT_CLASS = "guild-member__dot";

		/// <summary>USS class applied to a member row's name label.</summary>
		private const string ROW_NAME_CLASS = "guild-member__name";

		/// <summary>USS class applied to a member row's rank label.</summary>
		private const string ROW_LEVEL_CLASS = "guild-member__level";
		private const string ROW_RANK_CLASS = "guild-member__rank";

		/// <summary>USS class applied to a member row's class label.</summary>
		private const string ROW_CLASS_CLASS = "guild-member__class";

		/// <summary>USS class applied to a member row's location label.</summary>
		private const string ROW_LOCATION_CLASS = "guild-member__location";
		/// <summary>Name of the header label describing guild state.</summary>
		private const string SUBTITLE_NAME = "guild-subtitle";
		/// <summary>Name of the header badge showing member count.</summary>
		private const string COUNT_NAME = "guild-count";
		/// <summary>Name of the label shown when the player has no guild.</summary>
		private const string EMPTY_NAME = "guild-empty";
		/// <summary>Name of the column caption strip.</summary>
		private const string COLUMNS_NAME = "guild-columns";

		/// <summary>Name of the roster tab button.</summary>
		private const string TAB_ROSTER_NAME = "guild-tab-roster";
		/// <summary>Name of the info tab button.</summary>
		private const string TAB_INFO_NAME = "guild-tab-info";
		/// <summary>Name of the roster page.</summary>
		private const string PAGE_ROSTER_NAME = "guild-roster-tab";
		/// <summary>Name of the info page.</summary>
		private const string PAGE_INFO_NAME = "guild-info-tab";
		/// <summary>Name of the log tab button.</summary>
		private const string TAB_LOG_NAME = "guild-tab-log";
		/// <summary>Name of the log page.</summary>
		private const string PAGE_LOG_NAME = "guild-log-tab";
		/// <summary>Name of the container log entries are built into.</summary>
		private const string LOG_LIST_NAME = "guild-log-list";
		/// <summary>Name of the label shown when the log is empty.</summary>
		private const string LOG_EMPTY_NAME = "guild-log-empty";
		/// <summary>USS class applied to each generated log entry.</summary>
		private const string LOG_ENTRY_CLASS = "guild-log-entry";
		/// <summary>USS class applied to a log entry's sentence.</summary>
		private const string LOG_ENTRY_TEXT_CLASS = "guild-log-entry__text";
		/// <summary>USS class applied to a log entry's timestamp.</summary>
		private const string LOG_ENTRY_TIME_CLASS = "guild-log-entry__time";

		/// <summary>Name of the notice band container.</summary>
		private const string NOTICE_BAND_NAME = "guild-notice-band";
		/// <summary>Name of the notice band label.</summary>
		private const string NOTICE_BAND_LABEL_NAME = "guild-notice-band-label";
		/// <summary>Name of the info page's message-of-the-day label.</summary>
		private const string MOTD_NAME = "guild-motd";
		/// <summary>Name of the info page's notice label.</summary>
		private const string NOTICE_NAME = "guild-notice";
		/// <summary>Name of the edit-message-of-the-day button.</summary>
		private const string EDIT_MOTD_NAME = "guild-edit-motd";
		/// <summary>Name of the edit-notice button.</summary>
		private const string EDIT_NOTICE_NAME = "guild-edit-notice";
		/// <summary>Name of the disband button.</summary>
		private const string DISBAND_NAME = "guild-disband";

		/// <summary>Name of the roster search field.</summary>
		private const string SEARCH_NAME = "guild-search";
		/// <summary>Name of the sort-cycle button.</summary>
		private const string SORT_NAME = "guild-sort";
		/// <summary>Name of the online-filter-cycle button.</summary>
		private const string ONLINE_FILTER_NAME = "guild-online-filter";

		/// <summary>Name of the shared hover card element.</summary>
		private const string HOVER_CARD_NAME = "guild-hover-card";
		/// <summary>Name of the hover card's name label.</summary>
		private const string HOVER_NAME_NAME = "guild-hover-name";
		/// <summary>Name of the hover card's rank label.</summary>
		private const string HOVER_RANK_NAME = "guild-hover-rank";
		/// <summary>Name of the hover card's class label.</summary>
		private const string HOVER_CLASS_NAME = "guild-hover-class";
		/// <summary>Name of the hover card's location label.</summary>
		private const string HOVER_LOCATION_NAME = "guild-hover-location";
		/// <summary>Name of the hover card's last-seen label.</summary>
		private const string HOVER_SEEN_NAME = "guild-hover-seen";
		/// <summary>Name of the hover card's public note line.</summary>
		private const string HOVER_NOTE_NAME = "guild-hover-note";
		/// <summary>Name of the hover card's officer note line.</summary>
		private const string HOVER_OFFICER_NOTE_NAME = "guild-hover-officer-note";

		/// <summary>USS class marking the active tab button.</summary>
		private const string TAB_ACTIVE_CLASS = "fish-tab--active";

		/// <summary>
		/// Location string the server persists for a member who is not connected.
		/// </summary>
		/// <remarks>
		/// <c>GuildSystem.CharacterSystem_OnDisconnect</c> writes this exact literal into the
		/// membership row, so it is the presence signal the roster payload carries.
		/// </remarks>
		private const string OFFLINE_LOCATION = "Offline";

		/// <summary>
		/// How the roster is ordered.
		/// </summary>
		private enum RosterSort : byte
		{
			/// <summary>Rank descending, then name — the default a guild roster is read in.</summary>
			Rank = 0,
			/// <summary>Name, alphabetically.</summary>
			Name = 1,
			/// <summary>Online first, then name.</summary>
			Online = 2,
			/// <summary>Zone, then name.</summary>
			Location = 3,
		}

		/// <summary>
		/// Which page of the panel is showing.
		/// </summary>
		private enum GuildTab : byte
		{
			/// <summary>The member roster.</summary>
			Roster = 0,
			/// <summary>The notice, message of the day and leadership actions.</summary>
			Info = 1,
			/// <summary>The activity log.</summary>
			Log = 2,
		}

		/// <summary>
		/// Which members the roster shows.
		/// </summary>
		private enum RosterFilter : byte
		{
			/// <summary>Everyone.</summary>
			All = 0,
			/// <summary>Only members who are connected.</summary>
			Online = 1,
			/// <summary>Only members who are not connected.</summary>
			Offline = 2,
		}

		/// <summary>
		/// One guild member's data. Contains no <c>VisualElement</c> by design — see the class
		/// remarks for why the model and the view are kept apart.
		/// </summary>
		private sealed class MemberModel
		{
			/// <summary>The member's character ID. The ONLY identity any action may use.</summary>
			public long CharacterID;
			/// <summary>The member's position on the guild's rank ladder.</summary>
			/// <remarks>
			/// A number, not a name. The NAME of this rank is whatever the guild called the row at
			/// this position, which is looked up in <c>rankLadder</c> when the row is rendered —
			/// so a rank rename re-renders the roster without the roster knowing anything changed.
			/// </remarks>
			public byte RankOrder;
			/// <summary>The member's character level.</summary>
			public int Level;
			/// <summary>Note about this member visible to every member of the guild.</summary>
			public string PublicNote = string.Empty;
			/// <summary>
			/// Note about this member visible only to ranks holding <c>ViewOfficerNotes</c>.
			/// </summary>
			/// <remarks>
			/// Empty unless the SERVER decided this client may read it. There is no client-side
			/// filter here and there must not be one: the server does not put the column on the
			/// wire for a client that may not see it, so an empty string here means "not sent",
			/// not "sent and hidden".
			/// </remarks>
			public string OfficerNote = string.Empty;
			/// <summary>The member's location, or "Offline".</summary>
			public string Location = string.Empty;
			/// <summary>The member's race identifier, resolved to a name for display.</summary>
			public int RaceID;
			/// <summary>UTC ticks of the member's last character save.</summary>
			public long LastOnlineUtcTicks;
			/// <summary>
			/// Last resolved character name, or empty while the name lookup is still in flight.
			/// </summary>
			/// <remarks>
			/// Cached so a rebuilt row renders immediately instead of flashing blank while a
			/// second round trip to the naming system completes. It is a DISPLAY value only;
			/// nothing resolves a target from it.
			/// </remarks>
			public string Name = string.Empty;

			/// <summary>True when the member is connected somewhere.</summary>
			public bool IsOnline =>
				!string.IsNullOrEmpty(Location) &&
				!string.Equals(Location, OFFLINE_LOCATION, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Visual elements backing a single guild member row.
		/// </summary>
		private sealed class MemberRow
		{
			/// <summary>Root container for the row.</summary>
			public VisualElement Root;
			/// <summary>Presence dot leading the row.</summary>
			public VisualElement Dot;
			/// <summary>Member name label.</summary>
			public Label Name;
			/// <summary>Member level label.</summary>
			public Label Level;
			/// <summary>Member rank label.</summary>
			public Label Rank;
			/// <summary>Member class label.</summary>
			public Label Class;
			/// <summary>Member location label.</summary>
			public Label Location;
		}

		/// <summary>
		/// The roster MODEL, keyed by character ID. Survives tree rebuilds.
		/// </summary>
		private readonly Dictionary<long, MemberModel> roster = new Dictionary<long, MemberModel>();

		/// <summary>
		/// The guild's rank ladder, keyed by rank order.
		/// </summary>
		/// <remarks>
		/// Held so a roster row can render the rank's NAME. Under the old enum a rank rendered as
		/// <c>model.Rank.ToString()</c>; ranks are now rows a guild names for itself, so the name
		/// has to come from the ladder the server sent. A member whose order is not in the ladder
		/// yet — the roster and the ladder are separate messages and either may arrive first —
		/// renders as the bare number rather than as blank.
		/// </remarks>
		private readonly Dictionary<byte, GuildRankEntry> rankLadder = new Dictionary<byte, GuildRankEntry>();

		/* The viewer's own rank order, permission mask and the guild's leader seat are NOT
		 * mirrored into fields here. They live on IGuildController, which is the one place the
		 * whole client reads them from — this panel, the context menu and any future guild UI —
		 * and a second copy would be a second thing to forget to clear on leaving a guild.
		 * GuildController_OnReceiveGuildRanks writes them straight onto the controller, and every
		 * read below goes through it. */

		/// <summary>
		/// The roster VIEW, keyed by character ID. Belongs to one visual tree and is dropped
		/// whenever that tree is replaced.
		/// </summary>
		private readonly Dictionary<long, MemberRow> rows = new Dictionary<long, MemberRow>();

		/// <summary>
		/// Reusable ordering buffer, so re-sorting the roster does not allocate a list per keypress
		/// while the player is typing in the search box.
		/// </summary>
		private readonly List<MemberModel> orderBuffer = new List<MemberModel>();

		/// <summary>Last known guild name.</summary>
		private string guildName = string.Empty;
		/// <summary>Last known guild notice.</summary>
		private string guildNotice = string.Empty;
		/// <summary>Last known guild message of the day.</summary>
		private string guildMessageOfTheDay = string.Empty;

		/// <summary>Which page is showing.</summary>
		private GuildTab activeTab = GuildTab.Roster;

		/// <summary>Most recent activity log, newest first. Survives tree rebuilds.</summary>
		private GuildLogEntry[] logEntries = Array.Empty<GuildLogEntry>();

		/// <summary>
		/// Names resolved for the characters the log mentions.
		/// </summary>
		/// <remarks>
		/// The log carries character IDs, and the names have to be resolved asynchronously — often
		/// for people who have already left the guild and are therefore not in the roster at all.
		/// Cached here so re-opening the tab does not re-issue every lookup, and so a rebuild
		/// renders the sentences it had rather than a page of blanks.
		/// </remarks>
		private readonly Dictionary<long, string> logNameCache = new Dictionary<long, string>();
		/// <summary>Current roster ordering.</summary>
		private RosterSort sort = RosterSort.Rank;
		/// <summary>Current roster presence filter.</summary>
		private RosterFilter filter = RosterFilter.All;
		/// <summary>Current lower-cased search term, or empty.</summary>
		private string searchTerm = string.Empty;

		/// <summary>
		/// Set when the roster view no longer matches the model; cleared by the next tick.
		/// </summary>
		/// <remarks>
		/// The server sends the whole roster as one <c>GuildAddMultipleBroadcast</c>, which the
		/// controller unpacks into one add per member — and each add can change the sort order and
		/// the filter result, so each one invalidates the view. Rebuilding on the spot meant a
		/// hundred rebuilds of a hundred rows on every pump; the same is true of the name lookups,
		/// which resolve synchronously when the name is already cached. Coalescing to one rebuild
		/// per frame turns the whole burst into a single pass.
		/// </remarks>
		private bool rosterViewDirty;

		/// <summary>
		/// Set when the log view no longer matches its entries; cleared by the next tick.
		/// </summary>
		/// <remarks>
		/// Same reasoning as <see cref="rosterViewDirty"/>: resolving the names a full page of log
		/// entries mentions produces a burst of callbacks, each of which changes the text.
		/// </remarks>
		private bool logViewDirty;

		/// <summary>Label displaying the guild name.</summary>
		private Label guildLabel;
		/// <summary>The container element that holds the generated member rows.</summary>
		private VisualElement memberList;
		/// <summary>Header label describing guild state.</summary>
		private Label subtitleLabel;
		/// <summary>Header badge showing member count.</summary>
		private Label countLabel;
		/// <summary>Label shown in place of the roster when there is no guild.</summary>
		private Label emptyLabel;
		/// <summary>Column caption strip, hidden while the roster is empty.</summary>
		private VisualElement columns;

		/// <summary>Roster tab button.</summary>
		private Button rosterTabButton;
		/// <summary>Info tab button.</summary>
		private Button infoTabButton;
		/// <summary>Roster page container.</summary>
		private VisualElement rosterPage;
		/// <summary>Info page container.</summary>
		private VisualElement infoPage;
		/// <summary>Log tab button.</summary>
		private Button logTabButton;
		/// <summary>Log page container.</summary>
		private VisualElement logPage;
		/// <summary>Container log entries are built into.</summary>
		private VisualElement logList;
		/// <summary>Label shown when the log is empty.</summary>
		private Label logEmptyLabel;

		/// <summary>Notice band container.</summary>
		private VisualElement noticeBand;
		/// <summary>Notice band label.</summary>
		private Label noticeBandLabel;
		/// <summary>Info page message-of-the-day label.</summary>
		private Label motdLabel;
		/// <summary>Info page notice label.</summary>
		private Label noticeLabel;
		/// <summary>Edit-message-of-the-day button.</summary>
		private Button editMotdButton;
		/// <summary>Edit-notice button.</summary>
		private Button editNoticeButton;
		/// <summary>Disband button.</summary>
		private Button disbandButton;
		/// <summary>Create-guild button.</summary>
		private Button createButton;
		/// <summary>Invite-to-guild button.</summary>
		private Button inviteButton;
		/// <summary>Leave-guild button.</summary>
		private Button leaveButton;

		/// <summary>Roster search field.</summary>
		private TextField searchField;
		/// <summary>Sort-cycle button.</summary>
		private Button sortButton;
		/// <summary>Online-filter-cycle button.</summary>
		private Button onlineFilterButton;

		/// <summary>Shared hover card.</summary>
		private VisualElement hoverCard;
		/// <summary>Hover card name label.</summary>
		private Label hoverName;
		/// <summary>Hover card rank label.</summary>
		private Label hoverRank;
		/// <summary>Hover card class label.</summary>
		private Label hoverClass;
		/// <summary>Hover card location label.</summary>
		private Label hoverLocation;
		/// <summary>Hover card last-seen label.</summary>
		private Label hoverSeen;
		/// <summary>Hover card public note line.</summary>
		private Label hoverNote;
		/// <summary>Hover card officer note line.</summary>
		private Label hoverOfficerNote;

		/// <summary>
		/// Queries elements and wires up the action buttons.
		/// </summary>
		/// <remarks>
		/// Runs against a FRESH tree every time, including after a rebuild, so the first thing it
		/// does is drop the old view. The elements in it belong to a tree that no longer exists;
		/// keeping them would leave the panel writing into orphaned elements forever.
		/// The buttons are new objects too, so <c>+=</c> here cannot accumulate handlers.
		/// </remarks>
		public override void OnStarting()
		{
			rows.Clear();

			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			guildLabel = root.Q<Label>(GUILD_LABEL_NAME);
			memberList = root.Q(MEMBER_LIST_NAME);
			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			countLabel = root.Q<Label>(COUNT_NAME);
			emptyLabel = root.Q<Label>(EMPTY_NAME);
			columns = root.Q(COLUMNS_NAME);

			rosterTabButton = root.Q<Button>(TAB_ROSTER_NAME);
			infoTabButton = root.Q<Button>(TAB_INFO_NAME);
			rosterPage = root.Q(PAGE_ROSTER_NAME);
			infoPage = root.Q(PAGE_INFO_NAME);
			logTabButton = root.Q<Button>(TAB_LOG_NAME);
			logPage = root.Q(PAGE_LOG_NAME);
			logList = root.Q(LOG_LIST_NAME);
			logEmptyLabel = root.Q<Label>(LOG_EMPTY_NAME);

			noticeBand = root.Q(NOTICE_BAND_NAME);
			noticeBandLabel = root.Q<Label>(NOTICE_BAND_LABEL_NAME);
			motdLabel = root.Q<Label>(MOTD_NAME);
			noticeLabel = root.Q<Label>(NOTICE_NAME);
			editMotdButton = root.Q<Button>(EDIT_MOTD_NAME);
			editNoticeButton = root.Q<Button>(EDIT_NOTICE_NAME);
			disbandButton = root.Q<Button>(DISBAND_NAME);

			searchField = root.Q<TextField>(SEARCH_NAME);
			sortButton = root.Q<Button>(SORT_NAME);
			onlineFilterButton = root.Q<Button>(ONLINE_FILTER_NAME);

			hoverCard = root.Q(HOVER_CARD_NAME);
			hoverName = root.Q<Label>(HOVER_NAME_NAME);
			hoverRank = root.Q<Label>(HOVER_RANK_NAME);
			hoverClass = root.Q<Label>(HOVER_CLASS_NAME);
			hoverLocation = root.Q<Label>(HOVER_LOCATION_NAME);
			hoverSeen = root.Q<Label>(HOVER_SEEN_NAME);
			hoverNote = root.Q<Label>(HOVER_NOTE_NAME);
			hoverOfficerNote = root.Q<Label>(HOVER_OFFICER_NOTE_NAME);

			/* Every label below renders text a PLAYER typed — a guild name, a notice, a message
			 * of the day, a rank they named, a note they wrote about somebody. UI Toolkit labels
			 * parse rich text by default, so a notice containing a size or colour tag would be
			 * obeyed rather than shown: a guild could set a 500-point notice that covered the
			 * panel, or a rank name that recoloured every row it appeared in. Disabling it makes
			 * the tag render as the characters the author typed, which is also what somebody who
			 * genuinely wanted to write "<3" expects. */
			DisableRichText(guildLabel);
			DisableRichText(subtitleLabel);
			DisableRichText(motdLabel);
			DisableRichText(noticeLabel);
			DisableRichText(noticeBandLabel);
			DisableRichText(hoverName);
			DisableRichText(hoverRank);
			DisableRichText(hoverLocation);
			DisableRichText(hoverNote);
			DisableRichText(hoverOfficerNote);

			if (hoverCard != null)
			{
				// Never a pointer target: the card follows the pointer, and a card that could
				// take the pointer would steal the leave event that hides it.
				hoverCard.pickingMode = PickingMode.Ignore;
			}

			createButton = root.Q<Button>(CREATE_BUTTON_NAME);
			if (createButton != null)
			{
				createButton.clicked += OnButtonCreateGuild;
			}

			leaveButton = root.Q<Button>(LEAVE_BUTTON_NAME);
			if (leaveButton != null)
			{
				leaveButton.clicked += OnButtonLeaveGuild;
			}

			inviteButton = root.Q<Button>(INVITE_BUTTON_NAME);
			if (inviteButton != null)
			{
				inviteButton.clicked += OnButtonInviteToGuild;
			}

			if (rosterTabButton != null)
			{
				rosterTabButton.clicked += () => SetActiveTab(GuildTab.Roster);
			}

			if (infoTabButton != null)
			{
				infoTabButton.clicked += () => SetActiveTab(GuildTab.Info);
			}

			if (logTabButton != null)
			{
				logTabButton.clicked += () => SetActiveTab(GuildTab.Log);
			}

			if (editMotdButton != null)
			{
				editMotdButton.clicked += OnButtonEditMessageOfTheDay;
			}

			if (editNoticeButton != null)
			{
				editNoticeButton.clicked += OnButtonEditNotice;
			}

			if (disbandButton != null)
			{
				disbandButton.clicked += OnButtonDisbandGuild;
			}

			if (sortButton != null)
			{
				sortButton.clicked += OnButtonCycleSort;
			}

			if (onlineFilterButton != null)
			{
				onlineFilterButton.clicked += OnButtonCycleFilter;
			}

			if (searchField != null)
			{
				searchField.RegisterValueChangedCallback(OnSearchChanged);
			}
		}

		/// <summary>
		/// Re-applies the roster, guild text, tab and filters after the visual tree was rebuilt.
		/// </summary>
		/// <remarks>
		/// This is the hook that closes CRIT-5. The base implementation re-runs the character
		/// pre/post pair, which is what re-subscribes this panel to the guild controller; the
		/// rebuild below then re-renders whatever the model already holds, so a guild that was on
		/// screen before the panel was closed is on screen again when it reopens — without
		/// waiting for the next server pump.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			/* The search box is an element, so its text goes with the discarded tree. Pushing the
			 * remembered term back into it keeps the field and the list agreeing — a box that
			 * reopened empty over a still-filtered roster would look like a bug in the filter. */
			if (searchField != null && searchField.value != searchTerm)
			{
				searchField.SetValueWithoutNotify(searchTerm);
			}

			SetActiveTab(activeTab);
			ApplyGuildInfo();
			RefreshFilterButtons();
			RebuildRosterView();
			RebuildLogView();
		}

		/// <summary>
		/// Clears the guild member list when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ClearRoster();

			base.OnDestroying();
		}

		/// <summary>
		/// Unsubscribes from the outgoing character's guild controller.
		/// </summary>
		/// <remarks>
		/// <c>UITKCharacterControl.OnAfterStarting</c> calls Pre then Post on every tree rebuild
		/// specifically so the pair cancels out. Leaving this un-overridden — as this panel used
		/// to — made the Pre call a no-op, so every reopen stacked another subscription and each
		/// roster update ran the handlers one more time than the last.
		/// </remarks>
		public override void OnPreSetCharacter()
		{
			base.OnPreSetCharacter();

			UnsubscribeGuildEvents();
		}

		/// <summary>
		/// Subscribes to guild controller events after the character is set.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character != null && Character.TryGet(out IGuildController guildController))
			{
				guildController.OnReceiveGuildInvite += GuildController_OnReceiveGuildInvite;
				guildController.OnAddGuildMember += GuildController_OnAddGuildMember;
				guildController.OnValidateGuildMembers += GuildController_OnValidateGuildMembers;
				guildController.OnRemoveGuildMember += GuildController_OnRemoveMember;
				guildController.OnLeaveGuild += GuildController_OnLeaveGuild;
				guildController.OnReceiveGuildResult += GuildController_OnReceiveGuildResult;
				guildController.OnReceiveGuildInfo += GuildController_OnReceiveGuildInfo;
				guildController.OnReceiveGuildLog += GuildController_OnReceiveGuildLog;
				guildController.OnReceiveGuildRanks += GuildController_OnReceiveGuildRanks;
			}
		}

		/// <summary>
		/// Unsubscribes from guild controller events before the character is unset.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			UnsubscribeGuildEvents();
		}

		/// <summary>
		/// Drops the roster once the character is gone.
		/// </summary>
		/// <remarks>
		/// The model outlives the visual tree on purpose, which means nothing else would ever
		/// clear it. Without this, quitting to login and coming back on a different character
		/// showed the PREVIOUS character's guild until the next server pump overwrote it — and
		/// showed it permanently if the new character has no guild at all, because a guildless
		/// character generates no roster traffic to correct it.
		/// </remarks>
		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();

			ClearRoster();
		}

		/// <summary>
		/// Removes this panel's handlers from the current character's guild controller.
		/// </summary>
		private void UnsubscribeGuildEvents()
		{
			if (Character == null || !Character.TryGet(out IGuildController guildController))
			{
				return;
			}

			guildController.OnReceiveGuildInvite -= GuildController_OnReceiveGuildInvite;
			guildController.OnAddGuildMember -= GuildController_OnAddGuildMember;
			guildController.OnValidateGuildMembers -= GuildController_OnValidateGuildMembers;
			guildController.OnRemoveGuildMember -= GuildController_OnRemoveMember;
			guildController.OnLeaveGuild -= GuildController_OnLeaveGuild;
			guildController.OnReceiveGuildResult -= GuildController_OnReceiveGuildResult;
			guildController.OnReceiveGuildInfo -= GuildController_OnReceiveGuildInfo;
			guildController.OnReceiveGuildLog -= GuildController_OnReceiveGuildLog;
			guildController.OnReceiveGuildRanks -= GuildController_OnReceiveGuildRanks;
		}

		/// <summary>
		/// Prompts the local player to accept or decline a received guild invite.
		/// </summary>
		/// <param name="inviterCharacterID">The inviter's character ID.</param>
		/// <remarks>
		/// The inviter's ID is carried back on the accept broadcast. The server used to resolve
		/// "whatever invite is pending for this character" from an empty struct, so a dialog left
		/// open past the invitation TTL joined whichever guild invited the player NEXT. Sending
		/// the identity the dialog was actually raised for lets the server refuse the mismatch.
		/// </remarks>
		public void GuildController_OnReceiveGuildInvite(long inviterCharacterID)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, inviterCharacterID, (n) =>
			{
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiTooltip))
				{
					uiTooltip.Open("You have been invited to join " + n + "'s guild. Would you like to join?",
					() =>
					{
						Client.Broadcast(new GuildAcceptInviteBroadcast()
						{
							InviterCharacterID = inviterCharacterID,
						}, Channel.Reliable);
					},
					() =>
					{
						Client.Broadcast(new GuildDeclineInviteBroadcast()
						{
							InviterCharacterID = inviterCharacterID,
						}, Channel.Reliable);
					});
				}
			});
		}

		/// <summary>
		/// Adds a member row and refreshes the guild name label.
		/// </summary>
		/// <param name="msg">The roster row as the server sent it.</param>
		public void GuildController_OnAddGuildMember(GuildAddBroadcast msg)
		{
			GuildController_OnAddMember(msg);

			ClientNamingSystem.SetName(NamingSystemType.GuildName, msg.GuildID, (s) =>
			{
				// Held so a rebuilt tree can re-apply it without a second lookup.
				guildName = s;
				ApplyGuildName();
			});
		}

		/// <summary>
		/// Stores and displays the guild's descriptive text.
		/// </summary>
		/// <param name="guildID">The guild the text belongs to.</param>
		/// <param name="name">The guild's display name.</param>
		/// <param name="notice">The guild's notice text.</param>
		/// <param name="messageOfTheDay">The guild's message of the day.</param>
		/// <remarks>
		/// Held in fields, not read back out of the labels: the labels belong to the visual tree
		/// and this text arrives once on join and then only when somebody edits it, so a tree
		/// rebuild between edits would otherwise blank it until the next edit.
		/// </remarks>
		public void GuildController_OnReceiveGuildInfo(long guildID, string name, string notice, string messageOfTheDay)
		{
			if (!string.IsNullOrEmpty(name))
			{
				guildName = name;
			}
			guildNotice = notice ?? string.Empty;
			guildMessageOfTheDay = messageOfTheDay ?? string.Empty;

			ApplyGuildName();
			ApplyGuildInfo();
		}

		/// <summary>
		/// Removes member rows that are no longer in the validated member set.
		/// </summary>
		/// <param name="newMembers">The set of valid member IDs.</param>
		public void GuildController_OnValidateGuildMembers(HashSet<long> newMembers)
		{
			foreach (long id in new List<long>(roster.Keys))
			{
				if (!newMembers.Contains(id))
				{
					GuildController_OnRemoveMember(id);
				}
			}
		}

		/// <summary>
		/// Resets the guild label and clears all member rows when leaving the guild.
		/// </summary>
		public void GuildController_OnLeaveGuild()
		{
			/* The ladder belongs to the guild that was left. Keeping it would render the NEXT
			 * guild's rank orders against the previous guild's names — the orders overlap, so the
			 * result would be plausible and wrong rather than obviously empty. */
			rankLadder.Clear();
			guildName = string.Empty;
			guildNotice = string.Empty;
			guildMessageOfTheDay = string.Empty;
			logEntries = Array.Empty<GuildLogEntry>();
			ApplyGuildName();
			ApplyGuildInfo();
			RebuildLogView();
			ClearRoster();
		}

		/// <summary>
		/// Adds or updates a guild member.
		/// </summary>
		/// <param name="msg">The roster row as the server sent it.</param>
		public void GuildController_OnAddMember(GuildAddBroadcast msg)
		{
			long characterID = msg.CharacterID;

			if (!roster.TryGetValue(characterID, out MemberModel model))
			{
				model = new MemberModel
				{
					CharacterID = characterID,
				};
				roster.Add(characterID, model);
			}

			model.RankOrder = msg.RankOrder;
			model.Location = msg.Location ?? string.Empty;
			model.RaceID = msg.RaceID;
			model.Level = msg.Level;
			model.PublicNote = msg.PublicNote ?? string.Empty;
			/* Stored exactly as received. An empty officer note means the SERVER did not send one
			 * — either because there is none or because this client's rank may not read it — and
			 * the panel cannot and should not tell those apart. */
			model.OfficerNote = msg.OfficerNote ?? string.Empty;
			model.LastOnlineUtcTicks = msg.LastOnlineUtcTicks;

			/* The name lookup may complete on a later frame, and the tree may have been replaced
			 * by then. The callback writes into the MODEL and re-reads the view afterwards, so a
			 * late reply lands on whatever row is current rather than on a dead element. */
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, characterID, (n) =>
			{
				if (roster.TryGetValue(characterID, out MemberModel target))
				{
					target.Name = n;
					ApplyModelToRow(target);
					/* The name is a sort key and a search key, so a late arrival can change where
					 * the row belongs and whether it belongs at all. */
					InvalidateRosterView();
				}
			});

			InvalidateRosterView();
		}

		/// <summary>
		/// Removes a guild member.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		public void GuildController_OnRemoveMember(long characterID)
		{
			if (!roster.Remove(characterID))
			{
				return;
			}

			if (rows.TryGetValue(characterID, out MemberRow row))
			{
				row.Root?.RemoveFromHierarchy();
				rows.Remove(characterID);
			}

			/* The row goes immediately — a member who has left must not linger — but the ordering
			 * pass waits for the tick, because validation removes members in a burst too. */
			InvalidateRosterView();
			RefreshHeader();
		}

		/// <summary>
		/// Displays chat feedback for the result of a guild operation.
		/// </summary>
		/// <param name="result">The guild operation result.</param>
		public void GuildController_OnReceiveGuildResult(GuildResultType result)
		{
			if (!UIManager.TryGetTK("UIChat", out UITKChat chat))
			{
				return;
			}
			switch (result)
			{
				case GuildResultType.Success:
					break;
				case GuildResultType.InvalidGuildName:
					chat.InstantiateChatMessage(ChatChannel.System, "", "The requested guild name is invalid.");
					break;
				case GuildResultType.NameAlreadyExists:
					chat.InstantiateChatMessage(ChatChannel.System, "", "A guild with that name already exists.");
					break;
				case GuildResultType.AlreadyInGuild:
					chat.InstantiateChatMessage(ChatChannel.System, "", "You are already in a guild!");
					break;
				case GuildResultType.GuildNotFound:
					chat.InstantiateChatMessage(ChatChannel.System, "", "That guild no longer exists.");
					break;
				case GuildResultType.GuildFull:
					chat.InstantiateChatMessage(ChatChannel.System, "", "That guild is full.");
					break;
				case GuildResultType.InvitationExpired:
					chat.InstantiateChatMessage(ChatChannel.System, "", "That guild invitation is no longer valid.");
					break;
				case GuildResultType.TargetIsBlocked:
					chat.InstantiateChatMessage(ChatChannel.System, "", "That player is not accepting invitations from you.");
					break;
				case GuildResultType.InviteOnCooldown:
					chat.InstantiateChatMessage(ChatChannel.System, "", "You have invited that player too recently.");
					break;
				case GuildResultType.InsufficientRank:
					chat.InstantiateChatMessage(ChatChannel.System, "", "Your guild rank does not allow that.");
					break;
				case GuildResultType.Failed:
					chat.InstantiateChatMessage(ChatChannel.System, "", "That guild request could not be completed.");
					break;
				default:
					return;
			}
		}

		/// <summary>
		/// Prompts for a guild name and broadcasts a create-guild request.
		/// </summary>
		public void OnButtonCreateGuild()
		{
			if (Character != null &&
				Character.TryGet(out IGuildController guildController) &&
				guildController.ID < 1 && Client.NetworkManager.IsClientStarted)
			{
				if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox tooltip))
				{
					tooltip.Open("Please type the name of your new guild!", (s) =>
					{
						if (Authentication.IsAllowedGuildName(s))
						{
							Client.Broadcast(new GuildCreateBroadcast()
							{
								GuildName = s,
							}, Channel.Reliable);
						}
					}, null);
				}
			}
		}

		/// <summary>
		/// Confirms then broadcasts a leave-guild request.
		/// </summary>
		public void OnButtonLeaveGuild()
		{
			if (Character != null &&
				Character.TryGet(out IGuildController guildController) &&
				guildController.ID > 0 && Client.NetworkManager.IsClientStarted)
			{
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox tooltip))
				{
					tooltip.Open("Are you sure you want to leave your guild?", () =>
					{
						Client.Broadcast(new GuildLeaveBroadcast(), Channel.Reliable);
					}, () => { });
				}
			}
		}

		/// <summary>
		/// Invites the current target, or prompts for a name, to the guild.
		/// </summary>
		public void OnButtonInviteToGuild()
		{
			if (Character != null &&
				Character.TryGet(out IGuildController guildController) &&
				guildController.ID > 0 &&
				Client.NetworkManager.IsClientStarted)
			{
				if (Character.TryGet(out ITargetController targetController) &&
					targetController.Current.Target != null)
				{
					IPlayerCharacter targetCharacter = targetController.Current.Target.GetComponent<IPlayerCharacter>();
					if (targetCharacter != null)
					{
						Client.Broadcast(new GuildInviteBroadcast()
						{
							TargetCharacterID = targetCharacter.ID
						}, Channel.Reliable);

						return;
					}
				}

				if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox tooltip))
				{
					tooltip.Open("Please type the name of the person you wish to invite.", (s) =>
					{
						if (Authentication.IsAllowedCharacterName(s))
						{
							ClientNamingSystem.GetCharacterID(s, (id) =>
							{
								if (id != 0)
								{
									if (Character != null && Character.ID != id)
									{
										Client.Broadcast(new GuildInviteBroadcast()
										{
											TargetCharacterID = id,
										}, Channel.Reliable);
									}
									else if (UIManager.TryGetTK("UIChat", out UITKChat chat))
									{
										chat.InstantiateChatMessage(ChatChannel.System, "", "You can't invite yourself to the guild.");
									}
								}
								else if (UIManager.TryGetTK("UIChat", out UITKChat chat))
								{
									chat.InstantiateChatMessage(ChatChannel.System, "", "A person with that name could not be found.");
								}
							});
						}
					}, null);
				}
			}
		}

		/// <summary>
		/// Prompts for and submits a new guild message of the day.
		/// </summary>
		public void OnButtonEditMessageOfTheDay()
		{
			PromptGuildText(
				"Type the new guild message of the day.",
				guildMessageOfTheDay,
				GuildTextLimits.MaxMessageOfTheDayLength,
				GuildPermissions.EditMessageOfTheDay,
				(text) => Client.Broadcast(new GuildSetMessageOfTheDayBroadcast()
				{
					MessageOfTheDay = text,
				}, Channel.Reliable));
		}

		/// <summary>
		/// Prompts for and submits a new guild notice.
		/// </summary>
		public void OnButtonEditNotice()
		{
			PromptGuildText(
				"Type the new guild notice.",
				guildNotice,
				GuildTextLimits.MaxNoticeLength,
				GuildPermissions.EditNotice,
				(text) => Client.Broadcast(new GuildSetNoticeBroadcast()
				{
					Notice = text,
				}, Channel.Reliable));
		}

		/// <summary>
		/// Shared prompt-and-send for the two guild text fields.
		/// </summary>
		/// <param name="prompt">The prompt shown in the input dialog.</param>
		/// <param name="current">The current value (unused by the dialog, kept for clarity).</param>
		/// <param name="maxLength">Maximum accepted length.</param>
		/// <param name="send">Sends the trimmed text.</param>
		/// <remarks>
		/// Trimmed to <paramref name="maxLength"/> here as a courtesy so the player is not silently
		/// truncated at the database, but the server re-applies the same cap — a client's idea of
		/// a limit is never the limit.
		/// </remarks>
		private void PromptGuildText(string prompt, string current, int maxLength, GuildPermissions required, Action<string> send)
		{
			if (Character == null ||
				!Character.TryGet(out IGuildController guildController) ||
				guildController.ID < 1 ||
				!Client.NetworkManager.IsClientStarted)
			{
				return;
			}

			if (!guildController.HasGuildPermission(required))
			{
				if (UIManager.TryGetTK("UIChat", out UITKChat chat))
				{
					chat.InstantiateChatMessage(ChatChannel.System, "", "Your guild rank does not allow that.");
				}
				return;
			}

			if (!UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox input))
			{
				return;
			}

			input.Open(prompt, (s) =>
			{
				string text = (s ?? string.Empty).Trim();
				if (text.Length > maxLength)
				{
					text = text.Substring(0, maxLength);
				}
				send(text);
			}, null);
		}

		/// <summary>
		/// Confirms by name, then broadcasts a disband request.
		/// </summary>
		/// <remarks>
		/// Typing the guild's name is the confirmation. Disbanding cannot be undone and destroys
		/// something other people belong to, so a single Yes button is the wrong instrument — the
		/// name has to be produced deliberately. The server compares it against the guild the
		/// requester is actually in, so this is a real check rather than a client-side ritual.
		/// </remarks>
		public void OnButtonDisbandGuild()
		{
			if (Character == null ||
				!Character.TryGet(out IGuildController guildController) ||
				guildController.ID < 1 ||
				!Client.NetworkManager.IsClientStarted)
			{
				return;
			}

			if (!guildController.HasGuildPermission(GuildPermissions.Disband))
			{
				if (UIManager.TryGetTK("UIChat", out UITKChat chat))
				{
					chat.InstantiateChatMessage(ChatChannel.System, "", "Your guild rank does not allow that.");
				}
				return;
			}

			if (!UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox input))
			{
				return;
			}

			input.Open($"Disbanding cannot be undone. Type the guild name to confirm.", (s) =>
			{
				Client.Broadcast(new GuildDisbandBroadcast()
				{
					ConfirmationName = (s ?? string.Empty).Trim(),
				}, Channel.Reliable);
			}, null);
		}

		/// <summary>
		/// Cycles the roster ordering.
		/// </summary>
		private void OnButtonCycleSort()
		{
			sort = sort switch
			{
				RosterSort.Rank => RosterSort.Name,
				RosterSort.Name => RosterSort.Online,
				RosterSort.Online => RosterSort.Location,
				_ => RosterSort.Rank,
			};

			RefreshFilterButtons();
			RebuildRosterView();
		}

		/// <summary>
		/// Cycles the roster presence filter.
		/// </summary>
		private void OnButtonCycleFilter()
		{
			filter = filter switch
			{
				RosterFilter.All => RosterFilter.Online,
				RosterFilter.Online => RosterFilter.Offline,
				_ => RosterFilter.All,
			};

			RefreshFilterButtons();
			RebuildRosterView();
		}

		/// <summary>
		/// Applies a new search term.
		/// </summary>
		/// <param name="evt">The text field change event.</param>
		private void OnSearchChanged(ChangeEvent<string> evt)
		{
			searchTerm = (evt.newValue ?? string.Empty).Trim().ToLowerInvariant();
			RebuildRosterView();
		}

		/// <summary>
		/// Writes the current sort and filter onto their buttons.
		/// </summary>
		private void RefreshFilterButtons()
		{
			if (sortButton != null)
			{
				sortButton.text = sort.ToString();
			}

			if (onlineFilterButton != null)
			{
				onlineFilterButton.text = filter.ToString();
			}
		}

		/// <summary>
		/// Applies any pending view rebuild, at most once per frame.
		/// </summary>
		protected override void OnTick()
		{
			base.OnTick();

			if (rosterViewDirty)
			{
				rosterViewDirty = false;
				RebuildRosterView();
			}

			if (logViewDirty)
			{
				logViewDirty = false;
				RebuildLogView();
			}
		}

		/// <summary>
		/// Marks the roster view as needing a rebuild on the next tick.
		/// </summary>
		private void InvalidateRosterView()
		{
			rosterViewDirty = true;
		}

		/// <summary>
		/// Marks the log view as needing a rebuild on the next tick.
		/// </summary>
		private void InvalidateLogView()
		{
			logViewDirty = true;
		}

		/// <summary>
		/// Shows one of the panel's pages.
		/// </summary>
		/// <param name="tab">The page to show.</param>
		private void SetActiveTab(GuildTab tab)
		{
			activeTab = tab;

			if (rosterPage != null)
			{
				rosterPage.style.display = tab == GuildTab.Roster ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (infoPage != null)
			{
				infoPage.style.display = tab == GuildTab.Info ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (logPage != null)
			{
				logPage.style.display = tab == GuildTab.Log ? DisplayStyle.Flex : DisplayStyle.None;
			}

			rosterTabButton?.EnableInClassList(TAB_ACTIVE_CLASS, tab == GuildTab.Roster);
			infoTabButton?.EnableInClassList(TAB_ACTIVE_CLASS, tab == GuildTab.Info);
			logTabButton?.EnableInClassList(TAB_ACTIVE_CLASS, tab == GuildTab.Log);

			// The card belongs to a roster row; leaving it up over another page would strand it.
			HideHoverCard();

			/* Requested when the tab is opened rather than pushed as events happen. Almost nobody
			 * is looking at the log at any given moment, and pushing every entry to every member
			 * would put a message on the wire per event per member for a page nobody has open. */
			if (tab == GuildTab.Log)
			{
				RequestGuildLog();
			}
		}

		/// <summary>
		/// Asks the server for the guild's recent activity log.
		/// </summary>
		private void RequestGuildLog()
		{
			if (Character == null ||
				!Character.TryGet(out IGuildController guildController) ||
				guildController.ID < 1 ||
				!Client.NetworkManager.IsClientStarted)
			{
				return;
			}

			Client.Broadcast(new GuildLogRequestBroadcast(), Channel.Reliable);
		}

		/// <summary>
		/// Stores and renders the guild's activity log.
		/// </summary>
		/// <param name="guildID">The guild the log belongs to.</param>
		/// <param name="entries">The entries, newest first.</param>
		public void GuildController_OnReceiveGuildLog(long guildID, GuildLogEntry[] entries)
		{
			logEntries = entries ?? Array.Empty<GuildLogEntry>();

			/* Resolve every name the log mentions before rendering. These are frequently people
			 * who have already left, so the roster cannot supply them. Each reply re-renders,
			 * which is cheap at the log's fixed depth and avoids a second pass to fill blanks. */
			for (int i = 0; i < logEntries.Length; ++i)
			{
				CacheLogName(logEntries[i].ActorCharacterID);
				CacheLogName(logEntries[i].TargetCharacterID);
			}

			InvalidateLogView();
		}

		/// <summary>
		/// Requests a name for one log participant, if it is not already cached.
		/// </summary>
		/// <param name="characterID">The character to resolve, or zero.</param>
		private void CacheLogName(long characterID)
		{
			if (characterID < 1 || logNameCache.ContainsKey(characterID))
			{
				return;
			}

			// Placed before the request so a second entry naming the same character does not
			// issue a second lookup while the first is still in flight.
			logNameCache[characterID] = string.Empty;

			ClientNamingSystem.SetName(NamingSystemType.CharacterName, characterID, (n) =>
			{
				logNameCache[characterID] = n;
				InvalidateLogView();
			});
		}

		/// <summary>
		/// Returns a display name for a log participant.
		/// </summary>
		/// <param name="characterID">The character to name, or zero.</param>
		/// <returns>The resolved name, or "Someone" while unknown.</returns>
		private string LogName(long characterID)
		{
			if (characterID > 0 &&
				logNameCache.TryGetValue(characterID, out string cached) &&
				!string.IsNullOrEmpty(cached))
			{
				return cached;
			}

			return "Someone";
		}

		/// <summary>
		/// Rebuilds the log page from the stored entries.
		/// </summary>
		private void RebuildLogView()
		{
			if (logList == null)
			{
				return;
			}

			logList.Clear();

			for (int i = 0; i < logEntries.Length; ++i)
			{
				GuildLogEntry entry = logEntries[i];

				VisualElement row = new VisualElement();
				row.AddToClassList(LOG_ENTRY_CLASS);

				Label text = new Label(DescribeLogEntry(entry));
				text.AddToClassList(LOG_ENTRY_TEXT_CLASS);
				row.Add(text);

				Label time = new Label(DescribeLastSeen(entry.TimeUtcTicks));
				time.AddToClassList(LOG_ENTRY_TIME_CLASS);
				row.Add(time);

				logList.Add(row);
			}

			if (logEmptyLabel != null)
			{
				logEmptyLabel.style.display = logEntries.Length > 0 ? DisplayStyle.None : DisplayStyle.Flex;
			}
		}

		/// <summary>
		/// Renders one log entry as a sentence.
		/// </summary>
		/// <param name="entry">The entry to describe.</param>
		/// <returns>The sentence to display.</returns>
		/// <remarks>
		/// Composed here rather than stored as prose. A log written as sentences cannot be
		/// filtered, cannot be re-worded, and freezes today's phrasing into rows that outlive it.
		/// An unrecognised code still renders — a newer server sending an event this client does
		/// not know about should produce a vague line, not a hole in the history.
		/// </remarks>
		private string DescribeLogEntry(GuildLogEntry entry)
		{
			string actor = LogName(entry.ActorCharacterID);
			string target = LogName(entry.TargetCharacterID);

			switch (entry.Event)
			{
				case GuildLogEvent.Created:
					return $"{actor} founded the guild.";
				case GuildLogEvent.Joined:
					return $"{actor} joined the guild.";
				case GuildLogEvent.Left:
					return $"{actor} left the guild.";
				case GuildLogEvent.Kicked:
					return $"{actor} removed {target} from the guild.";
				case GuildLogEvent.Promoted:
					return string.IsNullOrEmpty(entry.Detail)
						? $"{actor} promoted {target}."
						: $"{actor} promoted {target} to {entry.Detail}.";
				case GuildLogEvent.Demoted:
					return string.IsNullOrEmpty(entry.Detail)
						? $"{actor} demoted {target}."
						: $"{actor} demoted {target} to {entry.Detail}.";
				case GuildLogEvent.LeadershipTransferred:
					return $"{actor} made {target} the guild leader.";
				case GuildLogEvent.MessageOfTheDayChanged:
					return $"{actor} changed the message of the day.";
				case GuildLogEvent.NoticeChanged:
					return $"{actor} changed the guild notice.";
				default:
					return $"{actor} did something the client does not recognise.";
			}
		}

		/// <summary>
		/// Applies the cached guild name to the header label.
		/// </summary>
		private void ApplyGuildName()
		{
			if (guildLabel != null)
			{
				guildLabel.text = string.IsNullOrEmpty(guildName) ? "GUILD" : guildName;
			}
		}

		/// <summary>
		/// Applies the cached notice and message of the day, and the rank-dependent buttons.
		/// </summary>
		/// <remarks>
		/// The buttons are hidden by rank as a courtesy — offering an action that will be refused
		/// is worse than not offering it. It is NOT the permission: the server checks the
		/// requester's rank from its own copy of the controller on every one of these messages.
		/// </remarks>
		private void ApplyGuildInfo()
		{
			bool hasNotice = !string.IsNullOrWhiteSpace(guildNotice);

			if (noticeBand != null)
			{
				noticeBand.style.display = hasNotice ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (noticeBandLabel != null)
			{
				noticeBandLabel.text = guildNotice;
			}

			if (noticeLabel != null)
			{
				noticeLabel.text = hasNotice ? guildNotice : "No notice set.";
			}

			if (motdLabel != null)
			{
				motdLabel.text = string.IsNullOrWhiteSpace(guildMessageOfTheDay)
					? "No message of the day set."
					: guildMessageOfTheDay;
			}

			/* Each control is shown against the permission that CONTROL needs, rather than against
			 * a rank tier. The two text edits used to share one "officer or better" test and were
			 * therefore inseparable; a guild can now hand out one without the other, and the panel
			 * has to be able to draw that. */
			GuildPermissions permissions = GuildPermissions.None;
			bool inGuild = false;
			if (Character != null && Character.TryGet(out IGuildController guildController) && guildController.ID > 0)
			{
				inGuild = true;
				permissions = guildController.Permissions;
			}

			DisplayStyle leaderDisplay = (permissions & GuildPermissions.Disband) == GuildPermissions.Disband
				? DisplayStyle.Flex
				: DisplayStyle.None;

			if (editMotdButton != null)
			{
				editMotdButton.style.display = (permissions & GuildPermissions.EditMessageOfTheDay) == GuildPermissions.EditMessageOfTheDay
					? DisplayStyle.Flex
					: DisplayStyle.None;
			}

			if (editNoticeButton != null)
			{
				editNoticeButton.style.display = (permissions & GuildPermissions.EditNotice) == GuildPermissions.EditNotice
					? DisplayStyle.Flex
					: DisplayStyle.None;
			}

			if (disbandButton != null)
			{
				disbandButton.style.display = leaderDisplay;
			}

			/* The footer used to show Create, Invite and Leave at all times, so a player with no
			 * guild was offered two actions that cannot do anything — there is nothing to invite
			 * anyone to and nothing to leave. Membership decides which of the three can apply,
			 * and Invite additionally answers to the same permission the server enforces.
			 *
			 * Membership is the flag resolved with the permissions above rather than a second
			 * lookup: it is not the same question as "holds any permission", since a member can
			 * hold none, but it is answered by the same controller read. */
			if (createButton != null)
			{
				createButton.style.display = inGuild ? DisplayStyle.None : DisplayStyle.Flex;
			}

			if (leaveButton != null)
			{
				leaveButton.style.display = inGuild ? DisplayStyle.Flex : DisplayStyle.None;
			}

			if (inviteButton != null)
			{
				inviteButton.style.display = inGuild && (permissions & GuildPermissions.Invite) == GuildPermissions.Invite
					? DisplayStyle.Flex
					: DisplayStyle.None;
			}
		}

		/// <summary>
		/// Rebuilds every visible row from the model into the current visual tree.
		/// </summary>
		/// <remarks>
		/// A full rebuild rather than an incremental reorder. At the configured cap of a hundred
		/// members this is a hundred elements on a roster that changes on a one-second pump and on
		/// keystrokes in a search box; the bookkeeping an incremental path would need to keep the
		/// filtered order correct is a far better source of bugs than it is of frames.
		/// </remarks>
		private void RebuildRosterView()
		{
			HideHoverCard();

			rows.Clear();

			if (memberList == null)
			{
				RefreshHeader();
				return;
			}

			memberList.Clear();

			orderBuffer.Clear();
			foreach (MemberModel model in roster.Values)
			{
				if (PassesFilters(model))
				{
					orderBuffer.Add(model);
				}
			}

			orderBuffer.Sort(CompareMembers);

			for (int i = 0; i < orderBuffer.Count; ++i)
			{
				MemberModel model = orderBuffer[i];
				GetOrCreateRow(model);
				ApplyModelToRow(model);
			}

			RefreshHeader();
		}

		/// <summary>
		/// Tests one member against the search term and the presence filter.
		/// </summary>
		/// <param name="model">The member to test.</param>
		/// <returns>True when the member should be shown.</returns>
		private bool PassesFilters(MemberModel model)
		{
			switch (filter)
			{
				case RosterFilter.Online when !model.IsOnline:
					return false;
				case RosterFilter.Offline when model.IsOnline:
					return false;
			}

			if (searchTerm.Length < 1)
			{
				return true;
			}

			/* Name and location both match. A player searching a roster is as often looking for
			 * "who is in Sunmoor" as for one person, and a search that only matched names would
			 * make the location column decorative. */
			return model.Name.ToLowerInvariant().Contains(searchTerm) ||
				   model.Location.ToLowerInvariant().Contains(searchTerm);
		}

		/// <summary>
		/// Orders two members according to the current sort.
		/// </summary>
		/// <param name="a">First member.</param>
		/// <param name="b">Second member.</param>
		/// <returns>Standard comparison result.</returns>
		/// <remarks>
		/// Every ordering falls back to name. Without a total order, members that tie on the
		/// primary key would shuffle between rebuilds — and this list rebuilds every server pump.
		/// </remarks>
		private int CompareMembers(MemberModel a, MemberModel b)
		{
			int result;

			switch (sort)
			{
				case RosterSort.Name:
					break;

				case RosterSort.Online:
					result = b.IsOnline.CompareTo(a.IsOnline);
					if (result != 0)
					{
						return result;
					}
					break;

				case RosterSort.Location:
					result = string.Compare(a.Location, b.Location, StringComparison.OrdinalIgnoreCase);
					if (result != 0)
					{
						return result;
					}
					break;

				default:
					// Highest rank first; a roster read top-down should start at the leader.
					result = b.RankOrder.CompareTo(a.RankOrder);
					if (result != 0)
					{
						return result;
					}
					break;
			}

			return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Drops both the model and the view.
		/// </summary>
		private void ClearRoster()
		{
			HideHoverCard();

			foreach (MemberRow row in rows.Values)
			{
				row.Root?.RemoveFromHierarchy();
			}
			rows.Clear();
			roster.Clear();

			/* Cleared with the roster so a character switch does not carry one guild's history
			 * and the names in it into the next. */
			logEntries = Array.Empty<GuildLogEntry>();
			logNameCache.Clear();
			RebuildLogView();

			RefreshHeader();
		}

		/// <summary>
		/// Updates the header count, state line and empty placeholder from the roster.
		/// </summary>
		private void RefreshHeader()
		{
			int count = roster.Count;
			int shown = rows.Count;
			int online = 0;
			foreach (MemberModel model in roster.Values)
			{
				if (model.IsOnline)
				{
					++online;
				}
			}

			bool inGuild = count > 0;

			if (countLabel != null)
			{
				countLabel.text = count.ToString();
				countLabel.EnableInClassList("fish-badge--accent", inGuild);
			}

			if (subtitleLabel != null)
			{
				if (!inGuild)
				{
					subtitleLabel.text = "Not in a guild";
				}
				else if (shown < count)
				{
					// Say so when a filter is hiding members, or the count reads as a bug.
					subtitleLabel.text = $"{shown} of {count} shown · {online} online";
				}
				else
				{
					subtitleLabel.text = $"{count} members · {online} online";
				}
			}

			if (emptyLabel != null)
			{
				emptyLabel.style.display = shown > 0 ? DisplayStyle.None : DisplayStyle.Flex;
				emptyLabel.text = inGuild
					? "No members match the current filter."
					: "You are not in a guild.";
			}

			if (columns != null)
			{
				// Column captions over an empty list name fields that are not there.
				columns.style.display = inGuild ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Returns the existing row for a member, or builds one into the current tree.
		/// </summary>
		/// <param name="model">The member the row renders.</param>
		/// <returns>The member row, or null when there is no tree to build into.</returns>
		private MemberRow GetOrCreateRow(MemberModel model)
		{
			if (rows.TryGetValue(model.CharacterID, out MemberRow existing))
			{
				return existing;
			}

			if (memberList == null)
			{
				/* No tree yet — the panel has never been shown. The model still holds the member,
				 * and OnAfterStarting builds the row as soon as there is somewhere to put it. */
				return null;
			}

			VisualElement rowRoot = new VisualElement();
			/* The theme class supplies the hover state and leading accent rail shared by every
			 * roster; the panel class only carries geometry. */
			rowRoot.AddToClassList("fish-row");
			rowRoot.AddToClassList(ROW_CLASS);

			VisualElement dot = new VisualElement();
			dot.AddToClassList("fish-dot");
			dot.AddToClassList(ROW_DOT_CLASS);
			rowRoot.Add(dot);

			Label name = new Label();
			name.AddToClassList("fish-row__name");
			name.AddToClassList(ROW_NAME_CLASS);
			// Character names and rank names are both player-authored. See OnAfterStarting.
			name.enableRichText = false;
			rowRoot.Add(name);

			Label level = new Label();
			level.AddToClassList("fish-row__meta");
			level.AddToClassList(ROW_LEVEL_CLASS);
			rowRoot.Add(level);

			Label rank = new Label();
			rank.AddToClassList("fish-row__meta");
			rank.AddToClassList(ROW_RANK_CLASS);
			rank.enableRichText = false;
			rowRoot.Add(rank);

			Label memberClass = new Label();
			memberClass.AddToClassList("fish-row__meta");
			memberClass.AddToClassList(ROW_CLASS_CLASS);
			rowRoot.Add(memberClass);

			Label location = new Label();
			location.AddToClassList("fish-row__meta");
			location.AddToClassList(ROW_LOCATION_CLASS);
			location.enableRichText = false;
			rowRoot.Add(location);

			MemberRow row = new MemberRow
			{
				Root = rowRoot,
				Dot = dot,
				Name = name,
				Level = level,
				Rank = rank,
				Class = memberClass,
				Location = location,
			};

			/* Captured by ID, never by element. A row's elements are replaced on every rebuild;
			 * the ID is what the action actually needs and the only thing that stays true. */
			long characterID = model.CharacterID;
			rowRoot.RegisterCallback<PointerDownEvent>(evt => OnMemberPointerDown(evt, characterID));
			rowRoot.RegisterCallback<PointerEnterEvent>(evt => ShowHoverCard(characterID, rowRoot));
			rowRoot.RegisterCallback<PointerLeaveEvent>(evt => HideHoverCard());

			memberList.Add(rowRoot);
			rows.Add(characterID, row);
			return row;
		}

		/// <summary>
		/// Writes one member's model values into its row, if the row currently exists.
		/// </summary>
		/// <param name="model">The member to render.</param>
		private void ApplyModelToRow(MemberModel model)
		{
			if (!rows.TryGetValue(model.CharacterID, out MemberRow row))
			{
				return;
			}

			bool online = model.IsOnline;

			row.Name.text = model.Name;
			row.Level.text = model.Level > 0 ? model.Level.ToString() : "—";
			row.Rank.text = ResolveRankName(model.RankOrder);
			row.Class.text = ResolveRaceName(model.RaceID);
			row.Location.text = online ? model.Location : DescribeLastSeen(model.LastOnlineUtcTicks);

			if (row.Dot != null)
			{
				row.Dot.EnableInClassList("fish-dot--online", online);
				row.Dot.EnableInClassList("fish-dot--offline", !online);
			}

			// Offline members recede so the ones a player can actually reach read first.
			row.Root.EnableInClassList("fish-row--dim", !online);
		}

		/// <summary>
		/// Resolves a race identifier to a display name.
		/// </summary>
		/// <param name="raceID">The race identifier from the roster payload.</param>
		/// <returns>The race name, or an em dash when it cannot be resolved.</returns>
		/// <remarks>
		/// An em dash rather than an empty cell: a blank column reads as a rendering failure,
		/// while a dash reads as "not known", which is what it means for a member whose race
		/// template is not loaded on this client.
		/// </remarks>
		private string ResolveRaceName(int raceID)
		{
			if (raceID == 0)
			{
				return "—";
			}

			RaceTemplate template = RaceTemplate.Get<RaceTemplate>(raceID);
			return template != null ? template.Name : "—";
		}

		/// <summary>
		/// Renders a last-seen tick count as a short relative string.
		/// </summary>
		/// <param name="ticks">UTC ticks of the member's last character save.</param>
		/// <returns>A short relative description, or "Offline" when the time is unknown.</returns>
		/// <remarks>
		/// Coarse on purpose. "3d" answers the question a guild leader is asking — has this
		/// person stopped playing — and a precise timestamp would take more of the column than the
		/// extra precision is worth.
		/// </remarks>
		private string DescribeLastSeen(long ticks)
		{
			if (ticks <= 0)
			{
				return OFFLINE_LOCATION;
			}

			TimeSpan elapsed = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);

			if (elapsed.TotalMinutes < 1.0)
			{
				return "Just now";
			}
			if (elapsed.TotalHours < 1.0)
			{
				return $"{(int)elapsed.TotalMinutes}m ago";
			}
			if (elapsed.TotalDays < 1.0)
			{
				return $"{(int)elapsed.TotalHours}h ago";
			}
			if (elapsed.TotalDays < 365.0)
			{
				return $"{(int)elapsed.TotalDays}d ago";
			}

			return "Long ago";
		}

		/// <summary>
		/// Fills and positions the shared hover card beside a row.
		/// </summary>
		/// <param name="characterID">The member being hovered.</param>
		/// <param name="rowRoot">The row element the card should sit against.</param>
		/// <remarks>
		/// One card is reused for every row. The card is positioned from the row's resolved layout
		/// rather than from the pointer, so it does not jitter as the pointer moves along a row,
		/// and it is clamped to the panel so a row near the bottom does not push it off-screen.
		/// Layout can be NaN on the frame an element is first laid out; the guard below is what
		/// keeps that from placing the card at zero.
		/// </remarks>
		private void ShowHoverCard(long characterID, VisualElement rowRoot)
		{
			if (hoverCard == null ||
				Root == null ||
				rowRoot == null ||
				!roster.TryGetValue(characterID, out MemberModel model))
			{
				return;
			}

			if (hoverName != null)
			{
				hoverName.text = string.IsNullOrEmpty(model.Name) ? "Unknown" : model.Name;
			}
			if (hoverRank != null)
			{
				hoverRank.text = $"Rank: {ResolveRankName(model.RankOrder)}";
			}
			if (hoverClass != null)
			{
				hoverClass.text = $"Class: {ResolveRaceName(model.RaceID)}";
			}
			if (hoverLocation != null)
			{
				hoverLocation.text = model.IsOnline ? $"Zone: {model.Location}" : "Zone: —";
			}
			if (hoverSeen != null)
			{
				hoverSeen.text = model.IsOnline ? "Online now" : $"Last seen: {DescribeLastSeen(model.LastOnlineUtcTicks)}";
			}
			/* Both note lines collapse when empty rather than rendering a bare caption. For the
			 * officer note, empty also covers "the server did not send it to this client", which
			 * is indistinguishable from "there isn't one" — and deliberately so. */
			if (hoverNote != null)
			{
				bool hasNote = !string.IsNullOrWhiteSpace(model.PublicNote);
				hoverNote.text = hasNote ? $"Note: {model.PublicNote}" : string.Empty;
				hoverNote.style.display = hasNote ? DisplayStyle.Flex : DisplayStyle.None;
			}
			if (hoverOfficerNote != null)
			{
				bool hasOfficerNote = !string.IsNullOrWhiteSpace(model.OfficerNote);
				hoverOfficerNote.text = hasOfficerNote ? $"Officer: {model.OfficerNote}" : string.Empty;
				hoverOfficerNote.style.display = hasOfficerNote ? DisplayStyle.Flex : DisplayStyle.None;
			}

			float rowTop = rowRoot.worldBound.yMin - Root.worldBound.yMin;
			float rowRight = rowRoot.worldBound.xMax - Root.worldBound.xMin;

			if (float.IsNaN(rowTop) || float.IsNaN(rowRight))
			{
				// Not laid out yet; showing it now would pin it to the panel's corner.
				return;
			}

			float panelHeight = Root.resolvedStyle.height;
			float cardHeight = hoverCard.resolvedStyle.height;
			if (!float.IsNaN(panelHeight) && !float.IsNaN(cardHeight) && cardHeight > 0.0f)
			{
				rowTop = Math.Min(rowTop, Math.Max(0.0f, panelHeight - cardHeight));
			}

			hoverCard.style.left = rowRight - 200.0f;
			hoverCard.style.top = rowTop;
			hoverCard.style.display = DisplayStyle.Flex;
		}

		/// <summary>
		/// Hides the shared hover card.
		/// </summary>
		private void HideHoverCard()
		{
			if (hoverCard != null)
			{
				hoverCard.style.display = DisplayStyle.None;
			}
		}

		/// <summary>
		/// Opens the member context menu on right-click.
		/// </summary>
		/// <param name="evt">The pointer-down event.</param>
		/// <param name="characterID">The member's character ID.</param>
		/// <remarks>
		/// Right-click rather than left, and the shared context menu rather than the dropdown.
		/// The dropdown path was unreachable by construction: it called <c>Hide()</c> and then
		/// added its entries, but <c>Hide()</c> disables the document, so every entry was added
		/// to a tree that the following <c>Show()</c> immediately discarded.
		/// <c>UITKContextMenu.Open</c> shows first and builds afterwards, which is the order the
		/// re-cloning document actually requires.
		/// </remarks>
		private void OnMemberPointerDown(PointerDownEvent evt, long characterID)
		{
			// 1 is the right button. Left-click is left free for selection.
			if (evt.button != 1)
			{
				return;
			}

			evt.StopPropagation();

			HideHoverCard();
			OpenMemberContextMenu(characterID);
		}

		/// <summary>
		/// Builds and opens the context menu for one guild member.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <remarks>
		/// Every entry closes over <paramref name="characterID"/>. The previous version read the
		/// target back out of the row's name LABEL and pushed it through an asynchronous name
		/// lookup to recover an ID it already had — so a roster that changed between the click
		/// and the reply, or a name the lookup resolved to a different character, kicked or
		/// demoted somebody else entirely.
		/// </remarks>
		private void OpenMemberContextMenu(long characterID)
		{
			if (Character == null ||
				!UIManager.TryGetTK("UIContextMenu", out UITKContextMenu contextMenu) ||
				!roster.TryGetValue(characterID, out MemberModel model))
			{
				return;
			}

			List<(string label, Action callback)> entries = new List<(string, Action)>();

			if (Character.ID != characterID)
			{
				string displayName = model.Name;

				if (!string.IsNullOrEmpty(displayName))
				{
					entries.Add(("Message", () =>
					{
						if (UIManager.TryGetTK("UIChat", out UITKChat uiChat))
						{
							uiChat.SetInputText($"/tell {displayName} ");
						}
					}
					));
				}

				entries.Add(("Add Friend", () =>
				{
					Client.Broadcast(new FriendAddNewBroadcast()
					{
						CharacterID = characterID,
					}, Channel.Reliable);
				}
				));

				AddRankEntries(entries, model);
			}

			contextMenu.Open(entries);
		}

		/// <summary>
		/// Appends the promote/demote/transfer/kick entries the local player's rank permits.
		/// </summary>
		/// <param name="entries">The entry list being built.</param>
		/// <param name="model">The target member.</param>
		/// <remarks>
		/// These checks decide what to DRAW, nothing more. The server re-derives the same rules
		/// from its own copy of both ranks before it acts, so a client that offers an entry it
		/// should not have is refused rather than obeyed.
		/// </remarks>
		private void AddRankEntries(List<(string label, Action callback)> entries, MemberModel model)
		{
			if (!Character.TryGet(out IGuildController guildController) ||
				guildController.ID < 1 ||
				model.RankOrder >= guildController.RankOrder)
			{
				return;
			}

			long characterID = model.CharacterID;
			string displayName = model.Name;

			/* The adjacent rungs on the guild's OWN ladder, not arithmetic on an enum. The old
			 * code did `model.Rank + 1`, which was correct only because the three enum values
			 * happened to be contiguous; a guild whose ranks are 1, 2, 5 and 9 has no member at
			 * "3", and promoting into a rank with no row is refused by the server. */
			byte nextRank = FindAdjacentRank(model.RankOrder, above: true);
			byte prevRank = FindAdjacentRank(model.RankOrder, above: false);

			bool mayPromote = guildController.HasGuildPermission(GuildPermissions.Promote);

			/* Promote is offered only up to a rung STRICTLY below the viewer's own. The server
			 * refuses a promotion to or above the requester's rank outright — an officer able to
			 * promote could otherwise manufacture somebody senior to themselves — so offering it
			 * would draw a button whose only outcome is a silent refusal. */
			if (mayPromote &&
				nextRank > 0 &&
				nextRank < guildController.RankOrder &&
				nextRank < guildController.LeaderRankOrder)
			{
				string nextName = ResolveRankName(nextRank);
				entries.Add(($"Promote to {nextName}", () =>
				{
					Client.Broadcast(new GuildChangeRankBroadcast()
					{
						CharacterID = characterID,
						RankOrder = nextRank,
					}, Channel.Reliable);
				}
				));
			}

			if (mayPromote && prevRank > 0)
			{
				string prevName = ResolveRankName(prevRank);
				entries.Add(($"Demote to {prevName}", () =>
				{
					Client.Broadcast(new GuildChangeRankBroadcast()
					{
						CharacterID = characterID,
						RankOrder = prevRank,
					}, Channel.Reliable);
				}
				));
			}

			/* Offered for any member, online or not. Leadership is a database rank rather than a
			 * session, and a guild whose leader can only hand over while the successor happens to
			 * be logged in is a guild that stays stuck. */
			if (guildController.HasGuildPermission(GuildPermissions.TransferLeadership) &&
				guildController.LeaderRankOrder > 0 &&
				guildController.RankOrder >= guildController.LeaderRankOrder)
			{
				entries.Add(("Transfer Leadership", () =>
				{
					if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox dialog))
					{
						dialog.Open($"Make {displayName} the guild leader? You will step down a rank.", () =>
						{
							Client.Broadcast(new GuildTransferLeadershipBroadcast()
							{
								CharacterID = characterID,
							}, Channel.Reliable);
						}, () => { });
					}
				}
				));
			}

			if (guildController.HasGuildPermission(GuildPermissions.EditPublicNotes))
			{
				entries.Add(("Set Public Note", () => PromptMemberNote(characterID, displayName, model.PublicNote, isOfficerNote: false)));
			}

			if (guildController.HasGuildPermission(GuildPermissions.EditOfficerNotes))
			{
				entries.Add(("Set Officer Note", () => PromptMemberNote(characterID, displayName, model.OfficerNote, isOfficerNote: true)));
			}

			if (guildController.HasGuildPermission(GuildPermissions.Kick))
			{
				entries.Add(("Kick", () =>
				{
					if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox dialog))
					{
						dialog.Open($"Remove {displayName} from the guild?", () =>
						{
							Client.Broadcast(new GuildRemoveBroadcast()
							{
								CharacterID = characterID,
							}, Channel.Reliable);
						}, () => { });
					}
				}
				));
			}
		}

		/// <summary>
		/// Turns off rich-text parsing on a label that renders player-authored text.
		/// </summary>
		/// <param name="label">The label, which may be null when the tree has no such element.</param>
		private static void DisableRichText(Label label)
		{
			if (label != null)
			{
				label.enableRichText = false;
			}
		}

		/// <summary>
		/// The nearest rank order above or below a given position on the guild's ladder.
		/// </summary>
		/// <param name="rankOrder">The position to step from.</param>
		/// <param name="above">True to step up, false to step down.</param>
		/// <returns>The adjacent position, or zero when there is none in that direction.</returns>
		/// <remarks>
		/// Zero is "no such rank", which is also what an empty ladder returns — the roster and the
		/// ladder arrive in separate messages and either may be first, and offering a promotion
		/// computed from a ladder that has not arrived would send a rank order the guild does not
		/// have.
		/// </remarks>
		private byte FindAdjacentRank(byte rankOrder, bool above)
		{
			byte best = 0;

			foreach (KeyValuePair<byte, GuildRankEntry> pair in rankLadder)
			{
				byte candidate = pair.Key;

				if (above)
				{
					if (candidate > rankOrder && (best == 0 || candidate < best))
					{
						best = candidate;
					}
				}
				else
				{
					if (candidate < rankOrder && candidate > best)
					{
						best = candidate;
					}
				}
			}

			return best;
		}

		/// <summary>
		/// The display name of a rank order, as this guild named it.
		/// </summary>
		/// <param name="rankOrder">The ladder position.</param>
		/// <returns>The rank's name, or a placeholder.</returns>
		/// <remarks>
		/// Falls back to the bare number rather than to a default name like "Member". A guild that
		/// renamed rank 1 to "Recruit" would otherwise briefly show "Member" for it, which is not
		/// a rank that guild has — a number is obviously provisional in a way a plausible wrong
		/// name is not.
		/// </remarks>
		private string ResolveRankName(byte rankOrder)
		{
			if (rankOrder < 1)
			{
				return "—";
			}

			if (rankLadder.TryGetValue(rankOrder, out GuildRankEntry entry) &&
				!string.IsNullOrEmpty(entry.Name))
			{
				return entry.Name;
			}

			return rankOrder.ToString();
		}

		/// <summary>
		/// Stores the guild's rank ladder and re-renders everything that depends on it.
		/// </summary>
		/// <param name="msg">The ladder, and the viewer's own standing in it.</param>
		/// <remarks>
		/// The viewer's permissions are taken from the message rather than derived by looking the
		/// viewer's rank up in the ladder that arrived with it. The server computed them; deriving
		/// them again here would be a second implementation of the permission rules, and the one
		/// that drew the buttons would be the one that was wrong.
		/// </remarks>
		public void GuildController_OnReceiveGuildRanks(GuildRankListBroadcast msg)
		{
			rankLadder.Clear();

			if (msg.Ranks != null)
			{
				for (int i = 0; i < msg.Ranks.Length; ++i)
				{
					rankLadder[msg.Ranks[i].RankOrder] = msg.Ranks[i];
				}
			}

			/* The viewer's standing was already written onto the controller by
			 * GuildController.OnClientGuildRankListBroadcastReceived, which handles this same
			 * message. Re-deriving or re-storing it here would be a second copy of a value the
			 * panel can simply ask for. */

			/* Every roster row renders a rank NAME resolved from this ladder, so a ladder that
			 * arrives after the roster — which is the normal ordering on join — has to repaint
			 * the rows that were drawn without it. */
			RebuildRosterView();
			ApplyGuildInfo();
		}

		/// <summary>
		/// Prompts for one of a member's two notes and sends the edit.
		/// </summary>
		/// <param name="characterID">The member the note is about.</param>
		/// <param name="displayName">The member's name, for the prompt.</param>
		/// <param name="current">The note's current value.</param>
		/// <param name="isOfficerNote">True for the officer-only note.</param>
		/// <remarks>
		/// The length cap is applied here so the player sees the limit rather than a silent
		/// truncation, and again on the server, which is where it counts.
		/// </remarks>
		private void PromptMemberNote(long characterID, string displayName, string current, bool isOfficerNote)
		{
			if (Character == null ||
				!Character.TryGet(out IGuildController guildController) ||
				guildController.ID < 1 ||
				!Client.NetworkManager.IsClientStarted)
			{
				return;
			}

			if (!guildController.HasGuildPermission(isOfficerNote ? GuildPermissions.EditOfficerNotes : GuildPermissions.EditPublicNotes))
			{
				return;
			}

			if (!UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox input))
			{
				return;
			}

			string label = isOfficerNote ? "Officer note" : "Public note";
			string who = string.IsNullOrEmpty(displayName) ? "this member" : displayName;

			input.Open($"{label} for {who}:", (text) =>
			{
				string note = (text ?? string.Empty).Trim();
				if (note.Length > GuildTextLimits.MaxMemberNoteLength)
				{
					note = note.Substring(0, GuildTextLimits.MaxMemberNoteLength);
				}

				Client.Broadcast(new GuildSetMemberNoteBroadcast()
				{
					CharacterID = characterID,
					Note = note,
					IsOfficerNote = isOfficerNote,
				}, Channel.Reliable);
			}, null);
		}
	}
}