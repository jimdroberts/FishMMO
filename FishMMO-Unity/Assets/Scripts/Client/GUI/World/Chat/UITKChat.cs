using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the in-world chat window: channel-coloured messages,
	/// tab-based channel filtering, rate limiting, sanitisation and the full set of per-channel
	/// message handlers.
	/// </summary>
	/// <remarks>
	/// The panel keeps a <em>model</em> (<see cref="ChatMessageRecord"/> and the tab definitions)
	/// separate from the elements that render it. That separation is not decoration: a
	/// <c>UIDocument</c> re-clones its UXML every time it is enabled, so every element this class
	/// has ever created can be replaced out from under it, and anything held only as a
	/// <see cref="VisualElement"/> is lost with it. Rebuilding the view from the model in
	/// <see cref="OnStarting"/> is what keeps a hide/show from emptying the window — and what
	/// stops it from stacking a second copy of the welcome block and a second default tab on top
	/// of the first every time the tree comes back.
	/// </remarks>
	public class UITKChat : UITKCharacterControl, IChatHelper
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Window;

		/// <summary>
		/// The maximum allowed length for chat messages.
		/// </summary>
		/// <remarks>
		/// Advisory only. The server enforces its own cap (clamped to
		/// <see cref="ChatBroadcast.MaxTextLength"/>) because a client is free not to apply this.
		/// </remarks>
		public const int MAX_LENGTH = 128;

		/// <summary>
		/// The maximum number of chat messages retained before the oldest are discarded.
		/// </summary>
		/// <remarks>
		/// Applies to the message <em>records</em>, and the rows are removed from the tree along
		/// with them. A cap that only hides old rows is not a cap: a long session accumulates
		/// elements forever, each one still laid out and still measured.
		/// </remarks>
		private const int MAX_MESSAGES = 128;

		/// <summary>
		/// The maximum number of chat tabs that may be created.
		/// </summary>
		private const int MAX_TABS = 12;

		/// <summary>
		/// The maximum number of submitted lines kept for up-arrow recall.
		/// </summary>
		private const int MAX_INPUT_HISTORY = 32;

		/// <summary>
		/// Placeholder rendered in the sender column while a name is still being resolved, and
		/// left in place permanently if it never is.
		/// </summary>
		private const string UNKNOWN_SENDER = "???";

		/// <summary>Name of the chat root element.</summary>
		private const string CHAT_ROOT_NAME = "chat-root";
		/// <summary>Name of the chat tabs container.</summary>
		private const string CHAT_TABS_NAME = "chat-tabs";
		/// <summary>Name of the add-tab button.</summary>
		private const string CHAT_ADD_TAB_NAME = "chat-add-tab";
		/// <summary>Name of the scroll view wrapping the message list.</summary>
		private const string CHAT_SCROLL_NAME = "chat-scroll";
		/// <summary>Name of the chat messages container.</summary>
		private const string CHAT_MESSAGES_NAME = "chat-messages";
		/// <summary>Name of the chat input field.</summary>
		private const string CHAT_INPUT_NAME = "chat-input";
		/// <summary>Name of the channel selector button beside the input.</summary>
		private const string CHAT_CHANNEL_SELECTOR_NAME = "chat-channel-selector";

		/// <summary>Theme class giving a chat tab its shared tab appearance.</summary>
		/// <remarks>
		/// Chat tabs are built in code rather than declared in the UXML, so they have to opt into
		/// the theme explicitly. Without this they carried only <c>.chat-tab</c>, which is layout
		/// only — the tabs rendered with Unity's default button chrome while every other tab bar
		/// in the game used the themed style.
		/// </remarks>
		private const string TAB_THEME_CLASS = "fish-tab";
		/// <summary>USS class applied to each chat tab button for layout.</summary>
		private const string TAB_CLASS = "chat-tab";
		/// <summary>Theme class marking the active chat tab.</summary>
		private const string TAB_ACTIVE_CLASS = "fish-tab--active";
		/// <summary>USS class applied to each chat message row.</summary>
		private const string MESSAGE_ROW_CLASS = "chat-message";
		/// <summary>USS class applied to a message row's sender name label.</summary>
		private const string MESSAGE_NAME_CLASS = "chat-message__name";
		/// <summary>USS class applied to a message row's text label.</summary>
		private const string MESSAGE_TEXT_CLASS = "chat-message__text";

		/// <summary>
		/// The welcome message displayed when the chat is initialised.
		/// </summary>
		public string WelcomeMessage = "Welcome to " + Constants.Configuration.ProjectName + "!\r\nChat channels are available.";

		/// <summary>
		/// Error code messages mapped to their respective error keys.
		/// </summary>
		public Dictionary<string, string> ErrorCodes = new Dictionary<string, string>()
		{
			{ ChatHelper.GUILD_ERROR_TARGET_IN_GUILD, " is already in a guild." },
			{ ChatHelper.PARTY_ERROR_TARGET_IN_PARTY, " is already in a party." },
			{ ChatHelper.TARGET_OFFLINE, " is not online." },
			{ ChatHelper.TELL_ERROR_MESSAGE_SELF, "... Are you messaging yourself again?" },
		};

		/// <summary>
		/// Colour mapping for each chat channel.
		///
		/// These are drawn on the dark chat panel, so a channel colour has to carry enough
		/// lightness to be read there. Trade was Color.black and Region was Color.blue; both
		/// are near-invisible against that background rather than merely dim.
		/// </summary>
		public Dictionary<ChatChannel, Color> ChannelColors = new Dictionary<ChatChannel, Color>()
		{
			{ ChatChannel.Say,      Color.white },
			{ ChatChannel.World,    Color.cyan },
			{ ChatChannel.Region,   new Color(0.45f, 0.65f, 1f) },
			{ ChatChannel.Party,    Color.red },
			{ ChatChannel.Guild,    Color.green},
			{ ChatChannel.Tell,     Color.magenta },
			{ ChatChannel.Trade,    new Color(0.98f, 0.68f, 0.34f) },
			{ ChatChannel.System,   Color.yellow },
			{ ChatChannel.Discord,  TinyColor.turquoise.ToUnityColor() },
		};

		/// <summary>
		/// Whether repeated messages are allowed.
		/// </summary>
		public bool AllowRepeatMessages = false;

		/// <summary>
		/// The rate at which messages can be sent, in milliseconds.
		/// </summary>
		[Tooltip("The rate at which messages can be sent in milliseconds.")]
		public float MessageRateLimit = 0.0f;

		/// <summary>
		/// Everything needed to render one chat line, independent of any visual element.
		/// </summary>
		/// <remarks>
		/// This is the panel's memory. Rows are derived from it, never the other way round, so a
		/// tree rebuild costs the elements and nothing else.
		/// </remarks>
		private sealed class ChatMessageRecord
		{
			/// <summary>The chat channel this message belongs to.</summary>
			public ChatChannel Channel;
			/// <summary>Character ID of the sender, or 0 when there is no sender to name.</summary>
			public long SenderID;
			/// <summary>Text placed before the sender name, e.g. <c>"[To: "</c>.</summary>
			public string NamePrefix = "";
			/// <summary>Text placed after the sender name, e.g. <c>"]"</c>.</summary>
			public string NameSuffix = "";
			/// <summary>Resolved sender name, or null while the naming system has not answered.</summary>
			public string ResolvedName;
			/// <summary>True when this line has no sender column at all.</summary>
			public bool HasSender;
			/// <summary>Body of the message.</summary>
			public string Text;
			/// <summary>Colour override, or null to use the channel colour.</summary>
			public Color? Color;
		}

		/// <summary>
		/// The elements currently rendering one <see cref="ChatMessageRecord"/>.
		/// </summary>
		private sealed class ChatMessageView
		{
			/// <summary>The record this row was built from.</summary>
			public ChatMessageRecord Record;
			/// <summary>Root visual element for the message row.</summary>
			public VisualElement Root;
			/// <summary>Label displaying the sender name and channel tag.</summary>
			public Label NameLabel;
			/// <summary>Label displaying the message text.</summary>
			public Label TextLabel;
		}

		/// <summary>
		/// Lightweight view model for a single chat tab and its active channel filter.
		/// </summary>
		/// <remarks>
		/// <see cref="Button"/> belongs to the visual tree and is replaced whenever the tree is;
		/// <see cref="Label"/> and <see cref="ActiveChannels"/> are the player's configuration and
		/// survive.
		/// </remarks>
		private sealed class ChatTabView
		{
			/// <summary>Button element for the tab. Rebuilt with the tree.</summary>
			public Button Button;
			/// <summary>Display name of the tab.</summary>
			public string Label;
			/// <summary>Set of chat channels active for this tab.</summary>
			public HashSet<ChatChannel> ActiveChannels = new HashSet<ChatChannel>()
			{
				ChatChannel.Say,
				ChatChannel.World,
				ChatChannel.Region,
				ChatChannel.Party,
				ChatChannel.Guild,
				ChatChannel.Tell,
				ChatChannel.Trade,
				ChatChannel.System,
				/* Discord is filterable like every other channel now.
				 *
				 * It used to be exempt: the receive handler let it through whatever the tab said,
				 * and ValidateMessages skipped straight past it. Bridged Discord traffic was
				 * therefore the one thing in the window a player could not turn off — from a
				 * source outside the game that needs no account to post into it. */
				ChatChannel.Discord,
			};
		}

		/// <summary>Container for chat tab buttons.</summary>
		private VisualElement tabsContainer;
		/// <summary>Scroll view wrapping the message list.</summary>
		private ScrollView scrollView;
		/// <summary>Container for chat message rows.</summary>
		private VisualElement messagesContainer;
		/// <summary>Chat text input field.</summary>
		private TextField inputField;
		/// <summary>Button naming the channel a plain line is sent on.</summary>
		private Button channelSelectorButton;

		/// <summary>
		/// Frame on which submitting deliberately released the input field, or -1.
		/// </summary>
		/// <remarks>
		/// Enter is bound to BOTH the send handled here and the <c>Chat</c> action that focuses
		/// this field (<c>PlayerControls.inputactions</c> binds <c>&lt;Keyboard&gt;/enter</c>), and
		/// <c>InputAction.triggered</c> stays true for the whole frame rather than just for the
		/// callback. <see cref="EnableChatInput"/> is polled from <see cref="OnTick"/>, so if that
		/// poll happens to run after the key event was dispatched — the two are ordinary
		/// MonoBehaviour updates at the same execution order, and Unity does not define which goes
		/// first — it would see a released field and a still-true trigger and immediately focus it
		/// again, undoing the release and forcing mouse mode back on. Stamping the frame makes the
		/// outcome deterministic instead of dependent on that ordering.
		/// </remarks>
		private int inputReleasedOnFrame = -1;

		/// <summary>
		/// Channels a player can actually send on, in selector order. Tell is excluded because it
		/// needs a recipient typed with it, and System and Discord are not player-sendable.
		/// </summary>
		private static readonly ChatChannel[] SelectableChannels = new ChatChannel[]
		{
			ChatChannel.Say,
			ChatChannel.World,
			ChatChannel.Region,
			ChatChannel.Party,
			ChatChannel.Guild,
			ChatChannel.Trade,
		};

		/// <summary>
		/// The channel a line with no leading slash command is sent on.
		/// </summary>
		private ChatChannel sendChannel = ChatChannel.Say;

		/// <summary>Chat tabs keyed by display name. Configuration survives a tree rebuild.</summary>
		private readonly Dictionary<string, ChatTabView> tabs = new Dictionary<string, ChatTabView>();
		/// <summary>Tab order, so a rebuild puts the buttons back the way the player left them.</summary>
		private readonly List<ChatTabView> tabOrder = new List<ChatTabView>();
		/// <summary>Message history in display order. The source of truth for the message list.</summary>
		private readonly List<ChatMessageRecord> messages = new List<ChatMessageRecord>();
		/// <summary>Rows currently in the tree, one per record, in the same order.</summary>
		private readonly List<ChatMessageView> messageViews = new List<ChatMessageView>();

		/// <summary>
		/// Sender names already known, so a repeat sender costs no lookup.
		/// </summary>
		private readonly Dictionary<long, string> senderNameCache = new Dictionary<long, string>();

		/// <summary>
		/// Records still waiting on a name, grouped by sender ID.
		/// </summary>
		/// <remarks>
		/// This exists to bound <see cref="ClientNamingSystem"/>. That class merges callbacks for
		/// an unanswered ID with <c>pendingActions[id] += action</c> and only ever removes the
		/// entry when the server replies — so an ID the server will never answer for (a deleted
		/// character, or the 0 that a server-authored message carries) accumulated one more
		/// delegate per message, forever, and not one of those messages was ever rendered because
		/// rendering happened *inside* the callback.
		/// <para>
		/// Now the row is rendered immediately with <see cref="UNKNOWN_SENDER"/>, and exactly one
		/// request per unresolved ID is ever outstanding: subsequent messages from the same
		/// sender queue against this list instead of against the naming system.
		/// </para>
		/// </remarks>
		private readonly Dictionary<long, List<ChatMessageRecord>> pendingSenderNames =
			new Dictionary<long, List<ChatMessageRecord>>();

		/// <summary>Recently submitted lines, oldest first, for up-arrow recall.</summary>
		private readonly List<string> inputHistory = new List<string>();

		/// <summary>
		/// Cursor into <see cref="inputHistory"/>. Equal to the count when nothing is recalled.
		/// </summary>
		private int inputHistoryIndex;

		/// <summary>Draft the player was typing before they started walking back through history.</summary>
		private string inputHistoryDraft = "";

		/// <summary>
		/// True once the welcome block has been written into <see cref="messages"/>.
		/// </summary>
		/// <remarks>
		/// The welcome block is content, so it lives in the message list and is re-rendered from
		/// there after a tree rebuild. Seeding it again on every rebuild is what produced a fresh
		/// copy of the banner and the whole channel-command list on every login.
		/// </remarks>
		private bool welcomeSeeded;

		/// <summary>
		/// True while the message list should follow new messages.
		/// </summary>
		/// <remarks>
		/// Sticky, not forced. The list scrolls to the bottom on a new message only when it was
		/// already at the bottom; a player who has scrolled up to read something is left where
		/// they are, because yanking them back down mid-read is worse than no auto-scroll at all
		/// (which is what there was).
		/// </remarks>
		private bool stickToBottom = true;

		/// <summary>
		/// The <c>ReleasesCursor</c> value authored on the scene, captured before this panel
		/// starts driving the flag itself.
		/// </summary>
		/// <remarks>
		/// Kept so that a scene which genuinely wants chat to hold the cursor released keeps that
		/// behaviour: the typing claim is OR-ed on top of it rather than replacing it.
		/// </remarks>
		private bool authoredReleasesCursor;

		/// <summary>Whether <see cref="authoredReleasesCursor"/> has been read yet.</summary>
		private bool authoredCursorCaptured;

		/// <summary>
		/// The name of the currently active chat tab.
		/// </summary>
		public string CurrentTab = "";

		/// <summary>
		/// Resolves and caches visual elements and rebuilds the window from the model.
		/// </summary>
		/// <remarks>
		/// Runs again every time the visual tree is replaced (see
		/// <c>UITKControl.ReinitializeIfTreeReplaced</c>), so everything it does has to be
		/// idempotent. It used to call <see cref="AddTab"/> and write the welcome block
		/// unconditionally, which meant a second default tab and a second welcome banner after
		/// every hide/show — and, because the old rows were still in the discarded tree, a
		/// message list that grew by a full screen of content per login.
		/// </remarks>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			tabsContainer = Root.Q<VisualElement>(CHAT_TABS_NAME);
			scrollView = Root.Q<ScrollView>(CHAT_SCROLL_NAME);
			messagesContainer = Root.Q<VisualElement>(CHAT_MESSAGES_NAME);
			inputField = Root.Q<TextField>(CHAT_INPUT_NAME);

			channelSelectorButton = Root.Q<Button>(CHAT_CHANNEL_SELECTOR_NAME);
			if (channelSelectorButton != null)
			{
				channelSelectorButton.clicked += CycleSendChannel;
				RefreshChannelSelector();
			}

			Button addTabButton = Root.Q<Button>(CHAT_ADD_TAB_NAME);
			if (addTabButton != null)
			{
				addTabButton.clicked += AddTab;
			}

			if (inputField != null)
			{
				inputField.maxLength = MAX_LENGTH;
				inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
				inputField.RegisterCallback<FocusOutEvent>(OnInputFocusOut);
			}

			if (scrollView != null)
			{
				// Track whether the player is parked at the bottom, so new messages know whether
				// they are allowed to scroll.
				scrollView.verticalScroller.valueChanged += OnVerticalScroll;
			}

			ChatHelper.InitializeOnce(GetChannelCommand);

			if (!welcomeSeeded)
			{
				welcomeSeeded = true;
				SeedWelcomeMessages();
			}

			// Rebuild the elements from the surviving model. Both are no-ops on a first run with
			// nothing in them yet, other than creating the default tab.
			RebuildTabViews();
			RebuildMessageViews();
		}

		/// <summary>
		/// Re-applies state after the visual tree was rebuilt.
		/// </summary>
		/// <remarks>
		/// <see cref="OnStarting"/> already rebuilds everything from the model, so all that is
		/// left is the scroll position — which cannot be set until the rows it is measuring
		/// against have been laid out.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			ScrollToBottomDeferred();
		}

		/// <summary>
		/// Writes the welcome banner and the channel-command list into the message model.
		/// </summary>
		private void SeedWelcomeMessages()
		{
			AddSystemMessage(WelcomeMessage, null);

			// Display available channel commands in the chat window.
			foreach (KeyValuePair<ChatChannel, List<string>> pair in ChatHelper.ChannelCommandMap)
			{
				string newLine = pair.Key.ToString() + ": ";
				foreach (string command in pair.Value)
				{
					newLine += command + ", ";
				}
				AddSystemMessage(newLine, ChannelColors[pair.Key]);
			}
		}

		/// <summary>
		/// Adds a sender-less System line to the model.
		/// </summary>
		/// <param name="text">Line to display.</param>
		/// <param name="color">Optional colour override.</param>
		private void AddSystemMessage(string text, Color? color)
		{
			AddRecord(new ChatMessageRecord()
			{
				Channel = ChatChannel.System,
				HasSender = false,
				Text = text,
				Color = color,
			});
		}

		/// <summary>
		/// Registers the chat broadcast handler when the client is injected.
		/// </summary>
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<ChatBroadcast>(OnClientChatBroadcastReceived);
		}

		/// <summary>
		/// Unregisters the chat broadcast handler when the client is cleared.
		/// </summary>
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<ChatBroadcast>(OnClientChatBroadcastReceived);
		}

		/// <summary>
		/// Per-frame hook. Checks for chat input activation and keeps the cursor released while
		/// the player is typing.
		/// </summary>
		/// <remarks>
		/// Overrides the base hook rather than declaring a <c>private void Update()</c>. Unity
		/// binds only the most-derived <c>Update</c>, so declaring one here silently disabled
		/// <c>UITKControl.Update</c> for this panel — taking <c>PollLoseFocus</c> and every other
		/// control that hangs off it with it. The base class documents this trap on its own
		/// <c>Update</c>; this panel was falling into it.
		/// </remarks>
		protected override void OnTick()
		{
			EnableChatInput();
			UpdateCursorRelease();
		}

		/// <summary>
		/// Focuses the chat input field when the chat key is pressed and no field currently has focus.
		/// </summary>
		public void EnableChatInput()
		{
			if (Character == null ||
				inputField == null)
			{
				return;
			}

			/* Enter both sends and focuses; see inputReleasedOnFrame. Without this, whether the
			 * send released the field at all comes down to undefined script execution order. */
			if (inputReleasedOnFrame == Time.frameCount)
			{
				return;
			}

			// If the input already has focus, skip to avoid interfering with typing.
			if (IsInputFocused)
			{
				return;
			}

			if (PlayerInputController.Controls == null ||
				!PlayerInputController.Controls.Player.Chat.triggered)
			{
				return;
			}

			inputField.Focus();

			// Enable mouse mode so the cursor is available for typing.
			PlayerInputController.MouseMode = true;
		}

		/// <summary>
		/// Keeps the cursor released for exactly as long as the player is typing.
		/// </summary>
		/// <remarks>
		/// <c>PlayerInputController.HandleAutoDismiss</c> runs every frame and re-captures the
		/// cursor whenever no visible panel claims it via <c>ReleasesCursor</c>. Chat is always
		/// visible and does not claim it — correctly, because a chat window sitting in the corner
		/// must not hold the cursor released during play — so pressing Enter to type released the
		/// cursor and auto-dismiss took it back on the very next frame, mid-sentence.
		/// <para>
		/// Claiming the cursor only while the input has focus resolves both halves: the cursor
		/// stays available for the whole time the player is typing, and is handed straight back
		/// when they finish. The flag is the same one the scene would otherwise set statically,
		/// so nothing else has to know about this.
		/// </para>
		/// </remarks>
		private void UpdateCursorRelease()
		{
			if (!authoredCursorCaptured)
			{
				authoredCursorCaptured = true;
				authoredReleasesCursor = ReleasesCursor;
			}

			bool wanted = authoredReleasesCursor || IsInputFocused;
			if (ReleasesCursor != wanted)
			{
				ReleasesCursor = wanted;
			}
		}

		/// <summary>
		/// True when the chat input field currently holds keyboard focus.
		/// </summary>
		private bool IsInputFocused =>
			inputField != null &&
			inputField.panel != null &&
			inputField.panel.focusController != null &&
			IsSelfOrDescendant(inputField.panel.focusController.focusedElement as VisualElement, inputField);

		/// <summary>
		/// Checks whether an element is the given field or one of its children.
		/// </summary>
		/// <remarks>
		/// A <see cref="TextField"/> hands focus to its inner <c>#unity-text-input</c> child, so a
		/// plain reference comparison against the field itself reports "not focused" the whole
		/// time the player is typing into it.
		/// </remarks>
		/// <param name="element">Currently focused element, may be null.</param>
		/// <param name="field">The field to test against.</param>
		/// <returns>True when the element is the field or lives inside it.</returns>
		private static bool IsSelfOrDescendant(VisualElement element, VisualElement field)
		{
			while (element != null)
			{
				if (ReferenceEquals(element, field))
				{
					return true;
				}
				element = element.parent;
			}
			return false;
		}

		/// <summary>
		/// Handles key presses inside the input field: submit, escape, and history recall.
		/// </summary>
		/// <remarks>
		/// Registered on the trickle-down phase so Escape and the arrow keys are seen before the
		/// text field's own handling of them consumes the event.
		/// <para>
		/// Before this, the only key the chat input understood was Return. There was no way at all
		/// to leave the field from the keyboard: clicking elsewhere was the only exit, and until
		/// the player found one, movement stayed disabled because a focused text field is exactly
		/// what <c>UIManager.InputControlHasFocus</c> gates player input on.
		/// </para>
		/// </remarks>
		/// <param name="evt">The key down event.</param>
		private void OnInputKeyDown(KeyDownEvent evt)
		{
			if (inputField == null)
			{
				return;
			}

			switch (evt.keyCode)
			{
				case KeyCode.Return:
				case KeyCode.KeypadEnter:
					OnSubmit(inputField.value);
					/* Sending has to release the field for the same reason Escape does. A focused
					 * text field is what UIManager.InputControlHasFocus gates ALL player input on
					 * — movement, hotkeys and interaction alike — so a field that keeps focus
					 * after Enter leaves the player unable to move or press E until they think to
					 * hit Escape or click the world. Escape was given this treatment and Enter was
					 * not, which is why sending one chat line silently disabled the game. */
					inputReleasedOnFrame = Time.frameCount;
					inputField.Blur();
					evt.StopPropagation();
					break;

				case KeyCode.Escape:
					/* Abandon the line and hand keyboard control back to the game.
					 *
					 * Deliberately does NOT close the panel. Chat is always visible; Escape here
					 * means "stop typing", which is what every other game means by it. */
					inputField.value = "";
					ResetInputHistoryCursor();
					inputField.Blur();
					evt.StopPropagation();
					break;

				case KeyCode.UpArrow:
					RecallHistory(-1);
					evt.StopPropagation();
					break;

				case KeyCode.DownArrow:
					RecallHistory(1);
					evt.StopPropagation();
					break;
			}
		}

		/// <summary>
		/// Releases the cursor claim as soon as the field loses focus by any route.
		/// </summary>
		/// <param name="evt">The focus-out event.</param>
		private void OnInputFocusOut(FocusOutEvent evt)
		{
			ResetInputHistoryCursor();
			ReleasesCursor = authoredCursorCaptured && authoredReleasesCursor;
		}

		/// <summary>
		/// Steps through <see cref="inputHistory"/>.
		/// </summary>
		/// <param name="direction">-1 for older, +1 for newer.</param>
		private void RecallHistory(int direction)
		{
			if (inputField == null || inputHistory.Count < 1)
			{
				return;
			}

			// Stepping off the newest entry restores whatever was being typed beforehand.
			if (inputHistoryIndex >= inputHistory.Count)
			{
				if (direction > 0)
				{
					return;
				}
				inputHistoryDraft = inputField.value ?? "";
			}

			int next = Mathf.Clamp(inputHistoryIndex + direction, 0, inputHistory.Count);
			if (next == inputHistoryIndex)
			{
				return;
			}
			inputHistoryIndex = next;

			inputField.value = inputHistoryIndex >= inputHistory.Count
				? inputHistoryDraft
				: inputHistory[inputHistoryIndex];

			// Put the caret at the end, which is where recall is useful.
			inputField.SelectRange(inputField.value.Length, inputField.value.Length);
		}

		/// <summary>
		/// Moves the history cursor back past the newest entry.
		/// </summary>
		private void ResetInputHistoryCursor()
		{
			inputHistoryIndex = inputHistory.Count;
			inputHistoryDraft = "";
		}

		/// <summary>
		/// Records a submitted line for later recall.
		/// </summary>
		/// <param name="input">The line as the player typed it.</param>
		private void PushInputHistory(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
				return;
			}

			// Do not stack an identical line twice; repeating a command is common and filling the
			// history with copies of it makes recall useless.
			if (inputHistory.Count > 0 &&
				inputHistory[inputHistory.Count - 1].Equals(input, StringComparison.Ordinal))
			{
				ResetInputHistoryCursor();
				return;
			}

			inputHistory.Add(input);
			if (inputHistory.Count > MAX_INPUT_HISTORY)
			{
				inputHistory.RemoveAt(0);
			}
			ResetInputHistoryCursor();
		}

		/// <summary>
		/// Updates which messages are visible based on the active tab and its channels.
		/// </summary>
		/// <remarks>
		/// Visibility only. Filtering must never destroy a message or prevent it from being
		/// created: the whole point of per-tab channel sets is that switching tabs shows a
		/// different view of the same history, and a message dropped because the tab that was
		/// active when it arrived did not want it can never appear on the tab that does.
		/// </remarks>
		public void ValidateMessages()
		{
			if (!tabs.TryGetValue(CurrentTab, out ChatTabView tab))
			{
				return;
			}

			for (int i = 0; i < messageViews.Count; ++i)
			{
				ChatMessageView view = messageViews[i];
				if (view == null || view.Root == null || view.Record == null)
				{
					continue;
				}

				bool visible = tab.ActiveChannels.Contains(view.Record.Channel);
				view.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Sets the chat input field text.
		/// </summary>
		/// <param name="input">Text to set in the input field.</param>
		public void SetInputText(string input)
		{
			if (inputField != null)
			{
				inputField.value = input;
			}
		}

		/// <summary>
		/// Handles chat message submission, including sanitisation, rate limiting and broadcasting.
		/// </summary>
		public void OnSubmit(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
				return;
			}

			PushInputHistory(input);

			/* The selector names where a plain line goes. Anything already carrying a slash
			 * command states its own destination and is left exactly as typed — that includes
			 * non-channel commands like /leaveinstance, which must not be prefixed into chat. */
			bool channelPrefixApplied = false;
			if (!input.StartsWith("/") && sendChannel != ChatChannel.Say)
			{
				input = $"{ChatHelper.ChannelCommandMap[sendChannel][0]} {input}";
				channelPrefixApplied = true;
			}

			/* Clean locally before sending.
			 *
			 * The server does this again and does not trust this — a client is free to skip it —
			 * but doing it here keeps the player's own window honest about what was actually
			 * sent, and stops a message that is going to be rejected from consuming a rate-limit
			 * slot. SanitizeIncoming also strips the FISHMMO_ control-code prefix, so a line
			 * typed with one in it no longer makes it as far as the wire. */
			input = ChatHelper.SanitizeIncoming(input, MAX_LENGTH);

			/* Nothing left to send is not an error and must not be sent.
			 *
			 * Typing "<b>" cleans to an empty string. The old code broadcast that empty string
			 * anyway, and the server treated empty text as an exploit and disconnected the player
			 * with ExploitExcessiveData. Both halves are fixed: the server no longer kicks, and
			 * this no longer sends. */
			if (string.IsNullOrWhiteSpace(input))
			{
				if (inputField != null)
				{
					inputField.value = "";
				}
				return;
			}

			if (Client.NetworkManager.IsClientStarted)
			{
				if (Character != null)
				{
					if (MessageRateLimit > 0)
					{
						long nowTicks = DateTime.UtcNow.Ticks;
						if (Character.NextChatMessageTicks > nowTicks)
						{
							return;
						}
						Character.NextChatMessageTicks = nowTicks + (long)(MessageRateLimit * TimeSpan.TicksPerMillisecond);
					}
					/* Never suppress a repeated slash command.
					 *
					 * The duplicate filter is for chat. Repeating a command is the normal
					 * response to one that appears to have done nothing — and commands often
					 * are refused the first time for a reason that then clears, such as
					 * /leaveinstance while still in combat. Dropped here, the second attempt
					 * never even reaches the server. The server applies the same exemption.
					 *
					 * A prefix the SELECTOR added is not a command the player typed, so it must
					 * not claim that exemption — testing the prefixed text alone would leave the
					 * duplicate filter applying to Say and to nothing else. Comparing the
					 * prefixed form is still right: the same words sent to a different channel
					 * are a different message and should go through. */
					if (!AllowRepeatMessages && (channelPrefixApplied || !input.StartsWith("/")))
					{
						if (!string.IsNullOrWhiteSpace(Character.LastChatMessage) &&
							Character.LastChatMessage.Equals(input))
						{
							return;
						}
						Character.LastChatMessage = input;
					}
				}
				ChatBroadcast message = new ChatBroadcast() { Text = input };
				// Send the message to the server.
				Client.Broadcast(message, Channel.Reliable);
			}

			if (inputField != null)
			{
				inputField.value = "";
			}
		}

		/// <summary>
		/// Advances the selector to the next sendable channel.
		/// </summary>
		private void CycleSendChannel()
		{
			int index = System.Array.IndexOf(SelectableChannels, sendChannel);
			sendChannel = SelectableChannels[(index + 1) % SelectableChannels.Length];
			RefreshChannelSelector();

			/* Clicking a UITK Button leaves it focused, and a focused Button answers submit
			 * navigation — so the next Enter would be ambiguous between "start typing" and
			 * "cycle again". Releasing it makes Enter mean what it means everywhere else. */
			channelSelectorButton?.Blur();
		}

		/// <summary>
		/// Repaints the selector to match the current send channel.
		/// </summary>
		private void RefreshChannelSelector()
		{
			if (channelSelectorButton == null)
			{
				return;
			}

			channelSelectorButton.text = sendChannel.ToString();

			/* Tinting the button with the channel colour means the destination is readable at a
			 * glance without reading the word — the same cue the messages themselves use. */
			if (ChannelColors.TryGetValue(sendChannel, out Color channelColor))
			{
				channelSelectorButton.style.color = channelColor;
			}
		}

		#region Tabs
		/// <summary>
		/// Adds a new chat tab to the UI, ensuring unique tab names.
		/// </summary>
		public void AddTab()
		{
			if (tabs.Count >= MAX_TABS)
			{
				return;
			}

			string newTabName = "New Tab";
			string finalName = newTabName;
			for (int i = 0; tabs.ContainsKey(finalName); ++i)
			{
				finalName = newTabName + " " + i;
			}

			ChatTabView tab = new ChatTabView()
			{
				Label = finalName,
			};
			tabs.Add(finalName, tab);
			tabOrder.Add(tab);

			BuildTabButton(tab);

			if (string.IsNullOrEmpty(CurrentTab))
			{
				ActivateTab(tab);
			}
		}

		/// <summary>
		/// Builds (or rebuilds) the button for a tab and attaches it to the current tab bar.
		/// </summary>
		/// <param name="tab">The tab to build a button for.</param>
		private void BuildTabButton(ChatTabView tab)
		{
			if (tabsContainer == null || tab == null)
			{
				return;
			}

			Button button = new Button(() => ActivateTab(tab))
			{
				text = tab.Label,
			};
			button.AddToClassList(TAB_THEME_CLASS);
			button.AddToClassList(TAB_CLASS);
			button.EnableInClassList(TAB_ACTIVE_CLASS, tab.Label.Equals(CurrentTab, StringComparison.Ordinal));
			button.RegisterCallback<PointerDownEvent>((evt) => OnTabPointerDown(evt, tab));
			tab.Button = button;

			tabsContainer.Add(button);
		}

		/// <summary>
		/// Re-creates every tab button against the current visual tree.
		/// </summary>
		/// <remarks>
		/// The tab <em>definitions</em> — names and channel sets — are the player's configuration
		/// and are kept; only the buttons are rebuilt. Creating a fresh default tab on every
		/// rebuild instead is what left a stray "General" behind after each login while the
		/// player's own tabs pointed at buttons in a tree that no longer existed.
		/// </remarks>
		private void RebuildTabViews()
		{
			if (tabsContainer == null)
			{
				return;
			}

			tabsContainer.Clear();

			if (tabOrder.Count < 1)
			{
				// First run: create the single default tab.
				AddTab();
				if (tabOrder.Count > 0)
				{
					CurrentTab = tabOrder[0].Label;
					RenameCurrentTab("General");
				}
				return;
			}

			for (int i = 0; i < tabOrder.Count; ++i)
			{
				BuildTabButton(tabOrder[i]);
			}

			// The active tab may have been removed while the tree was gone; fall back to the first.
			if (!tabs.ContainsKey(CurrentTab) && tabOrder.Count > 0)
			{
				CurrentTab = tabOrder[0].Label;
			}
			ActivateTab(tabs.TryGetValue(CurrentTab, out ChatTabView current) ? current : null);
		}

		/// <summary>
		/// Opens the channel picker overlay when a tab is right-clicked.
		/// </summary>
		/// <param name="evt">The pointer down event.</param>
		/// <param name="tab">The tab that was clicked.</param>
		private void OnTabPointerDown(PointerDownEvent evt, ChatTabView tab)
		{
			if (evt.button != 1)
			{
				return;
			}

			ActivateTab(tab);
			ToggleUIChatChannelPicker(tab);
		}

		/// <summary>
		/// Toggles the shared channel picker overlay for the supplied tab.
		/// </summary>
		/// <param name="tab">The tab whose channels are being edited.</param>
		private void ToggleUIChatChannelPicker(ChatTabView tab)
		{
			if (UIManager.TryGetTK("UIChatChannelPicker", out UITKChatChannelPicker channelPicker))
			{
				channelPicker.ToggleVisibility();
				if (channelPicker.Visible)
				{
					Vector3 position = tab.Button != null ? (Vector3)tab.Button.worldBound.center : Vector3.zero;
					channelPicker.Activate(tab.ActiveChannels, tab.Label, position);
				}
			}
		}

		/// <summary>
		/// Toggles the active state of a chat channel in the current tab.
		/// </summary>
		/// <param name="channel">The chat channel to toggle.</param>
		/// <param name="value">Whether the channel should be active.</param>
		public void ToggleChannel(ChatChannel channel, bool value)
		{
			if (tabs.TryGetValue(CurrentTab, out ChatTabView tab))
			{
				if (tab.ActiveChannels.Contains(channel))
				{
					if (!value)
					{
						tab.ActiveChannels.Remove(channel);
					}
				}
				else if (value)
				{
					tab.ActiveChannels.Add(channel);
				}
				ValidateMessages();
			}
		}

		/// <summary>
		/// Renames the current chat tab if the new name is not already taken.
		/// </summary>
		/// <param name="newName">The new name for the tab.</param>
		/// <returns>True if renamed successfully, false otherwise.</returns>
		public bool RenameCurrentTab(string newName)
		{
			if (tabs.ContainsKey(newName))
			{
				return false;
			}
			else if (tabs.TryGetValue(CurrentTab, out ChatTabView tab))
			{
				tabs.Remove(CurrentTab);
				tab.Label = newName;
				if (tab.Button != null)
				{
					tab.Button.text = newName;
				}
				tabs.Add(tab.Label, tab);
				ActivateTab(tab);
				return true;
			}
			return false; // something went wrong
		}

		/// <summary>
		/// Removes a chat tab and updates the current tab accordingly.
		/// </summary>
		/// <param name="tab">The tab to remove.</param>
		private void RemoveTab(ChatTabView tab)
		{
			if (tab == null)
			{
				return;
			}

			tabs.Remove(tab.Label);
			tabOrder.Remove(tab);
			if (tab.Button != null)
			{
				tab.Button.RemoveFromHierarchy();
				tab.Button = null;
			}

			if (CurrentTab.Equals(tab.Label))
			{
				CurrentTab = "";
				if (tabOrder.Count > 0)
				{
					ActivateTab(tabOrder[0]);
				}
			}
		}

		/// <summary>
		/// Activates the specified chat tab and refreshes message visibility.
		/// </summary>
		/// <param name="tab">The tab to activate.</param>
		private void ActivateTab(ChatTabView tab)
		{
			if (tab == null)
			{
				return;
			}

			CurrentTab = tab.Label;
			foreach (ChatTabView other in tabs.Values)
			{
				if (other.Button != null)
				{
					other.Button.EnableInClassList(TAB_ACTIVE_CLASS, other == tab);
				}
			}
			ValidateMessages();
		}
		#endregion

		#region Message model and rendering
		/// <summary>
		/// Appends a record to the model, trims the history and renders a row for it.
		/// </summary>
		/// <param name="record">The message to add.</param>
		private void AddRecord(ChatMessageRecord record)
		{
			if (record == null)
			{
				return;
			}

			messages.Add(record);

			// Messages are FIFO: drop the oldest record AND remove its row from the tree.
			while (messages.Count > MAX_MESSAGES)
			{
				ChatMessageRecord dropped = messages[0];
				messages.RemoveAt(0);
				RemoveOldestView();
				ForgetPendingRecord(dropped);
			}

			RenderRecord(record, messages.Count - 1);
		}

		/// <summary>
		/// Removes the oldest rendered row, if there is one.
		/// </summary>
		private void RemoveOldestView()
		{
			if (messageViews.Count < 1)
			{
				return;
			}

			ChatMessageView oldest = messageViews[0];
			messageViews.RemoveAt(0);
			oldest.Root?.RemoveFromHierarchy();
		}

		/// <summary>
		/// Drops a record from the pending-name bookkeeping once it has aged out of the history.
		/// </summary>
		/// <param name="record">The record leaving the model.</param>
		private void ForgetPendingRecord(ChatMessageRecord record)
		{
			if (record == null || record.SenderID == 0)
			{
				return;
			}
			if (pendingSenderNames.TryGetValue(record.SenderID, out List<ChatMessageRecord> waiting))
			{
				waiting.Remove(record);
				if (waiting.Count < 1)
				{
					pendingSenderNames.Remove(record.SenderID);
				}
			}
		}

		/// <summary>
		/// Rebuilds every row from the surviving message records.
		/// </summary>
		private void RebuildMessageViews()
		{
			messageViews.Clear();
			if (messagesContainer == null)
			{
				return;
			}
			messagesContainer.Clear();

			for (int i = 0; i < messages.Count; ++i)
			{
				RenderRecord(messages[i], i);
			}
		}

		/// <summary>
		/// Builds the row for a record and appends it to the message container.
		/// </summary>
		/// <param name="record">The record to render.</param>
		/// <param name="index">The record's position in <see cref="messages"/>.</param>
		private void RenderRecord(ChatMessageRecord record, int index)
		{
			if (messagesContainer == null || record == null)
			{
				return;
			}

			// Decide before adding anything, while the scroller still describes the old content.
			bool follow = stickToBottom;

			Color resolved = record.Color ?? ChannelColors[record.Channel];

			VisualElement row = new VisualElement();
			row.AddToClassList(MESSAGE_ROW_CLASS);

			Label nameLabel = new Label(BuildNameText(record));
			nameLabel.AddToClassList(MESSAGE_NAME_CLASS);
			nameLabel.style.color = resolved;
			DisableRichText(nameLabel);

			Label textLabel = new Label(record.Text);
			textLabel.AddToClassList(MESSAGE_TEXT_CLASS);
			textLabel.style.color = resolved;
			DisableRichText(textLabel);

			// Hide the sender column when the previous line was from the same sender on the same
			// channel, or when the line has no sender at all.
			if (!ShouldShowSender(record, index))
			{
				nameLabel.style.display = DisplayStyle.None;
			}

			row.Add(nameLabel);
			row.Add(textLabel);
			messagesContainer.Add(row);

			ChatMessageView view = new ChatMessageView()
			{
				Record = record,
				Root = row,
				NameLabel = nameLabel,
				TextLabel = textLabel,
			};
			messageViews.Add(view);

			// Apply the current tab filter to the new row.
			if (tabs.TryGetValue(CurrentTab, out ChatTabView tab) &&
				!tab.ActiveChannels.Contains(record.Channel))
			{
				row.style.display = DisplayStyle.None;
			}

			if (follow)
			{
				ScrollToBottomDeferred();
			}
		}

		/// <summary>
		/// Turns off markup parsing for a chat label.
		/// </summary>
		/// <remarks>
		/// Defence in depth behind <see cref="ChatSanitizer"/>. A <see cref="Label"/> parses Unity
		/// rich text by default — the belief that it does not is written down elsewhere in this
		/// codebase and is simply wrong — so any tag the sanitiser ever fails to remove becomes a
		/// live formatting instruction on every client that receives the message. With parsing
		/// off, the worst a future hole in the filter can produce is a visible <c>&lt;size=500&gt;</c>
		/// in somebody's chat log rather than a screen-filling one.
		/// <para>
		/// Chat has never wanted markup: every message that reaches these labels has been through
		/// a filter whose entire job is removing it.
		/// </para>
		/// </remarks>
		/// <param name="label">The label to render as plain text.</param>
		private static void DisableRichText(Label label)
		{
			if (label != null)
			{
				label.enableRichText = false;
			}
		}

		/// <summary>
		/// Builds the sender-column text for a record.
		/// </summary>
		/// <param name="record">The record being rendered.</param>
		/// <returns>The channel tag, plus the sender name when there is one.</returns>
		private static string BuildNameText(ChatMessageRecord record)
		{
			string tag = "[" + record.Channel.ToString() + "] ";
			if (!record.HasSender)
			{
				return tag;
			}
			return tag + record.NamePrefix + (record.ResolvedName ?? UNKNOWN_SENDER) + record.NameSuffix;
		}

		/// <summary>
		/// Decides whether a record renders its sender column.
		/// </summary>
		/// <param name="record">The record being rendered.</param>
		/// <param name="index">The record's position in <see cref="messages"/>.</param>
		/// <returns>True when the sender column should be visible.</returns>
		private bool ShouldShowSender(ChatMessageRecord record, int index)
		{
			if (!record.HasSender)
			{
				return false;
			}

			if (index <= 0 || index > messages.Count)
			{
				return true;
			}

			ChatMessageRecord previous = messages[index - 1];
			if (previous.Channel != record.Channel || !previous.HasSender)
			{
				return true;
			}

			// Same channel and the same sender: fold the repeat into the block above it.
			return previous.SenderID != record.SenderID ||
				   !string.Equals(previous.NamePrefix, record.NamePrefix, StringComparison.Ordinal);
		}

		/// <summary>
		/// Adds a message whose sender name has to be resolved from a character ID.
		/// </summary>
		/// <remarks>
		/// The row is created and shown immediately with a placeholder, and patched in place when
		/// the name arrives. Deferring creation until the naming system answered is what made an
		/// unresolvable sender ID silently swallow every message from it.
		/// </remarks>
		/// <param name="channel">The chat channel.</param>
		/// <param name="senderID">Character ID of the sender.</param>
		/// <param name="text">The message body.</param>
		/// <param name="namePrefix">Text placed before the resolved name.</param>
		/// <param name="nameSuffix">Text placed after the resolved name.</param>
		private void AddSenderMessage(ChatChannel channel, long senderID, string text,
			string namePrefix = "", string nameSuffix = "")
		{
			ChatMessageRecord record = new ChatMessageRecord()
			{
				Channel = channel,
				SenderID = senderID,
				HasSender = true,
				NamePrefix = namePrefix ?? "",
				NameSuffix = nameSuffix ?? "",
				Text = text,
			};

			// A sender ID of zero has no character behind it and never will; do not ask.
			if (senderID != 0 && senderNameCache.TryGetValue(senderID, out string known))
			{
				record.ResolvedName = known;
			}

			AddRecord(record);

			if (senderID == 0 || record.ResolvedName != null)
			{
				return;
			}

			if (pendingSenderNames.TryGetValue(senderID, out List<ChatMessageRecord> waiting))
			{
				// A request for this ID is already outstanding. Queue against it rather than
				// merging another delegate into the naming system's per-ID callback chain.
				waiting.Add(record);
				return;
			}

			pendingSenderNames[senderID] = new List<ChatMessageRecord>() { record };
			long requestedID = senderID;
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, requestedID,
				(name) => OnSenderNameResolved(requestedID, name));
		}

		/// <summary>
		/// Patches every row still waiting on a sender name once it arrives.
		/// </summary>
		/// <param name="senderID">The character ID that was resolved.</param>
		/// <param name="name">The name the server returned.</param>
		private void OnSenderNameResolved(long senderID, string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				name = UNKNOWN_SENDER;
			}
			senderNameCache[senderID] = name;

			if (!pendingSenderNames.TryGetValue(senderID, out List<ChatMessageRecord> waiting))
			{
				return;
			}
			pendingSenderNames.Remove(senderID);

			for (int i = 0; i < waiting.Count; ++i)
			{
				waiting[i].ResolvedName = name;
			}

			// Repaint only the rows that changed.
			for (int i = 0; i < messageViews.Count; ++i)
			{
				ChatMessageView view = messageViews[i];
				if (view?.Record == null || view.Record.SenderID != senderID || view.NameLabel == null)
				{
					continue;
				}
				view.NameLabel.text = BuildNameText(view.Record);
			}
		}

		/// <summary>
		/// Instantiates a new chat message element and adds it to the chat view.
		/// </summary>
		/// <param name="channel">The chat channel.</param>
		/// <param name="name">The sender's name, already resolved. Empty for no sender column.</param>
		/// <param name="message">The message text.</param>
		/// <param name="color">Optional colour override.</param>
		public void InstantiateChatMessage(ChatChannel channel, string name, string message, Color? color = null)
		{
			AddRecord(new ChatMessageRecord()
			{
				Channel = channel,
				HasSender = !string.IsNullOrWhiteSpace(name),
				ResolvedName = string.IsNullOrWhiteSpace(name) ? null : name,
				Text = message,
				Color = color,
			});
		}
		#endregion

		#region Scrolling
		/// <summary>
		/// Notes whether the player is parked at the bottom of the list.
		/// </summary>
		/// <param name="value">The scroller's new value.</param>
		private void OnVerticalScroll(float value)
		{
			if (scrollView == null)
			{
				return;
			}

			Scroller scroller = scrollView.verticalScroller;

			/* A list shorter than its viewport has highValue <= 0 and cannot be scrolled at all,
			 * which counts as "at the bottom" — otherwise the first screenful of messages would
			 * never start following. The epsilon absorbs the fractional offsets that fall out of
			 * a wrapping label's layout. */
			stickToBottom = scroller.highValue <= 0f || value >= scroller.highValue - 1f;
		}

		/// <summary>
		/// Scrolls to the newest message once the layout has caught up.
		/// </summary>
		/// <remarks>
		/// Deferred deliberately. A row added this frame has no resolved height until the next
		/// layout pass, so the scroller's <c>highValue</c> is still describing the list without it
		/// and scrolling immediately lands one message short every time.
		/// </remarks>
		private void ScrollToBottomDeferred()
		{
			if (scrollView == null)
			{
				return;
			}

			scrollView.schedule.Execute(() =>
			{
				if (scrollView == null || scrollView.panel == null)
				{
					return;
				}
				Scroller scroller = scrollView.verticalScroller;
				if (scroller.highValue > 0f)
				{
					scroller.value = scroller.highValue;
				}
				stickToBottom = true;
			});
		}
		#endregion

		#region Channel handlers
		/// <summary>
		/// Gets the chat command delegate for the specified channel.
		/// </summary>
		/// <param name="channel">The chat channel.</param>
		/// <returns>The chat command delegate.</returns>
		public ChatCommand GetChannelCommand(ChatChannel channel)
		{
			switch (channel)
			{
				case ChatChannel.World: return OnWorldChat;
				case ChatChannel.Region: return OnRegionChat;
				case ChatChannel.Party: return OnPartyChat;
				case ChatChannel.Guild: return OnGuildChat;
				case ChatChannel.Tell: return OnTellChat;
				case ChatChannel.Trade: return OnTradeChat;
				case ChatChannel.Say: return OnSayChat;
				case ChatChannel.System: return OnSystemChat;
				default: return OnSayChat;
			}
		}

		/// <summary>
		/// Handles incoming chat broadcasts from the server.
		/// </summary>
		/// <remarks>
		/// Every message is parsed and stored, whatever the active tab is. The tab filter is
		/// applied when the row is rendered and re-applied when the tab changes; it must not
		/// decide whether the message exists. Discarding messages here meant a second tab
		/// configured for a different channel set showed nothing that had arrived while it was
		/// not the active tab — which is most of what a second tab is for.
		/// </remarks>
		/// <param name="msg">The chat broadcast message.</param>
		/// <param name="channel">The network channel.</param>
		private void OnClientChatBroadcastReceived(ChatBroadcast msg, Channel channel)
		{
			ParseLocalMessage(Character, msg);
		}

		/// <summary>
		/// Parses and processes a local chat message, including Discord and other channels.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		private void ParseLocalMessage(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			// Validate message length.
			if (string.IsNullOrWhiteSpace(msg.Text) || msg.Text.Length > MAX_LENGTH)
			{
				return;
			}

			if (msg.Channel == ChatChannel.Discord)
			{
				OnDiscordChat(msg);
			}
			else if (msg.Channel == ChatChannel.System)
			{
				/* System has no slash command, so it has no entry in ChatHelper.ChannelCommandMap —
				 * and InitializeOnce builds the channel->handler table by iterating exactly that
				 * map. ParseChatChannel therefore returned null for System and the message was
				 * dropped on the floor, even though GetChannelCommand maps it to OnSystemChat.
				 * That silently swallowed every server-authored message the player relies on:
				 * shutdown countdowns, admin command acknowledgements, quest and achievement
				 * notices, boss emotes. Dispatched directly, the way Discord already is, because
				 * both are channels the player can never send to. */
				OnSystemChat(localCharacter, msg);
			}
			else
			{
				ChatCommand command = ChatHelper.ParseChatChannel(msg.Channel);
				if (command != null)
				{
					command?.Invoke(localCharacter, msg);
				}
			}
		}

		/// <summary>
		/// Handles Discord chat messages and displays them in the chat view.
		/// </summary>
		/// <remarks>
		/// The name here is a Discord display name, chosen by whoever sent it and outside the
		/// game's control entirely. It is sanitised by the bridge on the way in and again by the
		/// server before broadcast, and rendered — like every other chat label — with markup
		/// parsing switched off.
		/// </remarks>
		/// <param name="msg">The chat broadcast message.</param>
		public void OnDiscordChat(ChatBroadcast msg)
		{
			string characterName = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed).TrimEnd(':');
			InstantiateChatMessage(ChatChannel.Discord, characterName, trimmed);
		}

		/// <summary>
		/// Handles World chat messages and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnWorldChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			AddSenderMessage(msg.Channel, msg.SenderID, msg.Text);
			return true;
		}

		/// <summary>
		/// Handles Region chat messages and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnRegionChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			AddSenderMessage(msg.Channel, msg.SenderID, msg.Text);
			return true;
		}

		/// <summary>
		/// Handles Party chat messages, including error messages, and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnPartyChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			string cmd = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (!string.IsNullOrWhiteSpace(cmd) &&
				 cmd.Equals(ChatHelper.PARTY_ERROR_TARGET_IN_PARTY) &&
				 ErrorCodes.TryGetValue(ChatHelper.PARTY_ERROR_TARGET_IN_PARTY, out string targetErrorMsg))
			{
				AddSenderMessage(msg.Channel, msg.SenderID, targetErrorMsg);
			}
			else
			{
				AddSenderMessage(msg.Channel, msg.SenderID, msg.Text);
			}
			return true;
		}

		/// <summary>
		/// Handles Guild chat messages, including error messages, and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnGuildChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			string cmd = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);
			if (!string.IsNullOrWhiteSpace(cmd) &&
				 cmd.Equals(ChatHelper.GUILD_ERROR_TARGET_IN_GUILD) &&
				 ErrorCodes.TryGetValue(ChatHelper.GUILD_ERROR_TARGET_IN_GUILD, out string targetErrorMsg))
			{
				AddSenderMessage(msg.Channel, msg.SenderID, targetErrorMsg);
			}
			else
			{
				AddSenderMessage(msg.Channel, msg.SenderID, msg.Text);
			}
			return true;
		}

		/// <summary>
		/// Handles Tell (private) chat messages, including error and relay messages, and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnTellChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			string cmd = ChatHelper.GetWordAndTrimmed(msg.Text, out string trimmed);

			// Check if we have any special messages.
			if (!string.IsNullOrWhiteSpace(cmd))
			{
				// Returned message.
				if (cmd.Equals(ChatHelper.TELL_RELAYED))
				{
					AddSenderMessage(msg.Channel, msg.SenderID, trimmed, "[To: ", "]");
					return true;
				}
				// Target offline.
				else if (cmd.Equals(ChatHelper.TARGET_OFFLINE) &&
						 ErrorCodes.TryGetValue(ChatHelper.TARGET_OFFLINE, out string offlineMsg))
				{
					ChatHelper.GetWordAndTrimmed(trimmed, out string targetName);
					if (!string.IsNullOrWhiteSpace(targetName))
					{
						AddSenderMessage(msg.Channel, msg.SenderID, targetName + offlineMsg);
						return true;
					}
				}
				// Messaging ourself??
				else if (cmd.Equals(ChatHelper.TELL_ERROR_MESSAGE_SELF) &&
						 ErrorCodes.TryGetValue(ChatHelper.TELL_ERROR_MESSAGE_SELF, out string errorMsg))
				{
					AddSenderMessage(msg.Channel, msg.SenderID, errorMsg);
					return true;
				}
			}
			// We received a tell from someone else.
			if (localCharacter == null || msg.SenderID != localCharacter.ID)
			{
				AddSenderMessage(msg.Channel, msg.SenderID, msg.Text, "[From: ", "]");
			}
			return true;
		}

		/// <summary>
		/// Handles Trade chat messages and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnTradeChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			AddSenderMessage(msg.Channel, msg.SenderID, msg.Text);
			return true;
		}

		/// <summary>
		/// Handles Say chat messages and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnSayChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			AddSenderMessage(msg.Channel, msg.SenderID, msg.Text);
			return true;
		}

		/// <summary>
		/// Handles System chat messages and displays them in the chat view.
		/// </summary>
		/// <param name="localCharacter">The local player character.</param>
		/// <param name="msg">The chat broadcast message.</param>
		/// <returns>True if handled successfully.</returns>
		public bool OnSystemChat(IPlayerCharacter localCharacter, ChatBroadcast msg)
		{
			/* A system message has no sender to resolve.
			 *
			 * This used to ask the naming system for the name of character SenderID, which for a
			 * server-authored message is 0 — no such character. The callback only fires when the
			 * server answers with a name, so the message was held indefinitely and never
			 * displayed: the achievement "your bags are full" notice, the maintenance countdown,
			 * every /admin acknowledgement. Rendering directly is also simply correct — the
			 * sender column is meaningless here. */
			AddSystemMessage(msg.Text, null);
			return true;
		}
		#endregion
	}
}
